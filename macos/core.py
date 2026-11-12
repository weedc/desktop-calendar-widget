"""Platform independent calendar model. No Google credentials or remote writes."""
from __future__ import annotations
import calendar
import copy
import hashlib
import json
import os
import re
import tempfile
import unicodedata
from datetime import datetime, timedelta
from pathlib import Path
from zoneinfo import ZoneInfo

SEOUL = ZoneInfo('Asia/Seoul')
VERSION = '5.6.1'
PALETTE = ['#548b9b', '#607eb5', '#a073b0', '#b76c86', '#b58b48', '#6d9769']
OPTIONS = {'mode': 'account', 'account_email': '', 'id_calendars': [], 'account_calendars': [],
           'update_notifications': True,
           'auto_sync': True, 'compact_emojis': True, 'opacity': 84, 'font_size': 11,
           'desktop': True, 'startup': False, 'hour_reminders': True, 'all_day_reminders': False,
           'daily_summary': True, 'summary_time': '00:00', 'setup_completed': False}


def now():
    return datetime.now(SEOUL)


def month_valid(month):
    try:
        return datetime.strptime(month, '%Y-%m').strftime('%Y-%m') == month
    except (ValueError, TypeError):
        return False


def day_valid(day):
    try:
        return datetime.strptime(day, '%Y-%m-%d').strftime('%Y-%m-%d') == day
    except (ValueError, TypeError):
        return False


def time_valid(value):
    return isinstance(value, str) and bool(re.fullmatch(r'(?:[01]\d|2[0-3]):[0-5]\d', value))


