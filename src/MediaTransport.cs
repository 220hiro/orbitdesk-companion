using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using Windows.Media.Control;

[ComImport]
[Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
internal class MMDeviceEnumeratorComObject { }

[Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDeviceEnumerator {
    int NotImpl1();
    [PreserveSig]
    int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice ppDevice);
}

[Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDevice {
    [PreserveSig]
    int Activate(ref Guid iid, int dwClsCtx, IntPtr pActivationParams, [MarshalAs(UnmanagedType.IUnknown)] out object ppInterface);
}

[Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioEndpointVolume {
    [PreserveSig] int RegisterControlChangeNotify(IntPtr pNotify);
    [PreserveSig] int UnregisterControlChangeNotify(IntPtr pNotify);
    [PreserveSig] int GetChannelCount(out uint pnChannelCount);
    [PreserveSig] int SetMasterVolumeLevel(float fLevelDB, ref Guid pguidEventContext);
    [PreserveSig] int SetMasterVolumeLevelScalar(float fLevel, ref Guid pguidEventContext);
    [PreserveSig] int GetMasterVolumeLevel(out float pfLevelDB);
    [PreserveSig] int GetMasterVolumeLevelScalar(out float pfLevel);
    [PreserveSig] int SetChannelVolumeLevel(uint nChannel, float fLevelDB, ref Guid pguidEventContext);
    [PreserveSig] int SetChannelVolumeLevelScalar(uint nChannel, float fLevel, ref Guid pguidEventContext);
    [PreserveSig] int GetChannelVolumeLevel(uint nChannel, out float pfLevelDB);
    [PreserveSig] int GetChannelVolumeLevelScalar(uint nChannel, out float pfLevel);
    [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool bMute, ref Guid pguidEventContext);
    [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool pbMute);
}

partial class Companion {
    static GlobalSystemMediaTransportControlsSessionManager manager;
    static string key;

    static bool WithAudio(Func<IAudioEndpointVolume,bool> action) {
        object enumerator=null, device=null, endpoint=null;
        try {
            enumerator=new MMDeviceEnumeratorComObject(); IMMDevice dev;
            if(((IMMDeviceEnumerator)enumerator).GetDefaultAudioEndpoint(0,1,out dev)!=0||dev==null)return false;
            device=dev;var iid=new Guid("5CDF2C82-841E-4546-9722-0CF74078229A");
            if(dev.Activate(ref iid,1,IntPtr.Zero,out endpoint)!=0||endpoint==null)return false;
            return action((IAudioEndpointVolume)endpoint);
        } catch {return false;}
        finally {foreach(var obj in new[]{endpoint,device,enumerator})if(obj!=null&&Marshal.IsComObject(obj))Marshal.ReleaseComObject(obj);}
    }
    static bool GetAudio(out float volume,out bool mute) {
        float v=1;bool m=false;
        bool ok=WithAudio(ep=>ep.GetMasterVolumeLevelScalar(out v)==0&&ep.GetMute(out m)==0);
        volume=v;mute=m;return ok;
    }
    static bool SetVolume(float volume) {
        if(float.IsNaN(volume)||float.IsInfinity(volume))return false;
        return WithAudio(ep=>{var g=Guid.Empty;return ep.SetMasterVolumeLevelScalar(Math.Max(0,Math.Min(1,volume)),ref g)==0;});
    }
    static bool SetMute(bool mute) {return WithAudio(ep=>{var g=Guid.Empty;return ep.SetMute(mute,ref g)==0;});}
    static bool ToggleMute() {return WithAudio(ep=>{bool mute;var g=Guid.Empty;return ep.GetMute(out mute)==0&&ep.SetMute(!mute,ref g)==0;});}

    static T Await<T>(Windows.Foundation.IAsyncOperation<T> operation) {
        if (operation == null) return default(T);
        var deadline = DateTime.UtcNow.AddSeconds(4);
        while (operation.Status == Windows.Foundation.AsyncStatus.Started) { if(DateTime.UtcNow > deadline) { operation.Cancel(); throw new TimeoutException(); } System.Threading.Thread.Sleep(5); }
        return operation.GetResults();
    }

    static string J(object value) { return new JavaScriptSerializer().Serialize(value); }

    static double Position(GlobalSystemMediaTransportControlsSession s) {
        var t = s.GetTimelineProperties();
        if (t == null) return 0;
        var p = s.GetPlaybackInfo();
        double pos = (t.Position - t.StartTime).TotalSeconds;
        if (p != null && p.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing) {
            var delta = (DateTimeOffset.UtcNow - t.LastUpdatedTime).TotalSeconds;
            if (delta >= 0 && delta < 86400) pos += delta * (p.PlaybackRate ?? 1);
        }
        double duration = (t.EndTime - t.StartTime).TotalSeconds;
        return duration > 0 ? Math.Max(0, Math.Min(duration, pos)) : Math.Max(0, pos);
    }

    static object State() {
        float vol; bool mute;
        GetAudio(out vol, out mute);
        int volInt = (int)Math.Round(vol * 100);
        var s = manager == null ? null : manager.GetCurrentSession();
        if (s == null) return new { available = false, volume = volInt, muted = mute };
        var m = Await(s.TryGetMediaPropertiesAsync());
        var p = s.GetPlaybackInfo();
        var t = s.GetTimelineProperties();
        if (m == null || p == null || t == null) return new { available = false, volume = volInt, muted = mute };
        var c = p.Controls;
        return new {
            available = true,
            appId = s.SourceAppUserModelId ?? "",
            title = m.Title ?? "",
            artist = m.Artist ?? "",
            position = Position(s),
            duration = Math.Max(0, (t.EndTime - t.StartTime).TotalSeconds),
            playing = p.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing,
            rate = p.PlaybackRate ?? 1,
            canToggle = c != null && c.IsPlayPauseToggleEnabled,
            canNext = c != null && c.IsNextEnabled,
            canPrevious = c != null && c.IsPreviousEnabled,
            canSeek = c != null && c.IsPlaybackPositionEnabled,
            volume = volInt,
            muted = mute
        };
    }

    static bool Control(string action, string expected, string title, string seconds) {
        if (action == "volume") {
            double v;
            if (double.TryParse(seconds, NumberStyles.Float, CultureInfo.InvariantCulture, out v)) {
                return SetVolume((float)(v / 100.0));
            }
            return false;
        }
        if (action == "mute") {
            if (seconds == "true") return SetMute(true);
            if (seconds == "false") return SetMute(false);
            return ToggleMute();
        }
        var s = manager == null ? null : manager.GetCurrentSession();
        if (s == null || s.SourceAppUserModelId != expected) return false;
        var m = Await(s.TryGetMediaPropertiesAsync());
        if (m == null || m.Title != title) return false;
        var p = s.GetPlaybackInfo();
        if (p == null || p.Controls == null) return false;
        var c = p.Controls;
        if (action == "toggle" && c.IsPlayPauseToggleEnabled) return Await(s.TryTogglePlayPauseAsync());
        if (action == "next" && c.IsNextEnabled) return Await(s.TrySkipNextAsync());
        if (action == "previous" && c.IsPreviousEnabled) return Await(s.TrySkipPreviousAsync());
        if (action == "seek" && c.IsPlaybackPositionEnabled) {
            double value;
            if (!double.TryParse(seconds, NumberStyles.Float, CultureInfo.InvariantCulture, out value) || double.IsNaN(value) || double.IsInfinity(value)) return false;
            var t = s.GetTimelineProperties();
            if (t == null) return false;
            double duration = (t.EndTime - t.StartTime).TotalSeconds;
            if (duration <= 0 || value < 0 || value > duration) return false;
            long ticks = t.StartTime.Ticks + (long)(value * TimeSpan.TicksPerSecond);
            if (ticks < t.MinSeekTime.Ticks || ticks > t.MaxSeekTime.Ticks) return false;
            return Await(s.TryChangePlaybackPositionAsync(ticks));
        }
        return false;
    }

    static void Reply(Stream stream, int status, string body) {
        var bytes = Encoding.UTF8.GetBytes(body);
        var header = "HTTP/1.1 " + status + " OK\r\nContent-Type: application/json; charset=utf-8\r\nCache-Control: no-store\r\nAccess-Control-Allow-Origin: *\r\nAccess-Control-Allow-Methods: GET, POST, OPTIONS\r\nAccess-Control-Allow-Headers: X-Desktop-Key, X-Pair-Nonce, Content-Type\r\nAccess-Control-Allow-Private-Network: true\r\nConnection: close\r\nContent-Length: " + bytes.Length + "\r\n\r\n";
        var h = Encoding.ASCII.GetBytes(header);
        stream.Write(h, 0, h.Length);
        stream.Write(bytes, 0, bytes.Length);
    }

    static void Serve(TcpClient client) {
        using (client) {
            client.ReceiveTimeout = 3000;
            client.SendTimeout = 3000;
            using (var stream = client.GetStream()) {
                try {
                    var header = new List<byte>();
                    int ch;
                    while (header.Count < 8192 && (ch = stream.ReadByte()) >= 0) {
                        header.Add((byte)ch);
                        int n = header.Count;
                        if (n >= 4 && header[n - 4] == 13 && header[n - 3] == 10 && header[n - 2] == 13 && header[n - 1] == 10) break;
                    }
                    var lines = Encoding.ASCII.GetString(header.ToArray()).Split(new[] { "\r\n" }, StringSplitOptions.None);
                    var request = lines[0].Split(' ');
                    if (request.Length < 2) { Reply(stream, 400, "{}"); return; }
                    string supplied = "", pairSecret = "";
                    int length = 0;
                    foreach (var line in lines) {
                        if (line.StartsWith("X-Pair-Nonce:", StringComparison.OrdinalIgnoreCase)) pairSecret = line.Substring(13).Trim();
                        if (line.StartsWith("X-Desktop-Key:", StringComparison.OrdinalIgnoreCase)) supplied = line.Substring(14).Trim();
                        if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase)) int.TryParse(line.Substring(15).Trim(), out length);
                    }
                    if (request[0] == "OPTIONS") { Reply(stream, 200, "{}"); return; }
                    if(request[0]=="GET" && request[1]=="/pair/info"){Reply(stream,200,J(new{application="TerminalDeskNext",pairingVersion=1}));return;}
                    if(request[0]=="POST" && request[1]=="/pair/start" && length>0 && length<=256){
                        var body=new byte[length];int at=0;while(at<length){int n=stream.Read(body,at,length-at);if(n==0)break;at+=n;}
                        var input=new JavaScriptSerializer().Deserialize<Dictionary<string,string>>(Encoding.UTF8.GetString(body,0,at));
                        Reply(stream,200,J(PairStart(input.ContainsKey("nonce")?input["nonce"]:null)));return;
                    }
                    if(request[0]=="GET" && request[1].StartsWith("/pair/status?id=")){Reply(stream,200,J(PairStatus(request[1].Substring(16),pairSecret)));return;}
                    if(request[0]=="GET" && (request[1]=="/performance" || request[1].StartsWith("/performance?"))){Reply(stream,200,PerformanceJson());return;}
                    if (!Authorized(supplied)) { Reply(stream, 403, "{}"); return; }
                    if (request[0] == "GET" && request[1].StartsWith("/dashboard")) { Reply(stream, 200, DashboardJson()); return; }
                    if (request[0] == "GET" && request[1] == "/state") { Reply(stream, 200, J(State())); return; }
                    if (request[0] == "POST" && request[1] == "/control" && length > 0 && length <= 2048) {
                        var data = new byte[length];
                        int at = 0;
                        while (at < length) {
                            int got = stream.Read(data, at, length - at);
                            if (got == 0) break;
                            at += got;
                        }
                        var obj = new JavaScriptSerializer().Deserialize<Dictionary<string, string>>(Encoding.UTF8.GetString(data, 0, at));
                        bool ok = !readOnly && obj.ContainsKey("action") && Control(
                            obj["action"],
                            obj.ContainsKey("appId") ? obj["appId"] : "",
                            obj.ContainsKey("title") ? obj["title"] : "",
                            obj.ContainsKey("seconds") ? obj["seconds"] : ""
                        );
                        float curVol; bool curMute;
                        GetAudio(out curVol, out curMute);
                        Reply(stream, 200, J(new {
                            ok = ok,
                            volume = (int)Math.Round(curVol * 100),
                            muted = curMute
                        }));
                        return;
                    }
                    Reply(stream, 404, "{}");
                } catch {
                    try { Reply(stream, 503, "{\"available\":false}"); } catch {}
                }
            }
        }
    }

}
