using System.Globalization;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

// Collectors share a process, not an execution loop. A slow provider cannot block accounting.
partial class Companion {
    static readonly object gate = new object();
    static readonly Dictionary<string,double> apps = new Dictionary<string,double>();
    static string day = DateTime.Now.ToString("yyyy-MM-dd"), activeApp = "Idle", dataDir;
    static object network, batteries, quotes;
    static bool readOnly;
    static long bootMs;
    static long lastViewerMs;
    static readonly Stopwatch clock = Stopwatch.StartNew();
    static double lastAccount;
    static CancellationTokenSource shutdown = new CancellationTokenSource();
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint id);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetWindowText(IntPtr h,StringBuilder b,int n);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr h,StringBuilder b,int n);
    [StructLayout(LayoutKind.Sequential)] struct LastInput { public uint size,time; }
    [DllImport("user32.dll")] static extern bool GetLastInputInfo(ref LastInput info);
    [DllImport("kernel32.dll")] static extern ulong GetTickCount64();
    static long NowMs() { return (long)(DateTime.UtcNow-new DateTime(1970,1,1)).TotalMilliseconds; }
    static readonly Regex ignored = new Regex("^(idle|system|lockapp|searchhost|shellexperiencehost|applicationframehost|taskmgr|snippingtool|cmd|powershell|pwsh|conhost|windowsterminal|wallpaper.*|steam|steamwebhelper|epicgameslauncher|epicwebhelper|riotclientservices|battlenet|agent|origin|eadesktop|link2ea|gog|galaxyclient)$",RegexOptions.IgnoreCase);
    static readonly Dictionary<string,string> friendly = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase) {
        {"antigravity","Antigravity"},{"cs2","Counter-Strike 2"},{"dota2","Dota 2"},{"deadlock","Deadlock"},{"valorant","Valorant"},
        {"r5apex","Apex Legends"},{"pubg","PUBG"},{"gta5","GTA V"},{"cyberpunk2077","Cyberpunk 2077"},{"witcher3","The Witcher 3"},
        {"minecraft","Minecraft"},{"genshinimpact","Genshin Impact"},{"starrail","Honkai: Star Rail"},{"zenlesszonezero","Zenless Zone Zero"},
        {"chrome","Chrome"},{"msedge","Edge"},{"firefox","Firefox"},{"opera","Opera"},{"brave","Brave"},{"yandex","Yandex"},
        {"code","VS Code"},{"telegram","Telegram"},{"discord","Discord"},{"spotify","Spotify"},{"vlc","VLC Media Player"},
        {"obs64","OBS Studio"},{"photoshop","Photoshop"},{"figma","Figma"},{"notion","Notion"},{"explorer","File Explorer"}
    };
    public static double AccountedElapsed(double previous, double current) {
        // Suspend/resume or a stalled scheduler is unobserved time, never assigned to the next foreground app.
        double delta = current-previous;
        return delta >= 0 && delta <= 5 ? delta : 0;
    }
    static void SampleUsage() {
        double now=clock.Elapsed.TotalSeconds, delta=AccountedElapsed(lastAccount,now); lastAccount=now;
        var input=new LastInput();input.size=(uint)Marshal.SizeOf(input);
        bool idle=!GetLastInputInfo(ref input) || unchecked((uint)Environment.TickCount-input.time)>120000;
        string name="Idle";
        if(!idle) {
            var h=GetForegroundWindow(); var title=new StringBuilder(256);var cls=new StringBuilder(256);
            GetWindowText(h,title,256);GetClassName(h,cls,256);
            if(h!=IntPtr.Zero && title.Length>0 && !new[]{"Progman","WorkerW","Shell_TrayWnd"}.Contains(cls.ToString())) {
                uint pid;GetWindowThreadProcessId(h,out pid);
                try { using(var p=Process.GetProcessById((int)pid)) { var raw=p.ProcessName;
                    if(!ignored.IsMatch(raw) && !raw.Equals("TerminalDeskNext",StringComparison.OrdinalIgnoreCase))
                        name=friendly.ContainsKey(raw)?friendly[raw]:raw;
                }} catch {}
            }
        }
        lock(gate) {
            string date=DateTime.Now.ToString("yyyy-MM-dd");
            if(day!=date){day=date;apps.Clear();delta=0;}
            // Credit only a continuously observed foreground application, not a just-switched window.
            if(name!="Idle" && name==activeApp && delta>0) { if(!apps.ContainsKey(name))apps[name]=0;apps[name]+=delta; }
            activeApp=name;
        }
    }
    static object UsageSnapshot() {
        lock(gate) {
            double total=apps.Values.Sum();
            var top=apps.OrderByDescending(x=>x.Value).Take(3).Select(x=>new Dictionary<string,object>{{"name",x.Key},{"seconds",(int)x.Value},{"percent",total>0?Math.Round(x.Value/total*100):0}}).ToList();
            double other=total-apps.OrderByDescending(x=>x.Value).Take(3).Sum(x=>x.Value);
            if(other>60) top.Add(new Dictionary<string,object>{{"name","Other"},{"seconds",(int)other},{"percent",total>0?Math.Round(other/total*100):0}});
            return new {date=day,totalSeconds=(int)total,activeApp=activeApp,apps=new Dictionary<string,double>(apps),topApps=top,network=network,batteries=batteries,updatedMs=NowMs(),schemaVersion=1,companion=quotes,bootMs=bootMs,telemetry=TelemetrySnapshot()};
        }
    }
    static string DashboardJson(){Interlocked.Exchange(ref lastViewerMs,NowMs());return J(UsageSnapshot());}
    static void AtomicWrite(string name,string text) {
        string path=Path.Combine(dataDir,name),temp=path+".tmp";
        File.WriteAllText(temp,text,new UTF8Encoding(false));
        if(File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path);
    }
    static void SaveUsage(){lock(gate){AtomicWrite("usage.json",J(new{date=day,apps=apps}));}}
    static void LoadUsage(){try{
        var state=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(File.ReadAllText(Path.Combine(dataDir,"usage.json")));
        if(Convert.ToString(state["date"])!=day)return;
        foreach(var p in (Dictionary<string,object>)state["apps"]){double n=Convert.ToDouble(p.Value);if(n>=0 && n<=86400)apps[p.Key]=n;}
    }catch{}}
    static void ReadBatteries() {
        var readings = DesktopBatteryReader.Get();
        Dictionary<string, string> config = null;
        try {
            string cfgPath = Path.Combine(dataDir, "devices.json");
            if (File.Exists(cfgPath)) {
                config = new JavaScriptSerializer().Deserialize<Dictionary<string, string>>(File.ReadAllText(cfgPath));
            }
        } catch {}

        var slots = new[] {
            new[] { "headphones", "Headphones", "(headphone|headset|earphone|earbuds|buds|airpods|audio|hands-free|sound|tune|wh-|wf-|jbl|bose|sony)" },
            new[] { "mouse", "Mouse", "(mouse|trackball|touchpad|pointer|mx master|razer|logitech g|x3)" },
            new[] { "keyboard", "Keyboard", "(keyboard|kb\\b|keychron|aula|corsair|nuphy|filco|ducky)" }
        };

        var devices = new List<object>();
        foreach (var spec in slots) {
            string slotId = spec[0], slotName = spec[1];
            string pattern = (config != null && config.ContainsKey(slotId) && !string.IsNullOrEmpty(config[slotId]))
                ? config[slotId]
                : spec[2];

            DesktopBatteryReader.Reading item = null;
            try {
                item = readings.FirstOrDefault(r => Regex.IsMatch(r.name, pattern, RegexOptions.IgnoreCase));
            } catch {
                item = readings.FirstOrDefault(r => r.name.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0);
            }

            devices.Add(new {
                id = slotId,
                name = slotName,
                deviceName = item != null ? item.name : (string)null,
                percent = item != null ? (int?)item.percent : (int?)null,
                charging = (bool?)null,
                status = item != null ? "ok" : "unavailable"
            });
        }

        var detected = readings.Select(r => new { name = r.name, percent = (int?)r.percent }).ToArray();
        lock (gate) batteries = new {
            readMs = NowMs(),
            devices = devices,
            detectedDevices = detected
        };
    }
    class Relay { public string code,name,address; public int? latencyMs; }
    static List<Relay> selected=new List<Relay>();
    static DateTime scanAt=DateTime.MinValue;
    static async Task<List<Relay>> Probe(IEnumerable<Relay> targets) {
        using(var parallel=new SemaphoreSlim(12)) {
            var tasks=targets.Select(async r=>{await parallel.WaitAsync();try{int? ms=null;using(var ping=new Ping()) {try{var reply=await ping.SendPingAsync(r.address,900);if(reply.Status==IPStatus.Success)ms=(int)reply.RoundtripTime;}catch{}}
                return new Relay{code=r.code,name=r.name,address=r.address,latencyMs=ms};}finally{parallel.Release();}}).ToArray();
            return (await Task.WhenAll(tasks)).ToList();
        }
    }
    static async Task ReadNetwork() {
        if((DateTime.UtcNow-scanAt).TotalMinutes>=(selected.Count==0?1:30)){
            scanAt=DateTime.UtcNow;
            string cache=Path.Combine(dataDir,"valve-sdr.json");
            if(!File.Exists(cache)||(DateTime.UtcNow-File.GetLastWriteTimeUtc(cache)).TotalHours>=24){
                try { using(var client=new TimedWebClient()){string text=await client.DownloadStringTaskAsync("https://api.steampowered.com/ISteamApps/GetSDRConfig/v1/?appid=730");
                    if(text.Length<1048576 && text.Contains("\"pops\""))AtomicWrite("valve-sdr.json",text);
                }}catch{}
            }
            try{
                var directory=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(File.ReadAllText(cache));
                var targets=new List<Relay>();
                foreach(var pop in (Dictionary<string,object>)directory["pops"]){var value=(Dictionary<string,object>)pop.Value;if(!value.ContainsKey("relays"))continue;
                    foreach(var raw in ((IEnumerable)value["relays"]).Cast<object>().Take(2)){var r=(Dictionary<string,object>)raw;if(r.ContainsKey("ipv4"))targets.Add(new Relay{code=pop.Key,name=Convert.ToString(value["desc"]),address=Convert.ToString(r["ipv4"])});}}
                selected=(await Probe(targets)).Where(r=>r.latencyMs.HasValue).GroupBy(r=>r.code).Select(g=>g.OrderBy(r=>r.latencyMs).First()).OrderBy(r=>r.latencyMs).Take(6).ToList();
            }catch(Exception error){AtomicWrite("network-error.txt",error.GetType().Name+": "+error.Message);}
        }
        var cf=(await Probe(new[]{new Relay{code="cf",address="1.1.1.1"}}))[0];
        cfHistory.Enqueue(cf.latencyMs);while(cfHistory.Count>24)cfHistory.Dequeue();
        var vals=cfHistory.ToArray();var diffs=new List<int>();for(int i=1;i<vals.Length;i++)if(vals[i].HasValue&&vals[i-1].HasValue)diffs.Add(Math.Abs(vals[i].Value-vals[i-1].Value));
        var rounds=new List<Relay>();for(int i=0;i<3;i++){rounds.AddRange(await Probe(selected));if(i<2)await Task.Delay(100);}
        var results=selected.Select(r=>{var replies=rounds.Where(x=>x.address==r.address&&x.latencyMs.HasValue).Select(x=>x.latencyMs.Value).OrderBy(x=>x).ToArray();return new{code=r.code,name=r.name,address=r.address,latencyMs=replies.Length>0?(int?)replies[replies.Length/2]:null,replies=replies.Length,probes=3,method="ICMP"};}).OrderBy(r=>r.latencyMs??int.MaxValue).Take(3).ToArray();
        lock(gate)network=new{schemaVersion=2,measuredMs=NowMs(),cloudflare=new{address="1.1.1.1",latencyMs=cf.latencyMs,lossPercent=Math.Round(100.0*vals.Count(x=>!x.HasValue)/vals.Length,1),jitterMs=diffs.Count>0?(double?)Math.Round(diffs.Average(),1):null,samples=vals.Length},valveRelays=results,valveMethod="ICMP"};
    }
    static Queue<int?> cfHistory=new Queue<int?>();
    class TimedWebClient:WebClient { protected override WebRequest GetWebRequest(Uri address){var r=base.GetWebRequest(address);r.Timeout=6000;return r;} }
    static string Plain(string text){return Regex.Replace(WebUtility.HtmlDecode(Regex.Replace(text,"<[^>]+>"," ")),@"\s+"," ").Trim();}
    static async Task ReadQuotes(){
        var pool=new List<Dictionary<string,object>>();
        foreach(var path in new[]{"/game","/anime","/category/citaty-so-smyslom","/category/motiviruyushchie-citaty","/category/mudrye-citaty","/category/ironichnye-citaty"}){
            try{using(var client=new TimedWebClient()){client.Encoding=Encoding.UTF8;string html=await client.DownloadStringTaskAsync("https://citaty.info"+path);
                int count=0;foreach(Match article in Regex.Matches(html,"<article\\b[^>]*id=\"node-(\\d+)\".*?</article>",RegexOptions.Singleline|RegexOptions.IgnoreCase)){
                    var body=Regex.Match(article.Value,"<div class=\"field-name-body\">(.*?)<div class=\"actions\">",RegexOptions.Singleline);string text=Plain(body.Groups[1].Value);
                    if(text.Length<18||text.Length>320||!Regex.IsMatch(text,"[А-Яа-яЁё]"))continue;
                    string author=Plain(Regex.Match(article.Value,"<a\\b[^>]*title=\"(?:Автор цитаты|Цитируемый персонаж)\"[^>]*>(.*?)</a>",RegexOptions.Singleline).Groups[1].Value);
                    string work=Plain(Regex.Match(article.Value,"<a\\b[^>]*title=\"Цитата из [^\"]+\"[^>]*>(.*?)</a>",RegexOptions.Singleline).Groups[1].Value);
                    if(author==""&&work=="")continue;
                    pool.Add(new Dictionary<string,object>{{"text",text},{"author",author},{"work",work},{"url","https://citaty.info/quote/"+article.Groups[1].Value},{"language","ru"}});if(++count>=6)break;
                }
            }}catch{}
        }
        if(pool.Count>0){var choice=new Dictionary<string,object>(pool[new Random().Next(pool.Count)]);choice["fetchedMs"]=NowMs();var result=new{bootMs=bootMs,quote=choice,quotePool=pool};lock(gate)quotes=result;AtomicWrite("quotes.json",J(result));}
    }
    static async Task Worker(Func<Task> action,int seconds){while(!shutdown.IsCancellationRequested){try{await action();}catch{}try{await Task.Delay(seconds*1000,shutdown.Token);}catch{break;}}}
    static async Task NetworkWorker(){while(!shutdown.IsCancellationRequested){try{await ReadNetwork();}catch{}int seconds=NowMs()-Interlocked.Read(ref lastViewerMs)<20000?15:60;try{await Task.Delay(seconds*1000,shutdown.Token);}catch{break;}}}

    [DllImport("kernel32.dll", SetLastError=true)]
    static extern bool GetSystemTimes(out System.Runtime.InteropServices.ComTypes.FILETIME idleTime, out System.Runtime.InteropServices.ComTypes.FILETIME kernelTime, out System.Runtime.InteropServices.ComTypes.FILETIME userTime);

    [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Auto)]
    class MEMORYSTATUSEX {
        public uint dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }
    [DllImport("kernel32.dll", CharSet=CharSet.Auto, SetLastError=true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX lpBuffer);

    static ulong FileTimeToUl(System.Runtime.InteropServices.ComTypes.FILETIME ft) {
        return ((ulong)(uint)ft.dwHighDateTime << 32) | (uint)ft.dwLowDateTime;
    }

    static ulong prevSysIdle, prevSysKern, prevSysUser;
    static double curCpuPercent;
    static long prevNetRx, prevNetTx;
    static double prevNetTime;
    static double curNetDlKb, curNetUpKb;
    static readonly object telemetryLock = new object();
    static object latestTelemetry;

    public static void SampleTelemetry() {
        System.Runtime.InteropServices.ComTypes.FILETIME idle, kern, user;
        if (GetSystemTimes(out idle, out kern, out user)) {
            ulong curIdle = FileTimeToUl(idle);
            ulong curKern = FileTimeToUl(kern);
            ulong curUser = FileTimeToUl(user);
            if (prevSysKern > 0 || prevSysUser > 0) {
                ulong dIdle = curIdle - prevSysIdle;
                ulong dKern = curKern - prevSysKern;
                ulong dUser = curUser - prevSysUser;
                ulong dTotal = dKern + dUser;
                if (dTotal > 0) {
                    double pct = (double)(dTotal - dIdle) * 100.0 / dTotal;
                    curCpuPercent = Math.Max(0.0, Math.Min(100.0, Math.Round(pct, 1)));
                }
            }
            prevSysIdle = curIdle;
            prevSysKern = curKern;
            prevSysUser = curUser;
        }

        double nowSec = clock.Elapsed.TotalSeconds;
        long rx = 0, tx = 0;
        try {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces()) {
                if (ni.OperationalStatus == OperationalStatus.Up && ni.NetworkInterfaceType != NetworkInterfaceType.Loopback) {
                    try {
                        var stats = ni.GetIPv4Statistics();
                        rx += stats.BytesReceived;
                        tx += stats.BytesSent;
                    } catch {}
                }
            }
            if (prevNetTime > 0) {
                double dt = nowSec - prevNetTime;
                if (dt >= 0.5 && dt <= 10.0) {
                    curNetDlKb = Math.Max(0.0, (rx - prevNetRx) / (1024.0 * dt));
                    curNetUpKb = Math.Max(0.0, (tx - prevNetTx) / (1024.0 * dt));
                }
            }
            prevNetRx = rx;
            prevNetTx = tx;
            prevNetTime = nowSec;
        } catch {}

        int memPercent = 0;
        string memGb = "0.0/0.0";
        try {
            var mem = new MEMORYSTATUSEX();
            if (GlobalMemoryStatusEx(mem)) {
                memPercent = (int)mem.dwMemoryLoad;
                double tGb = mem.ullTotalPhys / 1073741824.0;
                double uGb = (mem.ullTotalPhys - mem.ullAvailPhys) / 1073741824.0;
                memGb = string.Format(CultureInfo.InvariantCulture, "{0:F1}/{1:F1}", uGb, tGb);
            }
        } catch {}

        string cDisk = null, dDisk = null;
        try {
            var c = new DriveInfo("C");
            if (c.IsReady) {
                double total = c.TotalSize / 1073741824.0;
                double used = (c.TotalSize - c.AvailableFreeSpace) / 1073741824.0;
                cDisk = string.Format(CultureInfo.InvariantCulture, "{0:F1}/{1:F1}", used, total);
            }
        } catch {}
        try {
            var d = new DriveInfo("D");
            if (d.IsReady) {
                double total = d.TotalSize / 1073741824.0;
                double used = (d.TotalSize - d.AvailableFreeSpace) / 1073741824.0;
                dDisk = string.Format(CultureInfo.InvariantCulture, "{0:F1}/{1:F1}", used, total);
            }
        } catch {}

        var psutil = new Dictionary<string, object> {
            { "cpu", curCpuPercent },
            { "memory", memPercent },
            { "memory_gb", memGb },
            { "download_speed", Math.Round(curNetDlKb, 1) },
            { "upload_speed", Math.Round(curNetUpKb, 1) }
        };
        if (cDisk != null) psutil["c_disk"] = cDisk;
        if (dDisk != null) psutil["d_disk"] = dDisk;

        lock (telemetryLock) {
            latestTelemetry = new {
                psutil = psutil,
                hwinfo = new object[0],
                updatedMs = NowMs()
            };
        }
    }

    public static object TelemetrySnapshot() {
        lock (telemetryLock) {
            if (latestTelemetry == null) SampleTelemetry();
            return latestTelemetry;
        }
    }

    public static string PerformanceJson() {
        return J(TelemetrySnapshot());
    }

}
