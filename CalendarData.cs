using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace TeamCalendar
{
    public class CalendarSource
    {
        public string id = "", name = "", color = "#68BCCB";
        public bool enabled = true;
    }
    public class LocalNote
    {
        public string id = "", date = "", text = "", time = "", color = "#487F91";
        public bool important;
    }
    public class Accent
    {
        public string key = "", color = "#A47B45";
    }
    public class ReminderRecord
    {
        public string id, key, title, start, remindAt, calendar, calendarId;
        public bool desktopShown, dotShown;
    }
    public class FeatureState
    {
        public List<CalendarSource> calendars = new List<CalendarSource>();
        public List<CalendarSource> accountCalendars = new List<CalendarSource>();
        public string connectionMode = "id", accountProfile = "", accountEmail = "";
        public List<CalendarSource> ConnectionSources() { return connectionMode == "account" ? accountCalendars : calendars; }
        public List<CalendarSource> ActiveCalendars() { return ConnectionSources().GroupBy(c => c.id, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).ToList(); }
        public string ActiveProfile() { return connectionMode == "account" ? accountProfile : chromeProfile; }
        public string ActiveUser() { return connectionMode == "account" ? accountEmail : googleUser; }
        public List<LocalNote> notes = new List<LocalNote>();
        public List<Accent> highlights = new List<Accent>();
        public List<Accent> days = new List<Accent>();
        public List<ReminderRecord> reminders = new List<ReminderRecord>();
        public List<EmojiMark> emojis = new List<EmojiMark>();
        public List<ProjectMark> projectEmojis = new List<ProjectMark>();
        public bool compactEmojis = true, autoSyncEnabled = true;
        public bool dailySummaryEnabled = true;
        public string dailySummaryCheckedDate = "", dailySummaryTime = "00:00";
        public void SetDailySummaryTime(string value) { if (dailySummaryTime != value) dailySummaryCheckedDate = ""; dailySummaryTime = value; }
        public string chromeProfile = "", googleUser = "";
        public bool remindersEnabled = true, allDayReminders = false;
        public int reminderMinutes = 60;
        public bool setupCompleted;
        public string dotReminderTime = "00:00", dotAutomationId = "";
        public static readonly string FilePath = Path.Combine(App.DataDir, "calendar-data.json");
        public static FeatureState Load(string filePath = null)
        {
            filePath = filePath ?? FilePath;
            try
            {
                if (File.Exists(filePath))
                {
                    string json = File.ReadAllText(filePath, Encoding.UTF8);
                    var existing = App.Json.Deserialize<FeatureState>(json);
                    if (!json.Contains("\"setupCompleted\"")) { existing.setupCompleted = true; existing.Save(filePath); }
                    return existing;
                }
            }
            catch (Exception e) { App.Log("Calendar data: " + e.Message); }
            var state = new FeatureState();
            return state;
        }
        public void Save(string filePath = null)
        {
            filePath = filePath ?? FilePath;
            Directory.CreateDirectory(App.DataDir);
            File.WriteAllText(filePath + ".tmp", App.Json.Serialize(this), new UTF8Encoding(false));
            if (File.Exists(filePath)) File.Replace(filePath + ".tmp", filePath, filePath + ".previous"); else File.Move(filePath + ".tmp", filePath);
        }
        public sealed class Profile { public string directory, name, email; public override string ToString() { return name + (email == "" ? "" : " · " + email); } }
    }
    sealed class MultiCalendarReader : IDisposable
    {
        FeatureState state;
        readonly List<CalendarReader> readers = new List<CalendarReader>();
        public MultiCalendarReader(FeatureState features) { Reconfigure(features); }
        public IntPtr Window { get { return readers.Count == 0 ? IntPtr.Zero : readers[0].Window; } }
        public void Reconfigure(FeatureState features)
        {
            Dispose(); state = features;
            foreach (var source in state.ActiveCalendars().Where(s => s.enabled)) readers.Add(new CalendarReader(source));
        }
        public MonthData Read(DateTime month, bool force)
        {
            var data = new MonthData { month = month.ToString("yyyy-MM"), syncedAt = DateTimeOffset.Now.ToString("o") };
            var failures = new List<string>();
            foreach (var reader in readers)
            {
                try { data.events.AddRange(reader.Read(month, force).events); }
                catch (Exception e) { failures.Add(reader.Source.name + ": " + e.Message); data.failedCalendars.Add(reader.Source.id); }
            }
            if (readers.Count > 0 && failures.Count == readers.Count) throw new InvalidOperationException(string.Join(" / ", failures));
            data.warnings = failures;
            data.events = data.events.OrderBy(e => e.date).ThenBy(e => e.sort).ThenBy(e => e.title).ToList();
            data.syncedAt = DateTimeOffset.Now.ToString("o");
            return data;
        }
        public void Dispose() { foreach (var reader in readers) reader.Dispose(); readers.Clear(); }
    }
    static class Design
    {
        public static readonly Color Background = Color.FromArgb(22, 43, 56), Panel = Color.FromArgb(30, 55, 70), Text = Color.FromArgb(231, 243, 248), Muted = Color.FromArgb(158, 186, 201);
        public static readonly string[] Colors = { "#487F91", "#536CA2", "#8A649E", "#A35D73", "#A47B45", "#628760" };
        public static Color Parse(string value)
        {
            try { if (Regex.IsMatch(value ?? "", "^#[0-9a-fA-F]{6}$")) return ColorTranslator.FromHtml(value); } catch { }
            return ColorTranslator.FromHtml(Colors[0]);
        }
        public static GraphicsPath Round(RectangleF rect, float radius)
        {
            float d = Math.Min(radius * 2, Math.Min(rect.Width, rect.Height)); var path = new GraphicsPath();
            path.AddArc(rect.Left, rect.Top, d, d, 180, 90); path.AddArc(rect.Right - d, rect.Top, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90); path.AddArc(rect.Left, rect.Bottom - d, d, d, 90, 90); path.CloseFigure(); return path;
        }
        public static string Start(string date, string time, bool allDay)
        {
            DateTime day; if (!DateTime.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out day)) return "";
            if (allDay) return new DateTimeOffset(day.AddHours(9), TimeSpan.FromHours(9)).ToString("o");
            var match = Regex.Match(time ?? "", @"(오전|오후|AM|PM)\s*(\d{1,2})(?::(\d{2}))?", RegexOptions.IgnoreCase);
            if (!match.Success)
            {
                match = Regex.Match(time ?? "", @"(?<!\d)(\d{1,2}):(\d{2})(?!\d)");
                if (!match.Success) return "";
                return new DateTimeOffset(day.AddHours(int.Parse(match.Groups[1].Value)).AddMinutes(int.Parse(match.Groups[2].Value)), TimeSpan.FromHours(9)).ToString("o");
            }
            int hour = int.Parse(match.Groups[2].Value) % 12 + (match.Groups[1].Value == "오후" || match.Groups[1].Value.ToUpperInvariant() == "PM" ? 12 : 0);
            return new DateTimeOffset(day.AddHours(hour).AddMinutes(match.Groups[3].Success ? int.Parse(match.Groups[3].Value) : 0), TimeSpan.FromHours(9)).ToString("o");
        }
    }
}
