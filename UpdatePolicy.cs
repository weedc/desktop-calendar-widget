using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace TeamCalendar
{
    public sealed class ReleaseAsset { public string name, browser_download_url; }
    public sealed class PublicRelease { public bool draft, prerelease; public ReleaseAsset[] assets; }
    public sealed class WidgetUpdate { public string version, url; }
    static class UpdatePolicy
    {
        public const string Feed = "https://api.github.com/repos/weedc/desktop-calendar-widget/releases?per_page=100";
        public const int IntervalHours = 6;
        public static Version ParseVersion(string text)
        {
            if (text == null || !Regex.IsMatch(text, @"^\d{1,5}\.\d{1,5}\.\d{1,5}(?:\.\d{1,5})?$")) return null;
            Version result; return Version.TryParse(text, out result) ? new Version(result.Major, result.Minor, result.Build, Math.Max(0, result.Revision)) : null;
        }
        public static bool ValidUrl(string value, string name)
        {
            Uri uri; return Uri.TryCreate(value, UriKind.Absolute, out uri) && uri.Scheme == "https" && uri.Host == "github.com" && uri.IsDefaultPort && uri.UserInfo == "" && uri.Query == "" && uri.Fragment == "" &&
                uri.AbsolutePath.StartsWith("/weedc/desktop-calendar-widget/releases/download/", StringComparison.Ordinal) &&
                Uri.UnescapeDataString(uri.Segments.Last()) == name;
        }
        public static WidgetUpdate Select(string json, string current, string platform)
        {
            Version installed = ParseVersion(current); if (installed == null) throw new ArgumentException("현재 버전을 확인하지 못했습니다.");
            var releases = new JavaScriptSerializer { MaxJsonLength = 2 * 1024 * 1024 }.Deserialize<PublicRelease[]>(json);
            WidgetUpdate best = null; Version newest = installed;
            foreach (var release in releases ?? new PublicRelease[0])
            {
                if (release == null || release.draft || release.prerelease) continue;
                foreach (var asset in release.assets ?? new ReleaseAsset[0])
                {
                    if (asset == null || asset.name == null) continue;
                    var match = Regex.Match(asset.name, @"^DesktopCalendarWidget-v(\d+\.\d+\.\d+(?:\.\d+)?)-" + Regex.Escape(platform) + @"\.zip$", RegexOptions.CultureInvariant);
                    if (!match.Success || !ValidUrl(asset.browser_download_url, asset.name)) continue;
                    var candidate = ParseVersion(match.Groups[1].Value);
                    if (candidate != null && candidate > newest) { newest = candidate; best = new WidgetUpdate { version = match.Groups[1].Value, url = asset.browser_download_url }; }
                }
            }
            return best;
        }
        public static bool Due(bool enabled, DateTime attempted, DateTime utcNow) { return enabled && (attempted == DateTime.MinValue || utcNow - attempted >= TimeSpan.FromHours(IntervalHours)); }
        public static string DownloadFeed()
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            var request = (HttpWebRequest)WebRequest.Create(Feed);
            request.UserAgent = "DesktopCalendarWidget/" + typeof(UpdatePolicy).Assembly.GetName().Version.ToString(3);
            request.Accept = "application/vnd.github+json"; request.Timeout = 12000; request.ReadWriteTimeout = 12000;
            request.AllowAutoRedirect = false;
            using (var response = (HttpWebResponse)request.GetResponse())
            using (var stream = response.GetResponseStream())
            using (var output = new MemoryStream())
            {
                var buffer = new byte[8192]; int count;
                while ((count = stream.Read(buffer, 0, buffer.Length)) > 0) { if (output.Length + count > 2 * 1024 * 1024) throw new InvalidDataException("업데이트 응답이 너무 큽니다."); output.Write(buffer, 0, count); }
                return Encoding.UTF8.GetString(output.ToArray());
            }
        }
    }
}
