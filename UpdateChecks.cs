using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

namespace TeamCalendar
{
    static class UpdateChecks
    {
        static void Check(bool value, string name, List<string> checks) { if (!value) throw new Exception(name); checks.Add(name); }
        static PublicRelease Release(string version, string platform, bool preview = false)
        {
            string name = "DesktopCalendarWidget-v" + version + "-" + platform + ".zip";
            return new PublicRelease { prerelease = preview, assets = new[] { new ReleaseAsset { name = name, browser_download_url = "https://github.com/weedc/desktop-calendar-widget/releases/download/test/" + name } } };
        }
        public static object Run(bool live = false)
        {
            var checks = new List<string>();
            var releases = new[] { Release("5.9.0", "win-x64"), Release("5.10.0", "win-x64"), Release("99.0.0", "mac-arm64"), Release("99.0.0", "win-x64", true) };
            string json = App.Json.Serialize(releases);
            Check(UpdatePolicy.Select(json, "5.8.0", "win-x64").version == "5.10.0", "numeric versions and Windows asset selection", checks);
            Check(UpdatePolicy.Select(json, "5.10.0", "win-x64") == null, "same versions and preview releases do not alert", checks);
            Check(UpdatePolicy.Select(json, "5.10.1", "win-x64") == null, "installed newer version does not downgrade", checks);
            Check(UpdatePolicy.Select(json, "5.0.0", "mac-arm64").version == "99.0.0" && UpdatePolicy.Select(json, "5.0.0", "mac-x86_64") == null, "Mac CPU assets cannot cross architectures", checks);
            releases[0].draft = true; releases[1].assets[0].browser_download_url = "https://example.invalid/" + releases[1].assets[0].name;
            Check(UpdatePolicy.Select(App.Json.Serialize(releases), "5.0.0", "win-x64") == null, "draft releases and foreign download URLs rejected", checks);
            Check(UpdatePolicy.ParseVersion("5.6") == null && UpdatePolicy.ParseVersion("5.6.0-beta") == null && UpdatePolicy.ParseVersion("5.6.0.1") != null, "version format validation", checks);
            Check(!UpdatePolicy.ValidUrl("https://github.com.evil.invalid/weedc/desktop-calendar-widget/releases/download/test/a.zip", "a.zip") && !UpdatePolicy.ValidUrl("https://github.com/weedc/other/releases/download/test/a.zip", "a.zip"), "only this repository's HTTPS download URLs accepted", checks);
            var utc = new DateTime(2026, 11, 12, 0, 0, 0, DateTimeKind.Utc);
            Check(UpdatePolicy.Due(true, DateTime.MinValue, utc) && !UpdatePolicy.Due(false, DateTime.MinValue, utc), "startup check respects the update toggle", checks);
            Check(!UpdatePolicy.Due(true, utc, utc.AddHours(6).AddSeconds(-1)) && UpdatePolicy.Due(true, utc, utc.AddHours(6)), "six-hour cadence includes failed attempts", checks);
            var original = new FeatureState { updateNotifications = true, autoSyncEnabled = false };
            var pending = new CalendarSettingsDraft(original, new Settings(), false, false, new FeatureState.Profile[0]);
            using (var dialog = new CalendarAdvancedSettings(pending, delegate { }))
            {
                var box = dialog.Controls.OfType<CheckBox>().Single(c => c.Text == "새 버전 업데이트 알림"); box.Checked = false;
                Check(original.updateNotifications && pending.Work.updateNotifications, "update checkbox stays pending inside advanced settings", checks);
                Check(dialog.ReturnToSettings() && !pending.Work.updateNotifications && original.updateNotifications, "returning from advanced stages only the update preference", checks);
            }
            pending.Commit(original);
            Check(!original.updateNotifications && !original.autoSyncEnabled, "final Apply commits update preference independently of calendar synchronization", checks);
            var history = App.Json.Deserialize<UpdateHistory>(App.Json.Serialize(new UpdateHistory { notifiedVersion = "5.10.0" }));
            Check(history.notifiedVersion == "5.10.0", "notification identity persists without calendar content", checks);
            if (live) { UpdatePolicy.Select(UpdatePolicy.DownloadFeed(), "5.6.0", "win-x64"); Check(true, "real public GitHub release feed fetched by the native Windows client", checks); }
            return new { passed = checks.Count, checks = checks };
        }
    }
}
