using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using System.Xml.Serialization;
using Microsoft.Win32;

namespace TeamCalendar
{
    public class Settings
    {
        public int X = -1, Y = -1, Width = 1120, Height = 670, Opacity = 82, FontSize = 11;
        public bool Desktop = true;
    }
    public class CalendarEvent
    {
        public string id, date, title, time, location, raw, uid, calendarId, calendarName, color, start;
        public bool allDay, isNote;
        public int sort;
    }
    public class MonthData
    {
        public string month, syncedAt;
        public List<CalendarEvent> events = new List<CalendarEvent>();
        public List<string> warnings = new List<string>();
        public List<string> failedCalendars = new List<string>();
    }

    static class App
    {
        public const string Name = "내 캘린더";
        public const string PipeName = "DesktopCalendarWidget.v2";
        public static readonly string DataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GoogleCalendarWidget");
        public static readonly string SettingsPath = Path.Combine(DataDir, "settings-v2.xml");
        public static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = 4 * 1024 * 1024 };
        public static EventWaitHandle ShowSignal;
        public static FeatureState Options;
        public static string EmbedUrl(CalendarSource source) { return "https://calendar.google.com/calendar/embed?authuser=" + Uri.EscapeDataString(Options.ActiveUser()) + "&src=" + Uri.EscapeDataString(source.id) + "&ctz=Asia%2FSeoul&mode=MONTH&wkst=1&showTitle=0&showPrint=0&showCalendars=0&showTz=0&showTabs=0&hl=ko"; }
        public static string CalendarUrl { get { var source = Options.ActiveCalendars().FirstOrDefault(s => s.enabled); return "https://calendar.google.com/calendar/r/month?authuser=" + Uri.EscapeDataString(Options.ActiveUser()) + (source == null ? "" : "&cid=" + Uri.EscapeDataString(Convert.ToBase64String(Encoding.UTF8.GetBytes(source.id)))); } }

