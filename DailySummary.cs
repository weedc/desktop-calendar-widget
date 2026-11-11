using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace TeamCalendar
{
    public class DailyNotice { public string date = "", title = "", body = ""; public int count; }
    static class DailySummaryPolicy
    {
        public static DateTimeOffset Seoul(DateTimeOffset now) { return now.ToOffset(TimeSpan.FromHours(9)); }
        public static bool ValidTime(string value) { return Regex.IsMatch(value ?? "", @"^(?:[01]\d|2[0-3]):[0-5]\d$"); }
        public static bool Due(bool enabled, string time, string checkedDate, DateTimeOffset now)
        {
            var local = Seoul(now);
            return enabled && ValidTime(time) && local.ToString("HH:mm") == time && checkedDate != local.ToString("yyyy-MM-dd");
        }
        public static bool CanNotify(DateTimeOffset previous, DateTimeOffset now) { return now - previous >= TimeSpan.FromSeconds(20); }
        public static DailyNotice Build(IEnumerable<CalendarEvent> source, DateTimeOffset now)
        {
            var local = Seoul(now); string date = local.ToString("yyyy-MM-dd");
            var events = source.Where(e => e.date == date).GroupBy(e => e.id).Select(g => g.First()).OrderBy(e => e.sort).ThenBy(e => e.title).ToList();
            var notice = new DailyNotice { date = date, count = events.Count, title = "오늘 일정 · " + local.ToString("M월 d일", CultureInfo.InvariantCulture) };
            if (events.Count == 0) return notice;
            var lines = new List<string>(); int shown = 0;
            foreach (var item in events.Take(5))
            {
                DateTimeOffset start;
                string time = item.allDay ? "종일" : DateTimeOffset.TryParse(item.start, out start) ? Seoul(start).ToString("HH:mm") : item.time;
                string line = Regex.Replace(time + " " + item.title + " · " + item.calendarName, @"\s+", " ").Trim();
                if (line.Length > 90) line = line.Substring(0, 89) + "…";
                if (string.Join("\n", lines).Length + line.Length + 30 > 250) break;
                lines.Add(line); shown++;
            }
            if (shown < events.Count) lines.Add("외 " + (events.Count - shown) + "개 · 클릭하여 전체 보기");
            notice.body = string.Join("\n", lines); return notice;
        }
    }
    sealed partial class CalendarWindow
    {
        string pendingDailyDate = "", pendingDailyTime = "";
        DateTimeOffset dailyDeadline, lastSystemNotice = DateTimeOffset.MinValue;
        void ShowSystemNotice(string title, string body)
        {
            tray.ShowBalloonTip(15000, title, body, ToolTipIcon.Info);
            lastSystemNotice = DateTimeOffset.Now;
        }
        void TickDailySummary(DateTimeOffset now)
        {
            string date = DailySummaryPolicy.Seoul(now).ToString("yyyy-MM-dd");
            if (DailySummaryPolicy.Due(features.dailySummaryEnabled, features.dailySummaryTime, features.dailySummaryCheckedDate, now))
            {
                features.dailySummaryCheckedDate = date; pendingDailyDate = date; pendingDailyTime = features.dailySummaryTime; dailyDeadline = now.AddMinutes(5); SaveFeatures();
            }
            if (pendingDailyDate == "") return;
            if (!features.dailySummaryEnabled || features.dailySummaryTime != pendingDailyTime || date != pendingDailyDate || now > dailyDeadline) { pendingDailyDate = ""; return; }
            DateTime target = DateTime.ParseExact(date.Substring(0, 7) + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture);
            string monthKey = target.ToString("yyyy-MM");
            if (!cache.ContainsKey(monthKey)) LoadCachedMonth(target);
            // The central hourly scheduler supplies missing data. Never bypass disabled sync.
            if (!cache.ContainsKey(monthKey) && features.autoSyncEnabled) return;
            var notice = DailySummaryPolicy.Build(ActiveItems(), now);
            if (notice.count > 0 && !DailySummaryPolicy.CanNotify(lastSystemNotice, now)) return;
            pendingDailyDate = "";
            if (notice.count > 0) ShowSystemNotice(notice.title, notice.body);
        }
        void TestDailySummary()
        {
            var now = DateTimeOffset.Now; string date = DailySummaryPolicy.Seoul(now).ToString("yyyy-MM-dd");
            var samples = new[] {
                new CalendarEvent { id = "demo-1", date = date, title = "디자인 회의", calendarName = "예시 캘린더", start = date + "T10:00:00+09:00", sort = 600 },
                new CalendarEvent { id = "demo-2", date = date, title = "책 읽기", calendarName = "예시 캘린더", start = date + "T19:00:00+09:00", sort = 1140 }
            };
            var notice = DailySummaryPolicy.Build(samples, now);
            ShowSystemNotice("테스트 · 매일 일정 요약", notice.body);
        }
    }
}
