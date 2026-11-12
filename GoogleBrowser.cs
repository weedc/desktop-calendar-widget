using System;
using System.Collections.Generic;
using System.Collections;
using System.Linq;
using System.Diagnostics;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Runtime.InteropServices;
using System.Web.Script.Serialization;
using System.Management;

namespace TeamCalendar
{
    sealed class GoogleLoginRequired : InvalidOperationException
    {
        public GoogleLoginRequired() : base("설정에서 Google 계정을 연결해 주세요. 로그인 창을 닫은 뒤 연결 버튼을 다시 누르세요.") { }
    }

    // Only this application's dedicated profile is controlled. Interactive login
    // has no debugging endpoint. Background work can ONLY launch headless Chrome.
    sealed class GoogleBrowser : IDisposable
    {
        public static readonly GoogleBrowser Shared = new GoogleBrowser(Path.Combine(App.DataDir, "google-profile"), true);
        public readonly object Gate = new object();
        readonly string profile;
        readonly bool requireLogin;
        readonly JavaScriptSerializer json = new JavaScriptSerializer { MaxJsonLength = 4 * 1024 * 1024 };
        Process background, login;
        ClientWebSocket socket;
        IntPtr processJob;
        string session = "";
        int sequence;
        public GoogleBrowser(string directory, bool needsLogin) { profile = Path.GetFullPath(directory); requireLogin = needsLogin; }
        public bool LoginStarted { get { return File.Exists(Path.Combine(profile, "widget-login-started")); } }
        public bool Verified { get { return File.Exists(Path.Combine(profile, "widget-connection-verified")); } }
        public void MarkVerified() { File.WriteAllText(Path.Combine(profile, "widget-connection-verified"), "1"); }
        void InvalidateVerification() { try { File.Delete(Path.Combine(profile, "widget-connection-verified")); } catch (IOException) { } }
        static bool Running(Process process) { try { return process != null && !process.HasExited; } catch { return false; } }
        internal static ProcessStartInfo LaunchInfo(string chrome, string directory, bool headless, string url)
        {
            // Paths are application-owned, not arbitrary command-line fragments.
            if (directory.IndexOfAny(new[] { '"', '\r', '\n' }) >= 0) throw new ArgumentException("잘못된 연결 폴더입니다.");
            if (!OfficialUrl(url) && (headless || !url.StartsWith("https://accounts.google.com/ServiceLogin?", StringComparison.Ordinal))) throw new ArgumentException("Google Calendar 공식 주소만 열 수 있습니다.");
            string flags = "--user-data-dir=\"" + directory + "\" --no-first-run --no-default-browser-check --disable-background-mode ";
            flags += headless ? "--headless=new --remote-debugging-address=127.0.0.1 --remote-debugging-port=0 --window-size=2000,3600 " : "--new-window ";
            return new ProcessStartInfo(chrome, flags + "\"" + url + "\"") { UseShellExecute = false, CreateNoWindow = headless, WindowStyle = headless ? ProcessWindowStyle.Hidden : ProcessWindowStyle.Normal };
        }
        internal static bool OfficialUrl(string url)
        {
            Uri parsed; return Uri.TryCreate(url, UriKind.Absolute, out parsed) && parsed.Scheme == "https" && parsed.Host == "calendar.google.com" && parsed.IsDefaultPort;
        }
        public void OpenLogin(string email)
        {
            lock (Gate)
            {
                if (Running(login)) throw new InvalidOperationException("열린 전용 Google 창에서 로그인을 마치고 창을 닫아 주세요.");
                Stop(); RequireProfileAvailable(); Directory.CreateDirectory(profile);
                InvalidateVerification();
                login = Process.Start(LaunchInfo(App.Chrome(), profile, false, AccountDiscovery.LoginUrl(email)));
                File.WriteAllText(Path.Combine(profile, "widget-login-started"), "1");
            }
        }
        void Ensure()
        {
            if (Running(login)) throw new InvalidOperationException("전용 Google 로그인 창을 닫은 뒤 다시 연결해 주세요.");
            if (requireLogin && !LoginStarted) throw new GoogleLoginRequired();
            if (Running(background) && socket != null && socket.State == WebSocketState.Open && session != "") return;
            Stop(); RequireProfileAvailable(); Directory.CreateDirectory(profile);
            string portFile = Path.Combine(profile, "DevToolsActivePort");
            // Never attach to a stale endpoint or another Chrome instance.
            if (File.Exists(portFile)) File.Delete(portFile);
            background = Process.Start(LaunchInfo(App.Chrome(), profile, true, AccountDiscovery.SettingsUrl("")));
            try
            {
                processJob = HeadlessProcessLease.Attach(background);
                var watch = Stopwatch.StartNew(); string endpoint = "";
                while (watch.ElapsedMilliseconds < 18000 && Running(background))
                {
                    try
                    {
                        string[] lines = File.ReadAllLines(portFile); int port;
                        if (lines.Length >= 2 && int.TryParse(lines[0], out port) && port > 0 && port <= 65535 && Regex.IsMatch(lines[1], @"^/devtools/browser/[A-Za-z0-9-]+$"))
                        { endpoint = "ws://127.0.0.1:" + port + lines[1]; break; }
                    }
                    catch (IOException) { }
                    Thread.Sleep(100);
                }
                if (endpoint == "") throw new InvalidOperationException("전용 Google 연결을 시작하지 못했습니다. 로그인 창이 열려 있다면 닫고 다시 시도하세요.");
                socket = new ClientWebSocket(); socket.Options.Proxy = null;
                using (var timeout = new CancellationTokenSource(10000)) socket.ConnectAsync(new Uri(endpoint), timeout.Token).GetAwaiter().GetResult();
                string target = App.Str(Call("Target.createTarget", new { url = "about:blank" }, false), "targetId");
                session = App.Str(Call("Target.attachToTarget", new { targetId = target, flatten = true }, false), "sessionId");
                Call("Page.enable", new { }); Call("Network.enable", new { });
                Call("Network.setCacheDisabled", new { cacheDisabled = true });
            }
            catch { Stop(); throw; }
        }
        void RequireProfileAvailable()
        {
            // A login window may outlive the widget. Launching headless Chrome
            // against that live profile would forward a URL into the visible
            // browser. Check process metadata before every new launch instead.
            try
            {
                using (var query = new ManagementObjectSearcher("SELECT CommandLine FROM Win32_Process WHERE Name='chrome.exe' AND SessionId=" + Process.GetCurrentProcess().SessionId))
                using (var processes = query.Get())
                    foreach (ManagementObject process in processes)
                    {
                        using (process)
                        {
                            string command = Convert.ToString(process["CommandLine"]);
                            if (command == "") throw new IOException("Chrome 연결 상태를 확인하지 못했습니다. 기존 데이터를 유지합니다.");
                            if (UsesProfile(command, profile)) throw new InvalidOperationException("전용 Google Chrome 창을 닫은 뒤 다시 연결하거나 새로고침해 주세요.");
                        }
                    }
            }
            catch (ManagementException) { throw new IOException("Chrome 연결 상태를 확인하지 못했습니다. 기존 데이터를 유지합니다."); }
        }
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern IntPtr CommandLineToArgvW(string command, out int count);
        [DllImport("kernel32.dll")] static extern IntPtr LocalFree(IntPtr value);
        internal static bool UsesProfile(string command, string directory)
        {
            int count; IntPtr block = CommandLineToArgvW(command, out count);
            if (block == IntPtr.Zero) throw new IOException("Chrome 연결 상태를 확인하지 못했습니다.");
            try
            {
                var args = new List<string>();
                for (int i = 0; i < count; i++) args.Add(Marshal.PtrToStringUni(Marshal.ReadIntPtr(block, i * IntPtr.Size)));
                for (int i = 1; i < args.Count; i++)
                {
                    string value = args[i].StartsWith("--user-data-dir=", StringComparison.Ordinal) ? args[i].Substring(16) : args[i] == "--user-data-dir" && i + 1 < args.Count ? args[i + 1] : null;
                    if (value != null && string.Equals(Path.GetFullPath(value).TrimEnd('\\'), Path.GetFullPath(directory).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) return true;
                }
                return false;
            }
            finally { LocalFree(block); }
        }
        Dictionary<string, object> Call(string method, object parameters, bool page = true)
        {
            int id = ++sequence;
            var request = new Dictionary<string, object> { { "id", id }, { "method", method }, { "params", parameters } };
            if (page) request["sessionId"] = session;
            byte[] payload = Encoding.UTF8.GetBytes(json.Serialize(request));
            using (var timeout = new CancellationTokenSource(15000))
            {
                socket.SendAsync(new ArraySegment<byte>(payload), WebSocketMessageType.Text, true, timeout.Token).GetAwaiter().GetResult();
                while (true)
                {
                    using (var message = new MemoryStream())
                    {
                        var buffer = new byte[16384]; WebSocketReceiveResult part;
                        do
                        {
                            part = socket.ReceiveAsync(new ArraySegment<byte>(buffer), timeout.Token).GetAwaiter().GetResult();
                            if (part.MessageType == WebSocketMessageType.Close) throw new IOException("Google 연결이 종료되었습니다. 다시 새로고침해 주세요.");
                            message.Write(buffer, 0, part.Count);
                            if (message.Length > 4 * 1024 * 1024) throw new IOException("Google 응답이 너무 큽니다.");
                        } while (!part.EndOfMessage);
                        var response = json.Deserialize<Dictionary<string, object>>(Encoding.UTF8.GetString(message.ToArray()));
                        object responseId;
                        if (!response.TryGetValue("id", out responseId) || Convert.ToInt32(responseId) != id) continue;
                        if (response.ContainsKey("error")) throw new IOException("Google 읽기 연결이 응답하지 않습니다. 다시 새로고침해 주세요.");
                        return (Dictionary<string, object>)response["result"];
                    }
                }
            }
        }
        public void Navigate(string url)
        {
            if (!OfficialUrl(url)) throw new ArgumentException("Google Calendar 공식 주소만 열 수 있습니다.");
            Ensure();
            try
            {
                var result = Call("Page.navigate", new { url = url });
                if (result.ContainsKey("errorText")) throw new IOException("Google Calendar에 연결하지 못했습니다. 네트워크를 확인하세요.");
                string loader = App.Str(result, "loaderId"); var watch = Stopwatch.StartNew();
                while (watch.ElapsedMilliseconds < 15000)
                {
                    var tree = (Dictionary<string, object>)Call("Page.getFrameTree", new { })["frameTree"];
                    var frame = (Dictionary<string, object>)tree["frame"];
                    string current = App.Str(frame, "url");
                    if (current.StartsWith("https://accounts.google.com/", StringComparison.Ordinal) || current.StartsWith("https://workspace.google.com/", StringComparison.Ordinal)) throw new GoogleLoginRequired();
                    if (OfficialUrl(current) && (loader == "" || App.Str(frame, "loaderId") == loader)) return;
                    Thread.Sleep(100);
                }
                throw new IOException("Google 화면 연결 시간이 초과되었습니다. 기존 데이터를 유지합니다.");
            }
            catch (GoogleLoginRequired) { InvalidateVerification(); Stop(); throw; }
            catch { Stop(); throw; }
        }
        public object Evaluate(string expression)
        {
            try
            {
                // Never inspect login forms, credentials or unrelated origins.
                var response = Call("Runtime.evaluate", new { expression = "(() => { if(location.origin !== 'https://calendar.google.com') return {widgetLoginRequired:true}; return (" + expression + "); })()", returnByValue = true, awaitPromise = true });
                if (response.ContainsKey("exceptionDetails")) throw new IOException("Google 화면 구조를 읽지 못했습니다. 기존 데이터를 유지합니다.");
                var result = (Dictionary<string, object>)response["result"]; object value;
                result.TryGetValue("value", out value);
                var error = value as Dictionary<string, object>;
                if (error != null && error.ContainsKey("widgetLoginRequired")) throw new GoogleLoginRequired();
                return value;
            }
            catch (GoogleLoginRequired) { InvalidateVerification(); throw; }
            catch { Stop(); throw; }
        }
        public object Script(string name, object argument) { return Evaluate("(" + GoogleDom.Script(name) + ")(" + json.Serialize(argument) + ")"); }
        void Stop()
        {
            // Only the process launched in headless mode is stopped. An explicit
            // login window belongs to the user until they close it themselves.
            try { if (socket != null && socket.State == WebSocketState.Open) Call("Browser.close", new { }, false); } catch { }
            if (socket != null) { socket.Dispose(); socket = null; }
            if (background != null)
            {
                try { if (!background.WaitForExit(3000)) background.Kill(); } catch { }
                background.Dispose(); background = null;
            }
            if (processJob != IntPtr.Zero) { HeadlessProcessLease.Close(processJob); processJob = IntPtr.Zero; }
            session = "";
        }
        public void Dispose() { lock (Gate) Stop(); }
    }

