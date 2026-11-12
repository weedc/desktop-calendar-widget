"""Read official Google Calendar DOM labels through the existing Chrome login and a retained hidden reader."""
from __future__ import annotations
import base64
import json
from pathlib import Path
import re
import time
import threading
import uuid
from urllib.parse import urlencode, unquote
from core import now, parse_snapshot, PALETTE
from google_browser import GoogleBrowser, LoginRequired

_dom_path = Path(__file__).resolve().parent / 'GoogleCalendarDom.json'
if not _dom_path.is_file(): _dom_path = _dom_path.parent.parent / 'GoogleCalendarDom.json'
SCRIPTS = json.loads(_dom_path.read_text(encoding='utf-8'))

class GoogleReader:
    def __init__(self, browser=None): self.browser = browser or GoogleBrowser(); self.cancelled = threading.Event()
    def cancel(self): self.cancelled.set()
    def script(self, name, value=None): return self.browser.evaluate('(' + SCRIPTS[name] + ')(' + json.dumps(value,ensure_ascii=True) + ')')
    def close(self): self.browser.close()
    def reset(self): self.browser.close(); self.browser = GoogleBrowser()
    def open_login(self, email=''): return self.discover(email)

    def read_month(self, month, sources, email='', allow_initial=False):
        successes, errors = {}, []
        with self.browser.gate:
            for source in sources:
                if self.cancelled.is_set(): break
                if not source.get('enabled', True):
                    continue
                query = dict(src=source['id'], mode='MONTH', wkst='1', hl='ko', ctz='Asia/Seoul',
                             dates=month.replace('-', '') + '01/' + month.replace('-', '') + '01',
                             widget_reader=self.browser.tag, showPrint='0', showCalendars='0')
                if email:
                    query['authuser'] = email
                try:
                    self.browser.navigate('https://calendar.google.com/calendar/embed?' + urlencode(query),allow_initial=allow_initial)
                    snapshot = None; stable = 0
                    for _ in range(85):
                        if self.cancelled.is_set(): raise RuntimeError('위젯 종료 중')
                        snapshot = self.script('snapshot')
                        if snapshot and snapshot.get('month') == month and len(snapshot.get('cells', [])) >= 28 and not snapshot.get('busy'):
                            stable += 1
                            if stable >= 2: break
                        else: stable = 0
                        time.sleep(0.4)
                    if stable >= 2 and self.browser.phase=='connecting': self.browser.hide()
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
                            self.script('dismiss')
                            for _ in range(20):
                                if not self.script('expanded'): break
                                time.sleep(.15)
                            else: raise RuntimeError('일정 펼침 창을 닫지 못했습니다. 기존 데이터를 유지합니다.')
                    successes[source['id']] = parse_snapshot(snapshot or {}, month, source)
                except Exception as error:
                    if self.browser.phase not in ('hidden','new'):
                        try: self.browser.hide()
                        except Exception: pass
                        self.browser.phase='lost'
                    # Background failures are reported in the widget. Only an explicit
                    # account-connection action may open an interactive login window.
                    errors.append(source['name'] + ': ' + str(error))
        return dict(month=month, outcomes=successes, errors=errors, at=now().isoformat())

    def discover(self, email=''):
        browser=GoogleBrowser()
        query={'hl':'ko','widget_account':browser.tag}
        if email: query['authuser']=email
        settings='https://calendar.google.com/calendar/u/0/r/settings?'+urlencode(query)
        login='https://accounts.google.com/ServiceLogin?'+urlencode({'hl':'ko','service':'cl','continue':settings})
        def script(name,value=None): return browser.evaluate('('+SCRIPTS[name]+')('+json.dumps(value,ensure_ascii=True)+')')
        with self.browser.gate:
            try:
                browser.navigate(login,allow_initial=True)
                catalog=None
                for _ in range(400):
                    if self.cancelled.is_set(): raise RuntimeError('위젯 종료 중')
                    try: catalog=script('catalog')
                    except LoginRequired: catalog=None
                    if catalog and catalog.get('rows') and catalog.get('email'): break
                    time.sleep(.3)
                if not catalog or not catalog.get('rows') or not catalog.get('email'):
                    raise RuntimeError('Chrome의 Google 로그인과 캘린더 접근 권한을 확인한 뒤 다시 연결하세요.')
                if email and email.casefold()!=catalog['email'].casefold():
                    raise RuntimeError('요청한 계정과 로그인한 계정이 다릅니다. 사용할 Chrome 계정을 확인하세요.')
                found={}
                for row in catalog['rows'][:250]:
                    if self.cancelled.is_set(): raise RuntimeError('위젯 종료 중')
                    before=script('route')
                    if not script('select',row): continue
                    match=None
                    for _ in range(27):
                        route=script('route')
                        if route!=before:
                            match=re.search(r'/settings/calendar/([^/?#]+)',route)
                            if match or '/settings/birthdays' in route: break
                        time.sleep(.15)
                    if not match: continue
                    try:
                        encoded=unquote(match[1]); sid=base64.urlsafe_b64decode(encoded+'='*(-len(encoded)%4)).decode()
                        if '@' not in sid or len(sid)>512 or any(c.isspace() for c in sid): continue
                        found[sid]=dict(id=sid,name=row['label'],enabled=True,color=PALETTE[len(found)%len(PALETTE)])
                    except (ValueError,UnicodeError): continue
                if not found: raise RuntimeError('선택 가능한 캘린더를 찾지 못했습니다. ID 입력 방식을 사용할 수도 있습니다.')
                return dict(email=catalog['email'],calendars=list(found.values()))
            finally: browser.close()