        [STAThread]
        static void Main(string[] args)
        {
            Directory.CreateDirectory(DataDir);
            if (args.Contains("--self-test")) { OpenStdio(); try { Console.WriteLine(Json.Serialize(SelfChecks.Run())); } catch (Exception e) { Console.WriteLine(Json.Serialize(new { error = e.Message })); Environment.ExitCode = 1; } return; }
            if (args.Contains("--mcp")) { Mcp.Run(); return; }
            if (args.Contains("--command"))
            {
                OpenStdio();
                try { Console.WriteLine(Bridge.Send(Console.ReadLine())); }
                catch (Exception e) { Console.WriteLine(Json.Serialize(new { error = e.Message })); }
                return;
            }
            bool first;
            using (var mutex = new Mutex(true, "Local\\DesktopCalendarWidget.v2", out first))
            using (ShowSignal = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\DesktopCalendarWidget.v2.Show"))
            {
                if (!first) { ShowSignal.Set(); return; }
                Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
                Application.ThreadException += delegate(object s, ThreadExceptionEventArgs e) { Log(e.Exception.ToString()); };
                try { Application.Run(new CalendarWindow()); }
                catch (Exception e) { Log(e.ToString()); MessageBox.Show(e.Message, "팀 캘린더 위젯"); }
                finally { GoogleBrowser.Shared.Dispose(); }
            }
        }
        public static void Log(string message) { try { File.AppendAllText(Path.Combine(DataDir, "widget-v2.log"), DateTime.Now.ToString("s") + " " + message + Environment.NewLine); } catch { } }
        public static void OpenStdio()
        {
            Console.SetIn(new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false)));
            Console.SetOut(new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true });
        }
        public static Settings Load()
        {
            try { using (var f = File.OpenRead(SettingsPath)) return (Settings)new XmlSerializer(typeof(Settings)).Deserialize(f); }
            catch { return new Settings(); }
        }
        public static void Save(Settings settings)
        {
            try { using (var f = File.Create(SettingsPath + ".tmp")) new XmlSerializer(typeof(Settings)).Serialize(f, settings); if (File.Exists(SettingsPath)) File.Replace(SettingsPath + ".tmp", SettingsPath, null); else File.Move(SettingsPath + ".tmp", SettingsPath); } catch { }
        }
        public static string Chrome()
        {
            foreach (var root in new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) })
            {
                string path = Path.Combine(root, "Google\\Chrome\\Application\\chrome.exe"); if (File.Exists(path)) return path;
            }
            throw new FileNotFoundException("Google Chrome을 찾을 수 없습니다.");
        }
        public static string ProfileArg { get { return Options == null || Options.ActiveProfile() == "" ? "" : "--profile-directory=\"" + Options.ActiveProfile() + "\" "; } }
        public static void Open(string url) { Process.Start(new ProcessStartInfo(Chrome(), ProfileArg + "\"" + url + "\"") { UseShellExecute = true }); }
        public static string DraftUrl(DateTime date, string title)
        {
            var source = Options.ActiveCalendars().FirstOrDefault(s => s.enabled);
            if (source == null) throw new InvalidOperationException("설정에서 구글 캘린더 ID를 먼저 추가해 주세요.");
            return "https://calendar.google.com/calendar/r/eventedit?authuser=" + Uri.EscapeDataString(Options.ActiveUser()) + "&src=" + Uri.EscapeDataString(source.id) + "&ctz=Asia%2FSeoul&dates=" + date.ToString("yyyyMMdd") + "/" + date.AddDays(1).ToString("yyyyMMdd") + "&text=" + Uri.EscapeDataString(title ?? "");
        }
        public static string Str(Dictionary<string, object> d, string key, string fallback = "") { object value; return d.TryGetValue(key, out value) ? Convert.ToString(value, CultureInfo.InvariantCulture) : fallback; }
    }

    // One headless browser is shared across all enabled calendars. There are
    // no Win32 reader windows to show, reparent, activate or recover.
    sealed class CalendarReader : IDisposable
    {
        public readonly CalendarSource Source;
        readonly GoogleBrowser browser;
        bool disposed;
        public CalendarReader(CalendarSource source) : this(source, GoogleBrowser.Shared) { }
        internal CalendarReader(CalendarSource source, GoogleBrowser connection) { Source = source; browser = connection; }
        public IntPtr Window { get { return IntPtr.Zero; } }
        public MonthData Read(DateTime month, bool reload)
        {
            if (disposed) throw new ObjectDisposedException("CalendarReader");
            month = new DateTime(month.Year, month.Month, 1);
            lock (browser.Gate)
            {
                // A fresh document is fetched for every actual read, including
                // manual refresh. Cached-only paths never call this method.
                browser.Navigate(App.EmbedUrl(Source) + "&dates=" + month.ToString("yyyyMMdd") + "/" + month.AddMonths(1).AddDays(-1).ToString("yyyyMMdd") + "&widget_refresh=" + Guid.NewGuid().ToString("N"));
                Dictionary<string, object> snapshot = null; int stable = 0;
                var watch = Stopwatch.StartNew();
                while (watch.ElapsedMilliseconds < 35000 && !disposed)
                {
                    snapshot = browser.Script("snapshot", null) as Dictionary<string, object>;
                    object[] cells = snapshot == null || !snapshot.ContainsKey("cells") ? new object[0] : GoogleDom.Array(snapshot["cells"]);
                    if (snapshot != null && App.Str(snapshot, "month") == month.ToString("yyyy-MM") && cells.Length >= 28 && !Convert.ToBoolean(snapshot["busy"])) { if (++stable >= 2) break; }
                    else stable = 0;
                    Thread.Sleep(400);
                }
                if (stable < 2) throw new InvalidOperationException("Google 일정 표를 읽지 못했습니다. 로그인과 접근 권한을 확인해 주세요. 기존 데이터를 유지합니다.");
                foreach (Dictionary<string, object> cell in GoogleDom.Array(snapshot["cells"]))
                {
                    if (!Convert.ToBoolean(cell["more"])) continue;
                    if (!Convert.ToBoolean(browser.Script("expand", App.Str(cell, "label")))) throw new InvalidOperationException("숨겨진 일정을 펼치지 못했습니다. 기존 데이터를 유지합니다.");
                    try
                    {
                        object[] expanded = new object[0]; var count = Regex.Match(App.Str(cell, "label"), @"^일정 (\d+)개,"); int expected = count.Success ? int.Parse(count.Groups[1].Value) : 1; watch.Restart();
                        while (watch.ElapsedMilliseconds < 5000) { expanded = GoogleDom.Array(browser.Script("expanded", null)); if (expanded.Select(Convert.ToString).Distinct().Count() >= expected) break; Thread.Sleep(200); }
                        if (expanded.Length == 0) throw new InvalidOperationException("숨겨진 일정 목록을 읽지 못했습니다. 기존 데이터를 유지합니다.");
                        cell["events"] = GoogleDom.Array(cell["events"]).Concat(expanded).Select(Convert.ToString).Distinct().Cast<object>().ToArray();
                    }
                    finally { if (!Convert.ToBoolean(browser.Script("dismiss", null))) throw new InvalidOperationException("일정 펼침 창을 닫지 못했습니다. 기존 데이터를 유지합니다."); }
                }
                var result = Parse(snapshot, month, Source);
                App.Log("Headless read month=" + result.month + ", day occurrences=" + result.events.Count);
                return result;
            }
        }
        internal static MonthData Parse(Dictionary<string, object> snapshot, DateTime month, CalendarSource source)
        {
            object[] cells = snapshot != null && snapshot.ContainsKey("cells") ? GoogleDom.Array(snapshot["cells"]) : null;
            if (cells == null || cells.Length < 28 || App.Str(snapshot, "month") != month.ToString("yyyy-MM")) throw new InvalidOperationException("Google 일정 표가 아직 준비되지 않았습니다.");
            var result = new MonthData { month = month.ToString("yyyy-MM"), syncedAt = DateTimeOffset.Now.ToString("o") };
            var seen = new HashSet<string>();
            foreach (Dictionary<string, object> cell in cells)
            {
                string label = App.Str(cell, "label");
                var match = Regex.Match(label, @"^일정 (?:\d+개|없음),\s*(\d{1,2})월\s*(\d{1,2})일");
                if (!match.Success) throw new InvalidOperationException("Google 날짜 형식이 변경되었습니다. 기존 데이터를 유지합니다.");
                int cellMonth = int.Parse(match.Groups[1].Value), day = int.Parse(match.Groups[2].Value), year = month.Year;
                if (cellMonth - month.Month > 6) year--; if (month.Month - cellMonth > 6) year++;
                string date = new DateTime(year, cellMonth, day).ToString("yyyy-MM-dd");
                var rawEvents = GoogleDom.Array(cell["events"]).Select(Convert.ToString).Distinct().ToArray();
                var count = Regex.Match(label, @"^일정 (\d+)개,");
                if (count.Success && int.Parse(count.Groups[1].Value) > rawEvents.Length) throw new InvalidOperationException("숨겨진 일정을 모두 읽지 못했습니다. 기존 데이터를 유지합니다.");
                foreach (string raw in rawEvents)
                {
                    int marker = raw.IndexOf(", 캘린더: ", StringComparison.Ordinal), comma = raw.IndexOf(", ", StringComparison.Ordinal);
                    if (marker < 0 || comma < 0 || marker <= comma || !seen.Add(date + "|" + raw)) continue;
                    string time = raw.Substring(0, comma), title = raw.Substring(comma + 2, marker - comma - 2);
                    var locationMatch = Regex.Match(raw.Substring(marker), @", 위치: (.*), \d{4}년");
                    using (var hash = System.Security.Cryptography.SHA256.Create())
                    {
                        string uid = BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(source.id + "|" + raw))).Replace("-", "").Substring(0, 20);
                        bool allDay = time.StartsWith("종일"); string startDate = date;
                        var startMatch = Regex.Match(raw.Substring(marker), @"(\d{4})년\s*(\d{1,2})월\s*(\d{1,2})일");
                        if (startMatch.Success) startDate = new DateTime(int.Parse(startMatch.Groups[1].Value), int.Parse(startMatch.Groups[2].Value), int.Parse(startMatch.Groups[3].Value)).ToString("yyyy-MM-dd");
                        result.events.Add(new CalendarEvent { id = uid + "-" + date, uid = uid, date = date, title = title, time = time, location = locationMatch.Success ? locationMatch.Groups[1].Value : "", raw = raw, sort = SortTime(time), calendarId = source.id, calendarName = source.name, color = source.color, allDay = allDay, start = Design.Start(startDate, time, allDay) });
                    }
                }
            }
            result.events = result.events.OrderBy(e => e.date).ThenBy(e => e.sort).ThenBy(e => e.title).ToList(); return result;
        }
        static int SortTime(string time)
        {
            if (time.StartsWith("종일")) return -1;
            var match = Regex.Match(time, @"(오전|오후)\s*(\d{1,2})(?::(\d{2}))?");
            if (!match.Success) return 0;
            int hour = int.Parse(match.Groups[2].Value) % 12 + (match.Groups[1].Value == "오후" ? 12 : 0);
            return hour * 60 + (match.Groups[3].Success ? int.Parse(match.Groups[3].Value) : 0);
        }
        public void Dispose() { disposed = true; }
    }

    sealed class EventHit { public Rectangle bounds; public CalendarEvent item; public DateTime day; }

    sealed partial class CalendarWindow : Form
    {
        readonly Settings settings = App.Load();
        readonly FeatureState features = FeatureState.Load();
        readonly MultiCalendarReader reader;
        readonly Dictionary<string, MonthData> cache = new Dictionary<string, MonthData>();
        readonly List<EventHit> hits = new List<EventHit>();
        readonly ToolTip tooltip = new ToolTip { InitialDelay = 300, AutoPopDelay = 15000 };
        readonly Panel toolbar;
        readonly Label monthLabel, nameLabel;
        readonly ContextMenuStrip menu;
        readonly NotifyIcon tray;
        readonly System.Windows.Forms.Timer pulse;
        readonly ToolStripMenuItem desktopMenu;
        DateTime month = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        bool pinned, positioning, loading, shuttingDown, dragging, resizing;
        Point mouseStart, formStart;
        Point resizeStart;
        Size resizeSize;
        Point initialPosition;
        IntPtr desktopHost;
        DateTime lastSync = DateTime.MinValue;
        readonly Dictionary<string, DateTime> syncAttempts = new Dictionary<string, DateTime>();
        bool pendingVisibleRefresh;
        int syncStarted, syncCompleted;
        string syncMessage = "구글 캘린더 연결 중…", hover = "";
        int rowCount = 6;
        const int HeaderHeight = 38, WeekHeight = 26, FooterHeight = 23;

        public CalendarWindow()
        {
            App.Options = features; reader = new MultiCalendarReader(features);
            Text = "투명 팀 캘린더 · " + App.Name;
            FormBorderStyle = FormBorderStyle.None; StartPosition = FormStartPosition.Manual;
            MinimumSize = new Size(700, 410); Size = new Size(settings.Width, settings.Height);
            BackColor = Color.FromArgb(20, 43, 56); ForeColor = Color.FromArgb(241, 248, 252);
            Font = new Font("맑은 고딕", settings.FontSize);
            DoubleBuffered = true; SetStyle(ControlStyles.ResizeRedraw, true); KeyPreview = true;
            ShowInTaskbar = !settings.Desktop; Icon = MakeIcon();
            SetInitialPosition();
            initialPosition = Location;
            menu = new ContextMenuStrip();
            menu.Items.Add("구글 캘린더 열기", null, delegate { App.Open(App.CalendarUrl); });
            menu.Items.Add("일정 새로고침", null, delegate { RefreshMonth(true); });
            menu.Items.Add(new ToolStripSeparator());
            desktopMenu = new ToolStripMenuItem("바탕화면에 붙이기") { Checked = settings.Desktop };
            desktopMenu.Click += delegate { Pin(!pinned); Save(); }; menu.Items.Add(desktopMenu);
            var opacityMenu = new ToolStripMenuItem("불투명도");
            foreach (int percent in new[] { 100, 90, 82, 70, 60, 45 })
            {
                int value = percent;
                var option = new ToolStripMenuItem(value + "%") { Tag = value };
                option.Click += delegate { SetOpacity(value); };
                opacityMenu.DropDownItems.Add(option);
            }
            menu.Items.Add(opacityMenu);
            var fontMenu = new ToolStripMenuItem("글자 크기");
            foreach (int points in new[] { 9, 10, 11, 12, 14 })
            {
                int value = points; fontMenu.DropDownItems.Add(value + "pt", null, delegate { settings.FontSize = value; Font = new Font("맑은 고딕", value); Save(); Invalidate(); });
            }
            menu.Items.Add(fontMenu);
            var startup = new ToolStripMenuItem("윈도우 로그인 시 자동 실행") { Checked = StartupEnabled() };
            startup.Click += delegate
            {
                bool enabled = !StartupEnabled();
                StartupRegistration.Set(enabled);
                startup.Checked = enabled;
            };
            menu.Items.Add(startup);
            menu.Items.Add("위치·크기 초기화", null, delegate { Pin(false); Size = new Size(1120, 670); settings.X = settings.Y = -1; SetInitialPosition(); Pin(true); Save(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("숨기기", null, delegate { Hide(); });
            menu.Items.Add("종료", null, delegate { Close(); });
            ContextMenuStrip = menu;

            toolbar = new Panel { Dock = DockStyle.Top, Height = HeaderHeight, BackColor = Color.FromArgb(24, 46, 60) };
            nameLabel = new Label { Text = "▦  " + App.Name, AutoSize = false, Width = 250, Height = HeaderHeight, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(10, 0, 0, 0), Font = new Font("맑은 고딕", 9.5F), Cursor = Cursors.SizeAll, AccessibleName = "위젯 이동" };
            monthLabel = new Label { TextAlign = ContentAlignment.MiddleCenter, Font = new Font("맑은 고딕", 12, FontStyle.Bold), Height = HeaderHeight, Cursor = Cursors.Hand, AccessibleName = "표시 중인 달" };
            monthLabel.Click += delegate { PickMonth(); };
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Right, Width = 262, Height = HeaderHeight, WrapContents = false, FlowDirection = FlowDirection.LeftToRight };
            buttons.Controls.Add(MakeButton("‹", "이전 달", 32, delegate { MoveMonth(-1); }));
            buttons.Controls.Add(MakeButton("오늘", "오늘", 49, delegate { SetMonth(new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1)); }));
            buttons.Controls.Add(MakeButton("›", "다음 달", 32, delegate { MoveMonth(1); }));
            buttons.Controls.Add(MakeButton("↻", "새로고침", 32, delegate { RefreshMonth(true); }));
            buttons.Controls.Add(MakeButton("↗", "구글 캘린더 열기", 32, delegate { App.Open(App.CalendarUrl); }));
            buttons.Controls.Add(MakeButton("⋯", "위젯 설정", 32, delegate { ShowCalendarManager(); }));
            buttons.Controls.Add(MakeButton("─", "위젯 숨기기", 32, delegate { Hide(); }));
            toolbar.Controls.Add(nameLabel); toolbar.Controls.Add(monthLabel); toolbar.Controls.Add(buttons); Controls.Add(toolbar);
            foreach (Control control in new Control[] { toolbar, nameLabel }) { control.MouseDown += DragStart; control.MouseMove += DragMove; control.MouseUp += DragEnd; }
            Layout += delegate { monthLabel.Left = Math.Max(250, (Width - 250) / 2); monthLabel.Width = Math.Max(130, Math.Min(220, Width - 530)); };
            ResizeEnd += delegate { Save(); };
            KeyDown += delegate(object s, KeyEventArgs e) { if (e.KeyCode == Keys.Left) MoveMonth(-1); else if (e.KeyCode == Keys.Right) MoveMonth(1); else if (e.KeyCode == Keys.Home) SetMonth(new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1)); else if (e.KeyCode == Keys.F5) RefreshMonth(true); };
            MouseMove += HoverEvent; MouseDoubleClick += OpenDay;
            MouseDown += delegate(object s, MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left && e.X >= Width - 20 && e.Y >= Height - 20)
                { resizing = true; resizeStart = Cursor.Position; resizeSize = Size; Capture = true; }
            };
            MouseUp += delegate { if (resizing) { resizing = false; Capture = false; Save(); } };
            tray = new NotifyIcon { Icon = Icon, Text = "팀 캘린더", ContextMenuStrip = menu, Visible = true };
            tray.DoubleClick += delegate { ShowWidget(); };
            pulse = new System.Windows.Forms.Timer { Interval = 500 };
            pulse.Tick += delegate { if (App.ShowSignal.WaitOne(0)) ShowWidget(); FeaturePulse(); AutoRefreshDue(); };
            Shown += delegate
            {
                if (settings.Desktop && desktopHost != IntPtr.Zero && Win.GetParent(Handle) == desktopHost) { pinned = true; desktopMenu.Checked = true; }
                else Pin(settings.Desktop);
                Location = initialPosition; SetOpacity(settings.Opacity); UpdateMonthLabel();
                if (pinned) Win.SetWindowPos(Handle, IntPtr.Zero, 0, 0, 0, 0, 0x0013);
                App.Log("Widget HWND=" + Handle.ToInt64()); Bridge.Start(this); pulse.Start(); LoadCachedMonth(month); if (features.autoSyncEnabled) RefreshMonth(false); else { syncMessage = "저장된 일정 · ↻ 버튼으로 새로고침"; Invalidate(); }
                if (!features.setupCompleted) BeginInvoke((Action)delegate { ShowCalendarManager(true); });
                Win.RepaintDesktop();
            };
            FormClosing += delegate { shuttingDown = true; Save(); pulse.Stop(); reader.Dispose(); tray.Visible = false; tray.Dispose(); App.Log("Widget closed."); };
            InitializeFeatures();
        }
        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                if (settings != null && settings.Desktop)
                {
                    if (desktopHost == IntPtr.Zero || !Win.IsWindow(desktopHost)) desktopHost = Win.DesktopHost();
                    if (desktopHost != IntPtr.Zero)
                    {
                        cp.Parent = desktopHost;
                        cp.Style = (cp.Style & ~unchecked((int)0x80cf0000)) | 0x40000000;
                        cp.ExStyle = (cp.ExStyle & ~0x00040300) | 0x80;
                    }
                }
                return cp;
            }
        }
        static Button MakeButton(string text, string name, int width, EventHandler handler)
        {
            var b = new Button { Text = text, AccessibleName = name, Width = width, Height = 30, Margin = new Padding(1, 4, 0, 0), FlatStyle = FlatStyle.Flat, ForeColor = Color.FromArgb(232, 245, 251), BackColor = Color.FromArgb(24, 46, 60), Cursor = Cursors.Hand, Font = new Font("맑은 고딕", 10) };
            b.FlatAppearance.BorderSize = 0; b.FlatAppearance.MouseOverBackColor = Color.FromArgb(54, 87, 105); b.Click += handler; return b;
        }
        void UpdateMonthLabel() { monthLabel.Text = month.ToString("yyyy년 M월"); Invalidate(); }
        void MoveMonth(int amount) { SetMonth(month.AddMonths(amount)); }
        void SetMonth(DateTime date) { month = new DateTime(date.Year, date.Month, 1); UpdateMonthLabel(); RefreshMonth(false); }
        void PickMonth()
        {
            using (var dialog = new Form { Text = "표시할 달", Size = new Size(280, 150), StartPosition = FormStartPosition.CenterScreen, FormBorderStyle = FormBorderStyle.FixedDialog, MinimizeBox = false, MaximizeBox = false })
            {
                var picker = new DateTimePicker { Format = DateTimePickerFormat.Custom, CustomFormat = "yyyy년 M월", ShowUpDown = true, Value = month, Left = 20, Top = 15, Width = 220 };
                var ok = new Button { Text = "이동", DialogResult = DialogResult.OK, Left = 150, Top = 60, Width = 90 }; dialog.Controls.Add(picker); dialog.Controls.Add(ok); dialog.AcceptButton = ok;
                if (dialog.ShowDialog() == DialogResult.OK) SetMonth(picker.Value);
            }
        }
        void RefreshMonth(bool force, DateTime? backgroundMonth = null)
        {
            if (shuttingDown) return;
            DateTime requested = backgroundMonth.HasValue ? backgroundMonth.Value : month;
            if (loading) { if (!backgroundMonth.HasValue) { pendingVisibleRefresh = true; if (force) pendingForceRefresh = true; } return; }
            syncAttempts[requested.ToString("yyyy-MM")] = DateTime.UtcNow; syncStarted++;
            loading = true; syncMessage = "구글 캘린더 확인 중…"; Invalidate();
            Task.Run(delegate
            {
                MonthData data = null; string error = null;
                try { data = reader.Read(requested, force); } catch (Exception e) { error = e.Message; App.Log("Sync failed: " + e.GetType().Name + ": " + e.Message); }
                if (shuttingDown) return;
                BeginInvoke((Action)delegate
                {
                    loading = false; lastSync = DateTime.Now; syncCompleted++;
                    if (reloadCalendars) { reloadCalendars = false; pendingForceRefresh = pendingVisibleRefresh = false; ReconfigureCalendars(); return; }
                    if (data != null) { StoreMonth(data); syncMessage = data.warnings.Count > 0 ? "일부 캘린더 연결 확인 · " + string.Join(" / ", data.warnings) : "Google Calendar · " + DateTime.Now.ToString("HH:mm") + " 확인"; }
                    else syncMessage = "연결 확인 필요 · " + error;
                    Invalidate(); if (dayDetails != null && !dayDetails.IsDisposed) dayDetails.RefreshItems();
                    if (pendingForceRefresh || pendingVisibleRefresh || month != requested && !backgroundMonth.HasValue) { bool nextForce = pendingForceRefresh; pendingForceRefresh = pendingVisibleRefresh = false; RefreshMonth(nextForce); }
                });
            });
        }
        List<CalendarEvent> VisibleEvents()
        {
            MonthData data; return (cache.TryGetValue(month.ToString("yyyy-MM"), out data) ? data.events : new List<CalendarEvent>()).Concat(NoteEvents()).OrderBy(e => e.sort).ToList();
        }
        DateTime GridStart() { return month.AddDays(-(int)month.DayOfWeek); }
        Rectangle GridBounds { get { return new Rectangle(6, HeaderHeight + WeekHeight, Width - 12, Height - HeaderHeight - WeekHeight - FooterHeight - 6); } }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e); var g = e.Graphics; g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            Rectangle grid = GridBounds; DateTime start = GridStart();
            rowCount = (int)Math.Ceiling(((int)month.DayOfWeek + DateTime.DaysInMonth(month.Year, month.Month)) / 7.0);
            rowCount = Math.Max(5, rowCount);
            float cw = grid.Width / 7F, ch = grid.Height / (float)rowCount;
            var events = VisibleEvents(); hits.Clear();
            using (var line = new Pen(Color.FromArgb(62, 174, 206, 219)))
            using (var small = new Font("맑은 고딕", 9))
            using (var dayFont = new Font("맑은 고딕", 10, FontStyle.Bold))
            using (var footer = new Font("맑은 고딕", 8))
            {
                string[] days = { "일요일", "월요일", "화요일", "수요일", "목요일", "금요일", "토요일" };
                for (int col = 0; col < 7; col++)
                {
                    Color color = col == 0 ? Color.FromArgb(255, 184, 172) : col == 6 ? Color.FromArgb(156, 211, 255) : ForeColor;
                    TextRenderer.DrawText(g, days[col], small, new Rectangle((int)(grid.Left + col * cw), HeaderHeight + 3, (int)cw, WeekHeight - 3), color, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                }
                for (int i = 0; i < rowCount * 7; i++)
                {
                    DateTime day = start.AddDays(i); int col = i % 7, row = i / 7;
                    var cell = new Rectangle((int)(grid.Left + col * cw), (int)(grid.Top + row * ch), (int)Math.Ceiling(cw), (int)Math.Ceiling(ch));
                    using (var brush = new SolidBrush(Color.FromArgb(col == 0 || col == 6 ? 28 : 12, 116, 160, 176))) g.FillRectangle(brush, cell);
                    Color? accentColor = DayColor(day.ToString("yyyy-MM-dd"), events);
                    if (accentColor.HasValue) using (var brush = new SolidBrush(Color.FromArgb(115, accentColor.Value))) g.FillRectangle(brush, cell);
                    g.DrawRectangle(line, cell);
                    bool today = day == DateTime.Today;
                    if (today) using (var todayPen = new Pen(Color.FromArgb(112, 225, 243), 2)) g.DrawRectangle(todayPen, cell.X + 1, cell.Y + 1, cell.Width - 2, cell.Height - 2);
                    Color dateColor = day.Month != month.Month ? Color.FromArgb(111, 147, 163) : col == 0 ? Color.FromArgb(255, 182, 169) : col == 6 ? Color.FromArgb(160, 211, 255) : ForeColor;
                    string dayText = day.Day == 1 ? day.ToString("M/d") : day.Day.ToString();
                    TextRenderer.DrawText(g, dayText + (today ? "  오늘" : ""), dayFont, new Rectangle(cell.X + 6, cell.Y + 4, cell.Width - 10, 21), dateColor, TextFormatFlags.Left | TextFormatFlags.NoPadding);
                    var list = events.Where(item => item.date == day.ToString("yyyy-MM-dd")).ToList();
                    var density = EmojiData.Density(cw, ch, features.compactEmojis);
                    if (density == CalendarDensity.Stickers) { DrawStickers(g, list, cell, day); continue; }
                    int lineHeight = Math.Max(23, Font.Height + 5), capacity = Math.Max(0, (cell.Height - 32) / lineHeight);
                    int shown = Math.Min(list.Count, list.Count > capacity ? Math.Max(0, capacity - 1) : capacity);
                    for (int j = 0; j < shown; j++)
                    {
                        var item = list[j]; var bounds = new Rectangle(cell.X + 5, cell.Y + 29 + j * lineHeight, cell.Width - 10, lineHeight - 2);
                        bool allDay = item.allDay || item.isNote;
                        var eventAccent = features.highlights.FirstOrDefault(a => a.key == item.uid);
                        Color barColor = eventAccent != null ? Design.Parse(eventAccent.color) : item.isNote ? Design.Parse(item.color) : ControlPaint.Dark(Design.Parse(item.color), .25F);
                        if (allDay || IsImportant(item)) using (var bar = new SolidBrush(Color.FromArgb(225, barColor))) using (var path = Design.Round(bounds, 5)) g.FillPath(bar, path);
                        else using (var dot = new SolidBrush(Design.Parse(item.color))) g.FillEllipse(dot, bounds.X + 1, bounds.Y + 7, 4, 4);
                        string time = allDay ? "" : item.time.Split('~')[0].Replace("오전 ", "").Replace("오후 ", "PM ").Replace("시", "");
                        string text = (IsImportant(item) ? "★ " : "") + (allDay ? (item.isNote && item.time != "메모" ? item.time + " " : "") : time + " ") + item.title;
                        TextRenderer.DrawText(g, text, Font, new Rectangle(bounds.X + (allDay ? 3 : 9), bounds.Y, bounds.Width - (allDay ? 5 : 9), bounds.Height), ForeColor, TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
                        hits.Add(new EventHit { bounds = bounds, item = item, day = day });
                    }
                    if (shown < list.Count) TextRenderer.DrawText(g, "+" + (list.Count - shown) + "개 더 보기", small, new Rectangle(cell.X + 8, cell.Y + 29 + shown * lineHeight, cell.Width - 12, 20), Color.FromArgb(164, 213, 226), TextFormatFlags.NoPadding);
                }
                TextRenderer.DrawText(g, SyncStatus, footer, new Rectangle(10, Height - FooterHeight + 2, Width - 35, FooterHeight - 3), Color.FromArgb(175, 202, 214), TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
                g.DrawLine(line, Width - 12, Height - 5, Width - 5, Height - 12); g.DrawLine(line, Width - 8, Height - 5, Width - 5, Height - 8);
            }
        }
        void HoverEvent(object sender, MouseEventArgs e)
        {
            if (resizing)
            {
                Size = new Size(Math.Max(MinimumSize.Width, resizeSize.Width + Cursor.Position.X - resizeStart.X), Math.Max(MinimumSize.Height, resizeSize.Height + Cursor.Position.Y - resizeStart.Y));
                return;
            }
            var hit = hits.FirstOrDefault(h => h.bounds.Contains(e.Location));
            string text = hit == null ? "" : EmojiData.Icons(EventEmoji(hit.item)) + " " + hit.item.title + "\n" + hit.day.ToString("M월 d일") + " · " + hit.item.time + (hit.item.location == "" ? "" : "\n" + hit.item.location);
            if (hover != text) { hover = text; tooltip.SetToolTip(this, text); }
            Cursor = e.X >= Width - 20 && e.Y >= Height - 20 ? Cursors.SizeNWSE : hit == null ? Cursors.Default : Cursors.Hand;
        }
        void OpenDay(object sender, MouseEventArgs e)
        {
            var grid = GridBounds; if (!grid.Contains(e.Location)) return;
            int col = Math.Min(6, (e.X - grid.Left) * 7 / grid.Width), row = Math.Min(rowCount - 1, (e.Y - grid.Top) * rowCount / grid.Height);
            DateTime day = GridStart().AddDays(row * 7 + col); var list = VisibleEvents().Where(item => item.date == day.ToString("yyyy-MM-dd")).ToList();
            ShowDay(day);
        }
        void SetInitialPosition()
        {
            Rectangle area = Screen.PrimaryScreen.WorkingArea;
            Location = settings.X == -1 && settings.Y == -1 ? new Point(area.Right - Width - 28, Math.Min(430, area.Bottom - Height - 20)) : new Point(settings.X, settings.Y);
            if (!Screen.AllScreens.Any(s => Rectangle.Intersect(s.WorkingArea, Bounds).Width >= 120 && Rectangle.Intersect(s.WorkingArea, Bounds).Height >= 80)) Location = new Point(area.Right - Width - 28, area.Top + 80);
        }
        void Pin(bool enabled)
        {
            if (!IsHandleCreated) return; positioning = true;
            IntPtr previousParent = Win.GetParent(Handle);
            if (WindowState != FormWindowState.Normal) WindowState = FormWindowState.Normal;
            Point origin = PointToScreen(Point.Empty); IntPtr parent = enabled ? Win.DesktopHost() : IntPtr.Zero;
            if (enabled && parent == IntPtr.Zero) enabled = false;
            settings.Desktop = enabled; desktopHost = parent;
            ShowInTaskbar = !enabled;
            long style = Win.GetWindowLongPtr(Handle, -16).ToInt64();
            if (enabled) Win.SetWindowLongPtr(Handle, -16, new IntPtr((style & ~0x80cf0000L) | 0x40000000L));
            Win.SetParent(Handle, parent);
            if (!enabled) Win.SetWindowLongPtr(Handle, -16, new IntPtr((style & ~0x40000000L) | 0x800a0000L));
            long extended = Win.GetWindowLongPtr(Handle, -20).ToInt64();
            Win.SetWindowLongPtr(Handle, -20, new IntPtr(enabled ? (extended & ~0x40000L) | 0x80L : (extended & ~0x80L) | 0x40000L));
            if (enabled) { var point = new Win.POINT { X = origin.X, Y = origin.Y }; Win.ScreenToClient(parent, ref point); Location = new Point(point.X, point.Y); } else Location = origin;
            pinned = settings.Desktop = enabled; desktopMenu.Checked = enabled;
            Win.SetWindowPos(Handle, IntPtr.Zero, 0, 0, 0, 0, enabled ? 0x0033u : 0x0037u); positioning = false;
            ShowWidget();
            if (previousParent != IntPtr.Zero && previousParent != parent) Win.RedrawWindow(previousParent, IntPtr.Zero, IntPtr.Zero, 0x0185);
        }
        void Save()
        {
            if (positioning || !IsHandleCreated || WindowState != FormWindowState.Normal) return; Point origin = PointToScreen(Point.Empty);
            settings.X = origin.X; settings.Y = origin.Y; settings.Width = Width; settings.Height = Height; settings.Desktop = pinned; App.Save(settings);
        }
        void SetOpacity(int percent) { settings.Opacity = Math.Max(35, Math.Min(100, percent)); Opacity = settings.Opacity / 100.0; Save(); }
        void ShowWidget() { if (WindowState != FormWindowState.Normal) WindowState = FormWindowState.Normal; Show(); Win.ShowWindow(Handle, 4); if (tray != null) tray.Visible = true; }
        bool StartupEnabled() { return StartupRegistration.Enabled(); }
        void DragStart(object s, MouseEventArgs e) { if (e.Button != MouseButtons.Left) return; dragging = true; mouseStart = Cursor.Position; formStart = Location; ((Control)s).Capture = true; }
        void DragMove(object s, MouseEventArgs e) { if (dragging) Location = new Point(formStart.X + Cursor.Position.X - mouseStart.X, formStart.Y + Cursor.Position.Y - mouseStart.Y); }
        void DragEnd(object s, MouseEventArgs e) { dragging = false; ((Control)s).Capture = false; Save(); }
        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (m.Msg == 0x84 && m.Result == (IntPtr)1)
            {
                Point p = PointToClient(new Point((short)(m.LParam.ToInt64() & 0xffff), (short)((m.LParam.ToInt64() >> 16) & 0xffff)));
                bool left = p.X < 6, right = p.X > Width - 8, top = p.Y < 3, bottom = p.Y > Height - 8;
                if (p.X >= Width - 20 && p.Y >= Height - 20) { m.Result = (IntPtr)1; return; }
                if (bottom && right) m.Result = (IntPtr)17; else if (bottom && left) m.Result = (IntPtr)16;
                else if (left) m.Result = (IntPtr)10; else if (right) m.Result = (IntPtr)11; else if (bottom) m.Result = (IntPtr)15; else if (top) m.Result = (IntPtr)12;
            }
        }
        public object Command(Dictionary<string, object> args)
        {
            object featureResult; if (TryFeatureCommand(args, out featureResult)) return featureResult;
            string command = App.Str(args, "command");
            if (command == "set_month") { DateTime target = DateTime.ParseExact(App.Str(args, "month") + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture); SetMonth(target); }
            else if (command == "refresh") RefreshMonth(true);
            else if (command == "opacity") SetOpacity(int.Parse(App.Str(args, "percent")));
            else if (command == "show") ShowWidget();
            else if (command == "hide") Hide();
            else if (command == "minimize") WindowState = FormWindowState.Minimized;
            else if (command == "repaint_desktop") Win.RepaintDesktop();
            else if (command == "resize") { Size = new Size(Math.Max(700, int.Parse(App.Str(args, "width"))), Math.Max(410, int.Parse(App.Str(args, "height")))); Save(); }
            else if (command == "move") { Location = new Point(int.Parse(App.Str(args, "x")), int.Parse(App.Str(args, "y"))); Save(); }
            else if (command == "pin") { Pin(App.Str(args, "enabled", "true").ToLowerInvariant() == "true"); Save(); }
            else if (command == "draft") { DateTime day = DateTime.ParseExact(App.Str(args, "date"), "yyyy-MM-dd", CultureInfo.InvariantCulture); App.Open(App.DraftUrl(day, App.Str(args, "title"))); return new { opened = true, saved = false, calendarId = features.ActiveCalendars().First(s => s.enabled).id, message = "구글 캘린더 초안을 열었습니다. 저장 전 대상 캘린더를 확인해 주세요." }; }
            else if (command == "quit") BeginInvoke((Action)Close);
            else throw new ArgumentException("지원하지 않는 명령입니다.");
            return new { ok = true, month = month.ToString("yyyy-MM"), loading = loading };
        }
        static Icon MakeIcon()
        {
            using (var bitmap = new Bitmap(32, 32)) using (var g = Graphics.FromImage(bitmap))
            {
                g.Clear(Color.Transparent); using (var brush = new SolidBrush(Color.FromArgb(42, 136, 166))) g.FillRectangle(brush, 2, 3, 28, 27);
                g.FillRectangle(Brushes.White, 6, 11, 20, 16); using (var font = new Font("Segoe UI", 11, FontStyle.Bold)) g.DrawString("31", font, Brushes.Teal, 6, 10);
                IntPtr handle = bitmap.GetHicon(); var icon = (Icon)Icon.FromHandle(handle).Clone(); Win.DestroyIcon(handle); return icon;
            }
        }
    }
}
