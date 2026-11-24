using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Threading;

namespace ProjectorDisplay {
    [StructLayout(LayoutKind.Sequential)] public struct Luid { public uint Low; public int High; }
    [StructLayout(LayoutKind.Sequential)] public struct Rational { public uint Numerator, Denominator; }
    [StructLayout(LayoutKind.Sequential)] public struct Source {
        public Luid Adapter; public uint Id, Mode, Status;
    }
    [StructLayout(LayoutKind.Sequential)] public struct Target {
        public Luid Adapter; public uint Id, Mode, Technology, Rotation, Scaling;
        public Rational Refresh; public uint Scanline; public int Available; public uint Status;
    }
    [StructLayout(LayoutKind.Sequential)] public struct Path {
        public Source Source; public Target Target; public uint Flags;
    }
    [StructLayout(LayoutKind.Explicit, Size=64)] public struct Mode {
        [FieldOffset(0)] public uint Type;
        [FieldOffset(4)] public uint Id;
        [FieldOffset(8)] public Luid Adapter;
        // Retain all 48 bytes of the native mode union without interpreting its type.
        [FieldOffset(16)] public ulong A;
        [FieldOffset(24)] public ulong B;
        [FieldOffset(32)] public ulong C;
        [FieldOffset(40)] public ulong D;
        [FieldOffset(48)] public ulong E;
        [FieldOffset(56)] public ulong F;
    }
    [StructLayout(LayoutKind.Sequential)] public struct Header {
        public uint Type, Size; public Luid Adapter; public uint Id;
    }
    [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)] public struct TargetName {
        public Header Header; public uint Flags, Technology; public ushort Manufacturer, Product;
        public uint Connector;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst=64)] public string Friendly;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst=128)] public string DevicePath;
    }
    public sealed class Display {
        public string Name, DevicePath, Adapter;
        public uint TargetId, SourceId, Technology;
        public uint Width, Height, RefreshNumerator, RefreshDenominator;
        public bool Active, Available;
    }
    public sealed class Snapshot { public Path[] Paths; public Mode[] Modes; }

    [DataContract]
    sealed class StoredModeProfile {
        [DataMember] public string DevicePath;
        [DataMember] public string Snapshot;
    }

    public static class Config {
        const uint UseSuppliedDisplayConfig = 0x20, Validate = 0x40, Apply = 0x80;
        const uint SaveToDatabase = 0x200, AllowChanges = 0x400;
        static string modeStoreFile;
        static Dictionary<string, Snapshot> preferredModes = new Dictionary<string, Snapshot>(StringComparer.OrdinalIgnoreCase);
        static readonly HashSet<string> fallbackModeDevices = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        [DllImport("user32.dll")] static extern int GetDisplayConfigBufferSizes(uint flags, out uint paths, out uint modes);
        [DllImport("user32.dll")] static extern int QueryDisplayConfig(uint flags, ref uint paths, [Out] Path[] pathArray,
            ref uint modes, [Out] Mode[] modeArray, IntPtr topology);
        [DllImport("user32.dll")] static extern int DisplayConfigGetDeviceInfo(ref TargetName name);
        [DllImport("user32.dll")] static extern int SetDisplayConfig(uint paths, [In] Path[] pathArray,
            uint modes, [In] Mode[] modeArray, uint flags);

        static void Check(int result, string operation) {
            if(result != 0) throw new InvalidOperationException(operation + " returned Win32 error " + result);
        }
        public static Snapshot Query(bool activeOnly) {
            if(Marshal.SizeOf(typeof(Path)) != 72 || Marshal.SizeOf(typeof(Mode)) != 64 ||
                Marshal.SizeOf(typeof(TargetName)) != 420) throw new InvalidOperationException("Native layout mismatch");
            uint flags = activeOnly ? 2u : 1u;
            for(int attempt=0; attempt<5; attempt++) {
                uint paths, modes;
                Check(GetDisplayConfigBufferSizes(flags,out paths,out modes),"GetDisplayConfigBufferSizes");
                var p=new Path[paths]; var m=new Mode[modes];
                int code=QueryDisplayConfig(flags,ref paths,p,ref modes,m,IntPtr.Zero);
                if(code==122) continue;
                Check(code,"QueryDisplayConfig");
                Array.Resize(ref p,(int)paths); Array.Resize(ref m,(int)modes);
                return new Snapshot { Paths=p, Modes=m };
            }
            throw new InvalidOperationException("Displays are still changing; retry later");
        }
        static TargetName Name(Path path) {
            var name=new TargetName();
            name.Header.Type=2; name.Header.Size=(uint)Marshal.SizeOf(typeof(TargetName));
            name.Header.Adapter=path.Target.Adapter; name.Header.Id=path.Target.Id;
            int code=DisplayConfigGetDeviceInfo(ref name);
            if(code!=0) { name.Friendly="Unavailable"; name.DevicePath=""; }
            return name;
        }
        public static Display[] List() {
            var state = Query(false);
            return state.Paths.Select(p => {
                var name=Name(p);
                var display = new Display { Name=name.Friendly, DevicePath=name.DevicePath,
                    Adapter=p.Target.Adapter.High+":"+p.Target.Adapter.Low, TargetId=p.Target.Id,
                    SourceId=p.Source.Id, Technology=p.Target.Technology,
                    Active=(p.Flags&1)!=0, Available=p.Target.Available!=0 };
                Mode source, target;
                if (display.Active && TryGetModes(state, p, out source, out target)) {
                    // The source mode starts with the desktop width and height.
                    display.Width = (uint)source.A;
                    display.Height = (uint)(source.A >> 32);
                    // The target timing's vSyncFreq describes the output signal.
                    display.RefreshNumerator = (uint)target.C;
                    display.RefreshDenominator = (uint)(target.C >> 32);
                }
                return display;
            }).GroupBy(d=>d.Adapter+":"+d.TargetId).Select(g=>g.OrderByDescending(d=>d.Active).First()).ToArray();
        }

        static bool TryGetModes(Snapshot state, Path path, out Mode source, out Mode target) {
            source = target = default(Mode);
            if (path.Source.Mode >= state.Modes.Length || path.Target.Mode >= state.Modes.Length) return false;
            source = state.Modes[path.Source.Mode];
            target = state.Modes[path.Target.Mode];
            return source.Type == 1 && source.Id == path.Source.Id && source.Adapter.Equals(path.Source.Adapter) &&
                target.Type == 2 && target.Id == path.Target.Id && target.Adapter.Equals(path.Target.Adapter) &&
                (uint)source.A > 0 && source.A >> 32 > 0 && (uint)target.D > 0 && target.D >> 32 > 0 &&
                (uint)target.C > 0 && target.C >> 32 > 0;
        }

        static Snapshot BuildProfileSnapshot(Snapshot profile, Path path) {
            if (profile.Paths.Length != 1 || (profile.Paths[0].Flags & 1) == 0) return null;
            Path recorded = profile.Paths[0];
            Mode source, target;
            if (recorded.Target.Technology != path.Target.Technology ||
                !TryGetModes(profile, recorded, out source, out target)) return null;

            source.Adapter = path.Source.Adapter;
            source.Id = path.Source.Id;
            target.Adapter = path.Target.Adapter;
            target.Id = path.Target.Id;

            path.Source.Mode = 0;
            path.Target.Mode = 1;
            path.Target.Rotation = recorded.Target.Rotation;
            path.Target.Scaling = recorded.Target.Scaling;
            path.Target.Refresh = recorded.Target.Refresh;
            path.Target.Scanline = recorded.Target.Scanline;
            path.Flags = 1;
            return new Snapshot { Paths = new[] { path }, Modes = new[] { source, target } };
        }

        static uint PreferredModeFlags(bool apply, bool persist) {
            return UseSuppliedDisplayConfig | (apply ? Apply : Validate) | (apply && persist ? SaveToDatabase : 0);
        }

        static bool ApplyPreferredProfile(Path[] paths, bool persist) {
            Snapshot profile = FindPreferredMode(Name(paths[0]).DevicePath);
            return profile != null && TryApplyPreferredProfile(profile, paths, persist,
                (requested, flags) => SetDisplayConfig(1, requested.Paths, 2, requested.Modes, flags));
        }

        static bool TryApplyPreferredProfile(Snapshot profile, Path[] paths, bool persist, Func<Snapshot, uint, int> configure) {
            foreach (Path path in paths.OrderByDescending(p => (p.Flags & 1) != 0)
                .ThenByDescending(p => p.Source.Id == profile.Paths[0].Source.Id)) {
                Snapshot requested = BuildProfileSnapshot(profile, path);
                if (requested == null || configure(requested, PreferredModeFlags(false, false)) != 0)
                    continue;
                if (configure(requested, PreferredModeFlags(true, persist)) == 0) return true;
            }
            return false;
        }

        static bool MatchesMarker(Path path, string deviceMarker) {
            string devicePath = Name(path).DevicePath;
            return !String.IsNullOrEmpty(devicePath) && devicePath.IndexOf(deviceMarker, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        static Path[] FindPaths(string deviceMarker) {
            var matches = Query(false).Paths.Where(p => p.Target.Available != 0 && MatchesMarker(p, deviceMarker)).ToArray();
            var targets = matches.Select(p => p.Target.Adapter.High + ":" + p.Target.Adapter.Low + ":" + p.Target.Id).Distinct().ToArray();
            if (targets.Length != 1) throw new InvalidOperationException("Expected one connected display, found " + targets.Length);
            return matches;
        }

        public static void ForcePreferredMode(string deviceMarker, bool persist) {
            if (String.IsNullOrWhiteSpace(deviceMarker)) throw new ArgumentException("Exact monitor marker is required");
            if (!ApplyPreferredProfile(FindPaths(deviceMarker), persist))
                throw new InvalidOperationException("No preferred display mode could be validated and applied");
        }

        public static void ProjectorOnly(string deviceMarker, bool persist) {
            if(String.IsNullOrWhiteSpace(deviceMarker)) throw new ArgumentException("Exact monitor marker is required");
            var current=Query(true);
            if(current.Paths.Length==1 && current.Paths[0].Target.Available!=0 &&
                MatchesMarker(current.Paths[0], deviceMarker)) {
                // Preserve resolution, refresh, and scaling when the requested layout is already active.
                Check(SetDisplayConfig((uint)current.Paths.Length,current.Paths,(uint)current.Modes.Length,
                    current.Modes,PreferredModeFlags(false, false)),"Validate current projector layout");
                Check(SetDisplayConfig((uint)current.Paths.Length,current.Paths,(uint)current.Modes.Length,
                    current.Modes,PreferredModeFlags(true, persist)),"Apply current projector layout");
                return;
            }
            if (ApplyPreferredProfile(FindPaths(deviceMarker), persist)) return;
            // The topology may have changed while the explicit mode was being validated or applied.
            var path=FindPaths(deviceMarker).OrderByDescending(p=>(p.Flags&1)!=0).First();
            string devicePath = Name(path).DevicePath;
            path.Source.Mode=UInt32.MaxValue; path.Target.Mode=UInt32.MaxValue;
            path.Flags=1;
            var paths=new[]{path};
            // Ask Windows for modes for this exact physical target, validate before applying.
            Check(SetDisplayConfig(1,paths,0,null,PreferredModeFlags(false, false)|AllowChanges),"Validate projector-only");
            Check(SetDisplayConfig(1,paths,0,null,PreferredModeFlags(true, persist)|AllowChanges),"Apply projector-only");
            RecordFallbackMode(devicePath, persist);
        }

        public static void ConfigureModeStore(string file) {
            modeStoreFile = null;
            preferredModes = new Dictionary<string, Snapshot>(StringComparer.OrdinalIgnoreCase);
            string fullPath = System.IO.Path.GetFullPath(file);
            preferredModes = ReadModeStore(fullPath);
            modeStoreFile = fullPath;
        }

        static Snapshot FindPreferredMode(string devicePath) {
            Snapshot profile;
            return !String.IsNullOrEmpty(devicePath) && preferredModes.TryGetValue(devicePath, out profile) ? profile : null;
        }

        static void RecordFallbackMode(string devicePath, bool persist) {
            if (!persist && !String.IsNullOrEmpty(devicePath)) fallbackModeDevices.Add(devicePath);
        }

        static bool ShouldRememberMode(string devicePath) {
            // An automatic default mode is not a preference. Leave it unrecorded until a later process observes the screen.
            return !String.IsNullOrEmpty(devicePath) && FindPreferredMode(devicePath) == null && !fallbackModeDevices.Contains(devicePath);
        }

        public static void RememberCurrentMode() {
            if (modeStoreFile == null) return;
            Snapshot current = Query(true);
            if (current.Paths.Length != 1 || current.Paths[0].Target.Available == 0) return;
            Path path = current.Paths[0];
            string devicePath = Name(path).DevicePath;
            if (!ShouldRememberMode(devicePath)) return;
            Snapshot profile = BuildProfileSnapshot(current, path);
            if (profile != null) StoreModeProfile(devicePath, profile);
        }

        public static void SeedModeProfile(string snapshotFile, string exactDevicePath) {
            if (String.IsNullOrWhiteSpace(exactDevicePath)) throw new ArgumentException("Exact display device path is required");
            using (var reader = new BinaryReader(File.OpenRead(snapshotFile))) {
                Snapshot profile = ReadSnapshot(reader);
                if (profile.Paths.Length != 1 || BuildProfileSnapshot(profile, profile.Paths[0]) == null)
                    throw new InvalidDataException("The snapshot does not contain a complete single-display mode");
                StoreModeProfile(exactDevicePath, profile);
            }
        }

        static Dictionary<string, Snapshot> ReadModeStore(string file) {
            var entries = new Dictionary<string, Snapshot>(StringComparer.OrdinalIgnoreCase);
            if (!File.Exists(file)) return entries;
            var serializer = new DataContractJsonSerializer(typeof(StoredModeProfile[]));
            using (var stream = File.Open(file, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete)) {
                var records = (StoredModeProfile[])serializer.ReadObject(stream);
                if (records == null) throw new InvalidDataException("Missing display mode profiles");
                foreach (var item in records) {
                    if (item == null || String.IsNullOrWhiteSpace(item.DevicePath) || String.IsNullOrEmpty(item.Snapshot) ||
                        entries.ContainsKey(item.DevicePath)) throw new InvalidDataException("Invalid display mode profile");
                    using (var reader = new BinaryReader(new MemoryStream(Convert.FromBase64String(item.Snapshot)))) {
                        Snapshot profile = ReadSnapshot(reader);
                        if (profile.Paths.Length != 1 || BuildProfileSnapshot(profile, profile.Paths[0]) == null)
                            throw new InvalidDataException("Invalid display mode profile");
                        entries.Add(item.DevicePath, profile);
                    }
                }
            }
            return entries;
        }

        static void StoreModeProfile(string devicePath, Snapshot profile) {
            string file = modeStoreFile;
            if (file == null) throw new InvalidOperationException("No display mode store is configured");
            using (var writeLock = new Mutex(false, @"Local\CodexProjectorDisplayModeStore")) {
                try {
                    if (!writeLock.WaitOne(2000)) throw new IOException("Display mode store is busy");
                } catch (AbandonedMutexException) { }
                try {
                    var entries = ReadModeStore(file);
                    if (!entries.ContainsKey(devicePath)) {
                        entries.Add(devicePath, profile);
                        WriteModeStore(file, entries);
                    }
                    preferredModes = entries;
                } finally {
                    writeLock.ReleaseMutex();
                }
            }
        }

        static void WriteModeStore(string file, Dictionary<string, Snapshot> entries) {
            var records = entries.Select(entry => {
                using (var stream = new MemoryStream())
                using (var writer = new BinaryWriter(stream)) {
                    WriteSnapshot(writer, entry.Value);
                    return new StoredModeProfile { DevicePath = entry.Key, Snapshot = Convert.ToBase64String(stream.ToArray()) };
                }
            }).ToArray();
            string temp = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try {
                using (var stream = File.Open(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None)) {
                    new DataContractJsonSerializer(typeof(StoredModeProfile[])).WriteObject(stream, records);
                }
                if (File.Exists(file)) File.Replace(temp, file, null);
                else File.Move(temp, file);
            } finally {
                if (File.Exists(temp)) File.Delete(temp);
            }
        }
        static byte[] Bytes<T>(T value) where T:struct {
            int length=Marshal.SizeOf(typeof(T)); var buffer=new byte[length];
            IntPtr ptr=Marshal.AllocHGlobal(length);
            try { Marshal.StructureToPtr(value,ptr,false); Marshal.Copy(ptr,buffer,0,length); return buffer; }
            finally { Marshal.FreeHGlobal(ptr); }
        }
        static T Read<T>(BinaryReader reader) where T:struct {
            int length=Marshal.SizeOf(typeof(T)); byte[] buffer=reader.ReadBytes(length);
            if(buffer.Length!=length) throw new EndOfStreamException();
            IntPtr ptr=Marshal.AllocHGlobal(length);
            try { Marshal.Copy(buffer,0,ptr,length); return Marshal.PtrToStructure<T>(ptr); }
            finally { Marshal.FreeHGlobal(ptr); }
        }
        public static void Save(string file) {
            var state=Query(true);
            using(var writer=new BinaryWriter(File.Open(file,FileMode.CreateNew))) {
                WriteSnapshot(writer, state);
            }
        }
        static void WriteSnapshot(BinaryWriter writer, Snapshot state) {
            writer.Write("ProjectorDisplay1"); writer.Write(state.Paths.Length); writer.Write(state.Modes.Length);
            foreach(var path in state.Paths) writer.Write(Bytes(path));
            foreach(var mode in state.Modes) writer.Write(Bytes(mode));
        }
        static Snapshot ReadSnapshot(BinaryReader reader) {
            if(reader.ReadString()!="ProjectorDisplay1") throw new InvalidDataException("Snapshot type mismatch");
            int count=reader.ReadInt32(), modeCount=reader.ReadInt32();
            if(count<1||count>100||modeCount<0||modeCount>1000) throw new InvalidDataException("Invalid snapshot counts");
            var paths=new Path[count]; var modes=new Mode[modeCount];
            for(int i=0;i<count;i++) paths[i]=Read<Path>(reader);
            for(int i=0;i<modeCount;i++) modes[i]=Read<Mode>(reader);
            return new Snapshot { Paths = paths, Modes = modes };
        }
        public static void Restore(string file) {
            using(var reader=new BinaryReader(File.OpenRead(file))) {
                Snapshot state = ReadSnapshot(reader);
                Check(SetDisplayConfig((uint)state.Paths.Length,state.Paths,(uint)state.Modes.Length,state.Modes,
                    UseSuppliedDisplayConfig|Apply|SaveToDatabase|AllowChanges),"Restore display snapshot");
            }
        }
    }
}
