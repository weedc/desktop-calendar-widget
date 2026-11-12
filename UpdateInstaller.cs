using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace TeamCalendar
{
    // Prepare while the old app is usable. A helper switches launchers after it exits.
    // Older MCP processes can keep using their own versioned executable.
    static class UpdateInstaller
    {
        const long DownloadLimit = 64L * 1024 * 1024;
        public static string FileName(string version)
        {
            if (UpdatePolicy.ParseVersion(version) == null) throw new InvalidDataException("업데이트 버전이 올바르지 않습니다.");
            return "GoogleCalendarWidget-v" + version + ".exe";
        }
        public static void VerifyDigest(string path, string digest)
        {
            if (string.IsNullOrEmpty(digest)) return; // Older releases predate asset digests.
            if (!Regex.IsMatch(digest, "^sha256:[a-fA-F0-9]{64}$")) throw new InvalidDataException("업데이트 검증 정보를 읽지 못했습니다.");
            using (var hash = SHA256.Create()) using (var stream = File.OpenRead(path))
                if (!string.Equals("sha256:" + BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", ""), digest, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("다운로드 검증에 실패했습니다. 다시 시도해 주세요.");
        }
        static bool AllowedRedirect(Uri uri)
        {
            return uri.Scheme == "https" && uri.IsDefaultPort && uri.UserInfo == "" &&
                (uri.Host == "github.com" || uri.Host == "release-assets.githubusercontent.com" || uri.Host == "objects.githubusercontent.com");
        }
        static void Download(WidgetUpdate update, string output)
        {
            string name = "DesktopCalendarWidget-v" + update.version + "-win-x64.zip";
            if (!UpdatePolicy.ValidUrl(update.url, name)) throw new InvalidDataException("업데이트 주소가 올바르지 않습니다.");
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            var uri = new Uri(update.url);
            for (int hop = 0; hop < 6; hop++)
            {
                if (!AllowedRedirect(uri)) throw new InvalidDataException("업데이트 다운로드 주소를 확인하지 못했습니다.");
                var request = (HttpWebRequest)WebRequest.Create(uri);
                request.UserAgent = "DesktopCalendarWidget/" + typeof(UpdateInstaller).Assembly.GetName().Version.ToString(3);
                request.Timeout = 20000; request.ReadWriteTimeout = 30000; request.AllowAutoRedirect = false;
                using (var response = (HttpWebResponse)request.GetResponse())
                {
                    if ((int)response.StatusCode >= 300 && (int)response.StatusCode < 400)
                    { uri = new Uri(uri, response.Headers["Location"]); continue; }
                    if (response.StatusCode != HttpStatusCode.OK || response.ContentLength > DownloadLimit) throw new InvalidDataException("업데이트 파일을 받지 못했습니다.");
                    using (var input = response.GetResponseStream()) using (var file = File.Create(output))
                    {
                        var bytes = new byte[65536]; int count;
                        while ((count = input.Read(bytes, 0, bytes.Length)) > 0)
                        { if (file.Length + count > DownloadLimit) throw new InvalidDataException("업데이트 파일이 너무 큽니다."); file.Write(bytes, 0, count); }
                    }
                    VerifyDigest(output, update.digest); return;
                }
            }
            throw new InvalidDataException("업데이트 다운로드가 너무 많이 이동했습니다.");
        }
        public static void ExtractExecutable(string archive, string output, string version)
        {
            using (var zip = ZipFile.OpenRead(archive))
            {
                ZipArchiveEntry executable = null;
                foreach (var entry in zip.Entries)
                {
                    string path = entry.FullName.Replace('\\', '/');
                    if (path.StartsWith("/") || path.Contains(":") || Array.Exists(path.Split('/'), p => p == "..")) throw new InvalidDataException("업데이트 압축 파일의 경로가 올바르지 않습니다.");
                    if (Path.GetFileName(path) == FileName(version))
                    { if (executable != null || entry.Length > DownloadLimit) throw new InvalidDataException("업데이트 실행 파일이 올바르지 않습니다."); executable = entry; }
                }
                if (executable == null) throw new InvalidDataException("업데이트 실행 파일이 없습니다.");
                using (var input = executable.Open()) using (var file = File.Create(output)) input.CopyTo(file);
            }
            // Inspect bytes without a file-backed assembly mapping: preparation and
            // retry cleanup must not leave the candidate executable locked.
            if (Assembly.ReflectionOnlyLoad(File.ReadAllBytes(output)).GetName().Version != UpdatePolicy.ParseVersion(version)) throw new InvalidDataException("업데이트 실행 파일의 버전이 일치하지 않습니다.");
        }
        public static string Prepare(WidgetUpdate update)
        {
            if (update == null || UpdatePolicy.ParseVersion(update.version) == null || UpdatePolicy.ParseVersion(update.version) <= Assembly.GetExecutingAssembly().GetName().Version)
                throw new InvalidDataException("새 버전만 설치할 수 있습니다.");
            string directory = Path.Combine(App.DataDir, "updates", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
            string target = null; bool copied = false;
            try
            {
                string zip = Path.Combine(directory, "release.zip"), staged = Path.Combine(directory, FileName(update.version));
                Download(update, zip); ExtractExecutable(zip, staged, update.version);
                string installed = Application.ExecutablePath; target = Path.Combine(Path.GetDirectoryName(installed), FileName(update.version));
                if (File.Exists(target))
                {
                    using (var hash = SHA256.Create())
                    using (var existing = File.OpenRead(target)) using (var candidate = File.OpenRead(staged))
                        if (!string.Equals(Convert.ToBase64String(hash.ComputeHash(existing)), Convert.ToBase64String(hash.ComputeHash(candidate)), StringComparison.Ordinal))
                            throw new IOException("같은 이름의 다른 실행 파일이 있습니다. 설치 폴더를 확인해 주세요.");
                }
                else { File.Copy(staged, target, false); copied = true; }
                string helper = Path.Combine(directory, "UpdateInstall.ps1"), plan = Path.Combine(directory, "plan.json");
                using (var input = new StreamReader(Assembly.GetExecutingAssembly().GetManifestResourceStream("TeamCalendar.UpdateInstall.ps1"), System.Text.Encoding.UTF8))
                    File.WriteAllText(helper, input.ReadToEnd(), new System.Text.UTF8Encoding(true));
                File.WriteAllText(plan, App.Json.Serialize(new { parent = Process.GetCurrentProcess().Id, installed = installed, target = target, version = update.version }));
                return plan;
            }
            catch { if (copied) try { File.Delete(target); } catch { } try { Directory.Delete(directory, true); } catch { } throw; }
        }
        public static void Handoff(string plan)
        {
            string helper = Path.Combine(Path.GetDirectoryName(plan), "UpdateInstall.ps1");
            string powershell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
            Process.Start(new ProcessStartInfo(powershell, "-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"" + helper + "\" -PlanPath \"" + plan + "\"")
            { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Path.GetDirectoryName(helper) });
        }
    }
}
