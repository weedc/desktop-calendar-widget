using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using System.Collections.Generic;

namespace TeamCalendar
{
    sealed class CodexDialog : ThemeWindow
    {
        readonly FeatureState state;
        readonly Label status, schedule, message;
        readonly TextBox time;
        public CodexDialog(FeatureState features) : base("Codex · Dot 연동", new Size(690, 620))
        {
            state = features; StartPosition = FormStartPosition.CenterScreen;
            Caption("1. 이 PC의 내 Codex에 연결", 22, 74, 640);
            status = Caption("", 22, 104, 646); status.Height = 42;
            Action("Codex 연동하기", 22, 157, 210, delegate { Try(delegate { message.Text = CodexIntegration.Register(true); UpdateStatus(); }); });
            Action("연동 해제", 244, 157, 130, delegate { Try(delegate { message.Text = CodexIntegration.Register(false); UpdateStatus(); }); });
            var description = Caption("연결은 현재 Windows 사용자에게만 등록됩니다.\n각 팀원이 자기 Codex로 알림을 받을 수 있습니다.", 22, 206, 646); description.Height = 50;
            Caption("2. 매일 한 번, 원하는 시각에 일정 알림", 22, 281, 646);
            time = Field("Dot 알림 시각", 22, 317, 100); time.Text = state.dotReminderTime;
            Caption("한국 시간 · 기본 오전 0시", 140, 321, 420);
            schedule = Caption("", 22, 365, 646); schedule.Height = 42;
            Action("Codex에서 알림 연결", 22, 418, 235, delegate { Try(delegate { string request = MakeRequest(); CodexIntegration.Register(true); CodexIntegration.Open(CodexIntegration.DraftLink(request)); message.Text = "Codex에 열린 요청을 한 번 전송하면 예약 알림이 등록됩니다."; UpdateStatus(); }); });
            Action("요청 복사", 270, 418, 130, delegate { Try(delegate { Clipboard.SetText(MakeRequest()); message.Text = "요청을 복사했습니다. 내 Codex/Dot에 붙여넣고 전송하세요."; }); });
            Action("등록 상태 확인", 413, 418, 220, delegate { UpdateStatus(); });
            message = Caption("Codex 연결과 예약 알림 등록은 각각의 상태를 표시합니다.", 22, 479, 646); message.Height = 60;
            Action("예약 알림 관리", 22, 558, 210, delegate { Try(delegate { CodexIntegration.Open("codex://automations"); }); });
            Action("닫기", 553, 558, 115, delegate { Close(); });
            UpdateStatus();
        }
        string MakeRequest()
        {
            string value = time.Text.Trim(); if (!CodexIntegration.ValidTime(value)) throw new ArgumentException("알림 시각은 00:00처럼 입력해 주세요.");
            state.dotReminderTime = value; state.Save();
            string request = CodexIntegration.DailyPrompt(value, Application.ExecutablePath);
            File.WriteAllText(Path.Combine(App.DataDir, "codex-setup-request.txt"), request, new System.Text.UTF8Encoding(false)); return request;
        }
        void Try(Action action) { try { action(); } catch (Exception e) { message.Text = e.Message; } }
        void UpdateStatus()
        {
            var data = App.Json.Deserialize<Dictionary<string, object>>(App.Json.Serialize(CodexIntegration.Status(state)));
            status.Text = Convert.ToBoolean(data["registered"]) ? "이 PC의 Codex에 등록됨 · 새 도구는 Codex를 다시 열면 반영됩니다." : "아직 Codex에 연결하지 않았습니다.";
            schedule.Text = Convert.ToBoolean(data["automationActive"]) ? "예약 알림 등록됨 · " + App.Str(data, "automationName") : "예약 알림 미등록 · Codex에서 요청을 전송해 등록하세요.";
        }
    }
}
