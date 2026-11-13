using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace TeamCalendar
{
    public class EmojiMark
    {
        public string key = "", icon = "📌", activity = "", label = "", project = "", source = "codex";
    }
    public class ProjectMark { public string calendarId = "", project = "", icon = ""; }
    enum CalendarDensity { Full, Stickers }
    static class SyncPolicy
    {
        public const int Minutes = 60;
        public static bool Due(bool enabled, DateTime attemptedUtc, DateTime nowUtc) { return enabled && (attemptedUtc == DateTime.MinValue || nowUtc - attemptedUtc >= TimeSpan.FromMinutes(Minutes)); }
    }
    static class EmojiData
    {
        public static string Key(CalendarEvent item)
        {
            string content = string.Join("\n", new[] { item.calendarId, item.calendarName, item.title, item.location }.Select(s => (s ?? "").Normalize(NormalizationForm.FormKC).Trim().ToLowerInvariant()));
            using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(content))).Replace("-", "").ToLowerInvariant();
        }
        public static CalendarDensity Density(float width, float height, bool enabled)
        {
            return enabled && width < 155 ? CalendarDensity.Stickers : CalendarDensity.Full;
        }
        public static int StickerSize(int cellHeight) { return Math.Min(26, Math.Max(16, cellHeight - 34)); }
        public static string FirstIcon(string value)
        {
            if (string.IsNullOrEmpty(value)) return "📌";
            int i = char.IsSurrogatePair(value, 0) ? 2 : 1;
            int first = char.ConvertToUtf32(value, 0);
            if (first >= 0x1f1e6 && first <= 0x1f1ff && i < value.Length)
            { int next = char.ConvertToUtf32(value, i); if (next >= 0x1f1e6 && next <= 0x1f1ff) i += char.IsSurrogatePair(value, i) ? 2 : 1; }
            while (i < value.Length)
            {
                int point = char.ConvertToUtf32(value, i), length = char.IsSurrogatePair(value, i) ? 2 : 1;
                if (point == 0xfe0f || point == 0xfe0e || point == 0x20e3 || point >= 0x1f3fb && point <= 0x1f3ff || point >= 0xe0020 && point <= 0xe007f) { i += length; continue; }
                if (point == 0x200d && i + length < value.Length) { i += length; i += char.IsSurrogatePair(value, i) ? 2 : 1; continue; }
                break;
            }
            return value.Substring(0, i);
        }
        public static bool ValidIcon(string value, bool optional = false)
        {
            if (string.IsNullOrEmpty(value)) return optional;
            if (value.Length > 32) return false;
            try
            {
                bool symbol = false;
                for (int i = 0; i < value.Length;)
                {
                    int point = char.ConvertToUtf32(value, i); var category = CharUnicodeInfo.GetUnicodeCategory(value, i);
                    if (category == UnicodeCategory.OtherSymbol || point >= 0x2600 && point <= 0x27ff || point >= 0x1f000 && point <= 0x1faff) symbol = true;
                    else if (point != 0x200d && point != 0xfe0f && point != 0xfe0e && category != UnicodeCategory.NonSpacingMark && !(point >= 0xe0020 && point <= 0xe007f) && !(point >= '0' && point <= '9' && value.Contains("\u20e3"))) return false;
                    i += char.IsSurrogatePair(value, i) ? 2 : 1;
                }
                return symbol || value.Contains("\u20e3");
            }
            catch { return false; }
        }
        public static EmojiMark Guess(CalendarEvent item)
        {
            string title = item.title ?? "", text = title.ToLowerInvariant();
            var result = new EmojiMark { key = Key(item), icon = item.isNote ? "📝" : "📌", label = item.isNote ? "메모" : "일정", source = "fallback" };
            if (Regex.IsMatch(text, "검진|병원|진료|치과|health|medical")) { result.icon = "🩺"; result.label = "검진"; }
            else if (Regex.IsMatch(text, "연차|휴가|휴무|반차|vacation|leave")) { result.icon = "🌴"; result.label = "휴가"; }
            else if (Regex.IsMatch(text, "출장|항공|비행|flight|travel|airport")) { result.icon = "✈️"; result.label = "출장"; }
            else if (Regex.IsMatch(text, "계약|서명|contract|signing")) { result.icon = "📝"; result.label = "계약"; }
            else if (Regex.IsMatch(text, "마감|제출|deadline|due date")) { result.icon = "🏁"; result.label = "마감"; }
            else if (Regex.IsMatch(text, "미술|디자인|design|artwork")) { result.icon = "🎨"; result.label = "디자인"; }
            else if (Regex.IsMatch(text, @"(?<![a-z])ai(?![a-z])|인공지능")) { result.icon = "🤖"; result.label = "AI"; result.project = "AI"; }
            else if (Regex.IsMatch(text, "생일|기념일|birthday|anniversary")) { result.icon = "🎂"; result.label = "기념일"; }
            else if (Regex.IsMatch(text, "점심|식사|저녁|lunch|dinner")) { result.icon = "🍽️"; result.label = "식사"; }
            else if (Regex.IsMatch(text, "회의|미팅|면담|meeting|interview")) { result.icon = "💬"; result.label = "회의"; }
            else if (Regex.IsMatch(text, "보고|발표|report|presentation")) { result.icon = "📊"; result.label = "보고"; }
            var project = Regex.Match(title, @"[<〈《]([^>〉》]{1,24})[>〉》]"); if (project.Success) result.project = project.Groups[1].Value.Trim();
            if (Regex.IsMatch(text, "회의|미팅|meeting|interview") && result.icon != "💬") result.activity = "💬";
            else if (Regex.IsMatch(text, "보고|발표|report|presentation") && result.icon != "📊") result.activity = "📊";
            return result;
        }
        public static EmojiMark Resolve(FeatureState state, CalendarEvent item)
        {
            string key = Key(item);
            var mark = state.emojis.FirstOrDefault(m => m.key == key) ?? Guess(item);
            var result = new EmojiMark { key = mark.key, icon = mark.icon, activity = mark.activity, label = mark.label, project = mark.project, source = mark.source };
            if (mark.source == "manual") return result;
            var project = state.projectEmojis.FirstOrDefault(p => p.calendarId == item.calendarId && (string.Equals(p.project, mark.project, StringComparison.OrdinalIgnoreCase) || p.project.Length > 1 && (item.title ?? "").IndexOf(p.project, StringComparison.OrdinalIgnoreCase) >= 0));
            if (project != null) { result.icon = project.icon; result.project = project.project; result.source = "project"; }
            return result;
        }
        public static EmojiMark Assign(FeatureState state, CalendarEvent item, string icon, string activity, string label, string project, bool manual, bool wholeProject = false)
        {
            if (!ValidIcon(icon) || !ValidIcon(activity, true)) throw new ArgumentException("이모지에는 그림 기호만 입력해 주세요.");
            if ((label ?? "").Length > 24 || (project ?? "").Length > 40) throw new ArgumentException("짧은 이름은 24자, 프로젝트는 40자까지 입력해 주세요.");
            string key = Key(item); var old = state.emojis.FirstOrDefault(m => m.key == key);
            if (!manual && old != null && old.source == "manual") return old;
            var mark = new EmojiMark { key = key, icon = icon, activity = activity ?? "", label = (label ?? "").Trim(), project = (project ?? "").Trim(), source = manual ? "manual" : "codex" };
            state.emojis.RemoveAll(m => m.key == key); state.emojis.Add(mark);
            if (manual && wholeProject && mark.project != "") { state.projectEmojis.RemoveAll(p => p.calendarId == item.calendarId && string.Equals(p.project, mark.project, StringComparison.OrdinalIgnoreCase)); state.projectEmojis.Add(new ProjectMark { calendarId = item.calendarId, project = mark.project, icon = icon }); }
            return mark;
        }
        public static string Icons(EmojiMark mark) { return mark.icon + (mark.activity == mark.icon ? "" : mark.activity); }
    }
}
