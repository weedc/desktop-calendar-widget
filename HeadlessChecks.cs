using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace TeamCalendar
{
    static class HeadlessChecks
    {
        static void Check(bool value, string name, List<string> checks) { if (!value) throw new InvalidOperationException(name); checks.Add(name); }
        public static void Run(List<string> checks)
        {
            string folder = Path.Combine(Path.GetTempPath(), "widget-browser-test-" + Guid.NewGuid().ToString("N"));
            string url = "https://calendar.google.com/calendar/u/0/r/settings?hl=ko";
            var hidden = GoogleBrowser.LaunchInfo("chrome.exe", folder, true, url);
            Check(hidden.CreateNoWindow && !hidden.UseShellExecute && hidden.Arguments.Contains("--headless=new") && hidden.Arguments.Contains("--remote-debugging-address=127.0.0.1") && !hidden.Arguments.Contains("--new-window"), "background launches are headless with a loopback-only endpoint and no console", checks);
            var login = GoogleBrowser.LaunchInfo("chrome.exe", folder, false, AccountDiscovery.LoginUrl(""));
            Check(!login.CreateNoWindow && login.Arguments.Contains("--new-window") && !login.Arguments.Contains("--remote-debugging") && !login.Arguments.Contains("--headless") && login.Arguments.Contains(folder), "explicit login uses the same dedicated profile without a debugging endpoint", checks);
            Check(!GoogleBrowser.OfficialUrl("https://calendar.google.com.example.invalid/") && !GoogleBrowser.OfficialUrl("http://calendar.google.com/") && GoogleBrowser.OfficialUrl(url), "DOM reads reject unrelated and insecure origins", checks);
            Check(GoogleBrowser.UsesProfile("chrome.exe --user-data-dir=\"" + folder + "\"", folder) && !GoogleBrowser.UsesProfile("chrome.exe --user-data-dir=\"" + folder + "-other\"", folder), "profile ownership matches an exact quoted directory rather than a path prefix", checks);
            bool rejected = false;
            using (var browser = new GoogleBrowser(folder, true)) { try { browser.Navigate(url); } catch (GoogleLoginRequired) { rejected = true; } }
            Check(rejected && !Directory.Exists(folder), "missing initial login never silently creates an interactive browser or changes a profile", checks);
            var cells = Enumerable.Range(1, 28).Select(day => (object)new Dictionary<string, object> { { "label", "일정 없음, 11월 " + day + "일" }, { "events", new object[0] } }).ToArray();
            string raw = "오후 2:30, 가상 미팅, 캘린더: 예시, 위치: 회의실, 2026년 11월 1일";
            var first = (Dictionary<string, object>)cells[0]; first["label"] = "일정 1개, 11월 1일"; first["events"] = new object[] { raw, raw };
            var snapshot = new Dictionary<string, object> { { "month", "2026-11" }, { "cells", cells } };
            var source = new CalendarSource { id = "demo@example.invalid", name = "예시", color = "#487F91" };
            var parsed = CalendarReader.Parse(snapshot, new DateTime(2026, 11, 1), source);
            Check(parsed.events.Count == 1 && parsed.events[0].title == "가상 미팅" && parsed.events[0].sort == 870 && parsed.events[0].location == "회의실" && parsed.events[0].uid == "DBD6656405DA940F708D", "headless labels retain stable occurrence IDs, locations, time parsing and deduplication", checks);
            first["label"] = "일정 3개, 11월 1일"; rejected = false;
            try { CalendarReader.Parse(snapshot, new DateTime(2026, 11, 1), source); } catch (InvalidOperationException) { rejected = true; }
            Check(rejected, "an incomplete overflow read fails instead of replacing cached events", checks);
        }
    }
}
