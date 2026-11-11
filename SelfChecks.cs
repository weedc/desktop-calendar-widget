using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;
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
                string request = CodexIntegration.EmojiRequest(executable), link = CodexIntegration.DraftLink(request);
                Require(Uri.UnescapeDataString(link.Substring(link.IndexOf("prompt=") + 7)) == request && request.Contains("emoji_pending") && request.Contains(executable), "Codex emoji draft carries each user's executable and pending-context instructions", checks);
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
                var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                Require(!SyncPolicy.Due(true, now, now.AddMinutes(59).AddSeconds(59)), "hourly synchronization never starts before 60 minutes", checks);
                Require(SyncPolicy.Due(true, now, now.AddHours(1)), "hourly synchronization becomes due at one hour", checks);
                Require(!SyncPolicy.Due(false, DateTime.MinValue, now.AddYears(1)), "disabled synchronization blocks all automatic attempts", checks);
                Require(!SyncPolicy.Due(true, now, now.AddSeconds(1)), "a failed attempt is not immediately retried", checks);
                Require(EmojiData.Density(300, 150, true) == CalendarDensity.Full && EmojiData.Density(200, 100, true) == CalendarDensity.Full && EmojiData.Density(100, 60, true) == CalendarDensity.Stickers && EmojiData.Density(300, 60, true) == CalendarDensity.Full, "only narrow cells use stickers; intermediate and short wide cells retain original text", checks);
                Require(EmojiData.Density(100, 60, false) == CalendarDensity.Full, "emoji layout can be disabled independently", checks);
                Require(EmojiData.FirstIcon("👩‍💻💬") == "👩‍💻" && EmojiData.FirstIcon("🇰🇷✈️") == "🇰🇷", "compact icons preserve joined emoji and flags", checks);
                Require(EmojiData.ValidIcon("🎨") && EmojiData.ValidIcon("👩‍💻") && EmojiData.ValidIcon("🧟") && EmojiData.ValidIcon("🦺") && !EmojiData.ValidIcon("ordinary text") && !EmojiData.ValidIcon("https://example.com"), "emoji input accepts symbols and rejects prose", checks);
                var design = new CalendarEvent { id = "qa-event", uid = "qa-event", calendarId = "qa@example.com", calendarName = "Demo", title = "<테스트프로젝트> 디자인 회의", location = "", date = "2026-01-01", time = "10:00", start = "2026-01-01T10:00:00+09:00" };
                var moved = new CalendarEvent { id = "qa-moved", calendarId = design.calendarId, calendarName = design.calendarName, title = design.title, location = "", date = "2026-01-02", time = "11:00" };
                Require(EmojiData.Key(design) == EmojiData.Key(moved), "time moves and recurring occurrences reuse a context classification", checks);
                moved.title = "다른 테스트 보고"; Require(EmojiData.Key(design) != EmojiData.Key(moved), "title changes request a new context classification", checks);
                var emojiState = new FeatureState(); var guessed = EmojiData.Guess(design);
                Require(guessed.icon == "🎨" && guessed.activity == "💬" && guessed.source == "fallback", "local context fallback works without a Codex connection", checks);
                EmojiData.Assign(emojiState, design, "🎨", "💬", "디자인회의", "테스트프로젝트", false);
                Require(EmojiData.Resolve(emojiState, design).source == "codex", "Codex classification is reused from the local cache", checks);
                EmojiData.Assign(emojiState, design, "🐣", "", "내 선택", "테스트프로젝트", true, true);
                EmojiData.Assign(emojiState, design, "🤖", "", "재분류", "테스트프로젝트", false);
                Require(EmojiData.Resolve(emojiState, design).icon == "🐣" && emojiState.emojis.Count == 1, "automatic classification preserves a manual override without duplicates", checks);
                moved.title = "<테스트프로젝트> 추가 회의";
                Require(EmojiData.Resolve(emojiState, moved).icon == "🐣", "project choice carries over to a new related event", checks);
                moved.calendarId = "other@example.com";
                Require(EmojiData.Resolve(emojiState, moved).icon != "🐣", "project emoji choices stay within their calendar", checks);
                emojiState.autoSyncEnabled = false; emojiState.compactEmojis = false; emojiState.Save(path); var loadedEmoji = FeatureState.Load(path);
                Require(!loadedEmoji.autoSyncEnabled && !loadedEmoji.compactEmojis && loadedEmoji.emojis.Count == 1 && loadedEmoji.projectEmojis.Count == 1, "sync switches and manual emoji cache survive restart", checks);
                Require(StartupRegistration.Owned("\"" + executable + "\"", Path.GetDirectoryName(executable)), "startup migration recognizes older versions in this install directory", checks);
                Require(!StartupRegistration.Owned("\"" + Path.Combine(Path.GetTempPath(), "Other", "GoogleCalendarWidget-v5.exe") + "\"", Path.GetDirectoryName(executable)) && !StartupRegistration.Owned(Path.Combine(Path.GetDirectoryName(executable), "another.exe"), Path.GetDirectoryName(executable)), "startup migration preserves unrelated applications", checks);
                Require(EmojiData.StickerSize(52) + 29 <= 52 && EmojiData.StickerSize(80) + 29 <= 80, "stickers fit the smallest six-week calendar cells", checks);
                var zero = DateTimeOffset.Parse("2026-11-10T15:00:00Z");
                Require(!DailySummaryPolicy.Due(true, "00:00", "", zero.AddSeconds(-1)) && DailySummaryPolicy.Due(true, "00:00", "", zero) && !DailySummaryPolicy.Due(true, "00:00", "", zero.AddMinutes(1)), "Windows daily summary is scheduled only in the Seoul midnight minute", checks);
                Require(!DailySummaryPolicy.Due(false, "00:00", "", zero) && !DailySummaryPolicy.Due(true, "00:00", "2026-11-11", zero), "disabled and already-checked daily summaries cannot repeat", checks);
                Require(DailySummaryPolicy.Due(true, "08:30", "", zero.AddHours(8).AddMinutes(30)) && !DailySummaryPolicy.Due(true, "08:30", "", zero) && !DailySummaryPolicy.Due(true, "08:30", "2026-11-11", zero.AddHours(8).AddMinutes(30)), "custom daily time uses Seoul time and still runs at most once per day", checks);
                Require(!DailySummaryPolicy.ValidTime("24:00") && !DailySummaryPolicy.ValidTime("8:30") && !DailySummaryPolicy.ValidTime("12:60") && DailySummaryPolicy.ValidTime("23:59"), "daily summary time accepts only valid HH:mm values", checks);
                var agenda = new[] {
                    new CalendarEvent { id = "demo-all-day", date = "2026-11-11", title = "예시 휴가", calendarName = "예시 캘린더", allDay = true, sort = -1 },
                    new CalendarEvent { id = "demo-timed", date = "2026-11-11", title = "예시 회의", calendarName = "예시 캘린더", start = "2026-11-11T10:00:00+09:00", sort = 600 },
                    new CalendarEvent { id = "other-day", date = "2026-11-12", title = "제외할 일정" }
                };
                var notice = DailySummaryPolicy.Build(agenda.Concat(agenda.Take(1)), zero);
                Require(notice.count == 2 && notice.body.Contains("종일 예시 휴가") && notice.body.Contains("10:00 예시 회의") && notice.body.Contains("예시 캘린더") && !notice.body.Contains("제외할"), "daily summary selects today's active occurrences and formats all-day and timed entries without duplicates", checks);
                Require(DailySummaryPolicy.Build(new CalendarEvent[0], zero).count == 0 && DailySummaryPolicy.Build(new CalendarEvent[0], zero).body == "", "an empty day produces no system summary", checks);
                var many = Enumerable.Range(0, 20).Select(i => new CalendarEvent { id = "demo-" + i, date = "2026-11-11", title = new string('가', 200), calendarName = "예시" });
                var large = DailySummaryPolicy.Build(many, zero);
                Require(large.count == 20 && large.body.Length <= 250 && large.body.Contains("전체 보기"), "long daily agendas fit the Windows notification and retain the remaining count", checks);
                Require(!DailySummaryPolicy.CanNotify(zero, zero.AddSeconds(19)) && DailySummaryPolicy.CanNotify(zero, zero.AddSeconds(20)), "midnight and one-hour notifications wait instead of replacing one another", checks);
                loadedEmoji.dailySummaryTime = "08:30"; loadedEmoji.dailySummaryEnabled = false; loadedEmoji.dailySummaryCheckedDate = "2026-11-11"; loadedEmoji.Save(path); var dailySaved = FeatureState.Load(path);
                Require(!dailySaved.dailySummaryEnabled && dailySaved.dailySummaryTime == "08:30" && dailySaved.dailySummaryCheckedDate == "2026-11-11", "daily summary preference and duplicate guard survive restart", checks);
                dailySaved.SetDailySummaryTime("08:30"); bool unchangedGuard = dailySaved.dailySummaryCheckedDate == "2026-11-11"; dailySaved.SetDailySummaryTime("09:00");
                Require(unchangedGuard && dailySaved.dailySummaryCheckedDate == "", "changing a daily time explicitly reschedules without resetting an unchanged schedule", checks);
                var previousMonth = new MonthData { events = new List<CalendarEvent> { new CalendarEvent { id = "old-a", calendarId = "a" }, new CalendarEvent { id = "disabled-c", calendarId = "c" } } };
                var partialMonth = new MonthData { events = new List<CalendarEvent> { new CalendarEvent { id = "new-b", calendarId = "b" } }, failedCalendars = new List<string> { "a", "c" } };
                MonthCachePolicy.PreserveFailed(partialMonth, previousMonth, new[] { "a", "b" });
                Require(partialMonth.events.Count == 2 && partialMonth.events.Any(e => e.id == "old-a") && partialMonth.events.Any(e => e.id == "new-b") && partialMonth.events.All(e => e.calendarId != "c"), "partial connection failures retain only enabled failed sources while accepting successful updates", checks);
                var successfulEmpty = new MonthData(); MonthCachePolicy.PreserveFailed(successfulEmpty, previousMonth, new[] { "a" });
                Require(successfulEmpty.events.Count == 0, "successful empty calendars do not resurrect deleted events from cache", checks);
                var fixtureCatalog = new AccountCatalog { email = "demo@example.com", calendars = new List<CalendarSource> { new CalendarSource { id = "a@example.com", name = "예시 A" }, new CalendarSource { id = "b@example.com", name = "예시 B" } } };
                using (var picker = new AccountPicker(fixtureCatalog, new List<CalendarSource> { new CalendarSource { id = "a@example.com", enabled = true } }))
                {
                    var boxes = DescendantChecks(picker).ToList();
                    Require(boxes.Count == 2 && boxes[0].Checked && !boxes[1].Checked, "account choices are real native checkboxes with the saved states", checks);
                    boxes[0].Checked = false; boxes[1].Checked = true;
                    Require(picker.SelectedCalendars.Single(c => c.enabled).id == "b@example.com", "toggling native account checkboxes changes the calendars that will be applied", checks);
                }
                SettingsChecks.Run(checks);
                return new { passed = checks.Count, checks = checks };
            }
            finally { foreach (var file in new[] { path, path + ".previous", path + ".tmp" }) if (File.Exists(file)) File.Delete(file); }
        }
        static void Require(bool condition, string name, List<string> checks) { if (!condition) throw new Exception(name); checks.Add(name); }
        static IEnumerable<CheckBox> DescendantChecks(Control parent) { foreach (Control child in parent.Controls) { if (child is CheckBox) yield return (CheckBox)child; foreach (var nested in DescendantChecks(child)) yield return nested; } }
    }
}
