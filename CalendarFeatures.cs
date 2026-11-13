using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace TeamCalendar
{
    sealed partial class CalendarWindow
    {
        DayDetails dayDetails;
        CalendarManager calendarManager;
        CodexDialog codexDialog;
        bool reloadCalendars, pendingForceRefresh;
        DateTime reminderCheck = DateTime.MinValue, lastBalloon = DateTime.MinValue;
        void InitializeFeatures()
        {
            menu.Items.Insert(0, new ToolStripMenuItem("캘린더 ID · 계정 · 알림 설정", null, delegate { ShowCalendarManager(); }));
            menu.Items.Insert(1, new ToolStripSeparator());
            menu.Items.Insert(1, new ToolStripMenuItem("Codex · Dot 연동", null, delegate { ShowCodexDialog(); }));
            tray.BalloonTipShown += delegate { lastBalloon = DateTime.Now; App.Log("Windows notification shown."); };
            tray.BalloonTipClicked += delegate { if (OpenUpdateNotice()) return; ShowWidget(); ShowDay(DailySummaryPolicy.Seoul(DateTimeOffset.Now).Date); };
            InitializeUpdates();
            FormClosed += delegate { if (dayDetails != null && !dayDetails.IsDisposed) dayDetails.Close(); if (codexDialog != null && !codexDialog.IsDisposed) codexDialog.Close(); if (emojiDialog != null && !emojiDialog.IsDisposed) emojiDialog.Close(); };
            UpdateCalendarTitle();
        }
        void UpdateCalendarTitle()
        {
            var sources = features.ActiveCalendars().Where(s => s.enabled).ToList();
            string title = sources.Count == 1 ? sources[0].name : sources.Count == 0 ? "내 캘린더" : "내 캘린더 · " + sources.Count;
            nameLabel.Text = "▦  " + title; Text = "투명 캘린더 · " + title;
        }
        void SaveFeatures() { features.Save(); Invalidate(); }
        void ShowCalendarManager()
        {
            ShowCalendarManager(false);
        }
        void ShowCalendarManager(bool initialSetup)
        {
            if (calendarManager != null && !calendarManager.IsDisposed) { calendarManager.Activate(); return; }
            string before = features.ConnectionKey();
            var dialog = new CalendarManager(features, settings, pinned, StartupRegistration.ForSetup(features.setupCompleted), ShowCodexDialog, TestDailySummary, initialSetup);
            calendarManager = dialog;
            dialog.FormClosed += delegate
            {
                calendarManager = null;
                if (dialog.DialogResult == DialogResult.OK)
                {
                    if (dialog.QuitRequested) { Close(); return; }
                    settings.FontSize = dialog.FontSizeValue; Font = new Font("맑은 고딕", settings.FontSize); SetOpacity(dialog.OpacityValue);
                    if (pinned != dialog.DesktopValue) Pin(dialog.DesktopValue);
                    StartupRegistration.Set(dialog.StartupValue);
                    features.Save(); Save();
                    if (dialog.ConnectionChanged || before != features.ConnectionKey()) ReconfigureCalendars();
                    else { UpdateCalendarTitle(); Invalidate(); }
                    if (dialog.OpenCodexAfterApply) BeginInvoke((Action)ShowCodexDialog);
                }
                dialog.Dispose();
            };
            dialog.Show();
        }
        void ShowCodexDialog()
        {
            if (codexDialog != null && !codexDialog.IsDisposed) { codexDialog.Activate(); return; }
            codexDialog = new CodexDialog(features); codexDialog.Show();
        }
        void ReconfigureCalendars()
        {
            cache.Clear(); syncAttempts.Clear(); LoadCachedMonth(month); UpdateCalendarTitle(); Invalidate();
            if (loading) { reloadCalendars = true; return; }
            reader.Reconfigure(features); RefreshMonth(true, null, true);
        }
        void ShowDay(DateTime day)
        {
            if (dayDetails == null || dayDetails.IsDisposed) dayDetails = new DayDetails(this);
            dayDetails.SetDay(day);
            if (!dayDetails.Visible)
            {
                var area = Screen.FromControl(this).WorkingArea;
                int x = PointToScreen(Point.Empty).X - dayDetails.Width - 12;
                dayDetails.Location = new Point(Math.Max(area.Left + 12, x), Math.Max(area.Top + 24, Math.Min(area.Bottom - dayDetails.Height - 12, PointToScreen(Point.Empty).Y)));
                dayDetails.Show();
            }
            dayDetails.Activate();
        }
        internal FeatureState FeatureData { get { return features; } }
        internal List<CalendarEvent> DayEvents(DateTime day)
        {
            string date = day.ToString("yyyy-MM-dd");
            return cache.Values.SelectMany(v => v.events).Concat(NoteEvents()).Where(e => e.date == date).GroupBy(e => e.id).Select(g => g.First()).OrderBy(e => e.sort).ToList();
        }
        internal void FeatureEdited() { SaveFeatures(); }
        internal void ToggleDay(DateTime day, string color)
        {
            string date = day.ToString("yyyy-MM-dd"); var old = features.days.FirstOrDefault(a => a.key == date);
            if (old != null) features.days.Remove(old); else features.days.Add(new Accent { key = date, color = color }); SaveFeatures();
        }
        internal void ToggleImportant(CalendarEvent item, string color)
        {
            var old = features.highlights.FirstOrDefault(a => a.key == item.uid);
            if (old != null) features.highlights.Remove(old); else features.highlights.Add(new Accent { key = item.uid, color = color }); SaveFeatures();
        }
        internal bool IsImportant(CalendarEvent item) { return features.highlights.Any(a => a.key == item.uid) || item.isNote && features.notes.Any(n => "note-" + n.id == item.uid && n.important); }
        Color? DayColor(string date, List<CalendarEvent> events)
        {
            var day = features.days.FirstOrDefault(a => a.key == date); if (day != null) return Design.Parse(day.color);
            foreach (var item in events.Where(e => e.date == date))
            {
                var accent = features.highlights.FirstOrDefault(a => a.key == item.uid);
                if (accent != null) return Design.Parse(accent.color);
                if (item.isNote && IsImportant(item)) return Design.Parse(item.color);
            }
            return null;
        }
        List<CalendarEvent> NoteEvents()
        {
            return features.notes.Select(n => new CalendarEvent { id = "note-" + n.id, uid = "note-" + n.id, date = n.date, title = n.text, time = n.time == "" ? "메모" : n.time, location = "", raw = n.text, sort = n.time == "" ? -2 : int.Parse(n.time.Substring(0, 2)) * 60 + int.Parse(n.time.Substring(3, 2)), color = n.color, calendarId = "local", calendarName = "내 메모", isNote = true, allDay = n.time == "", start = n.time == "" ? "" : Design.Start(n.date, n.time, false) }).ToList();
        }
        LocalNote UpsertNote(Dictionary<string, object> args)
        {
            string id = App.Str(args, "id"); var note = features.notes.FirstOrDefault(n => n.id == id);
            bool created = note == null;
            if (created) note = new LocalNote { id = Guid.NewGuid().ToString("N") };
            string date = App.Str(args, "date", note.date), text = App.Str(args, "text", note.text).Trim(), time = App.Str(args, "time", note.time);
            DateTime.ParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture);
            if (text == "" || text.Length > 2000) throw new ArgumentException("메모는 1~2000자로 입력해 주세요.");
            if (time != "" && !Regex.IsMatch(time, @"^(?:[01]\d|2[0-3]):[0-5]\d$")) throw new ArgumentException("시간은 HH:mm 형식으로 입력해 주세요.");
            note.date = date; note.text = text; note.time = time; note.color = App.Str(args, "color", note.color);
            note.important = App.Str(args, "important", note.important.ToString()).ToLowerInvariant() == "true";
            if (created) features.notes.Add(note); SaveFeatures();
            if (dayDetails != null && !dayDetails.IsDisposed) dayDetails.RefreshItems();
            return note;
        }
        void FeaturePulse()
        {
            if ((DateTime.Now - reminderCheck).TotalSeconds >= 10) { reminderCheck = DateTime.Now; TickDailySummary(DateTimeOffset.Now); TickReminders(); }
            TickUpdates();

        }
        List<ReminderRecord> CalculateReminders(DateTimeOffset now)
        {
            var list = new List<ReminderRecord>(); if (!features.remindersEnabled) return list;
            foreach (var item in cache.Values.SelectMany(d => d.events).Concat(NoteEvents()).GroupBy(e => e.uid + "|" + e.start).Select(g => g.First()))
            {
                DateTimeOffset start; if (!DateTimeOffset.TryParse(item.start, out start) || item.allDay && !features.allDayReminders) continue;
                DateTimeOffset remind = start.AddMinutes(-features.reminderMinutes);
                if (now < remind || now >= start) continue;
                list.Add(new ReminderRecord { id = item.uid + "|" + start.ToString("o"), key = item.uid, title = item.title, calendar = item.calendarName, calendarId = item.calendarId, start = start.ToString("o"), remindAt = remind.ToString("o") });
            }
            return list.OrderBy(r => r.start).ToList();
        }
        void TickReminders()
        {
            if (!features.remindersEnabled) return;
            bool changed = false;
            foreach (var record in CalculateReminders(DateTimeOffset.Now)) if (!features.reminders.Any(r => r.id == record.id)) { features.reminders.Add(record); changed = true; }
            var pending = PendingReminders().Where(r => !r.desktopShown).ToList();
            if (pending.Count > 0 && DailySummaryPolicy.CanNotify(lastSystemNotice, DateTimeOffset.Now))
            {
                string body = string.Join("\n", pending.Take(5).Select(r => DateTimeOffset.Parse(r.start).ToString("HH:mm") + "  " + r.title));
                int remaining = Math.Max(1, (int)Math.Ceiling((DateTimeOffset.Parse(pending[0].start) - DateTimeOffset.Now).TotalMinutes));
                ShowSystemNotice(pending.Count == 1 ? "일정 " + Math.Min(features.reminderMinutes, remaining) + "분 전" : "다가오는 일정 " + pending.Count + "개", body.Length > 250 ? body.Substring(0, 247) + "…" : body);
                foreach (var item in pending) item.desktopShown = true; changed = true;
            }
            if (changed) SaveFeatures();
        }
        List<ReminderRecord> PendingReminders()
        {
            var active = new HashSet<string>(features.ActiveCalendars().Where(c => c.enabled).Select(c => c.id)); active.Add("local");
            return features.reminders.Where(r => active.Contains(r.calendarId) && DateTimeOffset.Parse(r.start) > DateTimeOffset.Now && DateTimeOffset.Parse(r.remindAt) <= DateTimeOffset.Now).OrderBy(r => r.start).ToList();
        }
        bool TryFeatureCommand(Dictionary<string, object> args, out object result)
        {
            result = null; string command = App.Str(args, "command");
            if (command == "status") result = new { calendarId = features.ActiveCalendars().FirstOrDefault(s => s.enabled) == null ? "" : features.ActiveCalendars().First(s => s.enabled).id, calendars = features.ActiveCalendars(), connectionMode = features.connectionMode, calendar = nameLabel.Text, month = month.ToString("yyyy-MM"), loading = loading, autoSyncEnabled = features.autoSyncEnabled, autoSyncMinutes = SyncPolicy.Minutes, startupEnabled = StartupEnabled(), desktopShortcutExists = File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "팀 캘린더 위젯.lnk")), compactEmojis = features.compactEmojis, density = EmojiData.Density(GridBounds.Width / 7F, GridBounds.Height / (float)rowCount, features.compactEmojis).ToString(), syncStarted = syncStarted, syncCompleted = syncCompleted, message = SyncStatus, opacity = settings.Opacity, desktop = pinned, visible = Visible, processId = System.Diagnostics.Process.GetCurrentProcess().Id, windowId = Handle.ToInt64(), readerWindowId = reader.Window.ToInt64(), readerMode = "existing_chrome_hidden", bounds = new { x = PointToScreen(Point.Empty).X, y = PointToScreen(Point.Empty).Y, width = Width, height = Height }, noteCount = features.notes.Count, selectedDay = dayDetails == null || dayDetails.IsDisposed ? null : dayDetails.Day.ToString("yyyy-MM-dd"), detailsWindowId = dayDetails == null || dayDetails.IsDisposed ? 0 : dayDetails.Handle.ToInt64(), settingsWindowId = calendarManager == null || calendarManager.IsDisposed ? 0 : calendarManager.Handle.ToInt64(), reminderMinutes = features.reminderMinutes, remindersEnabled = features.remindersEnabled, lastBalloonShown = lastBalloon == DateTime.MinValue ? null : lastBalloon.ToString("o") };
            else if (command == "events")
            {
                string key = App.Str(args, "month", month.ToString("yyyy-MM")); MonthData data;
                if (cache.TryGetValue(key, out data)) result = new { calendarId = features.ActiveCalendars().FirstOrDefault(s => s.enabled) == null ? "" : features.ActiveCalendars().First(s => s.enabled).id, data = new MonthData { month = data.month, syncedAt = data.syncedAt, events = data.events.Concat(NoteEvents().Where(n => n.date.StartsWith(key))).ToList(), warnings = data.warnings } };
                else if (App.Str(args, "cached_only", "false").ToLowerInvariant() == "true") result = new { available = false, pending = false, month = key, message = "캐시가 없습니다. 필요할 때 수동으로 새로고침해 주세요." };
                else { RefreshMonth(false, DateTime.ParseExact(key + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture)); result = new { pending = true, month = key, message = syncMessage }; }
            }
            else if (command == "desktop_status") result = Win.DesktopDiagnostics(Handle);
            else if (command == "setup_status") result = new { completed = features.setupCompleted, settingsVisible = calendarManager != null && !calendarManager.IsDisposed && calendarManager.Visible };
            else if (command == "sync_settings") { if (args.ContainsKey("enabled")) { features.autoSyncEnabled = App.Str(args, "enabled").ToLowerInvariant() == "true"; SaveFeatures(); } result = new { enabled = features.autoSyncEnabled, intervalMinutes = SyncPolicy.Minutes, started = syncStarted, completed = syncCompleted }; }
            else if (command == "daily_summary_settings") {
                string time = App.Str(args, "time", features.dailySummaryTime);
                if (!DailySummaryPolicy.ValidTime(time)) throw new ArgumentException("요약 시각은 HH:mm 형식의 한국 시간으로 입력해 주세요.");
                if (args.ContainsKey("enabled")) features.dailySummaryEnabled = App.Str(args, "enabled").ToLowerInvariant() == "true";
                features.SetDailySummaryTime(time); SaveFeatures();
                result = new { enabled = features.dailySummaryEnabled, time = features.dailySummaryTime, timezone = "Asia/Seoul", delivery = "Windows", checkedDate = features.dailySummaryCheckedDate };
            }
            else if (command == "daily_summary_preview") result = DailySummaryPolicy.Build(ActiveItems(), DateTimeOffset.Parse(App.Str(args, "at", DateTimeOffset.Now.ToString("o"))));
            else if (command == "test_daily_summary") { TestDailySummary(); result = new { requested = true }; }
            else if (command == "emoji_pending") result = EmojiPending();
            else if (command == "emoji_set") result = SetEmojiCommand(args);
            else if (command == "emoji_get") result = EmojiValues();
            else if (command == "emoji_clear") { string key = App.Str(args, "key"); var item = ActiveItems().FirstOrDefault(e => EmojiData.Key(e) == key); if (item == null) throw new ArgumentException("현재 일정을 선택해 주세요."); ResetEmoji(item, "", false); result = new { ok = true }; }
            else if (command == "emoji_editor") { string key = App.Str(args, "key"); var item = ActiveItems().FirstOrDefault(e => EmojiData.Key(e) == key); if (item == null) throw new ArgumentException("현재 일정을 선택해 주세요."); ShowEmojiEditor(item); result = new { windowId = emojiDialog.Handle.ToInt64() }; }
            else if (command == "window_status") result = new { taskbar = ShowInTaskbar, trayVisible = tray.Visible, visible = Visible, desktop = pinned, state = WindowState.ToString(), windowId = Handle.ToInt64() };
            else if (command == "startup_settings") { if (args.ContainsKey("enabled")) StartupRegistration.Set(App.Str(args, "enabled").ToLowerInvariant() == "true"); result = new { enabled = StartupEnabled() }; }
            else if (command == "emoji_settings") { features.compactEmojis = App.Str(args, "enabled", "true").ToLowerInvariant() == "true"; SaveFeatures(); result = new { enabled = features.compactEmojis }; }
            else if (command == "open_settings") { ShowCalendarManager(); result = new { windowId = calendarManager.Handle.ToInt64() }; }
            else if (command == "account_catalog") result = AccountDiscovery.Status();
            else if (command == "connection_mode") { string mode = App.Str(args, "mode"); if (mode != "id" && mode != "account") throw new ArgumentException("id 또는 account 모드를 선택해 주세요."); features.connectionMode = mode; SaveFeatures(); ReconfigureCalendars(); result = new { mode = mode, calendars = features.ActiveCalendars() }; }
            else if (command == "codex_status") result = CodexIntegration.Status(features);
            else if (command == "codex_settings") { ShowCodexDialog(); result = new { windowId = codexDialog.Handle.ToInt64() }; }
            else if (command == "codex_automation_link")
            {
                string id = App.Str(args, "id"), time = App.Str(args, "time", features.dotReminderTime);
                if (!Regex.IsMatch(id, @"^[A-Za-z0-9_.-]+$") || !CodexIntegration.ValidTime(time)) throw new ArgumentException("올바른 자동화 ID와 시각이 필요합니다.");
                features.dotAutomationId = id; features.dotReminderTime = time; SaveFeatures(); result = CodexIntegration.Status(features);
            }
            else if (command == "calendars") result = new { calendars = features.ActiveCalendars(), profile = features.ActiveProfile(), mode = features.connectionMode, idCalendars = features.calendars, accountCalendars = features.accountCalendars };
            else if (command == "add_calendar")
            {
                if (features.connectionMode != "id") throw new InvalidOperationException("계정 연동 모드입니다. ID 입력 모드를 선택한 뒤 추가해 주세요.");
                string id = App.Str(args, "calendar_id").Trim();
                if (id.Length > 512 || !id.Contains("@") || id.Any(char.IsWhiteSpace)) throw new ArgumentException("올바른 구글 캘린더 ID를 입력해 주세요.");
                var source = features.ActiveCalendars().FirstOrDefault(s => s.id == id);
                if (source == null) { source = new CalendarSource { id = id, name = App.Str(args, "name", id.Split('@')[0]), color = App.Str(args, "color", Design.Colors[0]) }; features.calendars.Add(source); SaveFeatures(); ReconfigureCalendars(); }
                result = source;
            }
            else if (command == "remove_calendar") { features.ConnectionSources().RemoveAll(s => s.id == App.Str(args, "calendar_id")); SaveFeatures(); ReconfigureCalendars(); result = new { ok = true }; }
            else if (command == "notes") result = new { notes = features.notes.Where(n => App.Str(args, "date") == "" || n.date == App.Str(args, "date")).ToList() };
            else if (command == "upsert_note") result = UpsertNote(args);
            else if (command == "delete_note") { features.notes.RemoveAll(n => n.id == App.Str(args, "id")); SaveFeatures(); if (dayDetails != null && !dayDetails.IsDisposed) dayDetails.RefreshItems(); result = new { ok = true }; }
            else if (command == "highlight")
            {
                string key = App.Str(args, "event_id"), date = App.Str(args, "date"); var list = features.highlights;
                if (date != "") { DateTime.ParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture); key = date; list = features.days; }
                if (key == "") throw new ArgumentException("date 또는 event_id가 필요합니다.");
                var item = cache.Values.SelectMany(d => d.events).FirstOrDefault(e => e.id == key); if (item != null) key = item.uid;
                list.RemoveAll(a => a.key == key);
                if (App.Str(args, "enabled", "true").ToLowerInvariant() == "true") list.Add(new Accent { key = key, color = App.Str(args, "color", "#A47B45") });
                SaveFeatures(); result = new { ok = true };
            }
            else if (command == "open_day") { ShowDay(DateTime.ParseExact(App.Str(args, "date"), "yyyy-MM-dd", CultureInfo.InvariantCulture)); result = new { date = dayDetails.Day.ToString("yyyy-MM-dd"), windowId = dayDetails.Handle.ToInt64() }; }
            else if (command == "reminders_due") { TickReminders(); result = new { enabled = features.remindersEnabled, alerts = PendingReminders().Where(r => !r.dotShown).ToList() }; }
            else if (command == "reminder_ack") { var ids = App.Str(args, "ids").Split(','); foreach (var reminder in features.reminders.Where(r => ids.Contains(r.id))) reminder.dotShown = true; SaveFeatures(); result = new { ok = true }; }
            else if (command == "reminder_preview") result = new { alerts = CalculateReminders(DateTimeOffset.Parse(App.Str(args, "at"))) };
            else if (command == "test_notification") { tray.ShowBalloonTip(10000, "캘린더 알림 테스트", "일정 시작 1시간 전 알림이 이 위치에 표시됩니다.", ToolTipIcon.Info); result = new { requested = true }; }
            else return false;
            return true;
        }
    }
}
