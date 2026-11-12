using System;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace TeamCalendar
{
    sealed class CalendarManager : ThemeWindow
    {
        readonly FeatureState original;
        readonly CalendarSettingsDraft draft;
        readonly bool firstRun;
        readonly CalendarSourceList sources;
        readonly RadioButton accountMode, idMode;
        readonly SoftButton connect, advanced, apply;
        readonly Label connection, selection, message;
        readonly TextBox calendarId;
        readonly Panel accountPanel, idPanel;
        readonly Action testSummary;
        CalendarAdvancedSettings advancedWindow;
        bool changing, connecting;
        int generation;
        public int OpacityValue { get { return draft.Appearance.Opacity; } }
        public int FontSizeValue { get { return draft.Appearance.FontSize; } }
        public bool DesktopValue { get { return draft.Appearance.Desktop; } }
        public bool StartupValue { get { return draft.Startup; } }
        public bool OpenCodexAfterApply { get { return draft.OpenCodex; } }
        public bool QuitRequested { get { return false; } }
        public bool ConnectionChanged { get; private set; }
        public CalendarManager(FeatureState state, Settings appearance, bool pinned, bool autoStart, Action codexSetup, Action summaryPreview, bool initialSetup = false, System.Collections.Generic.IEnumerable<FeatureState.Profile> profiles = null)
            : base(initialSetup || !state.setupCompleted ? "캘린더 시작하기" : "캘린더 설정", new Size(620, 650))
        {
            original = state; firstRun = initialSetup || !state.setupCompleted; testSummary = summaryPreview;
            draft = new CalendarSettingsDraft(state, appearance, pinned, autoStart, profiles); StartPosition = FormStartPosition.CenterScreen;
            if (firstRun && draft.Work.calendars.Count == 0 && draft.Work.accountCalendars.Count == 0) draft.Work.connectionMode = "account";
            Caption("1  캘린더 연결", 22, 76, 576);
            accountMode = new RadioButton { Text = "Google 계정 연결", AccessibleName = "계정 연동 방식", Left = 22, Top = 111, Width = 280, Height = 28, ForeColor = Design.Text };
            idMode = new RadioButton { Text = "캘린더 ID 입력", AccessibleName = "ID 입력 방식", Left = 324, Top = 111, Width = 274, Height = 28, ForeColor = Design.Text };
            Controls.Add(accountMode); Controls.Add(idMode);
            accountPanel = new Panel { Left = 0, Top = 152, Width = 620, Height = 93, BackColor = Design.Background }; Controls.Add(accountPanel);
            idPanel = new Panel { Left = 0, Top = 152, Width = 620, Height = 93, BackColor = Design.Background }; Controls.Add(idPanel);
            connect = Action("Google 계정 연결", 22, 155, 245, delegate { ConnectAccount(); });
            var login = Action("Google 로그인 열기", 282, 155, 210, delegate { OpenLogin(); });
            connection = Caption("Google에서 로그인하고 사용할 캘린더를 선택하세요.", 22, 202, 576); connection.Height = 42;
            Reparent(accountPanel, connect); Reparent(accountPanel, login); Reparent(accountPanel, connection);
            calendarId = Field("구글 캘린더 ID", 22, 155, 421); var idField = calendarId.Parent;
            var add = Action("추가", 456, 154, 142, delegate { StageCalendarId(); });
            var idHelp = Caption("비공개 캘린더는 전용 Google 로그인도 한 번 필요합니다.", 22, 202, 402);
            var idLogin = Action("Google 로그인 열기", 442, 202, 156, delegate { OpenLogin(); }); idHelp.Height = 42;
            Reparent(idPanel, idField); Reparent(idPanel, add); Reparent(idPanel, idHelp); Reparent(idPanel, idLogin);
            Caption("2  표시할 캘린더 선택", 22, 261, 576);
            sources = new CalendarSourceList { Left = 22, Top = 298, Width = 576, Height = 199, Font = Font }; Controls.Add(sources);
            sources.CheckedChanged = delegate(string sourceId, bool enabled) { draft.Work.ConnectionSources().First(c => c.id == sourceId).enabled = enabled; UpdateSelection(); };
            selection = Caption("", 22, 510, 576);
            message = Caption("‘" + (firstRun ? "시작하기" : "적용") + "’ 버튼을 누르면 변경 사항이 저장됩니다.", 22, 547, 576); message.Height = 45;
            advanced = Action("고급 설정", 22, 601, 150, delegate { OpenAdvanced(); });
            Action("취소", 354, 601, 115, delegate { Close(); });
            apply = Action(firstRun ? "시작하기" : "적용", 483, 601, 115, delegate { TryApply(); });
            changing = true; accountMode.Checked = draft.Work.connectionMode == "account"; idMode.Checked = !accountMode.Checked; changing = false;
            accountMode.CheckedChanged += ChangeMode; idMode.CheckedChanged += ChangeMode;
            UpdateMode(); ReloadSources();
            FormClosed += delegate { generation++; if (advancedWindow != null && !advancedWindow.IsDisposed) advancedWindow.Close(); };
        }
        static void Reparent(Panel panel, Control control) { int top = control.Top; panel.Controls.Add(control); control.Top = top - panel.Top; }
        void ChangeMode(object sender, EventArgs args)
        {
            if (changing || (!idMode.Checked && !accountMode.Checked)) return;
            generation++; draft.Work.connectionMode = accountMode.Checked ? "account" : "id";
            if (accountMode.Checked && draft.Work.accountProfile == "" && draft.Work.accountCalendars.Count == 0)
            { draft.Work.accountProfile = draft.Work.chromeProfile; draft.Work.accountEmail = draft.Work.googleUser; }
            UpdateMode(); ReloadSources();
        }
        void UpdateMode()
        {
            bool account = draft.Work.connectionMode == "account";
            accountPanel.Visible = account; idPanel.Visible = !account;
            connection.Text = !GoogleBrowser.Shared.Verified ? "전용 Google 계정 연결을 한 번 확인해 주세요." : draft.Work.accountEmail == "" ? "Google에서 로그인하고 사용할 캘린더를 선택하세요." : "연결 계정 · " + draft.Work.accountEmail;
        }
        void UpdateSelection() { selection.Text = draft.Work.ActiveCalendars().Count(c => c.enabled) + "개 선택 · ‘" + (firstRun ? "시작하기" : "적용") + "’ 후 표시합니다."; }
        void ReloadSources()
        {
            bool account = draft.Work.connectionMode == "account";
            sources.EmptyMessage = account ? "Google 계정을 연결하면 캘린더 목록이 여기에 나타납니다." : "위에 캘린더 ID를 입력해 주세요.";
            sources.SetItems(draft.Work.ActiveCalendars().Select(c => new SourceChoice { id = c.id, name = c.name, color = c.color, enabled = c.enabled, context = account ? "" : c.id }).ToList()); UpdateSelection();
        }
        bool StageCalendarId()
        {
            if (!draft.AddId(calendarId.Text)) { message.Text = "올바른 캘린더 ID를 입력해 주세요."; calendarId.Focus(); return false; }
            calendarId.Clear(); ReloadSources(); message.Text = "목록에 추가했습니다. ‘" + apply.Text + "’ 버튼을 누르면 반영됩니다."; return true;
        }
        async void OpenLogin()
        {
            if (connecting) return;
            connecting = true; connect.Enabled = apply.Enabled = advanced.Enabled = accountMode.Enabled = idMode.Enabled = false;
            message.Text = "전용 Google 창에서 로그인한 뒤 창을 닫아 주세요.";
            connection.Text = "전용 Google 로그인 창을 여는 중…";
            try
            {
                await Task.Run(delegate { AccountDiscovery.OpenLogin("", draft.Work.ActiveUser()); });
                if (!IsDisposed) { ConnectionChanged = true; connection.Text = "로그인한 뒤 Chrome 창을 닫고 ‘Google 계정 연결’을 다시 눌러주세요."; connect.Text = "로그인 완료 · 연결"; connect.AccessibleName = connect.Text; message.Text = draft.Work.connectionMode == "id" ? "Chrome 창을 닫고 적용한 뒤 새로고침해 주세요." : "Chrome 창을 닫고 ‘로그인 완료 · 연결’을 눌러주세요."; }
            }
            catch (Exception error) { if (!IsDisposed) connection.Text = error.Message; }
            finally { if (!IsDisposed) { connecting = false; connect.Enabled = apply.Enabled = advanced.Enabled = accountMode.Enabled = idMode.Enabled = true; } }
        }
        async void ConnectAccount()
        {
            if (connecting) return;
            connecting = true; connect.Enabled = apply.Enabled = advanced.Enabled = accountMode.Enabled = idMode.Enabled = false;
            string profile = draft.Work.accountProfile, email = draft.Work.accountEmail; int request = ++generation; bool needsLogin = false;
            connection.Text = "Google 계정의 캘린더를 확인하고 있습니다…";
            try
            {
                var result = await Task.Run(delegate { return AccountDiscovery.Read(profile, email, delegate(string text)
                { if (!IsDisposed && IsHandleCreated) try { BeginInvoke((Action)delegate { if (!IsDisposed && generation == request) connection.Text = text; }); } catch { } }); });
                if (IsDisposed || request != generation) return;
                draft.UseAccountCatalog(result); ConnectionChanged = true; connect.Text = "Google 계정 연결"; connect.AccessibleName = connect.Text; UpdateMode(); ReloadSources();
                message.Text = result.warnings.Count == 0 ? "표시할 캘린더에 체크한 뒤 ‘" + apply.Text + "’ 버튼을 누르세요." : "일부 특수 캘린더를 제외했습니다. 표시할 캘린더를 선택하세요.";
            }
            catch (GoogleLoginRequired)
            {
                if (IsDisposed || request != generation) return;
                connection.Text = "전용 Google 창에서 로그인한 뒤 창을 닫고 같은 연결 버튼을 다시 눌러주세요.";
                connect.Text = "로그인 완료 · 연결"; connect.AccessibleName = connect.Text;
                needsLogin = true;
            }
            catch (Exception error) { if (!IsDisposed && request == generation) connection.Text = error.Message; }
            finally { if (!IsDisposed) { connecting = false; connect.Enabled = apply.Enabled = advanced.Enabled = accountMode.Enabled = idMode.Enabled = true; } }
            if (needsLogin && !IsDisposed) OpenLogin();
        }
        void OpenAdvanced()
        {
            if (advancedWindow != null && !advancedWindow.IsDisposed) { advancedWindow.Activate(); return; }
            advancedWindow = new CalendarAdvancedSettings(draft, testSummary); Enabled = false;
            advancedWindow.FormClosed += delegate
            {
                if (!IsDisposed) { if (advancedWindow.ConnectionOpened) ConnectionChanged = true; Enabled = true; UpdateMode(); ReloadSources(); }
                advancedWindow.Dispose(); advancedWindow = null;
            };
            advancedWindow.Show(this);
        }
        internal bool TryApply()
        {
            if (connecting) return false;
            if (draft.Work.connectionMode == "id" && calendarId.Text.Trim() != "" && !StageCalendarId()) return false;
            if (firstRun && !draft.Work.ActiveCalendars().Any(c => c.enabled)) { message.Text = "표시할 캘린더를 하나 이상 선택해 주세요."; return false; }
            draft.Commit(original); DialogResult = DialogResult.OK; Close(); return true;
        }
    }
}
