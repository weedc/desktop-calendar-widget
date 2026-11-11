"""Read only the widget-owned official Google Calendar pages in Google Chrome.

Chrome's Apple Events JavaScript permission and macOS Automation permission are
user-controlled. Never extract cookies, tokens, passwords or other tab contents.
"""
from __future__ import annotations
import base64
import json
import re
import subprocess
import sys
import time
import uuid
from urllib.parse import urlencode, urlparse
from core import now, parse_snapshot, PALETTE

DOM_HELPERS = r'''
  const accessible = e => {
    const refs=(e.getAttribute('aria-labelledby')||'').split(/\s+/).filter(Boolean);
    if(refs.length) return refs.map(id=>document.getElementById(id)?.textContent||'').join(' ').trim();
    if(e.hasAttribute('aria-label')) return e.getAttribute('aria-label').trim();
    const copy=e.cloneNode(true); copy.querySelectorAll('[aria-hidden="true"],[hidden],script,style').forEach(n=>n.remove());
    return copy.textContent.trim();
  };
  const isMore=b=>!/캘린더:/.test(accessible(b)) && /더보기|더 보기|개 더|more/i.test(accessible(b));
'''
SNAPSHOT_JS = '(() => {' + DOM_HELPERS + r'''
  const labels = [...document.querySelectorAll('[aria-label]')];
  const header = labels.map(accessible).find(s=>/^\d{4}년\s*\d{1,2}월(?:,|$)/.test(s));
  const m = header && header.match(/(\d{4})년\s*(\d{1,2})월/);
  const cells = [...document.querySelectorAll('[role="gridcell"]')].filter(e=>/^일정 (?:\d+개|없음),\s*\d{1,2}월\s*\d{1,2}일/.test(accessible(e)))
    .map(e=>({label:accessible(e), events:[...e.querySelectorAll('button,[role="button"]')]
       .map(accessible).filter(s=>s.includes(', 캘린더: ')),
       more:[...e.querySelectorAll('button,[role="button"]')].some(isMore)}));
  const busy = labels.some(e=>e.getAttribute('role')==='progressbar' && /로드/.test(accessible(e)) && e.getClientRects().length>0 && getComputedStyle(e).visibility!=='hidden');
  return JSON.stringify({month:m ? m[1]+'-'+m[2].padStart(2,'0') : '', cells, busy});
})()'''


