"""Owned Chrome profile: explicit login, headless-only background work, local CDP."""
from __future__ import annotations
import json
import os
import signal
from pathlib import Path
import re
import subprocess
import sys
import threading
import time
from urllib.parse import urlencode, urlparse
from platform_mac import app_folder


class LoginRequired(RuntimeError):
    def __init__(self):
        super().__init__('설정에서 Google 계정을 연결해 주세요. 로그인 창을 닫은 뒤 연결 버튼을 다시 누르세요.')


def official_url(url):
    parsed = urlparse(url)
    return parsed.scheme == 'https' and parsed.hostname == 'calendar.google.com' and parsed.port in (None, 443)


def chrome_path():
    for path in (Path('/Applications/Google Chrome.app/Contents/MacOS/Google Chrome'),
                 Path.home() / 'Applications/Google Chrome.app/Contents/MacOS/Google Chrome'):
        if path.is_file():
            return str(path)
    raise RuntimeError('Google Chrome을 응용 프로그램에 설치해 주세요.')


class GoogleBrowser:
    def __init__(self, folder=None, require_login=True, executable=None):
        self.profile = (Path(folder) if folder else app_folder() / 'google-profile').resolve()
        self.require_login = require_login
        self.executable = executable
        self.gate = threading.RLock()
        self.process = self.login_process = self.socket = None
        self.session = ''
        self.sequence = 0

    @property
    def login_started(self):
        return (self.profile / 'widget-login-started').is_file()

    @staticmethod
    def running(process):
        return process is not None and process.poll() is None

    def arguments(self, headless, url):
        if not official_url(url) and (headless or not url.startswith('https://accounts.google.com/ServiceLogin?')):
            raise ValueError('Google Calendar 공식 주소만 열 수 있습니다.')
        args = [self.executable or chrome_path(), '--user-data-dir=' + str(self.profile),
                '--no-first-run', '--no-default-browser-check', '--disable-background-mode']
        args += (['--headless=new', '--remote-debugging-address=127.0.0.1',
                  '--remote-debugging-port=0', '--window-size=2000,3600'] if headless else ['--new-window'])
        return args + [url]

    def launch(self, headless, url):
        options = dict(stdin=subprocess.DEVNULL, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        if sys.platform == 'win32':
            options['creationflags'] = subprocess.CREATE_NO_WINDOW if headless else 0
        else:
            options['start_new_session'] = True
        if headless:
            command = [sys.executable]
            if not getattr(sys,'frozen',False): command += [str(Path(__file__).resolve().parent/'main.py')]
            command += ['--google-background',str(self.profile),self.executable or chrome_path()]
            options['stdin'] = subprocess.PIPE
            return subprocess.Popen(command,**options)
        return subprocess.Popen(self.arguments(False,url),**options)

    def open_login(self, email=''):
        with self.gate:
            if self.running(self.login_process):
                raise RuntimeError('열린 전용 Google 창에서 로그인을 마치고 창을 닫아 주세요.')
            self.close()
            self.require_profile_available()
            self.profile.mkdir(parents=True, exist_ok=True, mode=0o700)
            query = {'hl': 'ko'}
            if email:
                query['authuser'] = email
            self.login_process = self.launch(False, 'https://accounts.google.com/ServiceLogin?' + urlencode({'hl':'ko','service':'cl','continue':'https://calendar.google.com/calendar/u/0/r/settings?' + urlencode(query)}))
            (self.profile / 'widget-login-started').write_text('1', encoding='ascii')

    def ensure(self):
        if self.running(self.login_process):
            raise RuntimeError('전용 Google 로그인 창을 닫은 뒤 다시 연결해 주세요.')
        if self.require_login and not self.login_started:
            raise LoginRequired()
        if self.running(self.process) and self.socket and self.session:
            return
        self.close()
        self.require_profile_available()
        self.profile.mkdir(parents=True, exist_ok=True, mode=0o700)
        active = self.profile / 'DevToolsActivePort'
        active.unlink(missing_ok=True)
        self.process = self.launch(True, 'https://calendar.google.com/calendar/u/0/r/settings?hl=ko')
        try:
            endpoint = ''
            end = time.monotonic() + 18
            while time.monotonic() < end and self.running(self.process):
                try:
                    lines = active.read_text(encoding='ascii').splitlines()
                    if len(lines) >= 2 and lines[0].isdigit() and 0 < int(lines[0]) <= 65535 and re.fullmatch(r'/devtools/browser/[A-Za-z0-9-]+', lines[1]):
                        endpoint = 'ws://127.0.0.1:' + lines[0] + lines[1]
                        break
                except (OSError, UnicodeError):
                    pass
                time.sleep(0.1)
            if not endpoint:
                raise RuntimeError('전용 Google 연결을 시작하지 못했습니다. 로그인 창이 열려 있다면 닫고 다시 시도하세요.')
            from websocket import create_connection
            self.socket = create_connection(endpoint, timeout=15, suppress_origin=True, http_no_proxy=['127.0.0.1'])
            target = self.call('Target.createTarget', {'url': 'about:blank'}, page=False)['targetId']
            self.session = self.call('Target.attachToTarget', {'targetId': target, 'flatten': True}, page=False)['sessionId']
            self.call('Page.enable')
            self.call('Network.enable')
            self.call('Network.setCacheDisabled', {'cacheDisabled': True})
        except Exception:
            self.close()
            raise

    def require_profile_available(self):
        # A login window can outlive the widget. Never let Chrome forward a
        # background request into that visible instance after an app restart.
        import psutil
        user = psutil.Process().username()
        for process in psutil.process_iter(['name','username']):
            if 'chrome' not in (process.info.get('name') or '').lower(): continue
            if process.info.get('username') not in (None,user): continue
            try:
                args = process.cmdline()
                for index,arg in enumerate(args):
                    value = arg[16:] if arg.startswith('--user-data-dir=') else args[index+1] if arg=='--user-data-dir' and index+1<len(args) else None
                    if value and Path(value).resolve()==self.profile:
                        raise RuntimeError('전용 Google Chrome 창을 닫은 뒤 다시 연결하거나 새로고침해 주세요.')
            except psutil.NoSuchProcess: continue
            except psutil.AccessDenied: raise RuntimeError('Chrome 연결 상태를 확인하지 못했습니다. 기존 데이터를 유지합니다.')

    def call(self, method, params=None, page=True):
        self.sequence += 1
        request = dict(id=self.sequence, method=method, params=params or {})
        if page:
            request['sessionId'] = self.session
        self.socket.send(json.dumps(request))
        end = time.monotonic() + 15
        while time.monotonic() < end:
            self.socket.settimeout(max(0.05, end - time.monotonic()))
            raw = self.socket.recv()
            if not raw or len(raw) > 4 * 1024 * 1024:
                raise RuntimeError('Google 읽기 연결이 종료되었거나 응답이 너무 큽니다.')
            response = json.loads(raw)
            if response.get('id') != request['id']:
                continue
            if 'error' in response:
                raise RuntimeError('Google 읽기 연결이 응답하지 않습니다. 다시 새로고침해 주세요.')
            return response['result']
        raise TimeoutError('Google 읽기 연결 시간이 초과되었습니다.')

    def navigate(self, url):
        if not official_url(url):
            raise ValueError('Google Calendar 공식 주소만 열 수 있습니다.')
        self.ensure()
        try:
            result = self.call('Page.navigate', {'url': url})
            if result.get('errorText'):
                raise RuntimeError('Google Calendar에 연결하지 못했습니다. 네트워크를 확인하세요.')
            loader = result.get('loaderId')
            end = time.monotonic() + 15
            while time.monotonic() < end:
                frame = self.call('Page.getFrameTree')['frameTree']['frame']
                current = frame.get('url','')
                if current.startswith(('https://accounts.google.com/','https://workspace.google.com/')): raise LoginRequired()
                if official_url(current) and (not loader or frame.get('loaderId') == loader): return
                time.sleep(0.1)
            raise RuntimeError('Google 화면 연결 시간이 초과되었습니다. 기존 데이터를 유지합니다.')
        except Exception:
            self.close()
            raise

    def evaluate(self, expression):
        try:
            response = self.call('Runtime.evaluate', dict(expression="(() => {if(location.origin !== 'https://calendar.google.com')return {widgetLoginRequired:true};return (" + expression + ');})()', returnByValue=True, awaitPromise=True))
            if 'exceptionDetails' in response:
                raise RuntimeError('Google 화면 구조를 읽지 못했습니다. 기존 데이터를 유지합니다.')
            value = response.get('result', {}).get('value')
            if isinstance(value, dict) and value.get('widgetLoginRequired'):
                raise LoginRequired()
            return value
        except LoginRequired:
            raise
        except Exception:
            self.close()
            raise

    def close(self):
        with self.gate:
            if self.socket:
                try:
                    self.call('Browser.close', page=False)
                except Exception:
                    pass
                self.socket.close()
                self.socket = None
            if self.process:
                if self.process.stdin: self.process.stdin.close()
                try:
                    self.process.wait(timeout=3)
                except subprocess.TimeoutExpired:
                    self.process.kill()
                    self.process.wait(timeout=3)
                self.process = None
            self.session = ''


def background_main(profile, executable):
    """No GUI imports: terminate only our headless Chrome when the parent dies."""
    browser = GoogleBrowser(profile,require_login=False,executable=executable)
    options = dict(stdin=subprocess.DEVNULL,stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL)
    if sys.platform == 'win32': options['creationflags'] = subprocess.CREATE_NO_WINDOW
    else: options['start_new_session'] = True
    process = subprocess.Popen(browser.arguments(True,'https://calendar.google.com/calendar/u/0/r/settings?hl=ko'),**options)
    def parent_closed():
        try: sys.stdin.buffer.read(1)
        except (OSError,ValueError): pass
        if process.poll() is None:
            try:
                if sys.platform == 'win32': process.terminate()
                else: os.killpg(process.pid,signal.SIGTERM)
                process.wait(timeout=3)
            except subprocess.TimeoutExpired:
                if sys.platform == 'win32': process.kill()
                else: os.killpg(process.pid,signal.SIGKILL)
            except ProcessLookupError: pass
    threading.Thread(target=parent_closed,daemon=True).start()
    return process.wait()
