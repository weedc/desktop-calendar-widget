using System;
using System.Drawing;
using System.Windows.Forms;

namespace TeamCalendar
{
    [Flags] enum ResizeEdge { Left = 1, Top = 2, Right = 4, Bottom = 8 }
    static class CalendarResize
    {
        public static Rectangle Calculate(Rectangle original, ResizeEdge edge, Point delta, Size minimum)
        {
            int left = original.Left, top = original.Top, right = original.Right, bottom = original.Bottom;
            if ((edge & ResizeEdge.Left) != 0) left = Math.Min(right - minimum.Width, left + delta.X);
            if ((edge & ResizeEdge.Right) != 0) right = Math.Max(left + minimum.Width, right + delta.X);
            if ((edge & ResizeEdge.Top) != 0) top = Math.Min(bottom - minimum.Height, top + delta.Y);
            if ((edge & ResizeEdge.Bottom) != 0) bottom = Math.Max(top + minimum.Height, bottom + delta.Y);
            return Rectangle.FromLTRB(left, top, right, bottom);
        }
        public static void Attach(Form owner, Action save)
        {
            var edges = new[] { ResizeEdge.Left, ResizeEdge.Top, ResizeEdge.Right, ResizeEdge.Bottom,
                ResizeEdge.Left | ResizeEdge.Top, ResizeEdge.Right | ResizeEdge.Top, ResizeEdge.Left | ResizeEdge.Bottom, ResizeEdge.Right | ResizeEdge.Bottom };
            var handles = new Handle[edges.Length];
            for (int i = 0; i < edges.Length; i++) { handles[i] = new Handle(owner, edges[i], save); owner.Controls.Add(handles[i]); }
            Action layout = delegate
            {
                int w = owner.ClientSize.Width, h = owner.ClientSize.Height, corner = 18, border = 7;
                handles[0].Bounds = new Rectangle(0, corner, border, Math.Max(0, h - 2 * corner));
                handles[1].Bounds = new Rectangle(corner, 0, Math.Max(0, w - 2 * corner), border);
                handles[2].Bounds = new Rectangle(w - border, corner, border, Math.Max(0, h - 2 * corner));
                handles[3].Bounds = new Rectangle(corner, h - border, Math.Max(0, w - 2 * corner), border);
                handles[4].Bounds = new Rectangle(0, 0, corner, corner);
                handles[5].Bounds = new Rectangle(w - corner, 0, corner, corner);
                handles[6].Bounds = new Rectangle(0, h - corner, corner, corner);
                handles[7].Bounds = new Rectangle(w - corner, h - corner, corner, corner);
                foreach (var handle in handles) handle.BringToFront();
            };
            owner.ClientSizeChanged += delegate { layout(); }; layout();
        }
        sealed class Handle : Control
        {
            readonly Form owner; readonly ResizeEdge edge; readonly Action save;
            Point start; Rectangle original; bool active;
            public Handle(Form owner, ResizeEdge edge, Action save)
            {
                this.owner = owner; this.edge = edge; this.save = save;
                SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.UserPaint, true);
                BackColor = Color.Transparent; TabStop = false; AccessibleName = "크기 조절 " + edge;
                Cursor = edge == ResizeEdge.Left || edge == ResizeEdge.Right ? Cursors.SizeWE :
                    edge == ResizeEdge.Top || edge == ResizeEdge.Bottom ? Cursors.SizeNS :
                    edge == (ResizeEdge.Left | ResizeEdge.Top) || edge == (ResizeEdge.Right | ResizeEdge.Bottom) ? Cursors.SizeNWSE : Cursors.SizeNESW;
            }
            protected override void OnMouseDown(MouseEventArgs e)
            {
                base.OnMouseDown(e); if (e.Button != MouseButtons.Left) return;
                original = owner.Bounds; start = PointToScreen(e.Location); active = true; Capture = true;
            }
            protected override void OnMouseMove(MouseEventArgs e)
            {
                base.OnMouseMove(e); if (!active) return;
                Point at = PointToScreen(e.Location);
                owner.Bounds = Calculate(original, edge, new Point(at.X - start.X, at.Y - start.Y), owner.MinimumSize);
            }
            protected override void OnMouseUp(MouseEventArgs e)
            { base.OnMouseUp(e); if (e.Button == MouseButtons.Left && active) { active = false; Capture = false; save(); } }
            protected override void OnMouseCaptureChanged(EventArgs e)
            { base.OnMouseCaptureChanged(e); if (!Capture && active) { active = false; save(); } }
        }
    }
}
