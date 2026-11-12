using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Automation;
using System.Windows.Forms;

namespace TeamCalendar
{
    // Losing a connected window must never authorize another browser launch.
    // A new lease is created only on app startup or explicit settings reconnect.
    sealed class ReaderLifecycle
    {
        public string Phase { get; private set; }
        public ReaderLifecycle() { Phase = "new"; }
        public bool BeginInitial(bool allowInitialConnection = true) { if (Phase != "new" || !allowInitialConnection) return false; Phase = "connecting"; return true; }
        public void Connected() { if (Phase != "connecting") throw new InvalidOperationException(); Phase = "hidden"; }
        public void Lost() { if (Phase != "closed") Phase = "lost"; }
        public void Close() { Phase = "closed"; }
        public void RequireExisting(bool live)
        {
            if (Phase == "hidden" && live) return;
            Lost(); throw new InvalidOperationException("Chrome 읽기 연결이 끊겼습니다. 저장된 일정을 유지합니다. 설정에서 다시 연결해 주세요.");
        }
    }

    static class ExistingChromeProfile
    {
        static string Root { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Google\\Chrome\\User Data"); } }
        public static List<FeatureState.Profile> List()
        {
            var result = new List<FeatureState.Profile>();
            try
            {
                var data = new JavaScriptSerializer { MaxJsonLength = 16 * 1024 * 1024 }.Deserialize<Dictionary<string, object>>(File.ReadAllText(Path.Combine(Root, "Local State"), Encoding.UTF8));
                var info = (Dictionary<string, object>)((Dictionary<string, object>)data["profile"])["info_cache"];
                foreach (var entry in info)
                {
                    var value = (Dictionary<string, object>)entry.Value;
                    if (Valid(entry.Key)) result.Add(new FeatureState.Profile { directory = entry.Key, name = App.Str(value, "name", entry.Key), email = App.Str(value, "user_name") });
                }
            }
            catch { }
            return result;
        }
        static bool Valid(string value) { return !string.IsNullOrEmpty(value) && value != "." && value != ".." && value.IndexOfAny(new[] { '"', '\r', '\n', '\\', '/' }) < 0; }
        public static string Resolve(string saved, string email)
        {
            var profiles = List();
            var byEmail = profiles.FirstOrDefault(p => email != "" && string.Equals(p.email, email, StringComparison.OrdinalIgnoreCase));
            if (byEmail != null) return byEmail.directory;
            if (Valid(saved) && Directory.Exists(Path.Combine(Root, saved))) return saved;
            try
            {
                var data = new JavaScriptSerializer { MaxJsonLength = 16 * 1024 * 1024 }.Deserialize<Dictionary<string, object>>(File.ReadAllText(Path.Combine(Root, "Local State"), Encoding.UTF8));
                string last = App.Str((Dictionary<string, object>)data["profile"], "last_used");
                if (Valid(last) && Directory.Exists(Path.Combine(Root, last))) return last;
            }
            catch { }
            return profiles.Count == 1 ? profiles[0].directory : "";
        }
        public static string Argument(string profile)
        {
            if (profile == "") return "";
            if (!Valid(profile)) throw new ArgumentException("올바른 Chrome 프로필을 선택해 주세요.");
            return "--profile-directory=\"" + profile + "\" ";
        }
    }

