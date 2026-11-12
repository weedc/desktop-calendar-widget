using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace TeamCalendar
{
    sealed class UpdateHistory { public string notifiedVersion = ""; }
    static class UpdateRuntime
    {
        public static Action<IWin32Window> Check;
        public static void CheckNow(IWin32Window owner) { if (Check != null) Check(owner); }
    }
    sealed class UpdateCheckDialog : ThemeWindow
    {
        readonly Label status;
        readonly Button download;
        string url;
        public UpdateCheckDialog(string current) : base("업데이트 확인", new Size(460, 255))
        {
            StartPosition = FormStartPosition.CenterParent;
            Caption("현재 버전  " + current, 22, 72, 416);
            status = Caption("새 버전을 확인하고 있습니다…", 22, 107, 416); status.Height = 62;
            download = Action("다운로드", 191, 195, 118, delegate { if (url != null) App.Open(url); }); download.Enabled = false;
            Action("닫기", 322, 195, 116, delegate { Close(); });
        }
        public void Finish(WidgetUpdate result, bool failed)
        {
            if (IsDisposed) return;
            status.Text = failed ? "업데이트를 확인하지 못했습니다. 잠시 후 다시 시도해 주세요." : result == null ? "최신 버전을 사용하고 있습니다." : "새 버전 " + result.version + "을 사용할 수 있습니다.";
            url = result == null ? null : result.url; download.Enabled = url != null;
        }
    }
    sealed partial class CalendarWindow
    {
        readonly string updateVersion = Assembly.GetExecutingAssembly().GetName().Version.ToString(3);
        readonly string updateHistoryPath = Path.Combine(App.DataDir, "update-state.json");
        UpdateHistory updateHistory;
        UpdateCheckDialog updateDialog;
        WidgetUpdate pendingUpdate;
        DateTime updateAttempt = DateTime.MinValue;
        bool checkingUpdates, lastUpdateEnabled;
        int updateGeneration;
        string systemNoticeUpdateUrl = "";

        void InitializeUpdates()
        {
            try { updateHistory = App.Json.Deserialize<UpdateHistory>(File.ReadAllText(updateHistoryPath)); } catch { }
            if (updateHistory == null) updateHistory = new UpdateHistory();
            lastUpdateEnabled = features.updateNotifications;
            UpdateRuntime.Check = ShowUpdateCheck;
            FormClosed += delegate { updateGeneration++; UpdateRuntime.Check = null; if (updateDialog != null && !updateDialog.IsDisposed) updateDialog.Close(); };
        }
        void SaveUpdateHistory()
        {
            try { string temp = updateHistoryPath + ".tmp"; File.WriteAllText(temp, App.Json.Serialize(updateHistory)); if (File.Exists(updateHistoryPath)) File.Replace(temp, updateHistoryPath, null); else File.Move(temp, updateHistoryPath); } catch { }
        }
        void ShowUpdateCheck(IWin32Window owner)
        {
            if (updateDialog != null && !updateDialog.IsDisposed) { updateDialog.Activate(); return; }
            updateDialog = new UpdateCheckDialog(updateVersion); updateDialog.Show(owner);
            StartUpdateCheck(updateDialog);
        }
        void StartUpdateCheck(UpdateCheckDialog manual)
        {
            // A manual check joins an in-flight check, even while notifications are off.
            if (checkingUpdates) return;
            checkingUpdates = true; updateAttempt = DateTime.UtcNow; int generation = updateGeneration;
            Task.Run(delegate
            {
                WidgetUpdate result = null; bool failed = false;
                try { result = UpdatePolicy.Select(UpdatePolicy.DownloadFeed(), updateVersion, "win-x64"); } catch { failed = true; }
                if (IsDisposed || !IsHandleCreated) return;
                try { BeginInvoke((Action)delegate
                {
                    checkingUpdates = false;
                    if (updateDialog != null && !updateDialog.IsDisposed) updateDialog.Finish(result, failed);
                    if (generation == updateGeneration && !failed && features.updateNotifications && result != null && result.version != updateHistory.notifiedVersion) pendingUpdate = result;
                }); } catch (InvalidOperationException) { }
            });
        }
        void TickUpdates()
        {
            if (lastUpdateEnabled != features.updateNotifications)
            {
                lastUpdateEnabled = features.updateNotifications; updateGeneration++; pendingUpdate = null;
                if (lastUpdateEnabled) updateAttempt = DateTime.MinValue;
            }
            if (!checkingUpdates && UpdatePolicy.Due(features.updateNotifications, updateAttempt, DateTime.UtcNow)) StartUpdateCheck(null);
            if (pendingUpdate != null && features.updateNotifications && DailySummaryPolicy.CanNotify(lastSystemNotice, DateTimeOffset.Now))
            {
                var result = pendingUpdate; pendingUpdate = null;
                ShowSystemNotice("캘린더 업데이트 · " + result.version, "새 버전이 있습니다. 알림을 클릭해 다운로드하세요.", result.url);
                updateHistory.notifiedVersion = result.version; SaveUpdateHistory();
            }
        }
        bool OpenUpdateNotice()
        {
            if (systemNoticeUpdateUrl == "") return false;
            App.Open(systemNoticeUpdateUrl); return true;
        }
    }
}
