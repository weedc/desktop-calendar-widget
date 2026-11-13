using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Drawing;
using System.Windows.Forms;

namespace TeamCalendar
{
    [Flags] enum ResizeEdge { Left = 1, Top = 2, Right = 4, Bottom = 8 }
    sealed class ResizeFrame : IDisposable
    {
        [DllImport("gdi32.dll")] static extern IntPtr CreateRoundRectRgn(int left, int top, int right, int bottom, int width, int height);
        [DllImport("gdi32.dll")] static extern IntPtr CreateRectRgn(int left, int top, int right, int bottom);
        [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr region);
        [DllImport("gdi32.dll")] static extern bool PtInRegion(IntPtr region, int x, int y);
        [DllImport("user32.dll")] static extern int SetWindowRgn(IntPtr window, IntPtr region, bool redraw);
        [DllImport("user32.dll")] static extern int GetWindowRgn(IntPtr window, IntPtr region);
        readonly Form owner; readonly int radius; bool disposed, applying; Size appliedSize; IntPtr appliedWindow;
        public int AppliedCount { get; private set; }
        public ResizeFrame(Form owner, int radius)
        {
            this.owner = owner; this.radius = radius;
            owner.HandleCreated += delegate { Apply(); }; owner.HandleDestroyed += delegate { appliedWindow = IntPtr.Zero; }; owner.SizeChanged += delegate { Apply(); }; owner.Disposed += delegate { Dispose(); };
            if (owner.IsHandleCreated) Apply();
        }
        void Apply()
        {
            if (disposed || applying || owner.IsDisposed || !owner.IsHandleCreated || owner.Width < 1 || owner.Height < 1) return;
            if (owner.Handle == appliedWindow && owner.Size == appliedSize) return;
            var region = CreateRoundRectRgn(0, 0, owner.Width + 1, owner.Height + 1, radius * 2, radius * 2);
            if (region == IntPtr.Zero) throw new InvalidOperationException("크기 조절 테두리를 만들지 못했습니다.");
            applying = true;
            try
            {
                // Windows owns the successful region. No synchronous erase/repaint:
                // the already-buffered resize frame paints the content once.
                if (SetWindowRgn(owner.Handle, region, false) == 0) { DeleteObject(region); throw new InvalidOperationException("크기 조절 테두리를 적용하지 못했습니다."); }
                appliedWindow = owner.Handle; appliedSize = owner.Size; AppliedCount++;
            }
            finally { applying = false; }
        }
        public bool Rounded
        {
            get
            {
                if (!owner.IsHandleCreated) return false;
                var region = CreateRectRgn(0, 0, 0, 0);
                try { return GetWindowRgn(owner.Handle, region) != 0 && !PtInRegion(region, 0, 0) && PtInRegion(region, owner.Width / 2, owner.Height / 2); }
                finally { if (region != IntPtr.Zero) DeleteObject(region); }
            }
        }
        public void SetResizing(bool active) { Apply(); }
        public void Dispose() { disposed = true; }
    }
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
        public static Input Attach(Form owner, Action save, Func<Point> pointer = null, Action<bool> resizeState = null)
        {
            var input = new Input(owner, save, pointer ?? delegate { return Cursor.Position; }, resizeState);
            Application.AddMessageFilter(input); owner.Disposed += delegate { input.Dispose(); }; return input;
        }
        internal static ResizeEdge HitTest(Size size, Point at)
        {
            if (at.X < 0 || at.Y < 0 || at.X >= size.Width || at.Y >= size.Height) return 0;
            const int corner = 18, border = 7;
            bool top = at.Y < corner, bottom = at.Y >= size.Height - corner, left = at.X < corner, right = at.X >= size.Width - corner;
            if ((top || bottom) && (left || right)) return (left ? ResizeEdge.Left : ResizeEdge.Right) | (top ? ResizeEdge.Top : ResizeEdge.Bottom);
            if (at.X < border) return ResizeEdge.Left; if (at.X >= size.Width - border) return ResizeEdge.Right;
            if (at.Y < border) return ResizeEdge.Top; if (at.Y >= size.Height - border) return ResizeEdge.Bottom; return 0;
        }
        internal sealed class Input : IMessageFilter, IDisposable
        {
            readonly Form owner; readonly Action save; readonly Func<Point> pointer; readonly Action<bool> resizeState;
            readonly Timer render = new Timer { Interval = 8 }; readonly Stopwatch frame = Stopwatch.StartNew();
            Rectangle original; Point start, latest; ResizeEdge edge; bool active, disposed;
            readonly Dictionary<Control, CursorWindow> cursors = new Dictionary<Control, CursorWindow>();
            public int Commits { get; private set; }
            public Input(Form owner, Action save, Func<Point> pointer, Action<bool> resizeState)
            {
                this.owner = owner; this.save = save; this.pointer = pointer; this.resizeState = resizeState;
                render.Tick += delegate { if (active) { latest = pointer(); CommitPending(); } };
                owner.MouseCaptureChanged += delegate { if (active && !owner.Capture) End(false); };
                Watch(owner);
            }
            public bool PreFilterMessage(ref Message message)
            {
                if (disposed || owner.IsDisposed || !owner.Visible) return false;
                var target = Control.FromHandle(message.HWnd);
                if (target == null || target != owner && !owner.Contains(target)) return false;
                if (message.Msg == 0x0200)
                {
                    if (active) { Move(true); return true; }
                    return false;
                }
                if (message.Msg == 0x0201 || message.Msg == 0x0203)
                {
                    var hit = HitTest(owner.ClientSize, owner.PointToClient(pointer()));
                    if (hit != 0) { Begin(hit); return true; }
                }
                if (message.Msg == 0x0202 && active) { End(true); return true; }
                return false;
            }
            void Watch(Control control)
            {
                if (disposed || cursors.ContainsKey(control)) return;
                cursors.Add(control, new CursorWindow(control, this)); foreach (Control child in control.Controls) Watch(child);
            }
            internal bool ApplyCursor()
            {
                if (disposed || owner.IsDisposed) return false;
                var hit = active ? edge : HitTest(owner.ClientSize, owner.PointToClient(pointer())); if (hit == 0) return false;
                Cursor.Current = hit == ResizeEdge.Left || hit == ResizeEdge.Right ? Cursors.SizeWE : hit == ResizeEdge.Top || hit == ResizeEdge.Bottom ? Cursors.SizeNS : hit == (ResizeEdge.Left | ResizeEdge.Top) || hit == (ResizeEdge.Right | ResizeEdge.Bottom) ? Cursors.SizeNWSE : Cursors.SizeNESW;
                return true;
            }
            sealed class CursorWindow : NativeWindow, IDisposable
            {
                readonly Control control; readonly Input input; readonly ControlEventHandler added;
                public CursorWindow(Control control, Input input)
                {
                    this.control = control; this.input = input; added = delegate(object sender, ControlEventArgs e) { input.Watch(e.Control); };
                    control.ControlAdded += added; control.HandleCreated += Created; control.HandleDestroyed += Destroyed;
                    if (control.IsHandleCreated) AssignHandle(control.Handle);
                }
                void Created(object sender, EventArgs e) { if (Handle != IntPtr.Zero) ReleaseHandle(); AssignHandle(control.Handle); }
                void Destroyed(object sender, EventArgs e) { if (Handle != IntPtr.Zero) ReleaseHandle(); }
                protected override void WndProc(ref Message message)
                {
                    if (message.Msg == 0x0020 && input.ApplyCursor()) { message.Result = new IntPtr(1); return; }
                    base.WndProc(ref message);
                }
                public void Dispose() { control.ControlAdded -= added; control.HandleCreated -= Created; control.HandleDestroyed -= Destroyed; if (Handle != IntPtr.Zero) ReleaseHandle(); }
            }
            internal void Begin(ResizeEdge value) { edge = value; original = owner.Bounds; start = latest = pointer(); active = true; owner.Capture = true; if (resizeState != null) resizeState(true); frame.Restart(); render.Start(); }
            internal void Move(bool responsive = false) { if (!active) return; latest = pointer(); if (responsive && frame.ElapsedMilliseconds >= 8) CommitPending(); }
            internal void CommitPending()
            {
                if (!active) return;
                var bounds = Calculate(original, edge, new Point(latest.X - start.X, latest.Y - start.Y), owner.MinimumSize);
                if (bounds != owner.Bounds) { owner.Bounds = bounds; owner.Update(); Commits++; }
                frame.Restart();
            }
            internal void End(bool readPointer = true)
            {
                if (!active) return; if (readPointer) latest = pointer(); CommitPending(); active = false; render.Stop(); owner.Capture = false;
                if (resizeState != null) resizeState(false); save();
            }
            public void Dispose() { if (disposed) return; disposed = true; active = false; render.Dispose(); foreach (var hook in cursors.Values) hook.Dispose(); cursors.Clear(); Application.RemoveMessageFilter(this); }
        }
    }
}
