using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using ProjectorDisplay;
using DisplayPath = ProjectorDisplay.Path;

static class DisplayModeProfilesTests {
    const string DevicePath = @"\\?\DISPLAY#TEST000#synthetic";
    const string OtherDevicePath = @"\\?\DISPLAY#TEST000#other";

    static int Main() {
        string directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "display-mode-tests-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        try {
            RebindBothModes();
            RejectMissingMode();
            UseStrictTemporaryFlags();
            ReloadStoredProfileWithoutOverwritingIt(directory);
            ContinueAfterStrictApplyFailure();
            DoNotRememberAutomaticFallback();
            Console.WriteLine("Display mode profile tests passed.");
            return 0;
        } catch (Exception error) {
            Console.Error.WriteLine(error);
            return 1;
        } finally {
            foreach (string file in Directory.GetFiles(directory)) File.Delete(file);
            Directory.Delete(directory);
        }
    }

    static void RebindBothModes() {
        Snapshot saved = Fixture(279961);
        Snapshot rebuilt = (Snapshot)Invoke("BuildProfileSnapshot", saved, NewPath());
        Require(rebuilt.Paths.Length == 1 && rebuilt.Modes.Length == 2, "The rebuilt path must provide both modes.");
        DisplayPath path = rebuilt.Paths[0];
        Require(path.Flags == 1 && path.Source.Mode == 0 && path.Target.Mode == 1, "The rebuilt path must reference the two supplied mode entries.");
        Require(path.Source.Id == 7 && path.Target.Id == 19 && path.Source.Adapter.Low == 42 && path.Target.Adapter.High == 5,
            "The rebuilt path must use live source and target identities.");
        Require(path.Target.Refresh.Numerator == 279961 && path.Target.Refresh.Denominator == 1000 && path.Target.Scanline == 1,
            "The rebuilt path must preserve the recorded refresh rate and scanline ordering.");
        Mode source = rebuilt.Modes[0];
        Mode target = rebuilt.Modes[1];
        Require(source.Type == 1 && source.Id == 7 && source.Adapter.Low == 42 && source.Adapter.High == 4,
            "The source mode must use the live source identity.");
        Require(target.Type == 2 && target.Id == 19 && target.Adapter.Low == 43 && target.Adapter.High == 5,
            "The target mode must use the live target identity.");
        Require(source.A == 0x000005a000000a00 && source.B == 4, "The source dimensions and format must be retained.");
        Require(target.A == 1258750000 && target.B == 0x00000aa04b070030 && target.C == 0x000003e800044599 &&
                target.D == 0x000005a000000a00 && target.E == 0x0000067500000aa0 && target.F == 0x00000001000000ff,
            "The exact target timing must be retained.");
    }

    static void RejectMissingMode() {
        Snapshot saved = Fixture(279961);
        saved.Modes = new[] { saved.Modes[0] };
        Require(Invoke("BuildProfileSnapshot", saved, NewPath()) == null, "A profile missing its source mode must be rejected.");
        saved = Fixture(279961);
        saved.Paths[0].Target.Mode = UInt32.MaxValue;
        Require(Invoke("BuildProfileSnapshot", saved, NewPath()) == null, "A profile missing its target mode must be rejected.");
    }

    static void UseStrictTemporaryFlags() {
        Require((uint)Invoke("PreferredModeFlags", false, false) == 0x60, "Validation must use explicitly supplied modes without changing them.");
        Require((uint)Invoke("PreferredModeFlags", true, false) == 0xa0, "Automatic application must be temporary and must preserve the specified modes.");
        Require((uint)Invoke("PreferredModeFlags", true, true) == 0x2a0, "Only an explicit persistent application may save the requested mode.");
    }

    static void ContinueAfterStrictApplyFailure() {
        Snapshot profile = Fixture(279961);
        DisplayPath originalSource = NewPath();
        originalSource.Source.Id = 0;
        DisplayPath alternative = NewPath();
        var paths = new[] { alternative, originalSource };
        int applyAttempts = 0;
        Func<Snapshot, uint, int> configure = (state, flags) => {
            if (flags == 0x60) return 0;
            Require(flags == 0xa0, "The strict automatic apply must stay temporary.");
            uint expectedSource = applyAttempts == 0 ? 0u : 7u;
            Require(state.Paths[0].Source.Id == expectedSource, "Try the recorded source first, then try the alternative.");
            applyAttempts++;
            return 31;
        };
        Require(!(bool)Invoke("TryApplyPreferredProfile", profile, paths, false, configure) && applyAttempts == 2,
            "A validated mode can fail to apply. Try remaining paths and allow the caller to use the fallback when all fail.");
    }

    static void DoNotRememberAutomaticFallback() {
        const string fallbackDevice = @"\\?\DISPLAY#TEST000#fallback";
        const string explicitDevice = @"\\?\DISPLAY#TEST000#explicit";
        Require((bool)Invoke("ShouldRememberMode", fallbackDevice), "A newly observed mode is eligible for capture.");
        Invoke("RecordFallbackMode", fallbackDevice, false);
        Require(!(bool)Invoke("ShouldRememberMode", fallbackDevice.ToLowerInvariant()),
            "An automatic fallback is ineligible for capture during the same router process.");
        Invoke("RecordFallbackMode", explicitDevice, true);
        Require((bool)Invoke("ShouldRememberMode", explicitDevice), "An explicit persistent command does not mark a screen as an automatic fallback.");
        Require(Invoke("FindPreferredMode", DevicePath) != null, "An unrelated trusted profile is preserved.");
    }

    static void ReloadStoredProfileWithoutOverwritingIt(string directory) {
        string file = System.IO.Path.Combine(directory, "display-modes.json");
        string original = System.IO.Path.Combine(directory, "original.bin");
        string fallback = System.IO.Path.Combine(directory, "fallback.bin");
        WriteSnapshot(original, Fixture(279961));
        WriteSnapshot(fallback, Fixture(60000));
        Invoke("ConfigureModeStore", file);
        Invoke("SeedModeProfile", original, DevicePath);
        Invoke("SeedModeProfile", fallback, DevicePath.ToLowerInvariant());
        Invoke("SeedModeProfile", fallback, OtherDevicePath);
        Require(File.ReadAllText(file).TrimStart().StartsWith("["), "The mode store must be JSON.");
        Invoke("ConfigureModeStore", System.IO.Path.Combine(directory, "empty.json"));
        Require(Invoke("FindPreferredMode", DevicePath) == null, "Changing the mode store must reset the in-memory profiles.");
        Invoke("ConfigureModeStore", file);
        Snapshot restored = (Snapshot)Invoke("FindPreferredMode", DevicePath.ToLowerInvariant());
        Require(restored != null && restored.Paths[0].Target.Refresh.Numerator == 279961 && restored.Modes[0].C == 0x000003e800044599,
            "Reloading the store must recover the trusted profile and its full timing instead of a fallback with a different refresh rate.");
        Snapshot second = (Snapshot)Invoke("FindPreferredMode", OtherDevicePath);
        Require(second != null && second.Paths[0].Target.Refresh.Numerator == 60000,
            "Two monitors with the same model ID must keep separate profiles.");
    }

    static DisplayPath NewPath() {
        return new DisplayPath {
            Source = new Source { Adapter = new Luid { Low = 42, High = 4 }, Id = 7, Mode = UInt32.MaxValue },
            Target = new Target { Adapter = new Luid { Low = 43, High = 5 }, Id = 19, Mode = UInt32.MaxValue, Available = 1, Technology = 10 }
        };
    }

    static Snapshot Fixture(uint refresh) {
        var adapter = new Luid { Low = 17, High = 2 };
        return new Snapshot {
            Paths = new[] { new DisplayPath {
                Source = new Source { Adapter = adapter, Id = 0, Mode = 1 },
                Target = new Target { Adapter = adapter, Id = 12, Mode = 0, Technology = 10, Rotation = 1, Scaling = 1,
                    Refresh = new Rational { Numerator = refresh, Denominator = 1000 }, Scanline = 1, Available = 1 },
                Flags = 1
            } },
            Modes = new[] {
                new Mode { Type = 2, Id = 12, Adapter = adapter, A = 1258750000, B = 0x00000aa04b070030,
                    C = 0x000003e800000000UL | refresh, D = 0x000005a000000a00, E = 0x0000067500000aa0, F = 0x00000001000000ff },
                new Mode { Type = 1, Id = 0, Adapter = adapter, A = 0x000005a000000a00, B = 4 }
            }
        };
    }

    static void WriteSnapshot(string file, Snapshot state) {
        using (var writer = new BinaryWriter(File.Create(file))) {
            writer.Write("ProjectorDisplay1");
            writer.Write(state.Paths.Length);
            writer.Write(state.Modes.Length);
            foreach (DisplayPath path in state.Paths) WriteStruct(writer, path);
            foreach (Mode mode in state.Modes) WriteStruct(writer, mode);
        }
    }

    static void WriteStruct<T>(BinaryWriter writer, T value) where T : struct {
        var bytes = new byte[Marshal.SizeOf(typeof(T))];
        IntPtr ptr = Marshal.AllocHGlobal(bytes.Length);
        try {
            Marshal.StructureToPtr(value, ptr, false);
            Marshal.Copy(ptr, bytes, 0, bytes.Length);
            writer.Write(bytes);
        } finally {
            Marshal.FreeHGlobal(ptr);
        }
    }

    static object Invoke(string name, params object[] arguments) {
        MethodInfo method = typeof(Config).GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
        Require(method != null, "Missing display mode behavior " + name + ".");
        return method.Invoke(null, arguments);
    }

    static void Require(bool condition, string message) {
        if (!condition) throw new InvalidOperationException(message);
    }
}
