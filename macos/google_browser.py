"""Reuse one owned Chrome window and the user's existing browser login.

Only initial connection may create/show a window. Missing windows and reading
errors never relaunch Chrome, restore a window, or fall back to a new one.
"""
from __future__ import annotations
import json
import subprocess
import sys
import threading
import uuid
from urllib.parse import urlparse


class LoginRequired(RuntimeError):
    def __init__(self):
        super().__init__('열린 Chrome에서 Google 로그인을 마쳐 주세요. 연결되면 자동으로 돌아옵니다.')


def official_url(url):
    parsed=urlparse(url)
    return parsed.scheme=='https' and parsed.hostname=='calendar.google.com' and parsed.port in (None,443)


def quote(value):
    return '"'+value.replace('\\','\\\\').replace('"','\\"').replace('\r','\\r').replace('\n','\\n')+'"'


class GoogleBrowser:
    def __init__(self):
        self.gate=threading.RLock()
        self.window_id=None
        self.tag='desktop_calendar_widget_'+uuid.uuid4().hex
        self.phase='new'

    def apple(self,script):
        if sys.platform!='darwin': raise RuntimeError('기존 Chrome 연결은 macOS 앱에서 사용할 수 있습니다.')
        try:
            result=subprocess.run(['/usr/bin/osascript','-e',script],capture_output=True,text=True,encoding='utf-8',timeout=35)
        except subprocess.TimeoutExpired:
            raise RuntimeError('Chrome 응답 시간이 초과되었습니다. 저장된 일정을 유지합니다. Chrome의 자동화 권한을 확인한 뒤 설정에서 다시 연결해 주세요.') from None
        if result.returncode:
            if '-1743' in result.stderr:
                raise RuntimeError('시스템 설정 → 개인정보 보호 및 보안 → 자동화에서 캘린더의 Chrome 제어를 허용해 주세요.')
            if 'JavaScript' in result.stderr or 'javascript' in result.stderr:
                raise RuntimeError('Chrome → 보기 → 개발자 → Apple Events의 JavaScript 허용을 켜 주세요.')
            raise RuntimeError('Chrome 읽기 연결이 끊겼습니다. 저장된 일정을 유지합니다. 설정에서 다시 연결해 주세요.')
        return result.stdout.strip()

    def scope(self):
        if self.window_id is None or self.phase in ('lost','closed'):
            raise RuntimeError('Chrome 읽기 연결이 없습니다. 설정에서 다시 연결해 주세요.')
        return ('if not (application "Google Chrome" is running) then error "WIDGET_CONNECTION_LOST"\n'
            'tell application "Google Chrome"\n'
            'if not (exists window id '+str(self.window_id)+') then error "WIDGET_CONNECTION_LOST"\n'
            'set w to window id '+str(self.window_id)+'\nset u to URL of active tab of w\n'
            'set owned to u contains '+quote(self.tag)+'\n'
            'if not owned and u starts with "https://calendar.google.com/" then\n'
            'set owned to (execute active tab of w javascript "window.__desktopCalendarOwner || \'\'") is '+quote(self.tag)+'\nend if\n'
            'if not owned then error "WIDGET_WINDOW_TAKEN_OVER"\n')

    def navigate(self,url):
        if not official_url(url) and not (self.phase=='new' and url.startswith('https://accounts.google.com/ServiceLogin?')):
            raise ValueError('Google Calendar 공식 주소만 열 수 있습니다.')
        with self.gate:
            if self.phase=='new':
                self.phase='connecting'
                try:
                    self.window_id=int(self.apple('tell application "Google Chrome"\nset w to make new window\n'
                        'set bounds of w to {80,80,2080,3680}\nset URL of active tab of w to '+quote(url)+'\nreturn id of w\nend tell'))
                except Exception:
                    self.phase='lost'; raise
                return
            if self.phase!='hidden':
                raise RuntimeError('Chrome 읽기 연결이 끊겼습니다. 설정에서 다시 연결해 주세요.')
            try:
                self.apple(self.scope()+'set visible of w to false\nset URL of active tab of w to '+quote(url)+'\nend tell')
            except Exception:
                self.phase='lost'; raise

    def evaluate(self,expression):
        with self.gate:
            try:
                code="if(location.href.includes("+json.dumps(self.tag)+"))window.__desktopCalendarOwner="+json.dumps(self.tag)+";JSON.stringify(("+expression+"))"
                text=self.apple(self.scope()+'if u does not start with "https://calendar.google.com/" then return "WIDGET_LOGIN_REQUIRED"\n'
                    'return execute active tab of w javascript '+quote(code)+'\nend tell')
                if text=='WIDGET_LOGIN_REQUIRED': raise LoginRequired()
                return json.loads(text)
            except LoginRequired: raise
            except Exception:
                self.phase='lost'; raise

    def hide(self):
        with self.gate:
            previous=self.phase
            if previous=='lost' and self.window_id is not None: self.phase='connecting'
            try:
                self.apple(self.scope()+'set visible of w to false\nend tell')
                self.phase='lost' if previous=='lost' else 'hidden'
            except Exception:
                self.phase='lost'; raise

    def close(self):
        with self.gate:
            if self.window_id is not None:
                try:
                    # Never launch a stopped Chrome or close an unrelated window.
                    previous=self.phase
                    if previous=='lost': self.phase='hidden'
                    self.apple(self.scope()+'close w\nend tell')
                except Exception: pass
            self.window_id=None; self.phase='closed'
