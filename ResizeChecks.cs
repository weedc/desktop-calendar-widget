using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;

namespace TeamCalendar
{
    static class ResizeChecks
    {
        static void Mouse(Control handle, string method, MouseButtons buttons, Point point)
        { handle.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(handle, new object[] { new MouseEventArgs(buttons, 1, point.X, point.Y, 0) }); }
        public static object Run()
        {
            var checks = new List<string>();
            bool desktopAvailable = false;
            var edges = new[] { ResizeEdge.Left, ResizeEdge.Top, ResizeEdge.Right, ResizeEdge.Bottom,
                ResizeEdge.Left | ResizeEdge.Top, ResizeEdge.Right | ResizeEdge.Top, ResizeEdge.Left | ResizeEdge.Bottom, ResizeEdge.Right | ResizeEdge.Bottom };
            foreach (int mode in new[] { 0, 1, 2 })
            using (var host = new Form { StartPosition = FormStartPosition.Manual, Location = new Point(50, 50), Size = new Size(900, 650), ShowInTaskbar = false })
            using (var form = new Form { FormBorderStyle = FormBorderStyle.None, StartPosition = FormStartPosition.Manual, MinimumSize = new Size(200, 140), ShowInTaskbar = false })
            {
                int saved = 0; form.Controls.Add(new Panel { Dock = DockStyle.Top, Height = 38 });
                CalendarResize.Attach(form, delegate { saved++; });
                bool child = mode != 0;
                if (mode == 1) { host.Show(); form.TopLevel = false; host.Controls.Add(form); }
                if (mode == 2)
                {
                    IntPtr desktop = Win.DesktopHost(); if (desktop == IntPtr.Zero) continue;
                    desktopAvailable = true;
                    form.TopLevel = false; Win.SetParent(form.Handle, desktop);
                }
                form.Show(); Application.DoEvents();
                foreach (var edge in edges)
                {
                    form.Bounds = new Rectangle(160, 150, 480, 320); Application.DoEvents();
                    var handle = form.Controls.Cast<Control>().Single(c => c.AccessibleName == "크기 조절 " + edge);
                    var original = form.Bounds; var start = new Point(handle.Width / 2, handle.Height / 2);
                    Point screen = handle.PointToScreen(start), delta = new Point(31, 23);
                    Mouse(handle, "OnMouseDown", MouseButtons.Left, start);
                    Mouse(handle, "OnMouseMove", MouseButtons.Left, handle.PointToClient(new Point(screen.X + delta.X, screen.Y + delta.Y)));
                    Mouse(handle, "OnMouseUp", MouseButtons.Left, Point.Empty);
                    if (form.Bounds != CalendarResize.Calculate(original, edge, delta, form.MinimumSize)) throw new Exception("Resize failed: " + edge + " child=" + child);
                    checks.Add((mode == 2 ? "actual desktop host" : child ? "parented" : "normal") + " drag " + edge);
                }
                if (saved != 8) throw new Exception("Each drag must save exactly once.");
                var minimum = CalendarResize.Calculate(new Rectangle(160, 150, 480, 320), ResizeEdge.Left | ResizeEdge.Top, new Point(9999, 9999), form.MinimumSize);
                if (minimum.Size != form.MinimumSize || minimum.Right != 640 || minimum.Bottom != 470) throw new Exception("Minimum-size anchor changed.");
                checks.Add("minimum size preserves opposite corner, mode=" + mode);
                form.Close(); if (mode == 2) Win.RepaintDesktop();
            }
            return new { passed = checks.Count, desktopAvailable = desktopAvailable, checks = checks };
        }
    }
}
