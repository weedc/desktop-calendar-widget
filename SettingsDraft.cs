using System;
using System.Collections.Generic;
using System.Linq;

namespace TeamCalendar
{
    // Only settings are merged back: notes, emoji choices and reminder delivery can
    // keep changing while the settings window is open.
    sealed class CalendarSettingsDraft
    {
        public readonly FeatureState Work;
        public readonly Settings Appearance;
        public readonly List<FeatureState.Profile> Profiles;
        public bool Startup, OpenCodex;
        public CalendarSettingsDraft(FeatureState state, Settings appearance, bool desktop, bool startup, IEnumerable<FeatureState.Profile> profiles = null)
        {
            Work = App.Json.Deserialize<FeatureState>(App.Json.Serialize(state));
            Appearance = App.Json.Deserialize<Settings>(App.Json.Serialize(appearance));
            Appearance.Desktop = desktop; Startup = startup;
            Profiles = (profiles ?? FeatureState.Profiles()).ToList();
        }
        static List<CalendarSource> CopySources(List<CalendarSource> values) { return values.Select(c => new CalendarSource { id = c.id, name = c.name, color = c.color, enabled = c.enabled }).ToList(); }
        static void CopyOptions(FeatureState from, FeatureState to)
        {
            to.calendars = CopySources(from.calendars); to.accountCalendars = CopySources(from.accountCalendars);
            to.chromeProfile = from.chromeProfile; to.googleUser = from.googleUser;
            to.accountProfile = from.accountProfile; to.accountEmail = from.accountEmail; to.connectionMode = from.connectionMode;
            to.autoSyncEnabled = from.autoSyncEnabled; to.compactEmojis = from.compactEmojis;
            to.dailySummaryEnabled = from.dailySummaryEnabled; to.SetDailySummaryTime(from.dailySummaryTime);
            to.remindersEnabled = from.remindersEnabled; to.allDayReminders = from.allDayReminders; to.reminderMinutes = 60;
        }
        public void MergeAdvanced(CalendarSettingsDraft child)
        {
            CopyOptions(child.Work, Work);
            Appearance.Opacity = child.Appearance.Opacity; Appearance.FontSize = child.Appearance.FontSize; Appearance.Desktop = child.Appearance.Desktop;
            Startup = child.Startup; OpenCodex = child.OpenCodex;
        }
        public void Commit(FeatureState destination) { CopyOptions(Work, destination); destination.setupCompleted = true; }
        public void UseAccountCatalog(AccountCatalog catalog)
        {
            var previous = Work.accountCalendars.GroupBy(c => c.id, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
            Work.accountCalendars = catalog.calendars.GroupBy(c => c.id, StringComparer.OrdinalIgnoreCase).Select(g =>
            {
                var source = g.First(); CalendarSource old;
                bool known = previous.TryGetValue(source.id, out old);
                return new CalendarSource { id = source.id, name = known ? old.name : source.name, color = known ? old.color : source.color, enabled = known && old.enabled };
            }).ToList();
            Work.accountProfile = catalog.profile; Work.accountEmail = catalog.email;
        }
        public bool AddId(string value)
        {
            value = (value ?? "").Trim();
            if (value.Length > 512 || !value.Contains("@") || value.Any(char.IsWhiteSpace)) return false;
            var old = Work.calendars.FirstOrDefault(c => string.Equals(c.id, value, StringComparison.OrdinalIgnoreCase));
            if (old != null) old.enabled = true;
            else Work.calendars.Add(new CalendarSource { id = value, name = Work.calendars.Count == 0 ? "내 캘린더" : "캘린더 " + (Work.calendars.Count + 1), color = Design.Colors[Work.calendars.Count % Design.Colors.Length] });
            return true;
        }
    }
}
