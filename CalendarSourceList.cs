using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace TeamCalendar
{
    sealed class SourceChoice
    {
        public string id, name, context, color;
        public bool enabled;
    }
    sealed class SourceRow : Panel
    {
        public Color Accent;
        public bool Selected;
        public SourceRow() { DoubleBuffered = true; BackColor = Design.Background; }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = Design.Round(new Rectangle(0, 0, Width - 1, Height - 1), 9))
            {
                using (var brush = new SolidBrush(Design.Panel)) e.Graphics.FillPath(brush, path);
                if (Selected) using (var pen = new Pen(Design.Muted)) e.Graphics.DrawPath(pen, path);
            }
            using (var pen = new Pen(Accent, 3)) e.Graphics.DrawLine(pen, 9, 10, 9, Height - 10);
        }
    }
    sealed class CalendarSourceList : Control
    {
        readonly List<SourceRow> rows = new List<SourceRow>();
        int scroll;
        string selected = "";
        public string EmptyMessage = "선택할 캘린더가 없습니다.";
        public Action<string> Selected { get; set; }
        public Action<string, bool> CheckedChanged;
        public Action<string> EditAppearance;
        public CalendarSourceList() { DoubleBuffered = true; BackColor = Design.Background; }
        public void SetItems(List<SourceChoice> choices)
        {
            SuspendLayout();
            foreach (var row in rows) { Controls.Remove(row); row.Dispose(); }
            rows.Clear();
            foreach (var choice in choices)
            {
                var item = choice;
                var row = new SourceRow { Height = 54, Accent = Design.Parse(item.color), Selected = selected == item.id, Tag = item.id };
                var checkbox = new CheckBox { Left = 22, Top = 5, Width = Math.Max(80, Width - 60), Height = 25, Text = item.name, Checked = item.enabled, UseMnemonic = false, AutoEllipsis = true, ForeColor = Design.Text, BackColor = Design.Panel, Font = Font, AccessibleName = item.name + " 표시", AccessibleDescription = item.context, TabStop = true };
                var caption = new Label { Left = 22, Top = 31, Width = Math.Max(80, Width - 60), Height = 21, AutoEllipsis = true, ForeColor = Design.Muted, BackColor = Design.Panel, Font = Font, UseMnemonic = false };
                Action updateCaption = delegate { caption.Text = item.context + (item.context == "" ? "" : " · ") + (checkbox.Checked ? "선택됨" : "선택 안 함"); };
                updateCaption();
                checkbox.CheckedChanged += delegate { SelectItem(item.id); item.enabled = checkbox.Checked; updateCaption(); if (CheckedChanged != null) CheckedChanged(item.id, checkbox.Checked); };
                checkbox.GotFocus += delegate { SelectItem(item.id); };
                row.Click += delegate { SelectItem(item.id); }; caption.Click += delegate { SelectItem(item.id); };
                foreach (Control control in new Control[] { row, checkbox, caption }) control.MouseWheel += delegate(object sender, MouseEventArgs e) { ScrollBy(e.Delta); };
                var colorButton = new SoftButton { Text = "●", AccessibleName = item.name + " 색상 및 소유자", Width = 34, Height = 32, Top = 10, Fill = Design.Parse(item.color), ForeColor = Design.Text };
                colorButton.Click += delegate { if (EditAppearance != null) EditAppearance(item.id); };
                row.Controls.Add(checkbox); row.Controls.Add(caption); row.Controls.Add(colorButton); rows.Add(row); Controls.Add(row);
            }
            PositionRows(); ResumeLayout(); Invalidate();
        }
        void SelectItem(string id)
        {
            selected = id;
            foreach (var row in rows) { row.Selected = Convert.ToString(row.Tag) == id; row.Invalidate(); }
            if (Selected != null) Selected(id);
        }
        void ScrollBy(int delta) { scroll = Math.Max(0, Math.Min(Math.Max(0, rows.Count * 60 - Height), scroll - delta / 3)); PositionRows(); Invalidate(); }
        void PositionRows()
        {
            scroll = Math.Min(scroll, Math.Max(0, rows.Count * 60 - Height));
            for (int i = 0; i < rows.Count; i++) { var row = rows[i]; row.SetBounds(0, i * 60 - scroll, Math.Max(1, Width - 10), 54); foreach (Control child in row.Controls) { if (child is SoftButton) { child.Left = row.Width - 44; child.Width = 34; } else child.Width = Math.Max(30, row.Width - 76); } }
        }
        protected override void OnMouseWheel(MouseEventArgs e) { ScrollBy(e.Delta); }
        protected override void OnResize(EventArgs e) { base.OnResize(e); PositionRows(); Invalidate(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            if (rows.Count == 0) TextRenderer.DrawText(e.Graphics, EmptyMessage, Font, ClientRectangle, Design.Muted, TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter | TextFormatFlags.WordBreak);
            int total = rows.Count * 60;
            if (total > Height && Height > 0)
            {
                int size = Math.Max(20, Height * Height / total), top = scroll * (Height - size) / Math.Max(1, total - Height);
                using (var path = Design.Round(new Rectangle(Width - 5, top, 4, size), 2)) using (var brush = new SolidBrush(Color.FromArgb(100, Design.Muted))) e.Graphics.FillPath(brush, path);
            }
        }
    }
}
