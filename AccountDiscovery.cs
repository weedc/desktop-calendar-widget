using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Automation;

namespace TeamCalendar
{
    sealed class AccountCatalog
    {
        public string email = "", profile = "";
        public List<CalendarSource> calendars = new List<CalendarSource>();
        public List<string> warnings = new List<string>();
    }
    static class AccountDiscovery
    {
        static readonly object gate = new object();
        static bool busy;
        static string message = "", error = "";
        static AccountCatalog last;
        public static object Status() { lock (gate) return new { loading = busy, message = message, error = error, result = last }; }
        public static string DecodeCalendar(string address)
        {
            var match = Regex.Match(address ?? "", @"/settings/calendar/([^/?#]+)"); if (!match.Success) return "";
            try
            {
                string encoded = Uri.UnescapeDataString(match.Groups[1].Value).Replace('-', '+').Replace('_', '/');
                encoded = encoded.PadRight(encoded.Length + (4 - encoded.Length % 4) % 4, '=');
                string value = new UTF8Encoding(false, true).GetString(Convert.FromBase64String(encoded));
                return value.Contains("@") && value.Length <= 512 && !value.Any(char.IsWhiteSpace) ? value : "";
            }
            catch { return ""; }
        }
        public static string SettingsUrl(string email, string tag = null)
        {
            return "https://calendar.google.com/calendar/u/0/r/settings?hl=ko" + (string.IsNullOrWhiteSpace(email) ? "" : "&authuser=" + Uri.EscapeDataString(email)) + (tag == null ? "" : "&widget_account=" + tag);
        }
        public static void OpenLogin(string profile, string email)
        {
            string args = (profile == "" ? "" : "--profile-directory=\"" + profile.Replace("\"", "") + "\" ") + "--app=\"" + SettingsUrl(email) + "\" --window-size=1100,900";
            Process.Start(new ProcessStartInfo(App.Chrome(), args) { UseShellExecute = true });
        }
        public static AccountCatalog Read(string profile, string email, Action<string> progress)
        {
            lock (gate) { if (busy) throw new InvalidOperationException("이미 계정의 캘린더를 불러오는 중입니다."); busy = true; message = "로그인된 Google 계정 확인 중…"; error = ""; last = null; }
            IntPtr window = IntPtr.Zero;
            try
            {
                if (profile.Contains("\"") || profile.Contains("\r") || profile.Contains("\n")) throw new ArgumentException("올바른 Chrome 프로필을 선택해 주세요.");
                string tag = Guid.NewGuid().ToString("N"), url = SettingsUrl(email, tag);
                var existing = new HashSet<IntPtr>(Win.ChromeWindows());
                string args = (profile == "" ? "" : "--profile-directory=\"" + profile + "\" ") + "--app=\"" + url + "\" --window-position=-16000,0 --window-size=1400,1600";
                Process.Start(new ProcessStartInfo(App.Chrome(), args) { UseShellExecute = true, WindowStyle = ProcessWindowStyle.Hidden });
                var watch = Stopwatch.StartNew();
                while (window == IntPtr.Zero && watch.Elapsed.TotalSeconds < 30)
                {
                    foreach (var candidate in Win.ChromeWindows())
                    {
                        if (existing.Contains(candidate)) continue;
                        string address = Address(candidate);
                        if (!address.Contains("widget_account=" + tag)) continue;
                        if (!address.Contains("calendar.google.com/") && !address.Contains("accounts.google.com/")) continue;
                        window = candidate; break;
                    }
                    if (window == IntPtr.Zero) Thread.Sleep(150);
                }
                if (window == IntPtr.Zero) throw new InvalidOperationException("Google 계정 화면을 찾지 못했습니다. Google 계정 열기로 로그인한 뒤 다시 시도해 주세요.");
                long ex = Win.GetWindowLongPtr(window, -20).ToInt64(), style = Win.GetWindowLongPtr(window, -16).ToInt64();
                Win.SetWindowLongPtr(window, -20, new IntPtr((ex & ~0x40000L) | 0x80L));
                Win.SetWindowLongPtr(window, -16, new IntPtr((style & ~0x80C40000L) | 0x40000000L | 0x10000000L));
                Win.SetParent(window, Win.DesktopHost()); Win.SetWindowPos(window, new IntPtr(1), -16000, 0, 1400, 1600, 0x0010 | 0x0040 | 0x0020);
                var result = new AccountCatalog { profile = profile };
                AutomationElement root = null; List<string> groups = null;
                watch.Restart();
                while (watch.Elapsed.TotalSeconds < 30)
                {
                    root = AutomationElement.FromHandle(window);
                    var all = root.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Tree));
                    groups = all.Cast<AutomationElement>().Select(e => e.Current.Name).Where(IsCalendarGroup).Distinct().ToList();
                    if (groups.Count > 0) break;
                    Thread.Sleep(250);
                }
                if (groups == null || groups.Count == 0) throw new InvalidOperationException("이 Chrome 계정의 캘린더를 읽지 못했습니다. Google 계정 열기에서 로그인 상태를 확인해 주세요.");
                foreach (AutomationElement button in root.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button)))
                {
                    string name = button.Current.Name ?? "";
                    if (!name.Contains("Google 계정") && !name.Contains("Google Account")) continue;
                    var found = Regex.Match(name, @"([A-Za-z0-9.!#$%&'*+/=?^_`{|}~-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,})");
                    if (found.Success) { result.email = found.Groups[1].Value; break; }
                }
                if (result.email == "") throw new InvalidOperationException("연결된 Google 계정을 확인하지 못했습니다. Google 계정 화면을 확인해 주세요.");
                if (email != "" && !string.Equals(email, result.email, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("요청한 계정과 실제 로그인 계정이 다릅니다. Google 계정 열기에서 계정을 선택해 주세요.");
                foreach (string group in groups)
                {
                    root = AutomationElement.FromHandle(window); var tree = CalendarTree(root, group);
                    int count = Math.Min(250, tree.FindAll(TreeScope.Children, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.TreeItem)).Count);
                    for (int index = 0; index < count; index++)
                    {
                        root = AutomationElement.FromHandle(window); tree = CalendarTree(root, group);
                        var rows = tree.FindAll(TreeScope.Children, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.TreeItem));
                        if (index >= rows.Count) break;
                        var item = rows[index]; string name = item.Current.Name;
                        lock (gate) message = "캘린더 확인 중 · " + name;
                        if (progress != null) progress(message);
                        string previous = Address(window); SelectCalendar(item);
                        bool clicked = false;
                        string id = ""; watch.Restart();
                        while (watch.Elapsed.TotalSeconds < 5)
                        {
                            string address = Address(window);
                            if (address != previous) { id = DecodeCalendar(address); if (id != "" || address.Contains("/settings/birthdays")) break; }
                            if (!clicked && watch.ElapsedMilliseconds > 600)
                            {
                                object scroll; if (item.TryGetCurrentPattern(ScrollItemPattern.Pattern, out scroll)) try { ((ScrollItemPattern)scroll).ScrollIntoView(); } catch { }
                                var bounds = item.Current.BoundingRectangle;
                                if (!bounds.IsEmpty) Win.ClickChrome(window, (int)(bounds.Left + Math.Min(bounds.Width / 2, 90)), (int)(bounds.Top + bounds.Height / 2));
                                clicked = true;
                            }
                            Thread.Sleep(100);
                        }
                        if (id == "") { result.warnings.Add(name + " · 표시용 캘린더를 확인할 수 없습니다."); continue; }
                        if (!result.calendars.Any(c => c.id == id)) result.calendars.Add(new CalendarSource { id = id, name = name, color = Design.Colors[result.calendars.Count % Design.Colors.Length] });
                    }
                }
                if (result.calendars.Count == 0) throw new InvalidOperationException("선택 가능한 캘린더를 찾지 못했습니다. ID 입력 방식을 사용할 수도 있습니다.");
                lock (gate) { last = result; message = result.calendars.Count + "개 캘린더를 찾았습니다."; }
                return result;
            }
            catch (Exception e) { lock (gate) error = e.Message; throw; }
            finally { if (window != IntPtr.Zero && Win.IsWindow(window)) Win.PostMessage(window, 0x10, IntPtr.Zero, IntPtr.Zero); lock (gate) busy = false; }
        }
        static bool IsCalendarGroup(string name) { return name == "내 캘린더의 설정" || name == "다른 캘린더의 설정" || name == "Settings for my calendars" || name == "Settings for other calendars"; }
        static AutomationElement CalendarTree(AutomationElement root, string name) { return root.FindFirst(TreeScope.Descendants, new AndCondition(new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Tree), new PropertyCondition(AutomationElement.NameProperty, name))); }
        public static string Address(IntPtr window)
        {
            try
            {
                var root = AutomationElement.FromHandle(window);
                var address = root.FindFirst(TreeScope.Descendants, new AndCondition(new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit), new OrCondition(new PropertyCondition(AutomationElement.NameProperty, "주소창 및 검색창"), new PropertyCondition(AutomationElement.NameProperty, "Address and search bar"))));
                object pattern; return address != null && address.TryGetCurrentPattern(ValuePattern.Pattern, out pattern) ? Uri.UnescapeDataString(((ValuePattern)pattern).Current.Value) : "";
            }
            catch { return ""; }
        }
        static void SelectCalendar(AutomationElement item)
        {
            object pattern;
            if (item.TryGetCurrentPattern(InvokePattern.Pattern, out pattern)) { ((InvokePattern)pattern).Invoke(); return; }
            if (item.TryGetCurrentPattern(SelectionItemPattern.Pattern, out pattern)) { ((SelectionItemPattern)pattern).Select(); return; }
        }
    }
}
