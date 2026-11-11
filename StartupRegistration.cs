using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Microsoft.Win32;

namespace TeamCalendar
{
    static class StartupRegistration
    {
        const string RunPath = "Software\\Microsoft\\Windows\\CurrentVersion\\Run", Name = "DesktopCalendarWidget";
        public static bool Owned(string value, string directory)
        {
            try
            {
                var match = Regex.Match(value ?? "", "^\\s*(?:\"([^\"]+\\.exe)\"|(.+?\\.exe))(?:\\s.*)?$", RegexOptions.IgnoreCase);
                if (!match.Success) return false;
                string file = match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
                return string.Equals(Path.GetFullPath(Path.GetDirectoryName(file)), Path.GetFullPath(directory), StringComparison.OrdinalIgnoreCase) && Regex.IsMatch(Path.GetFileName(file), @"^GoogleCalendarWidget(?:-v[0-9.]+)?\.exe$", RegexOptions.IgnoreCase);
            }
            catch { return false; }
        }
        public static bool Enabled()
        {
            using (var key = Registry.CurrentUser.OpenSubKey(RunPath)) return key != null && key.GetValueNames().Any(n => Owned(Convert.ToString(key.GetValue(n)), Path.GetDirectoryName(Application.ExecutablePath)));
        }
        public static void Set(bool enabled)
        {
            using (var key = Registry.CurrentUser.CreateSubKey(RunPath))
            {
                string directory = Path.GetDirectoryName(Application.ExecutablePath);
                foreach (string name in key.GetValueNames().Where(n => Owned(Convert.ToString(key.GetValue(n)), directory)).ToArray()) key.DeleteValue(name, false);
                if (enabled) key.SetValue(Name, "\"" + Application.ExecutablePath + "\"");
            }
        }
    }
}
