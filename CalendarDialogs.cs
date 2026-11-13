using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace TeamCalendar
{
    sealed class SoftButton : Button
    {
        public Color Fill = Design.Button;
        bool hover;
        public SoftButton() { FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0; ForeColor = Design.Text; Cursor = Cursors.Hand; Font = new Font("맑은 고딕", 9.5F); }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Parent == null ? Design.Background : Parent.BackColor); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = Design.Round(new RectangleF(0, 0, Width - 1, Height - 1), Design.ControlRadius)) using (var fill = new SolidBrush(hover ? ControlPaint.Light(Fill, .12F) : Fill)) e.Graphics.FillPath(fill, path);
            TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, Enabled ? ForeColor : Design.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
    }
    class ThemeWindow : Form
    {
        public readonly Label Heading;
        Point start, origin; bool dragging;
        public Func<bool> KeepOpen;
        internal static bool ShouldDismiss(Form popup, Form active) { for (var current = active; current != null; current = current.Owner) if (current == popup) return false; return true; }
        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            if (!IsHandleCreated || IsDisposed) return;
            BeginInvoke((System.Action)delegate { if (!IsDisposed && Visible && Enabled && (KeepOpen == null || !KeepOpen()) && ShouldDismiss(this, Form.ActiveForm)) { ThemeWindow root = this; while (root.Owner is ThemeWindow) root = (ThemeWindow)root.Owner; if (ShouldDismiss(root, Form.ActiveForm)) root.Close(); else Close(); } });
        }
        void Shape() { if (Width <= 0 || Height <= 0) return; using (var path = Design.Round(new RectangleF(0, 0, Width, Height), Design.WindowRadius)) { var old = Region; Region = new Region(path); if (old != null) old.Dispose(); } }
        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); Shape(); }
        protected override void OnPaint(PaintEventArgs e) { base.OnPaint(e); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias; using (var path = Design.Round(new RectangleF(0.5F, 0.5F, Width - 1, Height - 1), Design.WindowRadius)) using (var pen = new Pen(Design.Border)) e.Graphics.DrawPath(pen, path); }
        public ThemeWindow(string title, Size size)
        {
            KeyPreview = true; KeyDown += delegate(object sender, KeyEventArgs e) { if (e.KeyCode == Keys.Escape) { e.Handled = true; Close(); } };
            Text = title; Size = size; FormBorderStyle = FormBorderStyle.None; StartPosition = FormStartPosition.Manual;
            BackColor = Design.Background; ForeColor = Design.Text; Font = new Font("맑은 고딕", 10);
            var header = new Panel { Dock = DockStyle.Top, Height = 52, Width = size.Width, BackColor = Design.Header };
            Heading = new Label { Text = title, Left = 20, Top = 0, Height = 52, Width = size.Width - 85, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("맑은 고딕", 12, FontStyle.Bold), Cursor = Cursors.SizeAll };
            var close = new SoftButton { Text = "×", AccessibleName = "닫기", Width = 34, Height = 32, Left = size.Width - 48, Top = 10, Anchor = AnchorStyles.Top | AnchorStyles.Right, Fill = header.BackColor };
            close.Click += delegate { Close(); }; header.Controls.Add(Heading); header.Controls.Add(close); Controls.Add(header);
            foreach (Control c in new Control[] { header, Heading })
            {
                c.MouseDown += delegate(object s, MouseEventArgs e) { if (e.Button == MouseButtons.Left) { dragging = true; start = Cursor.Position; origin = Location; ((Control)s).Capture = true; } };
                c.MouseMove += delegate { if (dragging) Location = new Point(origin.X + Cursor.Position.X - start.X, origin.Y + Cursor.Position.Y - start.Y); };
                c.MouseUp += delegate(object s, MouseEventArgs e) { dragging = false; ((Control)s).Capture = false; };
            }
            Resize += delegate { Shape(); };
        }
        protected Label Caption(string text, int x, int y, int width) { var l = new Label { Text = text, Left = x, Top = y, Width = width, Height = 24, ForeColor = Design.Muted }; Controls.Add(l); return l; }
        protected TextBox Field(string accessibleName, int x, int y, int width, int height = 31, bool multiline = false)
        {
            var panel = new RoundedField { Left = x, Top = y, Width = width, Height = height, BackColor = Design.Panel, Padding = new Padding(9, 5, 9, 5) };
            var field = new TextBox { AccessibleName = accessibleName, Multiline = multiline, BorderStyle = BorderStyle.None, Dock = DockStyle.Fill, BackColor = Design.Panel, ForeColor = Design.Text, Font = Font, ScrollBars = ScrollBars.None };
            panel.Controls.Add(field); Controls.Add(panel); return field;
        }
        protected SoftButton Action(string text, int x, int y, int width, EventHandler handler)
        {
            var b = new SoftButton { Text = text, AccessibleName = text, Left = x, Top = y, Width = width, Height = 34 }; b.Click += handler; Controls.Add(b); return b;
        }
    }
    sealed class RoundedField : Panel
    {
        protected override void OnSizeChanged(EventArgs e) { base.OnSizeChanged(e); if (Width < 1 || Height < 1) return; using (var path = Design.Round(ClientRectangle, Design.ControlRadius)) { var old = Region; Region = new Region(path); if (old != null) old.Dispose(); } }
    }
    sealed class ColorPalette : Control
    {
        public string Selected = Design.Colors[0];
        public event EventHandler Changed;
        public ColorPalette() { Size = new Size(246, 30); DoubleBuffered = true; Cursor = Cursors.Hand; }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            for (int i = 0; i < Design.Colors.Length; i++)
            {
                var rect = new Rectangle(i * 38 + 2, 3, 27, 24);
                using (var path = Design.Round(rect, 6)) using (var brush = new SolidBrush(Design.Parse(Design.Colors[i]))) e.Graphics.FillPath(brush, path);
                if (Selected == Design.Colors[i]) using (var pen = new Pen(Design.Text, 2)) using (var outline = Design.Round(new RectangleF(rect.X - 2, rect.Y - 2, rect.Width + 4, rect.Height + 4), Design.ControlRadius)) e.Graphics.DrawPath(pen, outline);
            }
        }
        protected override void OnMouseDown(MouseEventArgs e) { int index = e.X / 38; if (index >= 0 && index < Design.Colors.Length) { Selected = Design.Colors[index]; Invalidate(); if (Changed != null) Changed(this, EventArgs.Empty); } }
    }
    sealed class EventCanvas : Control
    {
        public List<CalendarEvent> Items = new List<CalendarEvent>();
        public Action<CalendarEvent> Selected;
        public Func<CalendarEvent, bool> Important;
        public Func<CalendarEvent, Color> ItemColor;
        public string EmptyMessage = "등록된 일정이 없습니다. 아래에 메모를 남겨 보세요.";
        int scroll, selection = -1;
        const int RowHeight = 58;
        public EventCanvas() { DoubleBuffered = true; BackColor = Design.Background; Cursor = Cursors.Hand; }
        public void SetItems(List<CalendarEvent> items) { Items = items; selection = -1; scroll = Math.Min(scroll, Math.Max(0, items.Count * RowHeight - Height)); Invalidate(); }
        public void ResetScroll() { scroll = 0; Invalidate(); }
        protected override void OnMouseWheel(MouseEventArgs e) { scroll = Math.Max(0, Math.Min(Math.Max(0, Items.Count * RowHeight - Height), scroll - e.Delta / 3)); Invalidate(); }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            int index = (e.Y + scroll) / RowHeight; if (index < 0 || index >= Items.Count) return; selection = index; Invalidate(); if (Selected != null) Selected(Items[index]);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            if (Items.Count == 0) { TextRenderer.DrawText(e.Graphics, EmptyMessage, Font, new Rectangle(6, 15, Width - 16, 60), Design.Muted, TextFormatFlags.WordBreak); return; }
            for (int i = 0; i < Items.Count; i++)
            {
                int y = i * RowHeight - scroll; if (y + RowHeight < 0 || y > Height) continue;
                var item = Items[i]; var rect = new Rectangle(0, y + 2, Width - 13, RowHeight - 6);
                using (var path = Design.Round(rect, 8)) using (var brush = new SolidBrush(i == selection ? Color.FromArgb(53, 82, 100) : Design.Panel)) e.Graphics.FillPath(brush, path);
                using (var brush = new SolidBrush((ItemColor == null ? Design.Parse(item.color) : ItemColor(item)))) e.Graphics.FillRectangle(brush, rect.Left + 8, rect.Top + 11, 3, 28);
                TextRenderer.DrawText(e.Graphics, (Important != null && Important(item) ? "★ " : "") + item.title, Font, new Rectangle(20, y + 7, Width - 42, 24), Design.Text, TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
                using (var small = new Font("맑은 고딕", 8.5F)) TextRenderer.DrawText(e.Graphics, item.time + "  ·  " + item.calendarName, small, new Rectangle(20, y + 30, Width - 42, 18), Design.Muted, TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
            }
            if (Items.Count * RowHeight > Height)
            {
                float thumb = Math.Max(22, Height * Height / (float)(Items.Count * RowHeight));
                float y = scroll / (float)(Items.Count * RowHeight - Height) * (Height - thumb);
                using (var path = Design.Round(new RectangleF(Width - 5, y, 4, thumb), 2)) using (var brush = new SolidBrush(Color.FromArgb(91, 126, 145))) e.Graphics.FillPath(brush, path);
            }
        }
    }
    sealed class SourceAppearanceDialog : ThemeWindow
    {
        public SourceAppearanceDialog(FeatureState state, string sourceId) : base("캘린더 색상 · 소유자", new Size(520, 330))
        {
            var source = state.ConnectionSources().First(c => c.id == sourceId);
            StartPosition = FormStartPosition.CenterParent;
            var name = Caption(source.name, 22, 70, 476); name.AutoEllipsis = true;
            Caption("소유자 계정 (선택)", 22, 108, 476);
            var owner = Field("캘린더 소유자 계정", 22, 137, 476); owner.Text = FeatureState.OwnerKey(source);
            Caption("같은 소유자 계정의 캘린더는 같은 색으로 표시합니다.", 22, 182, 476);
            var colors = new ColorPalette { Left = 22, Top = 218, Selected = source.color }; Controls.Add(colors);
            Action("취소", 254, 275, 115, delegate { Close(); });
            Action("선택", 383, 275, 115, delegate { state.SetSourceColor(source, owner.Text, colors.Selected); DialogResult = DialogResult.OK; Close(); });
        }
    }
    sealed class DayDetails : ThemeWindow
    {
        readonly CalendarWindow owner;
        readonly EventCanvas list;
        readonly TextBox entry, time;
        readonly ColorPalette palette;
        readonly CheckBox important;
        readonly Label detail;
        readonly SoftButton delete;
        readonly SoftButton googleAdd, emojiEdit;
        readonly Timer autosave;
        LocalNote editing;
        CalendarEvent selected;
        bool changing;
        public DateTime Day { get; private set; }
        public DayDetails(CalendarWindow calendar) : base("날짜별 일정 · 메모", new Size(590, 680))
        {
            owner = calendar;
            Caption("일정을 선택해 중요 표시와 이모지를 바꿔 보세요.", 20, 66, 380);
            emojiEdit = Action("이모지 설정", 415, 61, 155, delegate { if (selected != null) { SavePending(); owner.ShowEmojiEditor(selected); } }); emojiEdit.Enabled = false;
            list = new EventCanvas { Left = 20, Top = 95, Width = 550, Height = 230, Font = Font, Important = owner.IsImportant, ItemColor = owner.EventColor }; Controls.Add(list);
            list.Selected = SelectItem;
            detail = Caption("", 20, 332, 550); detail.Height = 42;
            Action("★ 선택 일정 강조", 20, 379, 170, delegate { if (selected != null) { owner.ToggleImportant(selected, palette.Selected); RefreshItems(); } });
            Action("이 날짜 칸 강조", 202, 379, 170, delegate { owner.ToggleDay(Day, palette.Selected); });
            googleAdd = Action("구글에서 추가 ↗", 384, 379, 186, delegate { App.Open(App.DraftUrl(Day, "")); });
            Caption("메모", 20, 430, 100); Caption("시간 (선택)", 464, 430, 106);
            entry = Field("메모 내용", 20, 455, 432, 85, true); time = Field("메모 시간", 464, 455, 106);
            palette = new ColorPalette { Left = 20, Top = 551 }; Controls.Add(palette);
            important = new CheckBox { Text = "중요 메모", Left = 300, Top = 553, Width = 135, Height = 27, ForeColor = Design.Text }; Controls.Add(important);
            Action("새 메모", 20, 605, 115, delegate { SavePending(); changing = true; editing = null; entry.Clear(); time.Clear(); important.Checked = false; changing = false; entry.Focus(); });
            delete = Action("메모 삭제", 147, 605, 115, delegate { if (editing != null) { owner.FeatureData.notes.Remove(editing); editing = null; changing = true; entry.Clear(); time.Clear(); changing = false; owner.FeatureEdited(); RefreshItems(); } });
            Action("저장", 454, 605, 116, delegate { SavePending(); detail.Text = "이 PC에 저장했습니다."; });
            autosave = new Timer { Interval = 650 }; autosave.Tick += delegate { autosave.Stop(); SavePending(); };
            entry.TextChanged += AutoSave; time.TextChanged += AutoSave; palette.Changed += AutoSave; important.CheckedChanged += AutoSave;
            FormClosing += delegate { autosave.Stop(); SavePending(); autosave.Dispose(); };
        }
        void AutoSave(object sender, EventArgs e) { if (!changing) { autosave.Stop(); autosave.Start(); } }
        public void SetDay(DateTime day)
        {
            if (day.Date == Day && Visible) return;
            SavePending(); changing = true; Day = day.Date; Text = Heading.Text = day.ToString("M월 d일 dddd", CultureInfo.GetCultureInfo("ko-KR"));
            editing = null; selected = null; emojiEdit.Enabled = false; entry.Clear(); time.Clear(); important.Checked = false; detail.Text = "메모는 입력 후 자동으로 저장됩니다."; changing = false; RefreshItems(); list.ResetScroll();
        }
        public void RefreshItems() { var items = owner.DayEvents(Day); list.SetItems(items); if (selected != null && !items.Any(e => e.id == selected.id)) { selected = null; emojiEdit.Enabled = false; } delete.Enabled = editing != null; googleAdd.Enabled = owner.FeatureData.ActiveCalendars().Any(c => c.enabled); }
        void SelectItem(CalendarEvent item)
        {
            SavePending(); selected = item; emojiEdit.Enabled = true; detail.Text = item.time + (item.location == "" ? "" : "  ·  " + item.location);
            if (!item.isNote) { changing = true; editing = null; entry.Clear(); time.Clear(); important.Checked = false; changing = false; delete.Enabled = false; return; }
            editing = owner.FeatureData.notes.FirstOrDefault(n => "note-" + n.id == item.uid); if (editing == null) return;
            changing = true; entry.Text = editing.text; time.Text = editing.time; palette.Selected = editing.color; palette.Invalidate(); important.Checked = editing.important; changing = false; delete.Enabled = true;
        }
        void SavePending()
        {
            if (changing || Day == DateTime.MinValue || string.IsNullOrWhiteSpace(entry.Text)) return;
            bool validTime = time.Text == "" || Regex.IsMatch(time.Text, @"^(?:[01]\d|2[0-3]):[0-5]\d$");
            if (!validTime) detail.Text = "메모는 저장했어요. 시간은 14:30처럼 입력해 주세요.";
            if (editing == null) { editing = new LocalNote { id = Guid.NewGuid().ToString("N"), date = Day.ToString("yyyy-MM-dd") }; owner.FeatureData.notes.Add(editing); }
            editing.text = entry.Text.Trim(); if (validTime) editing.time = time.Text; editing.color = palette.Selected; editing.important = important.Checked;
            owner.FeatureEdited(); RefreshItems();
        }
    }
}
