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
        ContextMenuStrip profileMenu;
        bool changing, connecting;
        int generation;
        public int OpacityValue { get { return draft.Appearance.Opacity; } }
        public int FontSizeValue { get { return draft.Appearance.FontSize; } }
        public bool DesktopValue { get { return draft.Appearance.Desktop; } }
        public bool StartupValue { get { return draft.Startup; } }
        public bool OpenCodexAfterApply { get { return draft.OpenCodex; } }
        public bool QuitRequested { get { return false; } }
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
            connect = Action("Google 계정 연결", 22, 155, 245, delegate { ChooseAccount(); });
            connection = Caption("Google에서 로그인하고 사용할 캘린더를 선택하세요.", 22, 202, 576); connection.Height = 42;
            Reparent(accountPanel, connect); Reparent(accountPanel, connection);
            calendarId = Field("구글 캘린더 ID", 22, 155, 421); var idField = calendarId.Parent;
            var add = Action("추가", 456, 154, 142, delegate { StageCalendarId(); });
            var idHelp = Caption("캘린더 ID를 붙여넣고 추가하세요. 이름과 색상은 고급 설정에서 바꿀 수 있습니다.", 22, 202, 576); idHelp.Height = 42;
            Reparent(idPanel, idField); Reparent(idPanel, add); Reparent(idPanel, idHelp);
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
            FormClosed += delegate { generation++; if (advancedWindow != null && !advancedWindow.IsDisposed) advancedWindow.Close(); if (profileMenu != null) profileMenu.Dispose(); };
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
            connection.Text = draft.Work.accountEmail == "" ? "Google에서 로그인하고 사용할 캘린더를 선택하세요." : "연결 계정 · " + draft.Work.accountEmail;
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
        void ChooseAccount()
        {
            if (connecting) return;
            var available = draft.Profiles.Where(p => p.email != "").ToList();
            if (draft.Work.accountProfile != "" || draft.Work.accountEmail != "" || available.Count == 0) { ConnectAccount(); return; }
            if (available.Count == 1) { SelectAccount(available[0]); return; }
            if (profileMenu != null) profileMenu.Dispose(); profileMenu = new ContextMenuStrip();
            foreach (var profile in available)
            {
                var item = profile; profileMenu.Items.Add(profile.name + " · " + profile.email, null, delegate { SelectAccount(item); });
            }
            profileMenu.Items.Add("다른 Google 계정", null, delegate { ConnectAccount(); }); profileMenu.Show(connect, new Point(0, connect.Height));
        }
        void SelectAccount(FeatureState.Profile profile) { draft.Work.accountProfile = profile.directory; draft.Work.accountEmail = profile.email; UpdateMode(); ConnectAccount(); }
        async void ConnectAccount()
        {
            if (connecting) return;
            connecting = true; connect.Enabled = apply.Enabled = advanced.Enabled = accountMode.Enabled = idMode.Enabled = false;
            string profile = draft.Work.accountProfile, email = draft.Work.accountEmail; int request = ++generation;
            connection.Text = "Google 계정의 캘린더를 확인하고 있습니다…";
            try
            {
                var result = await Task.Run(delegate { return AccountDiscovery.Read(profile, email, delegate(string text)
                { if (!IsDisposed && IsHandleCreated) try { BeginInvoke((Action)delegate { if (!IsDisposed && generation == request) connection.Text = text; }); } catch { } }); });
                if (IsDisposed || request != generation) return;
                draft.UseAccountCatalog(result); UpdateMode(); ReloadSources();
                message.Text = result.warnings.Count == 0 ? "표시할 캘린더에 체크한 뒤 ‘" + apply.Text + "’ 버튼을 누르세요." : "일부 특수 캘린더를 제외했습니다. 표시할 캘린더를 선택하세요.";
            }
            catch (Exception)
            {
                if (IsDisposed || request != generation) return;
                connection.Text = "Google에서 로그인을 마친 뒤 같은 연결 버튼을 다시 눌러주세요.";
                try { AccountDiscovery.OpenLogin(profile, email); }
                catch (Exception error) { connection.Text = error.Message; }
            }
            finally { if (!IsDisposed) { connecting = false; connect.Enabled = apply.Enabled = advanced.Enabled = accountMode.Enabled = idMode.Enabled = true; } }
        }
        void OpenAdvanced()
        {
            if (advancedWindow != null && !advancedWindow.IsDisposed) { advancedWindow.Activate(); return; }
            advancedWindow = new CalendarAdvancedSettings(draft, testSummary); Enabled = false;
            advancedWindow.FormClosed += delegate
            {
                if (!IsDisposed) { Enabled = true; UpdateMode(); ReloadSources(); }
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
