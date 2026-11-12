"""Read official Google Calendar DOM labels through the widget's headless browser."""
from __future__ import annotations
import base64
import json
from pathlib import Path
import re
import time
import threading
import uuid
from urllib.parse import urlencode
from core import now, parse_snapshot, PALETTE
from google_browser import GoogleBrowser

_dom_path = Path(__file__).resolve().parent / 'GoogleCalendarDom.json'
if not _dom_path.is_file(): _dom_path = _dom_path.parent.parent / 'GoogleCalendarDom.json'
SCRIPTS = json.loads(_dom_path.read_text(encoding='utf-8'))

class GoogleReader:
    def __init__(self, browser=None): self.browser = browser or GoogleBrowser(); self.cancelled = threading.Event()
    def cancel(self): self.cancelled.set()
    def script(self, name, value=None): return self.browser.evaluate('(' + SCRIPTS[name] + ')(' + json.dumps(value,ensure_ascii=True) + ')')
    def close(self): self.browser.close()
    def open_login(self, email=''): self.browser.open_login(email)

    def read_month(self, month, sources, email=''):
        successes, errors = {}, []
        with self.browser.gate:
            for source in sources:
                if self.cancelled.is_set(): break
                if not source.get('enabled', True):
                    continue
                query = dict(src=source['id'], mode='MONTH', wkst='1', hl='ko', ctz='Asia/Seoul',
                             dates=month.replace('-', '') + '01/' + month.replace('-', '') + '01',
                             widget_refresh=uuid.uuid4().hex, showPrint='0', showCalendars='0')
                if email:
                    query['authuser'] = email
                try:
                    self.browser.navigate('https://calendar.google.com/calendar/embed?' + urlencode(query))
                    snapshot = None; stable = 0
                    for _ in range(85):
                        if self.cancelled.is_set(): raise RuntimeError('위젯 종료 중')
                        snapshot = self.script('snapshot')
                        if snapshot and snapshot.get('month') == month and len(snapshot.get('cells', [])) >= 28 and not snapshot.get('busy'):
                            stable += 1
                            if stable >= 2: break
                        else: stable = 0
                        time.sleep(0.4)
                    if stable < 2: raise RuntimeError('Google 일정 표를 읽지 못했습니다. 로그인과 접근 권한을 확인하세요. 기존 데이터를 유지합니다.')
                    for cell in snapshot['cells']:
                        if not cell.get('more'): continue
                        if not self.script('expand', cell['label']): raise RuntimeError('숨겨진 일정을 펼치지 못했습니다. 기존 데이터를 유지합니다.')
                        try:
                            expanded = []
                            expected = re.match(r'^일정 (\d+)개,',cell['label'])
                            expected = int(expected[1]) if expected else 1
                            for _ in range(25):
                                expanded = self.script('expanded') or []
                                if len(set(expanded)) >= expected: break
                                time.sleep(0.2)
                            if not expanded: raise RuntimeError('숨겨진 일정 목록을 읽지 못했습니다. 기존 데이터를 유지합니다.')
                            cell['events'] = list(dict.fromkeys(cell['events'] + expanded))
                        finally:
                            if not self.script('dismiss'): raise RuntimeError('일정 펼침 창을 닫지 못했습니다. 기존 데이터를 유지합니다.')
                    successes[source['id']] = parse_snapshot(snapshot or {}, month, source)
                except Exception as error:
                    # Background failures are reported in the widget. Only an explicit
                    # account-connection action may open an interactive login window.
                    errors.append(source['name'] + ': ' + str(error))
        return dict(month=month, outcomes=successes, errors=errors, at=now().isoformat())

    def discover(self, email=''):
        query = {'hl':'ko', 'widget_account':uuid.uuid4().hex}
        if email: query['authuser'] = email
        with self.browser.gate:
            self.browser.navigate('https://calendar.google.com/calendar/u/0/r/settings?' + urlencode(query))
            catalog = None
            for _ in range(85):
                catalog = self.script('catalog')
                if catalog and catalog.get('rows') and catalog.get('email'): break
                time.sleep(0.35)
            if not catalog or not catalog.get('rows') or not catalog.get('email'):
                raise RuntimeError('캘린더 목록을 읽지 못했습니다. Google 로그인 열기에서 계정과 접근 권한을 확인하세요.')
            if email and email.casefold() != catalog['email'].casefold():
                raise RuntimeError('요청한 계정과 로그인한 계정이 다릅니다. Google 로그인에서 사용할 계정을 선택하세요.')
            found = {}
            for row in catalog['rows'][:250]:
                if self.cancelled.is_set(): raise RuntimeError('위젯 종료 중')
                before = self.script('route')
                if not self.script('select', row): continue
                match = None
                for _ in range(27):
                    route = self.script('route')
                    if route != before:
                        match = re.search(r'/settings/calendar/([^/?#]+)',route)
                        if match or '/settings/birthdays' in route: break
                    time.sleep(0.15)
                if not match: continue
                try:
                    encoded = match[1]
                    sid = base64.urlsafe_b64decode(encoded + '=' * (-len(encoded)%4)).decode()
                    if '@' not in sid or len(sid)>512 or any(c.isspace() for c in sid): continue
                    found[sid] = dict(id=sid,name=row['label'],enabled=True,color=PALETTE[len(found)%len(PALETTE)])
                except (ValueError,UnicodeError): continue
            if not found: raise RuntimeError('선택 가능한 캘린더를 찾지 못했습니다. ID 입력 방식을 사용할 수도 있습니다.')
            return dict(email=catalog['email'],calendars=list(found.values()))
