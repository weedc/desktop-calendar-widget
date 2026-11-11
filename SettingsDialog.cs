using System;
using System.Drawing;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace TeamCalendar
{
    sealed class AccountPicker : ThemeWindow
    {
        readonly AccountCatalog catalog;
        readonly HashSet<string> chosen;
        readonly CalendarSourceList list;
        readonly Label message;
        public List<CalendarSource> SelectedCalendars { get { return catalog.calendars.Select(c => new CalendarSource { id = c.id, name = c.name, color = c.color, enabled = chosen.Contains(c.id) }).ToList(); } }
        public AccountPicker(AccountCatalog result, List<CalendarSource> previous) : base("계정에서 캘린더 선택", new Size(690, 555))
        {
            catalog = result; StartPosition = FormStartPosition.CenterScreen;
            var old = previous.Where(c => c.enabled).Select(c => c.id).ToList();
            chosen = new HashSet<string>(old.Count == 0 ? result.calendars.Select(c => c.id) : old);
            Caption(result.email, 22, 76, 646);
            Caption("표시할 캘린더를 선택하세요. ID는 입력하지 않아도 됩니다.", 22, 110, 646);
            list = new CalendarSourceList { Left = 22, Top = 153, Width = 646, Height = 260, Font = Font }; Controls.Add(list);
            list.CheckedChanged = delegate(string id, bool enabled) { if (enabled) chosen.Add(id); else chosen.Remove(id); };
            Action("전체 선택", 22, 431, 130, delegate { foreach (var c in catalog.calendars) chosen.Add(c.id); Reload(); });
            Action("선택 해제", 164, 431, 130, delegate { chosen.Clear(); Reload(); });
            message = Caption(result.warnings.Count == 0 ? "선택한 캘린더만 이 위젯에 표시됩니다." : "일부 특수 캘린더는 목록에서 제외되었습니다.", 22, 479, 370); message.Height = 55;
            Action("선택한 캘린더 사용", 413, 490, 255, delegate { if (chosen.Count == 0) { message.Text = "표시할 캘린더를 하나 이상 선택해 주세요."; return; } DialogResult = DialogResult.OK; Close(); });
            Reload();
        }
        void Reload() { list.SetItems(catalog.calendars.Select(c => new SourceChoice { id = c.id, name = c.name, context = catalog.email, enabled = chosen.Contains(c.id), color = c.color }).ToList()); }
    }
}
