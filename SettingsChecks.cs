using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

namespace TeamCalendar
{
    static class SettingsChecks
    {
        static FeatureState Fixture(bool completed = true)
        {
            var state = new FeatureState { setupCompleted = completed, connectionMode = "account", accountProfile = "demo", accountEmail = "demo@example.com" };
            state.accountCalendars.Add(new CalendarSource { id = "calendar@example.com", name = "예시 캘린더", enabled = true }); return state;
        }
        static FeatureState.Profile[] Profiles() { return new[] { new FeatureState.Profile { directory = "demo", name = "예시 계정", email = "demo@example.com" } }; }
        static IEnumerable<Control> All(Control root) { foreach (Control child in root.Controls) { yield return child; foreach (var nested in All(child)) yield return nested; } }
        static T Find<T>(Control root, string name) where T : Control { return All(root).OfType<T>().Single(c => c.AccessibleName == name); }
        static void Check(bool value, string name, List<string> checks) { if (!value) throw new Exception(name); checks.Add(name); }
        public static void Run(List<string> checks)
        {
            var owners = Fixture(); owners.accountCalendars[0].owner = "person@example.com";
            owners.accountCalendars.Add(new CalendarSource { id = "second@group.calendar.google.com", owner = "PERSON@example.com", color = "#487F91" });
            owners.accountCalendars.Add(new CalendarSource { id = "unassigned@group.calendar.google.com", color = "#487F91" });
            string connectionBeforeColor = owners.ConnectionKey();
            owners.SetSourceColor(owners.accountCalendars[0], "person@example.com", "#A35D73");
            Check(owners.accountCalendars[1].color == "#A35D73" && owners.accountCalendars[2].color == "#487F91" && connectionBeforeColor == owners.ConnectionKey(), "owner colors group accounts case-insensitively without guessing unknown calendar ownership", checks);
            var cached = new CalendarEvent { calendarId = "calendar@example.com", color = "#487F91" };
            Check(Design.EventColor(owners, cached) == Design.Parse("#A35D73") && cached.color == "#487F91", "cached events immediately use current source colors without mutating data or synchronizing Google", checks);
            var ownerDraft = new CalendarSettingsDraft(owners, new Settings(), true, false, Profiles());
            ownerDraft.UseAccountCatalog(new AccountCatalog { profile = "demo", email = "demo@example.com", calendars = new List<CalendarSource> { new CalendarSource { id = "calendar@example.com", name = "Example" } } });
            ownerDraft.Commit(owners);
            Check(owners.accountCalendars[0].owner == "person@example.com" && owners.accountCalendars[0].color == "#A35D73", "account reconnection and staged settings preserve owner colors", checks);
            using (var popup = new ThemeWindow("Synthetic popup", new System.Drawing.Size(320, 200)))
            using (var child = new ThemeWindow("Synthetic child", new System.Drawing.Size(300, 180)))
            using (var other = new Form())
            {
                child.Owner = popup; var handle = popup.Handle;
                Check(!ThemeWindow.ShouldDismiss(popup, child) && ThemeWindow.ShouldDismiss(child, popup) && ThemeWindow.ShouldDismiss(popup, other) && ThemeWindow.ShouldDismiss(popup, null), "outside activation dismisses popups while owned child dialogs preserve their parent", checks);
                Check(popup.Region != null && !popup.Region.IsVisible(0, 0) && popup.Region.IsVisible(160, 100), "popup rounded corners are applied on first handle creation", checks);
            }
            var original = Fixture(); var appearance = new Settings { Opacity = 82, FontSize = 11, Desktop = true };
            var draft = new CalendarSettingsDraft(original, appearance, true, true, Profiles());
            draft.Work.accountCalendars[0].enabled = false; draft.Appearance.Opacity = 55; draft.Work.autoSyncEnabled = false;
            Check(original.accountCalendars[0].enabled && original.autoSyncEnabled && appearance.Opacity == 82, "calendar and appearance edits stay in a draft until final apply", checks);
            original.notes.Add(new LocalNote { id = "late-note", text = "예시 메모", date = "2026-11-11" });
            original.emojis.Add(new EmojiMark { key = "late-emoji", icon = "🎨" });
            original.reminders.Add(new ReminderRecord { id = "late-reminder", desktopShown = true });
            original.dailySummaryCheckedDate = "2026-11-11";
            draft.Commit(original);
            Check(!original.accountCalendars[0].enabled && !original.autoSyncEnabled && original.notes.Count == 1 && original.emojis.Count == 1 && original.reminders[0].desktopShown && original.dailySummaryCheckedDate == "2026-11-11", "apply updates settings without losing notes, emoji choices or notification delivery recorded while editing", checks);
            original = Fixture(); draft = new CalendarSettingsDraft(original, appearance, true, true, Profiles());
            using (var advanced = new CalendarAdvancedSettings(draft, delegate { }))
            {
                Find<CheckBox>(advanced, "1시간마다 자동 동기화").Checked = false;
                Find<TextBox>(advanced, "일정 요약 시각 HH:mm (한국 시간)").Text = "24:00";
                Check(!advanced.ReturnToSettings() && draft.Work.autoSyncEnabled && original.autoSyncEnabled, "invalid advanced values cannot leak partial changes into the pending or live settings", checks);
                Find<TextBox>(advanced, "일정 요약 시각 HH:mm (한국 시간)").Text = "08:30";
                Check(advanced.ReturnToSettings() && !draft.Work.autoSyncEnabled && draft.Work.dailySummaryTime == "08:30" && original.autoSyncEnabled && original.dailySummaryTime == "00:00", "returning from advanced settings stages changes without applying them to the widget", checks);
            }
            using (var advanced = new CalendarAdvancedSettings(draft, delegate { }))
            { Find<CheckBox>(advanced, "1시간마다 자동 동기화").Checked = true; advanced.Close(); Check(!draft.Work.autoSyncEnabled, "canceling advanced settings discards only that dialog's edits", checks); }
            original = Fixture(false);
            using (var manager = new CalendarManager(original, appearance, true, false, delegate { }, delegate { }, true, Profiles()))
            {
                Check(manager.Text == "캘린더 시작하기" && All(manager).OfType<SoftButton>().Any(c => c.Text == "시작하기") && !All(manager).OfType<CheckBox>().Any(c => c.AccessibleName == "1시간마다 자동 동기화") && !All(manager).OfType<SoftButton>().Any(c => c.Text == "켜기 / 끄기" || c.Text == "Google 계정 열기" || c.Text == "캘린더 불러오기"), "first setup exposes connection, calendar selection and start while advanced options and duplicate buttons are absent", checks);
                Find<CheckBox>(manager, "예시 캘린더 표시").Checked = false;
                Check(!manager.TryApply() && original.accountCalendars[0].enabled && !original.setupCompleted, "first setup requires a selected calendar and does not mark setup complete before start", checks);
                Find<CheckBox>(manager, "예시 캘린더 표시").Checked = true;
                Check(manager.TryApply() && original.setupCompleted, "start commits the chosen calendars and completes first setup", checks);
            }
            original = Fixture();
            using (var manager = new CalendarManager(original, appearance, true, false, delegate { }, delegate { }, false, Profiles()))
            {
                Find<CheckBox>(manager, "예시 캘린더 표시").Checked = false;
                Check(original.accountCalendars[0].enabled, "clicking a calendar checkbox does not immediately change the running widget", checks);
                Check(manager.TryApply() && !original.accountCalendars[0].enabled, "normal settings allow disabling every calendar when apply is pressed", checks);
            }
            original = Fixture();
            using (var manager = new CalendarManager(original, appearance, true, false, delegate { }, delegate { }, false, Profiles()))
            { Find<CheckBox>(manager, "예시 캘린더 표시").Checked = false; manager.Close(); }
            Check(original.accountCalendars[0].enabled, "closing the main settings window without apply discards selection changes", checks);
            original = Fixture(); original.accountCalendars[0].enabled = false;
            draft = new CalendarSettingsDraft(original, appearance, true, false, Profiles());
            draft.UseAccountCatalog(new AccountCatalog { profile = "demo", email = "demo@example.com", calendars = new List<CalendarSource> { new CalendarSource { id = "calendar@example.com", name = "Google name" }, new CalendarSource { id = "new@example.com", name = "새 예시 캘린더" } } });
            Check(draft.Work.accountCalendars.All(c => !c.enabled) && draft.Work.accountCalendars[0].name == "예시 캘린더" && original.accountCalendars.Count == 1, "reconnecting preserves unchecked choices and local names and stages new calendars for selection", checks);
            original = new FeatureState { connectionMode = "id" };
            using (var manager = new CalendarManager(original, appearance, true, false, delegate { }, delegate { }, true, Profiles()))
            {
                Find<RadioButton>(manager, "ID 입력 방식").Checked = true;
                Find<TextBox>(manager, "구글 캘린더 ID").Text = "demo@group.calendar.google.com";
                Check(manager.TryApply() && original.calendars.Count == 1 && original.connectionMode == "id", "pasting a calendar ID and pressing start adds it without requiring a separate add click", checks);
            }
            Check(EmojiData.Density(154.9F, 140, true) == CalendarDensity.Stickers && EmojiData.Density(155, 140, true) == CalendarDensity.Full && EmojiData.Density(200, 45, true) == CalendarDensity.Full, "the compact-width boundary never substitutes summary labels for original text", checks);
        }
    }
}
