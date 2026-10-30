using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using Windows.Media.Control;

partial class Companion {
    static string Arg(string[] args,string name,string fallback){int i=Array.IndexOf(args,name);return i>=0&&i+1<args.Length?args[i+1]:fallback;}
    [STAThread] static void Main(string[] args) {
        if(args.Length==0){args=FirstRun();if(args==null)return;}
        // Explicit opt-in data directory; never loads production files or the old bridge key.
        string dir=Arg(args,"--data",null);if(dir==null)return;
        dataDir=Path.GetFullPath(dir);Directory.CreateDirectory(dataDir);
        if(args.Contains("--pairing-self-test")){File.WriteAllText(Path.Combine(dataDir,"pairing-test.json"),J(new{passed=PairingSelfTest()}));return;}
        if(args.Contains("--self-test")){
            bool ok=AccountedElapsed(1,3.7)>2.69&&AccountedElapsed(1,60)==0&&AccountedElapsed(3,1)==0&&!ignored.IsMatch("antigravity");
            File.WriteAllText(Path.Combine(dataDir,"self-test.json"),J(new{passed=ok,checks=new[]{"elapsed rather than fixed seconds","suspend gaps excluded","clock reversal excluded","Antigravity tracked"}}));return;
        }
        int port=int.Parse(Arg(args,"--port","18765"));if(port<1024||port>65535||port==18764)return;
        readOnly=args.Contains("--read-only");
        int runSeconds=int.Parse(Arg(args,"--run-seconds","0"));
        bool first;
        // This mutex is deliberately independent of all installed TerminalDesk components.
        using(var mutex=new Mutex(true,"Local\\TerminalDeskNext-"+port,out first)){
            if(!first)return;
            try{
                ServicePointManager.SecurityProtocol=SecurityProtocolType.Tls12;
                string keyFile=Path.Combine(dataDir,"access.key");
                if(!File.Exists(keyFile)){var bytes=new byte[32];using(var rng=RandomNumberGenerator.Create())rng.GetBytes(bytes);File.WriteAllText(keyFile,BitConverter.ToString(bytes).Replace("-",""));}
                key=File.ReadAllText(keyFile).Trim();if(key.Length<32)return;
                LoadPairs();
                bootMs=NowMs()-(long)GetTickCount64();
                LoadUsage();lastAccount=clock.Elapsed.TotalSeconds;
                // Quote cache is bounded to a single pool. Boot time always comes from this boot.
                try{var q=new JavaScriptSerializer().Deserialize<System.Collections.Generic.Dictionary<string,object>>(File.ReadAllText(Path.Combine(dataDir,"quotes.json")));q["bootMs"]=bootMs;quotes=q;}catch{}
                // Read-only test mode never asks Windows for media control access.
                if(!readOnly)try{manager=Await(GlobalSystemMediaTransportControlsSessionManager.RequestAsync());}catch{}
                if(!readOnly || args.Contains("--pairing-ui")){
                    if(args.Contains("--tray"))StartTray();else StartPairingPrompts();
                }
                var listener=new TcpListener(IPAddress.Loopback,port);listener.Start(8);
                SampleTelemetry();
                Task.Run(()=>Worker(()=>{SampleUsage();return Task.FromResult(0);},2));
                Task.Run(()=>Worker(()=>{SampleTelemetry();return Task.FromResult(0);},2));
                Task.Run(()=>Worker(()=>{ReadBatteries();return Task.FromResult(0);},120));
                Task.Run(async ()=>{await NetworkWorker();});
                Task.Run(()=>Worker(()=>{SaveUsage();return Task.FromResult(0);},60));
                if(!args.Contains("--no-quotes"))Task.Run(()=>Worker(ReadQuotes,3600));
                using(var capacity=new SemaphoreSlim(4)){
                    DateTime deadline=runSeconds>0?DateTime.UtcNow.AddSeconds(runSeconds):DateTime.MaxValue;
                    while(DateTime.UtcNow<deadline && !shutdown.IsCancellationRequested){
                        if(!listener.Pending()){Thread.Sleep(50);continue;}
                        var client=listener.AcceptTcpClient();
                        if(!capacity.Wait(0)){client.Close();continue;}
                        Task.Run(()=>{try{Serve(client);}finally{capacity.Release();}});
                    }
                    listener.Stop();shutdown.Cancel();SaveUsage();
                    // Workers are cooperative and process-local; no external process is stopped.
                    Thread.Sleep(100);
                }
            }catch(Exception e){try{File.WriteAllText(Path.Combine(dataDir,"last-error.txt"),e.GetType().Name+": "+e.Message);}catch{}}
        }
    }
}

