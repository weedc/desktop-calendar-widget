using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace TeamCalendar
{
    static class ResizeChecks
    {
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hwnd, int message, IntPtr wparam, IntPtr lparam);
        static Point At(Size size, ResizeEdge edge)
        {
            int x = (edge & ResizeEdge.Left) != 0 ? 9 : (edge & ResizeEdge.Right) != 0 ? size.Width - 9 : size.Width / 2;
            int y = (edge & ResizeEdge.Top) != 0 ? 9 : (edge & ResizeEdge.Bottom) != 0 ? size.Height - 9 : size.Height / 2;
            if (edge == ResizeEdge.Left) x = 3; if (edge == ResizeEdge.Right) x = size.Width - 3;
            if (edge == ResizeEdge.Top) y = 3; if (edge == ResizeEdge.Bottom) y = size.Height - 3; return new Point(x, y);
        }
        static bool Send(CalendarResize.Input input, Control target, int message)
        { var value = Message.Create(target.Handle, message, new IntPtr(1), new IntPtr(0x7fffffff)); return input.PreFilterMessage(ref value); }
        public static object Run()
        {
            var checks = new List<string>(); bool desktopAvailable = false;
            var edges = new[] { ResizeEdge.Left, ResizeEdge.Top, ResizeEdge.Right, ResizeEdge.Bottom, ResizeEdge.Left | ResizeEdge.Top, ResizeEdge.Right | ResizeEdge.Top, ResizeEdge.Left | ResizeEdge.Bottom, ResizeEdge.Right | ResizeEdge.Bottom };
            var fixture = new Rectangle(-14000, -14000, 480, 320);
            foreach (int mode in new[] { 0, 1, 2 })
            using (var host = new Form { StartPosition = FormStartPosition.Manual, Location = new Point(-16000, -16000), Size = new Size(900, 650), ShowInTaskbar = false })
            using (var form = new Form { FormBorderStyle = FormBorderStyle.None, StartPosition = FormStartPosition.Manual, MinimumSize = new Size(200, 140), ShowInTaskbar = false })
            {
                Point pointer = Point.Empty; int saved = 0; var header = new Panel { Dock = DockStyle.Top, Height = 38 }; form.Controls.Add(header);
                var frame = new ResizeFrame(form, Design.WindowRadius);
                var input = CalendarResize.Attach(form, delegate { saved++; }, delegate { return pointer; }, frame.SetResizing);
                if (mode == 1) { host.Show(); form.TopLevel = false; host.Controls.Add(form); }
                if (mode == 2) { IntPtr desktop = Win.DesktopHost(); if (desktop == IntPtr.Zero) continue; desktopAvailable = true; form.TopLevel = false; Win.SetParent(form.Handle, desktop); }
                form.Show(); Application.DoEvents();
                foreach (var edge in edges)
                {
                    form.Bounds = fixture; Application.DoEvents(); var point = At(form.ClientSize, edge); pointer = form.PointToScreen(point);
                    if (CalendarResize.HitTest(form.ClientSize, point) != edge) throw new Exception("Edge hit test failed: " + edge);
                    var anchor = pointer; var delta = new Point(31, 23); Control target = point.Y < 38 ? (Control)header : form;
                    if (!Send(input, target, edge == ResizeEdge.Right ? 0x0203 : 0x0201)) throw new Exception("Resize edge did not intercept native mouse down");
                    pointer = new Point(anchor.X + delta.X, anchor.Y + delta.Y); Send(input, form, 0x0200); input.CommitPending(); Send(input, form, 0x0202);
                    if (form.Bounds != CalendarResize.Calculate(fixture, edge, delta, form.MinimumSize)) throw new Exception("Resize failed: " + edge + " mode=" + mode);
                    checks.Add("native input edge " + edge + ", mode=" + mode);
                }
                if (saved != 8) throw new Exception("Each drag must save once");
                var minimum = CalendarResize.Calculate(fixture, ResizeEdge.Left | ResizeEdge.Top, new Point(9999, 9999), form.MinimumSize);
                if (minimum.Size != form.MinimumSize || minimum.Right != fixture.Right || minimum.Bottom != fixture.Bottom) throw new Exception("Minimum anchor changed");
                checks.Add("minimum size preserves opposite corner, mode=" + mode);
                form.Bounds = fixture; Application.DoEvents(); pointer = form.PointToScreen(At(form.ClientSize, ResizeEdge.Right | ResizeEdge.Bottom)); var start = pointer;
                int applied = frame.AppliedCount, transitions = 0; EventHandler changed = delegate { transitions++; }; form.VisibleChanged += changed;
                input.Begin(ResizeEdge.Right | ResizeEdge.Bottom);
                for (int i = 0; i < 120; i++)
                {
                    var delta = new Point(i % 2 == 0 ? 97 : -61, i % 3 == 0 ? -43 : 81); pointer = new Point(start.X + delta.X, start.Y + delta.Y);
                    input.Move(); input.CommitPending();
                    if (form.Bounds != CalendarResize.Calculate(fixture, ResizeEdge.Right | ResizeEdge.Bottom, delta, form.MinimumSize)) throw new Exception("Rapid direction reversed geometry, mode=" + mode);
                    Application.DoEvents(); if (!frame.Rounded) throw new Exception("Rounded corners disappeared during drag");
                }
                input.End(); Application.DoEvents();
                if (saved != 9 || transitions != 0 || !form.Visible || form.Controls.Count != 1) throw new Exception("Resize hid window or created painting overlays");
                checks.Add("120 direction changes preserve visible window without painting overlays, mode=" + mode);
                if (!frame.Rounded) throw new Exception("Rounded corners missing after release");
                checks.Add("rounded corners remain present during and after every resize frame, mode=" + mode);
                form.Bounds = fixture; pointer = form.PointToScreen(At(form.ClientSize, ResizeEdge.Right | ResizeEdge.Bottom)); start = pointer; int commits = input.Commits;
                input.Begin(ResizeEdge.Right | ResizeEdge.Bottom);
                for (int i = 0; i < 120; i++) { pointer = new Point(start.X + i + 1, start.Y + i % 30); input.Move(); }
                if (form.Bounds != fixture) throw new Exception("Input burst repainted before commit"); input.End();
                if (input.Commits != commits + 1 || form.Width != fixture.Width + 120 || transitions != 0) throw new Exception("Mouse burst not coalesced");
                checks.Add("120 queued moves coalesce into one final frame, mode=" + mode);
                form.Bounds = fixture; pointer = form.PointToScreen(At(form.ClientSize, ResizeEdge.Left));
                var originalCursor = Cursor.Current; var rootCursor = form.Cursor; var headerCursor = header.Cursor;
                try
                {
                    for (int i = 0; i < 40; i++)
                    {
                        var target = i % 2 == 0 ? (Control)form : header;
                        var result = SendMessage(target.Handle, 0x0020, target.Handle, new IntPtr((0x0200 << 16) | 1));
                        if (result != new IntPtr(1) || Cursor.Current.Handle != Cursors.SizeWE.Handle) throw new Exception("Resize cursor alternated across native children, mode=" + mode);
                    }
                    if (form.Cursor != rootCursor || header.Cursor != headerCursor) throw new Exception("Hover mutated normal control cursors");
                    pointer = form.PointToScreen(new Point(100, 100));
                    if (input.ApplyCursor()) throw new Exception("Resize cursor leaked outside edge");
                    checks.Add("native resize cursor stays stable across root and child windows without changing ordinary cursors, mode=" + mode);
                }
                finally { Cursor.Current = originalCursor; }
                form.VisibleChanged -= changed; form.Close(); if (mode == 2) Win.RepaintDesktop();
            }
            return new { passed = checks.Count, desktopAvailable = desktopAvailable, checks = checks };
        }
    }
}