    // Closing the widget (including a crash) must not orphan a headless process
    // holding the dedicated profile lock. Interactive login is never in this job.
    static class HeadlessProcessLease
    {
        [StructLayout(LayoutKind.Sequential)] struct BasicLimits
        {
            public long ProcessTime, JobTime; public uint Flags;
            public UIntPtr MinWorkingSet, MaxWorkingSet; public uint ActiveProcesses;
            public UIntPtr Affinity; public uint Priority, Scheduling;
        }
        [StructLayout(LayoutKind.Sequential)] struct IoCounters { public ulong ReadOps, WriteOps, OtherOps, ReadBytes, WriteBytes, OtherBytes; }
        [StructLayout(LayoutKind.Sequential)] struct Limits
        {
            public BasicLimits Basic; public IoCounters Io;
            public UIntPtr ProcessMemory, JobMemory, PeakProcessMemory, PeakJobMemory;
        }
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr CreateJobObject(IntPtr security, string name);
        [DllImport("kernel32.dll")] static extern bool SetInformationJobObject(IntPtr job, int type, ref Limits value, uint size);
        [DllImport("kernel32.dll")] static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr value);
        public static IntPtr Attach(Process process)
        {
            IntPtr job = CreateJobObject(IntPtr.Zero, null); var limits = new Limits(); limits.Basic.Flags = 0x2000;
            if (job == IntPtr.Zero || !SetInformationJobObject(job, 9, ref limits, (uint)Marshal.SizeOf(limits)) || !AssignProcessToJobObject(job, process.Handle))
            { if (job != IntPtr.Zero) CloseHandle(job); throw new IOException("창 없는 Google 연결의 종료 관리를 시작하지 못했습니다."); }
            return job;
        }
        public static void Close(IntPtr value) { CloseHandle(value); }
    }

    static class GoogleDom
    {
        static readonly Dictionary<string, string> scripts = Load();
        static Dictionary<string, string> Load()
        {
            using (var stream = typeof(GoogleDom).Assembly.GetManifestResourceStream("TeamCalendar.GoogleCalendarDom.json"))
            using (var reader = new StreamReader(stream, Encoding.UTF8)) return new JavaScriptSerializer().Deserialize<Dictionary<string, string>>(reader.ReadToEnd());
        }
        public static object[] Array(object value) { var sequence = value as IEnumerable; return sequence == null || value is string ? new object[0] : sequence.Cast<object>().ToArray(); }
        public static string Script(string name) { return scripts[name]; }
    }
}
