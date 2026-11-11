using System;
using System.Drawing;
using System.Windows.Forms;

namespace TeamCalendar
{
    sealed class EmojiDialog : ThemeWindow
    {
        public EmojiDialog(CalendarWindow calendar, CalendarEvent item) : base("이 일정의 이모지", new Size(620, 480))
        {
            StartPosition = FormStartPosition.CenterScreen;
            var mark = calendar.EventEmoji(item);
            var title = Caption(item.title, 22, 72, 576); title.Height = 45; title.AutoEllipsis = true;
            Caption("대표 이모지", 22, 129, 115); var icon = Field("대표 이모지", 22, 156, 115); icon.Text = mark.icon;
            Caption("짧은 이름", 160, 129, 438); var label = Field("이모지 짧은 이름", 160, 156, 438); label.Text = mark.label;
            string[] icons = { "🎨", "🤖", "💬", "📝", "✈️", "🌴", "🩺", "🏁", "🐣", "📌" };
            for (int i = 0; i < icons.Length; i++) { string value = icons[i]; var button = Action(value, 22 + i * 57, 210, 47, delegate { icon.Text = value; }); button.Font = new Font("Segoe UI Emoji", 14); }
            Caption("프로젝트 이름 (선택)", 22, 270, 576); var project = Field("이모지 프로젝트", 22, 297, 576); project.Text = mark.project;
            var whole = new CheckBox { Text = "같은 프로젝트의 대표 이모지로 사용", Left = 22, Top = 343, Width = 560, Height = 28, ForeColor = Design.Text }; Controls.Add(whole);
            var message = Caption("직접 고른 이모지는 Codex가 덮어쓰지 않습니다.", 22, 381, 576); message.Height = 38;
            Action("자동 지정으로", 22, 427, 170, delegate { calendar.ResetEmoji(item, project.Text.Trim(), whole.Checked); Close(); });
            Action("저장", 468, 427, 130, delegate { try { calendar.SetManualEmoji(item, icon.Text.Trim(), label.Text.Trim(), project.Text.Trim(), whole.Checked); Close(); } catch (Exception e) { message.Text = e.Message; } });
        }
    }
}
