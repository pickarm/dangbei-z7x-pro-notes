using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace ProjectorDisplay {
    public sealed class RouterSettings {
        public string key,device,monitorMarker,projectorMarker,laptopMarker;
        public int port=45383,computerSource=-999,systemSource=0;
    }
    public sealed class LinkState {
        public string endpoint,playing; public int source;
    }
    public static class DisplayRouter {
        static readonly JavaScriptSerializer Json=new JavaScriptSerializer();
        static readonly string DataDir=File.Exists(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"router-settings.json"))?
            AppDomain.CurrentDomain.BaseDirectory:System.IO.Path.GetFullPath(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"..","work","pc-display"));
        static readonly string SettingsFile=System.IO.Path.Combine(DataDir,"router-settings.json");
        static readonly string StatusFile=System.IO.Path.Combine(DataDir,"router-status.json");
        static readonly string LogFile=System.IO.Path.Combine(DataDir,"router.log");
        const string MutexName=@"Local\CodexProjectorDisplayRouter";
        const string StopName=@"Local\CodexProjectorDisplayRouterStop";
        static RouterSettings Settings;
        static byte[] Key;
        static UdpClient Client;
        static IPEndPoint Endpoint;
        static string LastLog="";

        static void Log(string text) {
            if(text==LastLog)return;LastLog=text;
            try {
                if(File.Exists(LogFile)&&new FileInfo(LogFile).Length>524288)File.Move(LogFile,LogFile+".old."+DateTime.UtcNow.Ticks);
                File.AppendAllText(LogFile,DateTime.Now.ToString("o")+" "+text+Environment.NewLine,Encoding.UTF8);
            }catch(IOException){}
        }
        static string Mac(string text) {
            using(var hmac=new HMACSHA256(Key))return BitConverter.ToString(hmac.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-","").ToLowerInvariant();
        }
        static bool Equal(string a,string b) {
            if(a==null||b==null||a.Length!=b.Length)return false;
            int diff=0;for(int i=0;i<a.Length;i++)diff|=a[i]^b[i];return diff==0;
        }
        static IEnumerable<IPAddress> Broadcasts() {
            var result=new List<IPAddress>();
            foreach(var nic in NetworkInterface.GetAllNetworkInterfaces()) {
                if(nic.OperationalStatus!=OperationalStatus.Up ||
                   (nic.NetworkInterfaceType!=NetworkInterfaceType.Ethernet&&nic.NetworkInterfaceType!=NetworkInterfaceType.Wireless80211))continue;
                var description=(nic.Name+" "+nic.Description).ToLowerInvariant();
                if(new[]{"hyper-v","virtual","vmware","wintun","clash","loopback"}.Any(description.Contains))continue;
                foreach(var address in nic.GetIPProperties().UnicastAddresses) {
                    if(address.Address.AddressFamily!=AddressFamily.InterNetwork||address.IPv4Mask==null)continue;
                    byte[] ip=address.Address.GetAddressBytes(),mask=address.IPv4Mask.GetAddressBytes();
                    if(!(ip[0]==10||ip[0]==192&&ip[1]==168||ip[0]==172&&ip[1]>=16&&ip[1]<=31))continue;
                    byte[] broadcast=new byte[4];for(int i=0;i<4;i++)broadcast[i]=(byte)(ip[i]|(mask[i]^255));
                    result.Add(new IPAddress(broadcast));
                }
            }
            return result.Distinct();
        }
        static void Discover(byte[] request) {
            foreach(var address in Broadcasts()) {
                try{Client.Send(request,request.Length,new IPEndPoint(address,Settings.port));}catch(SocketException){}
            }
        }
        static LinkState Receive(string nonce,int milliseconds) {
            var timer=Stopwatch.StartNew();int count=0;
            while(timer.ElapsedMilliseconds<milliseconds&&count<32) {
                if(Client.Available==0){Thread.Sleep(20);continue;}
                IPEndPoint sender=new IPEndPoint(IPAddress.Any,0);
                byte[] bytes;
                try{bytes=Client.Receive(ref sender);}catch(SocketException){return null;}
                count++;
                if(bytes.Length>2048||sender.Port!=Settings.port)continue;
                try {
                    var reply=Json.Deserialize<Dictionary<string,object>>(Encoding.UTF8.GetString(bytes));
                    if(Convert.ToInt32(reply["v"])!=1||(string)reply["device"]!=Settings.device||(string)reply["nonce"]!=nonce)continue;
                    string playing=(string)reply["playing"];int source=Convert.ToInt32(reply["source"]);
                    if(playing!="0"&&playing!="1"&&playing!="unknown"&&playing!="disabled")continue;
                    string expected=Mac("state|"+nonce+"|"+Settings.device+"|"+playing+"|"+source.ToString(CultureInfo.InvariantCulture));
                    if(!Equal(expected,(string)reply["mac"]))continue;
                    Endpoint=sender;
                    return new LinkState{endpoint=sender.ToString(),playing=playing,source=source};
                }catch(Exception){/* Ignore unpaired, stale, and malformed responses. */}
            }
            return null;
        }
        static LinkState Query() {
            // Every reply must answer a new challenge; unsolicited status cannot change a display.
            string nonce=Guid.NewGuid().ToString("N");
            byte[] query=Encoding.UTF8.GetBytes(Json.Serialize(new{v=1,nonce=nonce,mac=Mac("query|"+nonce)}));
            bool unicast=Endpoint!=null;
            if(unicast) {
                try{Client.Send(query,query.Length,Endpoint);}
                catch(SocketException){Endpoint=null;unicast=false;Discover(query);}
            }else Discover(query);
            var state=Receive(nonce,1000);
            if(state==null&&unicast){Endpoint=null;Discover(query);state=Receive(nonce,1000);}
            return state;
        }
        static bool Matches(Display display,string marker){return display.DevicePath!=null&&display.DevicePath.IndexOf(marker,StringComparison.OrdinalIgnoreCase)>=0;}
        static string Desired(LinkState state) {
            if(state==null)return "unknown";
            if(state.playing=="disabled")return "paused";
            if(state.playing=="0"&&state.source==Settings.systemSource)return "monitor";
            if(Settings.computerSource!=-999&&state.source==Settings.computerSource&&
               (state.playing=="1"||Settings.computerSource!=Settings.systemSource))return "projector";
            if(state.playing=="0")return "monitor";
            if(Settings.computerSource!=-999&&state.source!=Settings.computerSource)return "monitor";
            return "uncalibrated";
        }
        static void WriteStatus(LinkState state,string desired,string outcome) {
            var displays=Config.List().Where(d=>d.Available||d.Active).ToArray();
            File.WriteAllText(StatusFile,Json.Serialize(new{time=DateTime.Now.ToString("o"),
                link=state,desired=desired,outcome=outcome,displays=displays}),Encoding.UTF8);
        }
        static string Apply(string desired) {
            var displays=Config.List();
            string marker=desired=="projector"?Settings.projectorMarker:Settings.monitorMarker;
            var available=displays.Where(d=>d.Available&&Matches(d,marker)).ToArray();
            if(available.Length!=1&&desired=="projector") {
                marker=Settings.monitorMarker;available=displays.Where(d=>d.Available&&Matches(d,marker)).ToArray();
            }
            if(available.Length!=1&&desired=="monitor") {
                marker=Settings.laptopMarker;available=displays.Where(d=>d.Available&&Matches(d,marker)).ToArray();
            }
            if(available.Length!=1)return "kept-no-unique-target";
            var active=displays.Where(d=>d.Active).ToArray();
            if(active.Length==1&&Matches(active[0],marker))return "already-correct";
            Config.ProjectorOnly(marker,true);
            active=Config.List().Where(d=>d.Active).ToArray();
            if(active.Length!=1||!Matches(active[0],marker))throw new InvalidOperationException("Display readback mismatch");
            return "switched:"+(marker==Settings.projectorMarker?"projector":marker==Settings.monitorMarker?"monitor":"laptop");
        }
        [STAThread] public static int Main(string[] args) {
            if(args.Contains("--stop")) {
                try{using(var stop=EventWaitHandle.OpenExisting(StopName))stop.Set();}catch(WaitHandleCannotBeOpenedException){}
                return 0;
            }
            bool probe=args.Contains("--probe");bool once=args.Contains("--once");
            try {
                Settings=Json.Deserialize<RouterSettings>(File.ReadAllText(SettingsFile,Encoding.UTF8));
                Key=Convert.FromBase64String(Settings.key);if(Key.Length!=32)throw new InvalidOperationException("Invalid pairing");
                if(args.Contains("--monitor-only")){string outcome=Apply("monitor");WriteStatus(null,"monitor",outcome);return 0;}
                using(Client=new UdpClient(new IPEndPoint(IPAddress.Any,0))) {
                    Client.EnableBroadcast=true;
                    if(probe){var state=Query();WriteStatus(state,Desired(state),"probe-only");return state==null?2:0;}
                    bool created;
                    using(var mutex=new Mutex(true,MutexName,out created)) {
                        if(!created)return 0;
                        using(var stop=new EventWaitHandle(false,EventResetMode.ManualReset,StopName)) {
                            stop.Reset();Log("Started LAN display routing; no ADB dependency");
                            string candidate="";int stable=0;DateTime lastGood=DateTime.UtcNow;
                            while(!stop.WaitOne(0)) {
                                try {
                                    var state=Query();string desired=Desired(state);
                                    if(state!=null)lastGood=DateTime.UtcNow;
                                    if(desired=="unknown"&&(DateTime.UtcNow-lastGood).TotalSeconds>=15)desired="monitor";
                                    if(desired==candidate)stable++;else{candidate=desired;stable=1;}
                                    string outcome="waiting-for-stable-source";
                                    if((desired=="monitor"||desired=="projector")&&(stable>=2||once))outcome=Apply(desired);
                                    WriteStatus(state,desired,outcome);
                                    Log(desired+" "+outcome+(state==null?"":" source="+state.playing+":"+state.source));
                                    if(once)return 0;
                                }catch(Exception error){Log("Retrying after "+error.GetType().Name+": "+error.Message);}
                                if(stop.WaitOne(2000))break;
                            }
                            Log("Stopped display routing");
                        }
                    }
                }
                return 0;
            }catch(Exception error){Log("Startup failed: "+error.Message);return 1;}
        }
    }
}