def days_for_month(month):
    first = datetime.strptime(month + '-01', '%Y-%m-%d').date()
    # Sunday first; Windows uses five or six rows, with a minimum of five.
    rows=max(5,((first.weekday()+1)%7+calendar.monthrange(first.year,first.month)[1]+6)//7)
    first -= timedelta(days=(first.weekday() + 1) % 7)
    return [(first + timedelta(days=i)).isoformat() for i in range(rows*7)]


def context_key(event):
    text = '\n'.join(unicodedata.normalize('NFKC', str(event.get(k, ''))).strip().lower()
                     for k in ('calendarId', 'calendarName', 'title', 'location'))
    return hashlib.sha256(text.encode()).hexdigest().upper()[:24]


def guess(event):
    title = event.get('title', '')
    icon, label = ('📝', '메모') if event.get('isNote') else ('📌', '일정')
    for pattern, candidate, name in [
        ('검진|병원|진료|치과|health|medical', '🩺', '검진'),
        ('연차|휴가|휴무|반차|vacation|leave', '🌴', '휴가'),
        ('출장|항공|비행|flight|travel|airport', '✈️', '출장'),
        ('계약|서명|contract', '📝', '계약'), ('마감|제출|deadline', '🏁', '마감'),
        ('미술|디자인|design|artwork', '🎨', '디자인'), ('인공지능|(?<![a-z])ai(?![a-z])', '🤖', 'AI'),
        ('생일|기념일|birthday', '🎂', '기념일'), ('점심|식사|저녁|lunch|dinner', '🍽️', '식사'),
        ('회의|미팅|면담|meeting|interview', '💬', '회의'), ('보고|발표|report|presentation', '📊', '보고')]:
        if re.search(pattern, title, re.I):
            icon, label = candidate, name
            break
    project = re.search(r'[<〈《]([^>〉》]{1,24})[>〉》]', title)
    return dict(key=context_key(event), icon=icon, activity='', label=label,
                project=project.group(1).strip() if project else '', source='fallback')


def valid_icon(value, optional=False):
    return isinstance(value, str) and ((optional and not value) or
        0 < len(value) <= 16 and any(0x1f000 <= ord(c) <= 0x1faff or 0x2300 <= ord(c) <= 0x27ff for c in value)
        and all(ord(c) >= 0x2300 or c in '\u200d\ufe0f\ufe0e\u20e3' or c.isdigit() for c in value))


def source_key(options):
    return 'account_calendars' if options['mode'] == 'account' else 'id_calendars'


def parse_snapshot(snapshot, month, source):
    """Parse Google's Korean accessible day/event labels; reject incomplete pages."""
    if snapshot.get('month') != month or len(snapshot.get('cells', [])) < 28:
        raise ValueError('Google 일정 표가 아직 준비되지 않았습니다. 로그인과 접근 권한을 확인하세요.')
    result, seen = [], set()
    for cell in snapshot['cells']:
        match = re.match(r'^일정 (?:\d+개|없음),\s*(\d{1,2})월\s*(\d{1,2})일', cell['label'])
        if not match:
            continue
        cm, cd = map(int, match.groups())
        year, mm = map(int, month.split('-'))
        year += -1 if cm - mm > 6 else 1 if mm - cm > 6 else 0
        day = f'{year:04}-{cm:02}-{cd:02}'
        if not day_valid(day):
            raise ValueError('Google 날짜 형식이 변경되었습니다.')
        count=re.match(r'^일정 (\d+)개,',cell['label'])
        if count and int(count[1])>len(cell['events']):
            raise ValueError('숨겨진 일정을 모두 읽지 못했습니다. 기존 데이터를 유지합니다.')
        for raw in cell['events']:
            if (day, raw) in seen or ', 캘린더: ' not in raw or ', ' not in raw:
                continue
            seen.add((day, raw))
            time_text, rest = raw.split(', ', 1)
            title, metadata = rest.split(', 캘린더: ', 1)
            location = re.search(r', 위치: (.*), \d{4}년', metadata)
            start_day = re.search(r'(\d{4})년\s*(\d{1,2})월\s*(\d{1,2})일', metadata)
            start_day = '-'.join([start_day[1], start_day[2].zfill(2), start_day[3].zfill(2)]) if start_day else day
            all_day = time_text.startswith('종일')
            clock = re.search(r'(오전|오후)\s*(\d{1,2})(?::(\d{2}))?', time_text)
            hh, mi = (9, 0) if all_day else (0, 0)
            if clock:
                hh, mi = int(clock[2]) % 12 + (12 if clock[1] == '오후' else 0), int(clock[3] or 0)
            start = datetime.fromisoformat(start_day).replace(hour=hh, minute=mi, tzinfo=SEOUL)
            uid = hashlib.sha256((source['id'] + '|' + raw).encode()).hexdigest().upper()[:20]
            result.append(dict(id=uid + '-' + day, uid=uid, date=day, title=title, time=time_text,
                               location=location[1] if location else '', raw=raw, calendarId=source['id'],
                               calendarName=source['name'], color=source['color'], allDay=all_day,
                               sort=-1 if all_day else hh * 60 + mi, start=start.isoformat()))
    return result


class Store:
    def __init__(self, folder):
        self.folder = Path(folder)
        self.folder.mkdir(parents=True, exist_ok=True, mode=0o700)
        self.path = self.folder / 'calendar-data.json'
        self.data = dict(options=copy.deepcopy(OPTIONS), months={}, notes=[], highlights={}, emojis={},
                         projects={}, delivered=[], summary_checked='', geometry=[80, 80, 1000, 650])
        if self.path.exists():
            # Corrupt data must not be silently overwritten by a fresh installation.
            saved = json.loads(self.path.read_text(encoding='utf-8'))
            self.data.update(saved)
            self.data['options'] = {**copy.deepcopy(OPTIONS), **saved.get('options', {})}

    @property
    def options(self):
        return self.data['options']

    def save(self):
        fd, path = tempfile.mkstemp(dir=self.folder, prefix='.calendar-', suffix='.tmp')
        try:
            with os.fdopen(fd, 'w', encoding='utf-8') as f:
                json.dump(self.data, f, ensure_ascii=False)
                f.flush()
                os.fsync(f.fileno())
            os.chmod(path, 0o600)
            os.replace(path, self.path)
        finally:
            if os.path.exists(path):
                os.unlink(path)

    def sources(self):
        return self.options[source_key(self.options)]

    def events(self, month):
        enabled = {s['id'] for s in self.sources() if s.get('enabled', True)}
        events = [e for e in self.data['months'].get(month, {}).get('events', []) if e['calendarId'] in enabled]
        for note in self.data['notes']:
            if note['date'] in days_for_month(month):
                clock = note.get('time', '')
                events.append(dict(id=note['id'], uid=note['id'], date=note['date'], title=note['text'],
                                   time=clock or '메모', start=note['date'] + 'T' + (clock or '09:00') + ':00+09:00',
                                   color=note['color'], calendarId='local', calendarName='개인 메모',
                                   location='', allDay=not clock, sort=-1 if not clock else int(clock[:2])*60+int(clock[3:]),
                                   isNote=True, important=note.get('important', False)))
        return sorted(events, key=lambda e: (e['date'], not self.important(e), e.get('sort', 0), e['title']))

    def important(self, event):
        return event.get('important', False) or event['id'] in self.data['highlights']

    def merge(self, month, outcomes):
        # Failed sources retain their last successful data; disabled sources remain cached.
        old = self.data['months'].get(month, {'events': []})
        events = list(old['events'])
        for source_id, incoming in outcomes.items():
            events = [e for e in events if e['calendarId'] != source_id] + incoming
        self.data['months'][month] = {'events': events, 'syncedAt': now().isoformat()}
        self.save()

    def apply_options(self, draft):
        if draft['mode'] not in ('id', 'account') or not time_valid(draft['summary_time']):
            raise ValueError('연결 방식 또는 요약 시각을 확인하세요.')
        if not 35 <= draft['opacity'] <= 100 or not 8 <= draft['font_size'] <= 20:
            raise ValueError('투명도 또는 글자 크기를 확인하세요.')
        ids = set()
        for source in draft[source_key(draft)]:
            if not source['id'].strip() or source['id'] in ids:
                raise ValueError('비어 있거나 중복된 캘린더 ID입니다.')
            ids.add(source['id'])
        # Copy options only: async sync, notes and delivered notifications are preserved.
        if draft['summary_time'] != self.options['summary_time'] or (draft['daily_summary'] and not self.options['daily_summary']):
            self.data['summary_checked'] = ''
        self.data['options'] = copy.deepcopy(draft)
        self.save()

    def resolved(self, event):
        mark = copy.deepcopy(self.data['emojis'].get(context_key(event), guess(event)))
        if mark['source'] != 'manual':
            for project, icon in self.data['projects'].get(event['calendarId'], {}).items():
                if project.casefold() == mark.get('project', '').casefold() or project.casefold() in event['title'].casefold():
                    mark.update(icon=icon, project=project, source='project')
                    break
        return mark

    def classify(self, key, icon, activity='', label='', project='', manual=False, whole_project=False):
        event = next((e for m in self.data['months'] for e in self.events(m) if context_key(e) == key), None)
        if not event:
            raise ValueError('더 이상 존재하지 않는 일정 맥락입니다.')
        if not valid_icon(icon) or not valid_icon(activity, True) or len(label) > 24 or len(project) > 40:
            raise ValueError('이모지와 짧은 이름을 확인하세요.')
        old = self.data['emojis'].get(key)
        if old and old['source'] == 'manual' and not manual:
            return old
        mark = dict(key=key, icon=icon, activity=activity, label=label, project=project,
                    source='manual' if manual else 'codex')
        self.data['emojis'][key] = mark
        if manual and whole_project and project:
            self.data['projects'].setdefault(event['calendarId'], {})[project] = icon
        self.save()
        return mark

    def pending(self):
        result = {}
        for month in self.data['months']:
            for e in self.events(month):
                key = context_key(e)
                if key not in self.data['emojis']:
                    result[key] = {k: e[k] for k in ('calendarId', 'calendarName', 'title', 'location')}
                    result[key]['key'] = key
        return list(result.values())

    def notifications(self, at):
        options = self.options
        current=at.strftime('%Y-%m')
        events = self.events(current)
        next_month=(at+timedelta(hours=1)).strftime('%Y-%m')
        if next_month!=current:
            events=list({e['id']:e for e in events+self.events(next_month)}.values())
        delivered = set(self.data['delivered'])
        alerts = []
        today = at.strftime('%Y-%m-%d')
        summary_ready=current in self.data['months'] or not options['auto_sync']
        if summary_ready and options['daily_summary'] and at.strftime('%H:%M') == options['summary_time'] and self.data['summary_checked'] != today:
            self.data['summary_checked'] = today
            todays = [e for e in events if e['date'] == today]
            if todays:
                alerts.append(dict(id='summary-' + today, title='오늘의 일정', date=today,
                                   body='\n'.join(f"{e['time']} {e['title']} · {e['calendarName']}" for e in todays)))
        if options['hour_reminders']:
            occurrences = set()
            for event in events:
                key = event['uid'] + '|' + event['start']
                if key in delivered or key in occurrences or (event['allDay'] and not options['all_day_reminders']):
                    continue
                occurrences.add(key)
                start = datetime.fromisoformat(event['start'])
                minutes = (start - at).total_seconds() / 60
                if 0 < minutes <= 60:
                    alerts.append(dict(id=key, title=f'{int(minutes)}분 뒤 · {event["title"]}', date=event['date'],
                                       body=f"{start.strftime('%H:%M')} · {event['calendarName']}"))
        return alerts

    def acknowledge(self, alert):
        self.data['delivered'] = (self.data['delivered'] + [alert['id']])[-5000:]
        self.save()