class GoogleReader:
    def __init__(self):
        self.window_id = None
        self.tag = 'desktop_calendar_widget_' + uuid.uuid4().hex

    def apple(self, script):
        if sys.platform != 'darwin':
            raise RuntimeError('Google 계정 읽기는 macOS 앱에서 사용할 수 있습니다.')
        result = subprocess.run(['/usr/bin/osascript', '-e', script], capture_output=True, text=True, encoding='utf-8', timeout=45)
        if result.returncode:
            message = result.stderr.strip()
            if '-1743' in message:
                raise RuntimeError('시스템 설정 → 개인정보 보호 및 보안 → 자동화에서 이 앱의 Chrome 제어를 허용해 주세요.')
            if 'JavaScript' in message or 'javascript' in message:
                raise RuntimeError('Chrome 메뉴 → 보기 → 개발자 → Apple Events의 JavaScript 허용을 켜 주세요.')
            raise RuntimeError('Google Chrome 연결 실패: ' + message[:350])
        return result.stdout.strip()

    @staticmethod
    def quote(value):
        # AppleScript literals: JSON's ASCII escapes are not AppleScript escapes.
        return '"' + value.replace('\\', '\\\\').replace('"', '\\"').replace('\r', '\\r').replace('\n', '\\n') + '"'

    def open(self, url, background=False):
        self.close()
        if urlparse(url).hostname != 'calendar.google.com':
            raise ValueError('Google Calendar 공식 주소만 열 수 있습니다.')
        self.window_id = int(self.apple('tell application "Google Chrome"\n'
            'set w to make new window\nset bounds of w to {80, 80, 1560, 1680}\n'+
            ('set minimized of w to true\n' if background else '')+
            'set URL of active tab of w to ' + self.quote(url) + '\nreturn id of w\nend tell'))

    def js(self, source):
        if self.window_id is None:
            raise RuntimeError('Google 읽기 창이 없습니다.')
        # Address an owned ID, never the front/active window belonging to the user.
        return self.apple('tell application "Google Chrome"\nset w to window id ' + str(self.window_id) +
            '\nset u to URL of active tab of w\nif u does not start with "https://calendar.google.com/" then error "Google 로그인을 완료한 뒤 연결 버튼을 다시 누르세요."\n'
            'return execute active tab of w javascript ' + self.quote('if(location.href.includes('+json.dumps(self.tag)+')) window.__desktopCalendarOwner='+json.dumps(self.tag)+';\n'+source) + '\nend tell')

    def close(self):
        if self.window_id is not None:
            try:
                # A window navigated away by its user is no longer ours to close.
                self.apple('tell application "Google Chrome"\nset w to window id ' + str(self.window_id) +
                    '\nset u to URL of active tab of w\nif u contains ' + self.quote(self.tag) + ' then\nclose w\nelse if u starts with "https://calendar.google.com/" then\n'
                    'set ownerTag to execute active tab of w javascript "window.__desktopCalendarOwner || \'\'"\nif ownerTag is ' + self.quote(self.tag) + ' then close w\nend if\nend tell')
            except (RuntimeError, subprocess.TimeoutExpired):
                pass
            self.window_id = None

    def read_month(self, month, sources, email=''):
        successes, errors = {}, []
        for source in sources:
            if not source.get('enabled', True):
                continue
            query = dict(src=source['id'], mode='MONTH', wkst='1', hl='ko', ctz='Asia/Seoul',
                         dates=month.replace('-', '') + '01/' + month.replace('-', '') + '01',
                         widget_refresh=self.tag, showPrint='0', showCalendars='0')
            if email:
                query['authuser'] = email
            try:
                self.open('https://calendar.google.com/calendar/embed?' + urlencode(query),background=True)
                snapshot = None
                for _ in range(50):
                    snapshot = json.loads(self.js(SNAPSHOT_JS))
                    if snapshot.get('month') == month and len(snapshot.get('cells', [])) >= 28 and not snapshot.get('busy'):
                        break
                    time.sleep(0.4)
                # Expand Google's day-overflow popup to avoid dropping hidden events.
                for cell in (snapshot or {}).get('cells',[]):
                    if not cell.get('more'): continue
                    label=json.dumps(cell['label'])
                    clicked=self.js('(() => {'+DOM_HELPERS+'const c=[...document.querySelectorAll(\'[role="gridcell"]\')].find(e=>accessible(e)==='+label+');if(!c)return false;const b=[...c.querySelectorAll(\'button,[role="button"]\')].find(isMore);if(b){b.click();return true;}return false;})()')
                    if clicked!='true': raise RuntimeError('숨겨진 일정을 펼치지 못했습니다. Google 화면 구조를 확인하세요.')
                    time.sleep(0.25)
                    expanded=json.loads(self.js('(() => {'+DOM_HELPERS+'return JSON.stringify([...document.querySelectorAll(\'[role="dialog"] button,[role="dialog"] [role="button"]\')].map(accessible).filter(s=>s.includes(", 캘린더: ")));})()'))
                    if not expanded: raise RuntimeError('숨겨진 일정 목록을 읽지 못했습니다. 기존 캐시를 유지합니다.')
                    cell['events']=list(dict.fromkeys(cell['events']+expanded))
                    self.js('document.activeElement.dispatchEvent(new KeyboardEvent("keydown",{key:"Escape",code:"Escape",keyCode:27,bubbles:true})); "closed"')
                successes[source['id']] = parse_snapshot(snapshot or {}, month, source)
                self.close()
            except Exception as error:
                # Preserve a login/permission page so the user can finish logging in.
                if self.window_id is not None:
                    try: self.apple('tell application "Google Chrome" to set minimized of window id '+str(self.window_id)+' to false')
                    except Exception: pass
                errors.append(source['name'] + ': ' + str(error))
        return dict(month=month, outcomes=successes, errors=errors, at=now().isoformat())

    def discover(self, email=''):
        query = {'hl': 'ko', 'widget_refresh': self.tag}
        if email:
            query['authuser'] = email
        self.open('https://calendar.google.com/calendar/u/0/r/settings?' + urlencode(query))
        items = []
        for _ in range(40):
            items = json.loads(self.js('(() => {for(const e of document.querySelectorAll(\'[role="treeitem"][aria-expanded="false"]\')){if(/캘린더의 설정|Settings for my calendars|Other calendars/i.test(e.getAttribute("aria-label")||e.innerText))e.click();}return JSON.stringify([...document.querySelectorAll(\'[role="treeitem"]\')].map(e=>e.getAttribute("aria-label")||e.innerText.trim()).filter(Boolean));})()'))
            if len(items) > 2:
                break
            time.sleep(0.4)
        found = {}
        for index,label in enumerate(items):
            if not label or re.search('설정$|Settings$|일반|General|단축키|Keyboard|가져오기|Import', label, re.I):
                continue
            # Labels are JSON data, not source code; the fixed program only selects a tree item.
            before=self.js('location.href')
            clicked = self.js('(() => {const e=[...document.querySelectorAll(\'[role="treeitem"]\')]['+str(index)+'];if(e){e.click();return "ok";}return "";})()')
            if not clicked:
                continue
            time.sleep(0.35)
            match=None
            for _ in range(12):
                route = self.js('location.href')
                match = re.search(r'/settings/calendar/([^/?#]+)', route)
                if match and route!=before:
                    break
                time.sleep(0.15)
            if not match or route==before:
                continue
            try:
                encoded = match[1]
                source_id = base64.urlsafe_b64decode(encoded + '=' * (-len(encoded) % 4)).decode()
                if '@' not in source_id:
                    continue
                found[source_id] = dict(id=source_id, name=label, enabled=True, color=PALETTE[len(found) % len(PALETTE)])
            except (ValueError, UnicodeError):
                continue
        if not found:
            raise RuntimeError('캘린더 목록을 읽지 못했습니다. 열린 Chrome에서 Google 로그인을 마친 뒤 다시 연결하세요. 계정 설정의 캘린더 목록도 펼쳐 주세요.')
        self.close()
        return list(found.values())
