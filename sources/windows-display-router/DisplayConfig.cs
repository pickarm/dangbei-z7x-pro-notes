using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

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
        public bool Active, Available;
    }
    public sealed class Snapshot { public Path[] Paths; public Mode[] Modes; }

    public static class Config {
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
            return Query(false).Paths.Select(p => {
                var name=Name(p);
                return new Display { Name=name.Friendly, DevicePath=name.DevicePath,
                    Adapter=p.Target.Adapter.High+":"+p.Target.Adapter.Low, TargetId=p.Target.Id,
                    SourceId=p.Source.Id, Technology=p.Target.Technology,
                    Active=(p.Flags&1)!=0, Available=p.Target.Available!=0 };
            }).GroupBy(d=>d.Adapter+":"+d.TargetId).Select(g=>g.OrderByDescending(d=>d.Active).First()).ToArray();
        }
        public static void ProjectorOnly(string deviceMarker, bool persist) {
            if(String.IsNullOrWhiteSpace(deviceMarker)) throw new ArgumentException("Exact monitor marker is required");
            var current=Query(true);
            if(current.Paths.Length==1 && current.Paths[0].Target.Available!=0 &&
                Name(current.Paths[0]).DevicePath.IndexOf(deviceMarker,StringComparison.OrdinalIgnoreCase)>=0) {
                // Preserve resolution, refresh, and scaling when the requested layout is already active.
                Check(SetDisplayConfig((uint)current.Paths.Length,current.Paths,(uint)current.Modes.Length,
                    current.Modes,0x20|0x40),"Validate current projector layout");
                Check(SetDisplayConfig((uint)current.Paths.Length,current.Paths,(uint)current.Modes.Length,
                    current.Modes,0x20|0x80|(persist?0x200u:0u)),"Save current projector layout");
                return;
            }
            var matches=Query(false).Paths.Where(p => p.Target.Available!=0 &&
                Name(p).DevicePath.IndexOf(deviceMarker,StringComparison.OrdinalIgnoreCase)>=0).ToArray();
            var targets=matches.Select(p=>p.Target.Adapter.High+":"+p.Target.Adapter.Low+":"+p.Target.Id).Distinct().ToArray();
            if(targets.Length!=1) throw new InvalidOperationException("Expected one connected projector, found "+targets.Length);
            var path=matches.OrderByDescending(p=>(p.Flags&1)!=0).First();
            path.Source.Mode=UInt32.MaxValue; path.Target.Mode=UInt32.MaxValue;
            path.Flags=1;
            var paths=new[]{path};
            // Ask Windows for modes for this exact physical target, validate before applying.
            Check(SetDisplayConfig(1,paths,0,null,0x20|0x40|0x400),"Validate projector-only");
            Check(SetDisplayConfig(1,paths,0,null,0x20|0x80|0x400|(persist?0x200u:0u)),"Apply projector-only");
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
                writer.Write("ProjectorDisplay1"); writer.Write(state.Paths.Length); writer.Write(state.Modes.Length);
                foreach(var path in state.Paths) writer.Write(Bytes(path));
                foreach(var mode in state.Modes) writer.Write(Bytes(mode));
            }
        }
        public static void Restore(string file) {
            using(var reader=new BinaryReader(File.OpenRead(file))) {
                if(reader.ReadString()!="ProjectorDisplay1") throw new InvalidDataException("Snapshot type mismatch");
                int count=reader.ReadInt32(), modeCount=reader.ReadInt32();
                if(count<1||count>100||modeCount<0||modeCount>1000) throw new InvalidDataException("Invalid snapshot counts");
                var paths=new Path[count]; var modes=new Mode[modeCount];
                for(int i=0;i<count;i++) paths[i]=Read<Path>(reader);
                for(int i=0;i<modeCount;i++) modes[i]=Read<Mode>(reader);
                Check(SetDisplayConfig((uint)count,paths,(uint)modeCount,modes,0x20|0x80|0x200|0x400),"Restore display snapshot");
            }
        }
    }
}
