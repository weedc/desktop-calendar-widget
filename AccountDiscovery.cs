using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

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
        public static string LoginUrl(string email) { return "https://accounts.google.com/ServiceLogin?hl=ko&service=cl&continue=" + Uri.EscapeDataString(SettingsUrl(email)); }
        public static void OpenLogin(string profile, string email) { GoogleBrowser.Shared.OpenLogin(email); }
        public static AccountCatalog Read(string profile, string email, Action<string> progress)
        {
            lock (gate) { if (busy) throw new InvalidOperationException("이미 계정의 캘린더를 불러오는 중입니다."); busy = true; message = "전용 Google 연결 확인 중…"; error = ""; last = null; }
            try
            {
                var browser = GoogleBrowser.Shared;
                lock (browser.Gate)
                {
                    browser.Navigate(SettingsUrl(email, Guid.NewGuid().ToString("N")));
                    Dictionary<string, object> catalog = null; object[] rows = new object[0]; var watch = Stopwatch.StartNew();
                    while (watch.ElapsedMilliseconds < 30000)
                    {
                        catalog = browser.Script("catalog", null) as Dictionary<string, object>;
                        if (catalog != null && catalog.ContainsKey("rows")) rows = GoogleDom.Array(catalog["rows"]);
                        if (rows.Length > 0 && App.Str(catalog, "email") != "") break;
                        Thread.Sleep(350);
                    }
                    if (catalog == null || rows.Length == 0 || App.Str(catalog, "email") == "") throw new InvalidOperationException("캘린더 목록을 읽지 못했습니다. ‘Google 로그인 열기’에서 계정과 접근 권한을 확인해 주세요.");
                    var result = new AccountCatalog { profile = profile, email = App.Str(catalog, "email") };
                    if (email != "" && !string.Equals(email, result.email, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("요청한 계정과 로그인한 계정이 다릅니다. Google 로그인에서 사용할 계정을 선택해 주세요.");
                    foreach (Dictionary<string, object> row in rows.Take(250))
                    {
                        string name = App.Str(row, "label");
                        lock (gate) message = "캘린더 확인 중 · " + name;
                        if (progress != null) progress(message);
                        string before = Convert.ToString(browser.Script("route", null));
                        if (!Convert.ToBoolean(browser.Script("select", row))) { result.warnings.Add(name); continue; }
                        string id = ""; watch.Restart();
                        while (watch.ElapsedMilliseconds < 4000)
                        {
                            string route = Convert.ToString(browser.Script("route", null));
                            if (route != before) { id = DecodeCalendar(route); if (id != "" || route.Contains("/settings/birthdays")) break; }
                            Thread.Sleep(150);
                        }
                        if (id == "") { result.warnings.Add(name + " · 표시용 캘린더를 확인할 수 없습니다."); continue; }
                        if (!result.calendars.Any(c => c.id == id)) result.calendars.Add(new CalendarSource { id = id, name = name, color = Design.Colors[result.calendars.Count % Design.Colors.Length] });
                    }
                    if (result.calendars.Count == 0) throw new InvalidOperationException("선택 가능한 캘린더를 찾지 못했습니다. ID 입력 방식을 사용할 수도 있습니다.");
                    browser.MarkVerified();
                    lock (gate) { last = result; message = result.calendars.Count + "개 캘린더를 찾았습니다."; }
                    return result;
                }
            }
            catch (Exception e) { lock (gate) error = e.Message; throw; }
            finally { lock (gate) busy = false; }
        }
    }
}
