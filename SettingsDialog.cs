using System;
using System.Drawing;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace TeamCalendar
{
    sealed class AccountPicker : ThemeWindow
    {
        readonly AccountCatalog catalog;
        readonly HashSet<string> chosen;
        readonly EventCanvas list;
        readonly Label message;
        public List<CalendarSource> SelectedCalendars { get { return catalog.calendars.Select(c => new CalendarSource { id = c.id, name = c.name, color = c.color, enabled = chosen.Contains(c.id) }).ToList(); } }
        public AccountPicker(AccountCatalog result, List<CalendarSource> previous) : base("계정에서 캘린더 선택", new Size(690, 555))
        {
            catalog = result; StartPosition = FormStartPosition.CenterScreen;
            var old = previous.Where(c => c.enabled).Select(c => c.id).ToList();
            chosen = new HashSet<string>(old.Count == 0 ? result.calendars.Select(c => c.id) : old);
            Caption(result.email, 22, 76, 646);
            Caption("표시할 캘린더를 선택하세요. ID는 입력하지 않아도 됩니다.", 22, 110, 646);
            list = new EventCanvas { Left = 22, Top = 153, Width = 646, Height = 260, Font = Font }; Controls.Add(list);
            list.Selected = delegate(CalendarEvent item) { if (!chosen.Add(item.uid)) chosen.Remove(item.uid); Reload(); };
            Action("전체 선택", 22, 431, 130, delegate { foreach (var c in catalog.calendars) chosen.Add(c.id); Reload(); });
            Action("선택 해제", 164, 431, 130, delegate { chosen.Clear(); Reload(); });
            message = Caption(result.warnings.Count == 0 ? "선택한 캘린더만 이 위젯에 표시됩니다." : "일부 특수 캘린더는 목록에서 제외되었습니다.", 22, 479, 370); message.Height = 55;
            Action("선택한 캘린더 사용", 413, 490, 255, delegate { if (chosen.Count == 0) { message.Text = "표시할 캘린더를 하나 이상 선택해 주세요."; return; } DialogResult = DialogResult.OK; Close(); });
            Reload();
        }
        void Reload() { list.SetItems(catalog.calendars.Select(c => new CalendarEvent { uid = c.id, title = (chosen.Contains(c.id) ? "☑  " : "☐  ") + c.name, time = catalog.email, calendarName = chosen.Contains(c.id) ? "선택됨" : "선택 안 함", color = c.color }).ToList()); }
    }
    sealed class CalendarManager : ThemeWindow
    {
        readonly FeatureState original, work;
        readonly EventCanvas sources;
        readonly TextBox id, nickname, email;
        readonly ColorPalette palette;
        readonly ComboBox profiles;
        readonly CheckBox reminder, allDay;
        readonly TrackBar opacity;
        readonly ComboBox fontSize;
        readonly CheckBox desktop, startup;
        readonly RadioButton idMode, accountMode;
        readonly Panel manualPanel, accountPanel;
        readonly Label connectionInfo, accountInfo;
        readonly SoftButton import;
        bool changing = true;
        AccountPicker picker;
        public int OpacityValue { get { return opacity.Value; } }
        public int FontSizeValue { get { return int.Parse(Convert.ToString(fontSize.SelectedItem)); } }
        public bool DesktopValue { get { return desktop.Checked; } }
        public bool StartupValue { get { return startup.Checked; } }
        public bool QuitRequested;
        CalendarSource selected;
        public CalendarManager(FeatureState state, Settings appearance, bool pinned, bool autoStart, Action codexSetup) : base("캘린더 · 계정 · 알림", new Size(690, 855))
        {
            original = state; work = App.Json.Deserialize<FeatureState>(App.Json.Serialize(state)); StartPosition = FormStartPosition.CenterScreen;
            Caption("캘린더 연결 방식", 22, 66, 330);
            Action("Codex · Dot 연동", 448, 60, 220, delegate { codexSetup(); });
            idMode = new RadioButton { Text = "캘린더 ID 직접 입력", AccessibleName = "ID 입력 방식", Left = 22, Top = 103, Width = 310, Height = 28, ForeColor = Design.Text };
            accountMode = new RadioButton { Text = "Google 계정 연동", AccessibleName = "계정 연동 방식", Left = 350, Top = 103, Width = 310, Height = 28, ForeColor = Design.Text };
            Controls.Add(idMode); Controls.Add(accountMode);
            connectionInfo = Caption("선택한 방식 하나만 표시하며, 다른 방식의 설정은 보관합니다.", 22, 139, 646); connectionInfo.Height = 26;
            Caption("연결할 Google 계정 (Chrome 프로필)", 22, 172, 315);
            profiles = new ComboBox { Left = 22, Top = 198, Width = 310, Height = 30, DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat, BackColor = Design.Panel, ForeColor = Design.Text, AccessibleName = "Google 계정 프로필" };
            profiles.Items.Add(new FeatureState.Profile { directory = "", name = "기본 Chrome 계정", email = "" });
            foreach (var profile in FeatureState.Profiles()) profiles.Items.Add(profile); Controls.Add(profiles);
            Caption("계정 이메일 (선택)", 350, 172, 300); email = Field("구글 계정 이메일", 350, 198, 318);
            sources = new EventCanvas { Left = 22, Top = 251, Width = 646, Height = 130, Font = Font }; Controls.Add(sources);
            sources.Selected = delegate(CalendarEvent item) { selected = work.ConnectionSources().First(c => c.id == item.uid); };
            Action("켜기 / 끄기", 22, 394, 135, delegate { if (selected != null) { selected.enabled = !selected.enabled; ReloadSources(); } });
            Action("선택 제외", 168, 394, 115, delegate { if (selected != null) { work.ConnectionSources().Remove(selected); selected = null; ReloadSources(); } });
            manualPanel = new Panel { Left = 0, Top = 441, Width = 690, Height = 135, BackColor = Design.Background }; Controls.Add(manualPanel);
            accountPanel = new Panel { Left = 0, Top = 441, Width = 690, Height = 135, BackColor = Design.Background }; Controls.Add(accountPanel);
            var idLabel = Caption("구글 캘린더 ID", 22, 447, 460); id = Field("구글 캘린더 ID", 22, 470, 460); var idField = id.Parent;
            var nameLabel = Caption("표시 이름", 22, 511, 310); nickname = Field("캘린더 표시 이름", 22, 535, 310); var nameField = nickname.Parent;
            palette = new ColorPalette { Left = 350, Top = 535 }; Controls.Add(palette);
            var add = Action("캘린더 추가", 500, 469, 168, delegate
            {
                string value = id.Text.Trim();
                if (value.Length > 512 || !value.Contains("@") || value.Any(char.IsWhiteSpace)) { connectionInfo.Text = "올바른 캘린더 ID를 입력해 주세요."; id.Focus(); return; }
                if (!work.calendars.Any(c => c.id == value)) work.calendars.Add(new CalendarSource { id = value, name = nickname.Text.Trim() == "" ? value.Split('@')[0] : nickname.Text.Trim(), color = palette.Selected });
                id.Clear(); nickname.Clear(); ReloadSources();
            });
            foreach (Control control in new Control[] { idLabel, idField, nameLabel, nameField, palette, add }) { int y = control.Top; manualPanel.Controls.Add(control); control.Top = y - manualPanel.Top; }
            var login = Action("Google 계정 열기", 22, 451, 195, delegate { try { AccountDiscovery.OpenLogin(CurrentProfile(), email.Text.Trim()); accountInfo.Text = "Google에서 로그인하거나 계정을 선택한 뒤 캘린더 불러오기를 누르세요."; } catch (Exception e) { accountInfo.Text = e.Message; } });
            import = Action("캘린더 불러오기", 230, 451, 225, delegate { ImportAccount(); });
            accountInfo = Caption("로그인한 계정의 캘린더를 이름으로 선택합니다.\n계정 로그인과 권한은 Google에서 처리합니다.", 22, 501, 646); accountInfo.Height = 70;
            foreach (Control control in new Control[] { login, import, accountInfo }) { int y = control.Top; accountPanel.Controls.Add(control); control.Top = y - accountPanel.Top; }
            reminder = new CheckBox { Text = "Windows · 일정 시작 1시간 전 알림", Left = 22, Top = 591, Width = 510, Height = 28, Checked = work.remindersEnabled, ForeColor = Design.Text }; Controls.Add(reminder);
            allDay = new CheckBox { Text = "종일 일정은 오전 9시 기준으로 알림", Left = 22, Top = 624, Width = 510, Height = 28, Checked = work.allDayReminders, ForeColor = Design.Muted }; Controls.Add(allDay);
            var opacityLabel = Caption("불투명도  " + appearance.Opacity + "%", 22, 666, 220);
            opacity = new TrackBar { Left = 18, Top = 691, Width = 220, Height = 30, Minimum = 35, Maximum = 100, Value = Math.Max(35, Math.Min(100, appearance.Opacity)), TickStyle = TickStyle.None, BackColor = Design.Background, AccessibleName = "위젯 불투명도" };
            opacity.ValueChanged += delegate { opacityLabel.Text = "불투명도  " + opacity.Value + "%"; }; Controls.Add(opacity);
            Caption("글자 크기", 268, 666, 100);
            fontSize = new ComboBox { Left = 268, Top = 691, Width = 90, Height = 30, DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat, BackColor = Design.Panel, ForeColor = Design.Text, AccessibleName = "위젯 글자 크기" };
            foreach (int size in new[] { 9, 10, 11, 12, 14 }) fontSize.Items.Add(size);
            fontSize.SelectedItem = appearance.FontSize; if (fontSize.SelectedIndex < 0) fontSize.SelectedIndex = 2; Controls.Add(fontSize);
            desktop = new CheckBox { Text = "바탕화면에 붙이기", Left = 389, Top = 691, Width = 250, Height = 30, Checked = pinned, ForeColor = Design.Text }; Controls.Add(desktop);
            startup = new CheckBox { Text = "윈도우 로그인 시 자동 실행", Left = 22, Top = 739, Width = 390, Height = 28, Checked = autoStart, ForeColor = Design.Muted }; Controls.Add(startup);
            Action("위젯 종료", 22, 799, 125, delegate { QuitRequested = true; DialogResult = DialogResult.OK; Close(); });
            Action("취소", 424, 799, 115, delegate { Close(); });
            Action("적용", 553, 799, 115, delegate
            {
                SaveProfile();
                original.calendars = work.calendars; original.chromeProfile = work.chromeProfile; original.googleUser = work.googleUser;
                original.accountCalendars = work.accountCalendars; original.accountProfile = work.accountProfile; original.accountEmail = work.accountEmail; original.connectionMode = work.connectionMode;
                original.remindersEnabled = reminder.Checked; original.allDayReminders = allDay.Checked; original.reminderMinutes = 60;
                DialogResult = DialogResult.OK; Close();
            });
            idMode.CheckedChanged += ChangeMode; accountMode.CheckedChanged += ChangeMode;
            profiles.SelectedIndexChanged += delegate { if (!changing) { var profile = (FeatureState.Profile)profiles.SelectedItem; email.Text = profile.email; } };
            idMode.Checked = work.connectionMode != "account"; accountMode.Checked = work.connectionMode == "account";
            LoadProfile(); changing = false; ReloadSources(); UpdateMode();
            FormClosed += delegate { if (picker != null && !picker.IsDisposed) picker.Close(); };
        }
        string CurrentProfile() { return profiles.SelectedItem == null ? "" : ((FeatureState.Profile)profiles.SelectedItem).directory; }
        void SaveProfile() { if (work.connectionMode == "account") { work.accountProfile = CurrentProfile(); work.accountEmail = email.Text.Trim(); } else { work.chromeProfile = CurrentProfile(); work.googleUser = email.Text.Trim(); } }
        void LoadProfile()
        {
            bool previous = changing; changing = true; profiles.SelectedIndex = 0;
            for (int i = 0; i < profiles.Items.Count; i++) if (((FeatureState.Profile)profiles.Items[i]).directory == work.ActiveProfile()) profiles.SelectedIndex = i;
            email.Text = work.ActiveUser(); changing = previous;
        }
        void ChangeMode(object sender, EventArgs e)
        {
            if (changing || (!idMode.Checked && !accountMode.Checked)) return;
            SaveProfile();
            if (accountMode.Checked && work.accountCalendars.Count == 0 && work.accountProfile == "") { work.accountProfile = work.chromeProfile; work.accountEmail = work.googleUser; }
            work.connectionMode = accountMode.Checked ? "account" : "id"; LoadProfile(); selected = null; ReloadSources(); UpdateMode();
        }
        void UpdateMode() { manualPanel.Visible = work.connectionMode == "id"; accountPanel.Visible = !manualPanel.Visible; }
        void ReloadSources()
        {
            bool account = work.connectionMode == "account";
            sources.EmptyMessage = account ? "캘린더 불러오기로 계정의 캘린더를 선택하세요." : "아래에 캘린더 ID를 입력해 추가하세요.";
            sources.SetItems(work.ActiveCalendars().Select(c => new CalendarEvent { uid = c.id, title = (c.enabled ? "☑  " : "☐  ") + c.name, time = account ? work.accountEmail : c.id, calendarName = c.enabled ? "표시 중" : "숨김", color = c.color }).ToList());
            int overlap = work.calendars.Select(c => c.id).Intersect(work.accountCalendars.Select(c => c.id)).Count();
            connectionInfo.Text = overlap > 0 ? "중복된 캘린더 " + overlap + "개 · 선택한 방식에서만 표시합니다." : "선택한 방식 하나만 표시하며, 다른 방식의 설정은 보관합니다.";
        }
        async void ImportAccount()
        {
            if (picker != null && !picker.IsDisposed) { picker.Activate(); return; }
            import.Enabled = false; string profile = CurrentProfile(), user = email.Text.Trim();
            accountInfo.Text = "Google 계정의 캘린더를 불러오는 중…";
            try
            {
                var result = await Task.Run(delegate { return AccountDiscovery.Read(profile, user, delegate(string text) { if (!IsDisposed && IsHandleCreated) try { BeginInvoke((Action)delegate { if (!IsDisposed) accountInfo.Text = text; }); } catch { } }); });
                if (IsDisposed || work.connectionMode != "account") return;
                picker = new AccountPicker(result, work.accountCalendars);
                picker.FormClosed += delegate
                {
                    if (!IsDisposed && picker.DialogResult == DialogResult.OK)
                    {
                        work.accountCalendars = picker.SelectedCalendars; work.accountProfile = result.profile; work.accountEmail = result.email;
                        LoadProfile(); ReloadSources(); accountInfo.Text = "계정 연결 준비됨 · " + result.email + "\n적용을 누르면 선택한 캘린더를 표시합니다.";
                    }
                };
                picker.Show(); accountInfo.Text = "불러온 캘린더 목록에서 표시할 항목을 선택하세요.";
            }
            catch (Exception e) { if (!IsDisposed) accountInfo.Text = e.Message; }
            finally { if (!IsDisposed) import.Enabled = true; }
        }
    }
}
