using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace TeamCalendar
{
    sealed class SettingsChoice : ComboBox
    {
        public SettingsChoice() { DrawMode = DrawMode.OwnerDrawFixed; ItemHeight = 23; }
        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            using (var brush = new SolidBrush((e.State & DrawItemState.Selected) != 0 ? Color.FromArgb(47, 79, 96) : Design.Panel)) e.Graphics.FillRectangle(brush, e.Bounds);
            string value = e.Index >= 0 && e.Index < Items.Count ? GetItemText(Items[e.Index]) : Text;
            TextRenderer.DrawText(e.Graphics, value, Font, new Rectangle(e.Bounds.X + 7, e.Bounds.Y, Math.Max(0, e.Bounds.Width - 12), e.Bounds.Height), Design.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            if ((e.State & DrawItemState.Focus) != 0) e.DrawFocusRectangle();
        }
    }
    sealed class CalendarAdvancedSettings : ThemeWindow
    {
        readonly CalendarSettingsDraft parent, draft;
        readonly ComboBox calendar, fontSize;
        readonly TextBox email, nickname, summaryTime;
        readonly ColorPalette palette;
        readonly CheckBox reminder, allDay, autoSync, compact, summary, desktop, startup, updateNotifications;
        readonly TrackBar opacity;
        readonly Label message;
        CalendarSource selected;
        bool loading, connecting;
        public bool ConnectionOpened { get; private set; }
        sealed class SourceItem
        {
            public CalendarSource Source;
            public override string ToString() { return Source.name; }
        }
        public CalendarAdvancedSettings(CalendarSettingsDraft pending, Action testSummary) : base("고급 설정", new Size(690, 710))
        {
            parent = pending; draft = new CalendarSettingsDraft(parent.Work, parent.Appearance, parent.Appearance.Desktop, parent.Startup, parent.Profiles) { OpenCodex = parent.OpenCodex };
            StartPosition = FormStartPosition.CenterParent; KeepOpen = delegate { return connecting; };
            Caption("기존 Chrome 계정", 22, 76, 315); Caption("계정 이메일 (선택)", 350, 76, 318);
            var login = Action("Google 로그인 열기", 22, 103, 310, async delegate
            {
                connecting = true; string loginEmail = email.Text.Trim(), loginProfile = draft.Work.ActiveProfile(); message.Text = "Chrome 계정을 확인하는 중…";
                try
                {
                    var result = await System.Threading.Tasks.Task.Run(delegate { return AccountDiscovery.Read(loginProfile, loginEmail, null); });
                    if (!IsDisposed) { ConnectionOpened = true; if (draft.Work.connectionMode == "account") draft.UseAccountCatalog(result); else { draft.Work.chromeProfile = result.profile; draft.Work.googleUser = result.email; } email.Text = result.email; ReloadCalendars(); message.Text = "Chrome 연결을 확인했습니다. 돌아간 뒤 적용해 주세요."; }
                }
                catch (Exception error) { if (!IsDisposed) message.Text = error.Message; } finally { connecting = false; }
            });
            email = Field("구글 계정 이메일", 350, 103, 318); email.Text = draft.Work.ActiveUser();
            Caption("캘린더 관리", 22, 158, 310); Caption("표시 이름", 350, 158, 318);
            calendar = new SettingsChoice { Left = 22, Top = 187, Width = 310, DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat, BackColor = Design.Panel, ForeColor = Design.Text, AccessibleName = "관리할 캘린더" }; Controls.Add(calendar);
            nickname = Field("캘린더 표시 이름", 350, 185, 318);
            palette = new ColorPalette { Left = 350, Top = 232 }; Controls.Add(palette);
            Action("목록에서 제외", 22, 232, 155, delegate
            {
                if (selected == null) return;
                draft.Work.ConnectionSources().Remove(selected); selected = null; ReloadCalendars();
            });
            calendar.SelectedIndexChanged += delegate { if (loading) return; SaveSource(); LoadSource(); };
            ReloadCalendars();
            Caption("알림과 표시", 22, 290, 646);
            reminder = Check("Windows · 시작 1시간 전 알림", 22, 325, 315, draft.Work.remindersEnabled);
            autoSync = Check("1시간마다 자동 동기화", 350, 325, 318, draft.Work.autoSyncEnabled);
            allDay = Check("종일 일정은 오전 9시 기준으로 알림", 22, 359, 315, draft.Work.allDayReminders);
            compact = Check("작은 창에서만 이모지로 표시", 350, 359, 318, draft.Work.compactEmojis);
            summary = Check("매일 일정 요약", 22, 393, 150, draft.Work.dailySummaryEnabled);
            Caption("한국 시각", 176, 398, 85);
            summaryTime = Field("일정 요약 시각 HH:mm (한국 시간)", 265, 391, 73); summaryTime.Text = draft.Work.dailySummaryTime;
            Action("시스템 알림 테스트", 350, 391, 220, delegate { testSummary(); });
            var opacityLabel = Caption("불투명도  " + draft.Appearance.Opacity + "%", 22, 444, 220);
            opacity = new TrackBar { Left = 18, Top = 472, Width = 220, Height = 30, Minimum = 35, Maximum = 100, Value = Math.Max(35, Math.Min(100, draft.Appearance.Opacity)), TickStyle = TickStyle.None, BackColor = Design.Background, AccessibleName = "위젯 불투명도" }; Controls.Add(opacity);
            opacity.ValueChanged += delegate { opacityLabel.Text = "불투명도  " + opacity.Value + "%"; };
            Caption("글자 크기", 268, 444, 100);
            fontSize = new SettingsChoice { Left = 268, Top = 472, Width = 90, DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat, BackColor = Design.Panel, ForeColor = Design.Text, AccessibleName = "위젯 글자 크기" };
            foreach (int value in new[] { 9, 10, 11, 12, 14 }) fontSize.Items.Add(value);
            fontSize.SelectedItem = draft.Appearance.FontSize; if (fontSize.SelectedIndex < 0) fontSize.SelectedIndex = 2; Controls.Add(fontSize);
            desktop = Check("바탕화면에 붙이기", 389, 472, 265, draft.Appearance.Desktop);
            startup = Check("윈도우 로그인 시 자동 실행", 22, 522, 390, draft.Startup);
            updateNotifications = Check("새 버전 업데이트 알림", 22, 558, 315, draft.Work.updateNotifications);
            Action("지금 확인", 350, 556, 220, delegate { UpdateRuntime.CheckNow(this); });
            message = Caption("돌아간 뒤 ‘적용’을 눌러야 변경 사항이 저장됩니다.", 22, 602, 646); message.Height = 42;
            Action("적용 후 Codex · Dot 설정", 22, 653, 255, delegate { draft.OpenCodex = true; message.Text = "최종 적용을 마치면 Codex · Dot 설정이 열립니다."; });
            Action("취소", 424, 653, 115, delegate { Close(); });
            Action("돌아가기", 553, 653, 115, delegate { ReturnToSettings(); });
        }
        CheckBox Check(string text, int x, int y, int width, bool enabled)
        {
            var box = new CheckBox { Text = text, AccessibleName = text, Left = x, Top = y, Width = width, Height = 28, Checked = enabled, ForeColor = Design.Text }; Controls.Add(box); return box;
        }
        void SaveSource() { if (selected == null) return; string value = nickname.Text.Trim(); if (value != "") selected.name = value; draft.Work.SetSourceColor(selected, selected.owner, palette.Selected); }
        void LoadSource()
        {
            var item = calendar.SelectedItem as SourceItem; selected = item == null ? null : item.Source;
            nickname.Text = selected == null ? "" : selected.name; nickname.Enabled = selected != null; palette.Enabled = selected != null;
            if (selected != null) { palette.Selected = selected.color; palette.Invalidate(); }
        }
        void ReloadCalendars()
        {
            loading = true; calendar.Items.Clear(); foreach (var source in draft.Work.ActiveCalendars()) calendar.Items.Add(new SourceItem { Source = source });
            if (calendar.Items.Count > 0) calendar.SelectedIndex = 0; loading = false; LoadSource();
        }
        internal bool ReturnToSettings()
        {
            if (!DailySummaryPolicy.ValidTime(summaryTime.Text.Trim())) { message.Text = "요약 시각은 00:00~23:59 사이의 HH:mm으로 입력해 주세요."; summaryTime.Focus(); return false; }
            SaveSource();
            if (draft.Work.connectionMode == "account") draft.Work.accountEmail = email.Text.Trim();
            else draft.Work.googleUser = email.Text.Trim();
            draft.Work.remindersEnabled = reminder.Checked; draft.Work.allDayReminders = allDay.Checked;
            draft.Work.autoSyncEnabled = autoSync.Checked; draft.Work.compactEmojis = compact.Checked;
            draft.Work.updateNotifications = updateNotifications.Checked;
            draft.Work.dailySummaryEnabled = summary.Checked; draft.Work.SetDailySummaryTime(summaryTime.Text.Trim());
            draft.Appearance.Opacity = opacity.Value; draft.Appearance.FontSize = Convert.ToInt32(fontSize.SelectedItem); draft.Appearance.Desktop = desktop.Checked;
            draft.Startup = startup.Checked; parent.MergeAdvanced(draft);
            DialogResult = DialogResult.OK; Close(); return true;
        }
    }
}
