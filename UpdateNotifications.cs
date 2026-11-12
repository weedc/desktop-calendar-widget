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
        WidgetUpdate available;
        bool installing;
        public UpdateCheckDialog(string current, Action<WidgetUpdate, UpdateCheckDialog> install) : base("업데이트 확인", new Size(460, 255))
        {
            StartPosition = FormStartPosition.CenterParent;
            Caption("현재 버전  " + current, 22, 72, 416);
            status = Caption("새 버전을 확인하고 있습니다…", 22, 107, 416); status.Height = 62;
            download = Action("업데이트하기", 191, 195, 118, delegate { if (available != null && !installing) { installing = true; download.Enabled = false; status.Text = "업데이트를 다운로드하고 있습니다…\n완료되면 위젯을 다시 시작합니다."; install(available, this); } }); download.Enabled = false;
            Action("닫기", 322, 195, 116, delegate { Close(); });
            FormClosing += delegate(object sender, FormClosingEventArgs args) { if (installing && args.CloseReason == CloseReason.UserClosing) args.Cancel = true; };
        }
        public void Finish(WidgetUpdate result, bool failed)
        {
            if (IsDisposed || installing) return;
            status.Text = failed ? "업데이트를 확인하지 못했습니다. 잠시 후 다시 시도해 주세요." : result == null ? "최신 버전을 사용하고 있습니다." : "새 버전 " + result.version + "을 사용할 수 있습니다.";
            available = result; download.Enabled = available != null;
        }
        public void InstallFailed(string message) { installing = false; status.Text = "업데이트하지 못했습니다. " + message; download.Enabled = available != null; }
        public void InstallReady() { installing = false; }
    }
    sealed partial class CalendarWindow
    {
        readonly string updateVersion = Assembly.GetExecutingAssembly().GetName().Version.ToString(3);
        readonly string updateHistoryPath = Path.Combine(App.DataDir, "update-state.json");
        UpdateHistory updateHistory;
        UpdateCheckDialog updateDialog;
        WidgetUpdate pendingUpdate;
        DateTime updateAttempt = DateTime.MinValue;
        bool checkingUpdates, lastUpdateEnabled, installingUpdate;
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
            updateDialog = new UpdateCheckDialog(updateVersion, InstallUpdate);
            // System notifications also work while the desktop widget is hidden.
            if (owner == this) { updateDialog.StartPosition = FormStartPosition.CenterScreen; updateDialog.Show(); }
            else updateDialog.Show(owner);
            StartUpdateCheck(updateDialog);
        }
        void InstallUpdate(WidgetUpdate result, UpdateCheckDialog dialog)
        {
            if (installingUpdate) return;
            installingUpdate = true;
            Task.Run(delegate
            {
                string plan = null, error = null;
                try { plan = UpdateInstaller.Prepare(result); } catch (Exception e) { error = e.Message; }
                if (IsDisposed || !IsHandleCreated) return;
                try { BeginInvoke((Action)delegate
                {
                    if (error != null) { installingUpdate = false; if (!dialog.IsDisposed) dialog.InstallFailed(error); return; }
                    try { UpdateInstaller.Handoff(plan); dialog.InstallReady(); Close(); }
                    catch (Exception e) { installingUpdate = false; if (!dialog.IsDisposed) dialog.InstallFailed(e.Message); }
                }); } catch (InvalidOperationException) { }
            });
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
            if (installingUpdate) return;
            if (lastUpdateEnabled != features.updateNotifications)
            {
                lastUpdateEnabled = features.updateNotifications; updateGeneration++; pendingUpdate = null;
                if (lastUpdateEnabled) updateAttempt = DateTime.MinValue;
            }
            if (!checkingUpdates && UpdatePolicy.Due(features.updateNotifications, updateAttempt, DateTime.UtcNow)) StartUpdateCheck(null);
            if (pendingUpdate != null && features.updateNotifications && DailySummaryPolicy.CanNotify(lastSystemNotice, DateTimeOffset.Now))
            {
                var result = pendingUpdate; pendingUpdate = null;
                ShowSystemNotice("캘린더 업데이트 · " + result.version, "새 버전이 있습니다. 알림을 클릭해 업데이트하세요.", result.url);
                updateHistory.notifiedVersion = result.version; SaveUpdateHistory();
            }
        }
        bool OpenUpdateNotice()
        {
            if (systemNoticeUpdateUrl == "") return false;
            ShowUpdateCheck(this); return true;
        }
    }
}