    // Uses the user's existing Chrome login. Only initial connection may show a
    // frame. Refresh/navigation never show, restore, activate or recreate one.
    sealed class ChromeReaderWindow : IDisposable
    {
        readonly CalendarSource source;
        readonly ReaderLifecycle lifecycle = new ReaderLifecycle();
        readonly string tag = Guid.NewGuid().ToString("N");
        IntPtr window;
        volatile bool disposed;
        public IntPtr Window { get { return window; } }
        public ChromeReaderWindow(CalendarSource value) { source = value; }
        public static void Mark(IntPtr handle, string owner) { if (!Win.SetProp(handle, "DesktopCalendarWidget.Reader", Owner(owner))) throw new InvalidOperationException("Chrome 읽기창을 확인하지 못했습니다."); }
        static IntPtr Owner(string tag) { return new IntPtr(long.Parse(tag.Substring(0, 15), NumberStyles.HexNumber, CultureInfo.InvariantCulture) + 1); }
        public static bool Owns(IntPtr handle, string owner) { return handle != IntPtr.Zero && Win.IsWindow(handle) && Win.GetProp(handle, "DesktopCalendarWidget.Reader") == Owner(owner); }
        bool Live()
        {
            if (!Owns(window, tag)) return false;
            string url = AccountDiscovery.Address(window);
            Uri address; string absolute = url.Contains("://") ? url : "https://" + url;
            return Uri.TryCreate(absolute, UriKind.Absolute, out address) && address.Scheme == "https" && address.Host == "calendar.google.com" && Regex.IsMatch(address.AbsolutePath, @"^/calendar/(?:u/\d+/)?embed$") && url.Contains("widget_reader=" + tag);
        }
        static AutomationElementCollection Buttons(AutomationElement root) { return root.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button)); }
        public static DateTime Header(AutomationElement root)
        {
            foreach (AutomationElement item in Buttons(root))
            {
                var match = Regex.Match(item.Current.Name ?? "", @"^(\d{4})년\s*(\d{1,2})월(?:,|$)");
                if (match.Success) return new DateTime(int.Parse(match.Groups[1].Value), int.Parse(match.Groups[2].Value), 1);
            }
            throw new InvalidOperationException("Google 캘린더 화면을 기다리는 중입니다.");
        }
        void Park()
        {
            if (!Owns(window, tag)) return;
            long ex = Win.GetWindowLongPtr(window, -20).ToInt64(), style = Win.GetWindowLongPtr(window, -16).ToInt64();
            Win.SetWindowLongPtr(window, -20, new IntPtr((ex & ~0x40000L) | 0x080800a0L));
            Win.SetLayeredWindowAttributes(window, 0, 0, 2);
            Win.SetWindowLongPtr(window, -16, new IntPtr((style & ~0xa1c40000L) | 0x40000000L | 0x10000000L));
            Win.SetParent(window, Win.DesktopHost());
            Win.SetWindowPos(window, new IntPtr(1), -16000, 0, 2000, 3600, 0x0010 | 0x0040 | 0x0020);
        }
        bool Ensure(DateTime month, bool allowInitialConnection)
        {
            if (lifecycle.Phase == "new" && !allowInitialConnection) throw new InvalidOperationException("읽기 연결이 없습니다. 새로고침은 창을 열지 않습니다. 설정에서 Google 계정을 연결해 주세요.");
            if (!lifecycle.BeginInitial(allowInitialConnection)) { lifecycle.RequireExisting(Live()); return false; }
            var previous = new HashSet<IntPtr>(Win.ChromeWindows());
            string profile = ExistingChromeProfile.Resolve(App.Options.ActiveProfile(), App.Options.ActiveUser());
            string url = App.EmbedUrl(source) + "&dates=" + month.ToString("yyyyMMdd") + "/" + month.AddMonths(1).AddDays(-1).ToString("yyyyMMdd") + "&widget_reader=" + tag;
            try
            {
                Process.Start(new ProcessStartInfo(App.Chrome(), ExistingChromeProfile.Argument(profile) + "--app=\"" + url + "\" --window-size=2000,1300") { UseShellExecute = true, WindowStyle = ProcessWindowStyle.Normal });
                var watch = Stopwatch.StartNew();
                while (watch.ElapsedMilliseconds < 45000 && !disposed)
                {
                    if (window == IntPtr.Zero)
                    {
                        foreach (var candidate in Win.ChromeWindows())
                        {
                            if (previous.Contains(candidate) || !AccountDiscovery.Address(candidate).Contains(tag)) continue;
                            window = candidate; Mark(window, tag);
                            // Rendering/AX initialization is allowed only in this
                            // first connection. Never run this in a refresh path.
                            var area = Screen.PrimaryScreen.WorkingArea;
                            Win.SetWindowPos(window, new IntPtr(-1), area.Left + 30, area.Top + 30, Math.Min(2000, area.Width - 60), Math.Min(1300, area.Height - 60), 0x0010 | 0x0040);
                            break;
                        }
                    }
                    if (window != IntPtr.Zero)
                    {
                        try
                        {
                            var root = AutomationElement.FromHandle(window); Header(root);
                            var cells = root.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.DataItem));
                            if (cells.Cast<AutomationElement>().Count(e => Regex.IsMatch(e.Current.Name ?? "", @"^일정 (?:\d+개|없음),")) >= 28)
                            { Park(); lifecycle.Connected(); App.Log("Existing Chrome reader connected."); return true; }
                        }
                        catch { }
                    }
                    Thread.Sleep(200);
                }
                throw new InvalidOperationException("기존 Chrome에서 캘린더에 접근할 수 있는 계정으로 로그인한 뒤 설정에서 다시 연결해 주세요.");
            }
            catch { lifecycle.Lost(); throw; }
            finally { Park(); }
        }
        static string DocumentId(IntPtr handle)
        {
            try { var doc = AutomationElement.FromHandle(handle).FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Document)); return doc == null ? "" : string.Join("-", doc.GetRuntimeId()); }
            catch { return ""; }
        }
        void Refresh()
        {
            lifecycle.RequireExisting(Live()); string previous = DocumentId(window);
            if (!Win.RefreshChrome(window)) throw new InvalidOperationException("Chrome 읽기창이 응답하지 않습니다. 기존 데이터를 유지합니다.");
            var watch = Stopwatch.StartNew(); Thread.Sleep(350);
            while (watch.ElapsedMilliseconds < 20000 && !disposed)
            {
                lifecycle.RequireExisting(Live()); string current = DocumentId(window);
                if (current != "" && (previous == "" || current != previous)) return;
                Thread.Sleep(150);
            }
            throw new InvalidOperationException("Google 새로고침 완료를 확인하지 못했습니다. 기존 데이터를 유지합니다.");
        }
        static void Invoke(AutomationElement root, string name)
        {
            foreach (AutomationElement button in Buttons(root)) if (button.Current.Name == name) { Invoke(button); return; }
            throw new InvalidOperationException("Google 캘린더의 월 이동 버튼을 찾지 못했습니다.");
        }
        static void Invoke(AutomationElement button) { object value; if (!button.TryGetCurrentPattern(InvokePattern.Pattern, out value)) throw new InvalidOperationException("Google 화면의 버튼에 접근하지 못했습니다."); ((InvokePattern)value).Invoke(); }
        static string[] Events(AutomationElement parent) { return Buttons(parent).Cast<AutomationElement>().Select(e => e.Current.Name ?? "").Where(s => s.Contains(", 캘린더: ")).Distinct().ToArray(); }
        static string[] Expand(AutomationElement root, AutomationElement cell)
        {
            var more = Buttons(cell).Cast<AutomationElement>().FirstOrDefault(e => !(e.Current.Name ?? "").Contains(", 캘린더: ") && Regex.IsMatch(e.Current.Name ?? "", "더보기|더 보기|개 더|more", RegexOptions.IgnoreCase));
            if (more == null) return Events(cell);
            Invoke(more); AutomationElement close = null; string[] result = null;
            try
            {
                for (int wait = 0; wait < 25; wait++)
                {
                    var document = root.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Document));
                    if (document != null) foreach (AutomationElement button in Buttons(document))
                    {
                        if (button.Current.Name != "닫기" && button.Current.Name != "Close") continue;
                        var node = TreeWalker.ControlViewWalker.GetParent(button);
                        for (int depth = 0; node != null && depth < 6 && node != document; depth++, node = TreeWalker.ControlViewWalker.GetParent(node))
                        {
                            if (node.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.DataItem)).Count > 0) break;
                            var found = Events(node); if (found.Length > 0) { result = found; close = button; break; }
                        }
                        if (close != null) break;
                    }
                    if (result != null) return result;
                    Thread.Sleep(150);
                }
                throw new InvalidOperationException("숨겨진 일정을 읽지 못했습니다. 기존 데이터를 유지합니다.");
            }
            finally { if (close != null) Invoke(close); }
        }
        public Dictionary<string, object> Snapshot(DateTime month, bool reload, bool allowInitialConnection = false)
        {
            if (disposed) throw new ObjectDisposedException("ChromeReaderWindow");
            bool initial = Ensure(month, allowInitialConnection); if (reload && !initial) Refresh();
            DateTime shown = DateTime.MinValue; var watch = Stopwatch.StartNew();
            while (watch.ElapsedMilliseconds < 30000 && !disposed)
            { try { shown = Header(AutomationElement.FromHandle(window)); break; } catch { Thread.Sleep(200); } }
            if (shown == DateTime.MinValue) throw new InvalidOperationException("Google 캘린더를 읽지 못했습니다. 기존 데이터를 유지합니다.");
            if (Math.Abs((shown.Year - month.Year) * 12 + shown.Month - month.Month) > 120) throw new InvalidOperationException("한 번에 10년 이내의 달로 이동해 주세요.");
            while (shown != month)
            {
                lifecycle.RequireExisting(Live()); if (disposed) throw new OperationCanceledException();
                Invoke(AutomationElement.FromHandle(window), shown < month ? "다음 달" : "이전 달"); DateTime previous = shown;
                for (int wait = 0; wait < 40; wait++) { Thread.Sleep(100); try { shown = Header(AutomationElement.FromHandle(window)); if (shown != previous) break; } catch { } }
                if (shown == previous) throw new InvalidOperationException("Google 캘린더의 월 이동을 확인하지 못했습니다.");
            }
            var root = AutomationElement.FromHandle(window); bool loaded = false;
            for (int wait = 0; wait < 100; wait++)
            {
                var progress = root.FindFirst(TreeScope.Descendants, new AndCondition(new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ProgressBar), new PropertyCondition(AutomationElement.NameProperty, "캘린더 로드 중")));
                if (progress == null) { loaded = true; break; } Thread.Sleep(200);
            }
            if (!loaded) throw new InvalidOperationException("Google 일정 로딩이 지연되고 있습니다. 기존 데이터를 유지합니다.");
            Thread.Sleep(550); root = AutomationElement.FromHandle(window); var cells = new List<object>();
            foreach (AutomationElement cell in root.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.DataItem)))
            {
                string label = cell.Current.Name ?? "";
                if (!Regex.IsMatch(label, @"^일정 (?:\d+개|없음),\s*\d{1,2}월\s*\d{1,2}일") || Regex.Matches(label, "일정 ").Count != 1) continue;
                cells.Add(new Dictionary<string, object> { { "label", label }, { "events", Expand(root, cell).Cast<object>().ToArray() } });
            }
            return new Dictionary<string, object> { { "month", month.ToString("yyyy-MM") }, { "cells", cells.ToArray() } };
        }
        public void Dispose() { disposed = true; lifecycle.Close(); if (Owns(window, tag)) Win.PostMessage(window, 0x10, IntPtr.Zero, IntPtr.Zero); }
    }

    static class GoogleDom
    {
        public static object[] Array(object value) { var sequence = value as System.Collections.IEnumerable; return sequence == null || value is string ? new object[0] : sequence.Cast<object>().ToArray(); }
    }
}
