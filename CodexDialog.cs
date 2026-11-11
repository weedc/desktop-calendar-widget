using System;
using System.Drawing;
using System.Windows.Forms;
using System.Collections.Generic;

namespace TeamCalendar
{
    sealed class CodexDialog : ThemeWindow
    {
        readonly FeatureState state;
        readonly Label status, message;
        public CodexDialog(FeatureState features) : base("Codex · Dot 연동", new Size(690, 520))
        {
            state = features; StartPosition = FormStartPosition.CenterScreen;
            Caption("1. 이 PC의 내 Codex에 연결 (선택)", 22, 74, 640);
            status = Caption("", 22, 104, 646); status.Height = 42;
            Action("Codex 연동하기", 22, 157, 210, delegate { Try(delegate { message.Text = CodexIntegration.Register(true); UpdateStatus(); }); });
            Action("연동 해제", 244, 157, 130, delegate { Try(delegate { message.Text = CodexIntegration.Register(false); UpdateStatus(); }); });
            var description = Caption("각 팀원이 자기 Codex에서 이모지를 정리하고 위젯을 조작합니다.\n매일 일정 요약과 1시간 전 알림은 위젯이 Windows 알림으로 보냅니다.", 22, 206, 646); description.Height = 55;
            Caption("2. 새로 들어오거나 내용이 바뀐 일정의 이모지 정리", 22, 281, 646);
            Action("이모지 정리 요청", 22, 324, 235, delegate { Try(delegate { CodexIntegration.Register(true); CodexIntegration.Open(CodexIntegration.DraftLink(MakeRequest())); message.Text = "Codex에 열린 요청을 전송하면 미분류 일정의 이모지를 저장합니다."; }); });
            Action("요청 복사", 270, 324, 130, delegate { Try(delegate { Clipboard.SetText(MakeRequest()); message.Text = "요청을 복사했습니다. 내 Codex/Dot에 붙여넣고 전송하세요."; }); });
            Action("연결 상태 확인", 413, 324, 220, delegate { UpdateStatus(); });
            message = Caption("분류는 요청을 전송할 때 수행하며, 창 크기를 바꿀 때 다시 호출하지 않습니다.\n분류 전에는 로컬 규칙으로 표시하고 직접 고른 이모지는 보존합니다.", 22, 381, 646); message.Height = 65;
            Action("기존 Codex 예약 관리", 22, 464, 235, delegate { Try(delegate { CodexIntegration.Open("codex://automations"); }); });
            Action("닫기", 553, 464, 115, delegate { Close(); });
            UpdateStatus();
        }
        string MakeRequest() { return CodexIntegration.EmojiRequest(Application.ExecutablePath); }
        void Try(Action action) { try { action(); } catch (Exception e) { message.Text = e.Message; } }
        void UpdateStatus()
        {
            var data = App.Json.Deserialize<Dictionary<string, object>>(App.Json.Serialize(CodexIntegration.Status(state)));
            status.Text = Convert.ToBoolean(data["registered"]) ? "이 PC의 Codex에 등록됨 · 새 도구는 Codex를 다시 열면 반영됩니다." : "아직 Codex에 연결하지 않았습니다.";
        }
    }
}
