using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
namespace TeamCalendar
{
    static class SelfChecks
    {
        public static object Run()
        {
            var checks = new List<string>(); string path = Path.Combine(Path.GetTempPath(), "calendar-widget-qa-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                var fresh = FeatureState.Load(path);
                Require(!fresh.setupCompleted, "fresh install requests setup", checks);
                fresh.setupCompleted = true;
                fresh.notes.Add(new LocalNote { id = "qa", date = "2026-11-11", text = "test", color = "#8A649E" });
                fresh.Save(path);
                var reopened = FeatureState.Load(path);
                Require(reopened.setupCompleted && reopened.notes.Any(n => n.id == "qa"), "setup flag and notes survive restart", checks);
                File.WriteAllText(path, "{\"calendars\":[{\"id\":\"qa@example.com\",\"name\":\"QA\",\"enabled\":true}],\"notes\":[{\"id\":\"legacy\",\"date\":\"2026-11-11\",\"text\":\"preserve\"}]}");
                var legacy = FeatureState.Load(path);
                Require(legacy.setupCompleted && legacy.notes.Any(n => n.id == "legacy") && legacy.calendars[0].id == "qa@example.com", "existing installs migrate without another setup prompt or data loss", checks);
                Require(DateTimeOffset.Parse(Design.Start("2026-11-11", "오전 12시", false)).Hour == 0, "midnight is parsed correctly", checks);
                Require(DateTimeOffset.Parse(Design.Start("2026-11-11", "오후 12시", false)).Hour == 12, "noon is parsed correctly", checks);
                var timed = DateTimeOffset.Parse(Design.Start("2026-11-11", "14:30", false));
                Require(timed.Hour == 14 && timed.Minute == 30 && timed.Offset == TimeSpan.FromHours(9), "local note time uses Asia Seoul", checks);
                var midnight = DateTimeOffset.Parse(Design.Start("2026-11-11", "00:30", false));
                Require(midnight.AddHours(-1).Day == 10 && midnight.AddHours(-1).Hour == 23, "one-hour reminder crosses the date boundary correctly", checks);
                string executable = Path.Combine(Path.GetTempPath(), "Calendar Widget", "GoogleCalendarWidget-v5.exe");
                string other = "model = \"example\"\n[mcp_servers.other]\ncommand = \"untouched\"\nargs = [\"one\"]\n";
                string registered = CodexIntegration.Rewrite(other, executable, true);
                Require(registered.StartsWith(other.TrimEnd()) && registered.Contains("--mcp"), "Codex registration preserves other settings", checks);
                Require(CodexIntegration.Rewrite(registered, executable, true) == registered, "Codex registration is idempotent", checks);
                string disabled = CodexIntegration.Rewrite(registered, executable, false);
                Require(disabled.Contains("enabled = false") && disabled.StartsWith(other.TrimEnd()), "Codex disconnect affects only this widget", checks);
                string upgraded = CodexIntegration.Rewrite(disabled, executable.Replace("v5.exe", "v5.1.exe"), true);
                Require(upgraded.Contains("v5.1.exe") && !upgraded.Contains("enabled = false"), "Codex connection upgrades and re-enables safely", checks);
                bool rejected = false; try { CodexIntegration.Rewrite("[mcp_servers.team_calendar_widget]\ncommand = \"foreign.exe\"\nargs = [\"--mcp\"]\n", executable, true); } catch (InvalidOperationException) { rejected = true; }
                Require(rejected, "Codex registration refuses a foreign server name collision", checks);
                string request = CodexIntegration.DailyPrompt("00:00", executable), link = CodexIntegration.DraftLink(request);
                Require(Uri.UnescapeDataString(link.Substring(link.IndexOf("prompt=") + 7)) == request && request.Contains("00:00") && request.Contains(executable), "Codex setup draft carries each user's executable and midnight time", checks);
                Require(!CodexIntegration.ValidTime("24:00") && !CodexIntegration.ValidTime("1:00") && CodexIntegration.ValidTime("09:30"), "Codex notification time is validated", checks);
                var modes = new FeatureState { chromeProfile = "Manual profile", googleUser = "manual@example.com", accountProfile = "Account profile", accountEmail = "account@example.com" };
                modes.calendars.Add(new CalendarSource { id = "same@example.com", name = "Manual source" });
                modes.accountCalendars.Add(new CalendarSource { id = "same@example.com", name = "Account source" });
                modes.accountCalendars.Add(new CalendarSource { id = "same@example.com", name = "Duplicate" });
                Require(modes.ActiveCalendars().Count == 1 && modes.ActiveCalendars()[0].name == "Manual source", "ID mode excludes overlapping account sources", checks);
                modes.connectionMode = "account";
                Require(modes.ActiveCalendars().Count == 1 && modes.ActiveCalendars()[0].name == "Account source" && modes.ActiveProfile() == "Account profile", "account mode excludes ID sources and deduplicates its own list", checks);
                modes.Save(path); var storedModes = FeatureState.Load(path); storedModes.connectionMode = "id";
                Require(storedModes.calendars.Count == 1 && storedModes.accountCalendars.Count == 2 && storedModes.ActiveProfile() == "Manual profile" && storedModes.ActiveUser() == "manual@example.com", "switching modes preserves each connection and profile", checks);
                string calendarId = "shared-team@group.calendar.google.com", encoded = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(calendarId)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
                Require(AccountDiscovery.DecodeCalendar("https://calendar.google.com/calendar/u/0/r/settings/calendar/" + encoded) == calendarId, "Google settings route resolves calendar ID without manual entry", checks);
                Require(AccountDiscovery.DecodeCalendar("https://example.invalid/settings/calendar/invalid") == "" && AccountDiscovery.DecodeCalendar("https://calendar.google.com/calendar/u/0/r/settings") == "", "invalid account calendar routes are rejected", checks);
                return new { passed = checks.Count, checks = checks };
            }
            finally { foreach (var file in new[] { path, path + ".previous", path + ".tmp" }) if (File.Exists(file)) File.Delete(file); }
        }
        static void Require(bool condition, string name, List<string> checks) { if (!condition) throw new Exception(name); checks.Add(name); }
    }
}
