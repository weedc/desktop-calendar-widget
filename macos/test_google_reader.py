"""Retained Chrome window lifecycle; no credentials or real calendars in fixtures."""
import base64
import copy
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch

from google_browser import GoogleBrowser, LoginRequired, official_url, quote
from google_reader import GoogleReader, SCRIPTS


class ScriptedChrome(GoogleBrowser):
    def __init__(self):
        super().__init__(); self.scripts=[]; self.windows=set(); self.running=True
        self.next_id=100; self.taken=False; self.fail_create=False; self.login=False; self.fail_evaluate=False
    def apple(self,script):
        self.scripts.append(script)
        if 'make new window' in script:
            if self.fail_create: raise RuntimeError('creation failed')
            self.running=True; self.next_id+=1; self.windows.add(self.next_id); return str(self.next_id)
        if not self.running or self.window_id not in self.windows or self.taken: raise RuntimeError('missing or unowned')
        if 'close w' in script: self.windows.discard(self.window_id)
        if 'return execute' in script:
            if self.fail_evaluate: raise RuntimeError('permission denied')
            if self.login: return 'WIDGET_LOGIN_REQUIRED'
            return json.dumps({'ok':True})
        return ''
    @property
    def creations(self): return sum('make new window' in script for script in self.scripts)


class FixtureReader(GoogleReader):
    def __init__(self):
        super().__init__(ScriptedChrome()); self.calls=[]; self.expanded=[]; self.dismissed=False
        self.snapshot=dict(month='2026-11',busy=False,cells=[dict(label=f'일정 없음, 11월 {day}일',events=[],more=False) for day in range(1,29)])
    def script(self,name,value=None):
        self.calls.append((name,value))
        if name=='snapshot': self.dismissed=False; return copy.deepcopy(self.snapshot)
        if name=='expand': self.dismissed=False; return True
        if name=='expanded': return [] if self.dismissed else self.expanded
        if name=='dismiss': self.dismissed=True; return True
        raise AssertionError(name)


class ReaderTests(unittest.TestCase):
    sources=[dict(id='demo@example.invalid',name='가상 캘린더',enabled=True,color='#487F91')]
    def setUp(self): patch('google_reader.time.sleep').start()
    def tearDown(self): patch.stopall()
    def test_cold_refresh_never_opens_a_window_before_explicit_connection(self):
        reader=FixtureReader()
        for _ in range(3): self.assertTrue(reader.read_month('2026-11',self.sources)['errors'])
        self.assertEqual(reader.browser.creations,0); self.assertEqual(reader.browser.phase,'new')
        self.assertFalse(reader.read_month('2026-11',self.sources,allow_initial=True)['errors'])
        self.assertEqual(reader.browser.creations,1)
    def test_refresh_and_multiple_month_requests_keep_one_hidden_window(self):
        reader=FixtureReader()
        self.assertEqual(reader.read_month('2026-11',self.sources,allow_initial=True)['errors'],[])
        for _ in range(3): self.assertEqual(reader.read_month('2026-11',self.sources)['errors'],[])
        self.assertEqual(reader.browser.creations,1); self.assertEqual(reader.browser.phase,'hidden'); self.assertEqual(reader.browser.window_id,101)
        reads=reader.browser.scripts[2:]
        self.assertFalse(any('make new window' in s or 'set visible of w to true' in s or 'activate' in s for s in reads))
    def test_multiple_calendars_share_the_same_reader(self):
        reader=FixtureReader(); sources=self.sources+[{**self.sources[0],'id':'other@example.invalid'}]
        self.assertEqual(len(reader.read_month('2026-11',sources,allow_initial=True)['outcomes']),2)
        self.assertEqual(reader.browser.creations,1)
    def test_disabled_sources_never_create_a_window(self):
        reader=FixtureReader(); reader.read_month('2026-11',[{**self.sources[0],'enabled':False}],allow_initial=True); self.assertEqual(reader.browser.creations,0)
    def test_missing_window_is_never_recreated_by_repeated_reads(self):
        reader=FixtureReader(); reader.read_month('2026-11',self.sources,allow_initial=True); reader.browser.windows.clear()
        for _ in range(3):
            result=reader.read_month('2026-11',self.sources,allow_initial=True); self.assertTrue(result['errors']); self.assertEqual(result['outcomes'],{})
        self.assertEqual(reader.browser.creations,1)
    def test_closed_chrome_is_not_automatically_relaunched(self):
        reader=FixtureReader(); reader.read_month('2026-11',self.sources,allow_initial=True); reader.browser.running=False
        for _ in range(3): self.assertTrue(reader.read_month('2026-11',self.sources,allow_initial=True)['errors'])
        self.assertEqual(reader.browser.creations,1); self.assertFalse(reader.browser.running)
    def test_user_taken_window_is_not_navigated_restored_or_closed(self):
        reader=FixtureReader(); reader.read_month('2026-11',self.sources,allow_initial=True); reader.browser.taken=True
        self.assertTrue(reader.read_month('2026-11',self.sources,allow_initial=True)['errors']); reader.close()
        self.assertIn(101,reader.browser.windows); self.assertEqual(reader.browser.creations,1)
    def test_initial_failure_cannot_create_popup_on_next_poll(self):
        reader=FixtureReader(); reader.browser.fail_create=True
        for _ in range(3): self.assertTrue(reader.read_month('2026-11',self.sources,allow_initial=True)['errors'])
        self.assertEqual(reader.browser.creations,1)
    def test_incomplete_page_is_hidden_and_not_accepted_as_empty_success(self):
        reader=FixtureReader(); reader.snapshot['cells']=[]
        result=reader.read_month('2026-11',self.sources,allow_initial=True)
        self.assertTrue(result['errors']); self.assertEqual(result['outcomes'],{}); self.assertEqual(reader.browser.phase,'lost')
        reader.read_month('2026-11',self.sources,allow_initial=True); self.assertEqual(reader.browser.creations,1)
    def test_overflow_read_keeps_stable_ids_and_closes_popup(self):
        reader=FixtureReader(); reader.snapshot['cells'][0].update(label='일정 1개, 11월 1일',more=True)
        reader.expanded=['오후 2:30, 가상 미팅, 캘린더: 예시, 위치: 회의실, 2026년 11월 1일']
        result=reader.read_month('2026-11',self.sources,allow_initial=True); self.assertFalse(result['errors'])
        event=result['outcomes'][self.sources[0]['id']][0]
        self.assertEqual(event['uid'],'DBD6656405DA940F708D'); self.assertEqual(event['sort'],870); self.assertTrue(reader.dismissed)
    def test_failed_overflow_does_not_replace_cache_with_partial_data(self):
        reader=FixtureReader(); reader.snapshot['cells'][0].update(label='일정 2개, 11월 1일',more=True)
        result=reader.read_month('2026-11',self.sources,allow_initial=True); self.assertTrue(result['errors']); self.assertEqual(result['outcomes'],{}); self.assertTrue(reader.dismissed)
    def test_cancel_does_not_start_another_connection(self):
        reader=FixtureReader(); reader.cancel(); self.assertEqual(reader.read_month('2026-11',self.sources,allow_initial=True)['outcomes'],{}); self.assertEqual(reader.browser.creations,0)
    def test_only_explicit_reset_permits_a_new_first_connection(self):
        reader=FixtureReader(); reader.read_month('2026-11',self.sources,allow_initial=True); original=reader.browser
        with patch('google_reader.GoogleBrowser',ScriptedChrome): reader.reset()
        reader.read_month('2026-11',self.sources,allow_initial=True)
        self.assertEqual(original.phase,'closed'); self.assertEqual(reader.browser.creations,1)


class BrowserTests(unittest.TestCase):
    def test_default_navigation_cannot_create_a_first_window(self):
        browser=ScriptedChrome()
        for _ in range(3):
            with self.assertRaises(RuntimeError): browser.navigate('https://calendar.google.com/calendar/embed?widget_reader='+browser.tag)
        self.assertEqual(browser.creations,0); self.assertEqual(browser.phase,'new')
    def test_background_scripts_guard_against_launching_a_stopped_chrome(self):
        browser=ScriptedChrome(); browser.navigate('https://calendar.google.com/calendar/embed?widget_reader='+browser.tag,allow_initial=True); browser.hide()
        browser.navigate('https://calendar.google.com/calendar/embed?widget_reader='+browser.tag)
        self.assertTrue(browser.scripts[-1].startswith('if not (application "Google Chrome" is running)'))
        self.assertNotIn('make new window',browser.scripts[-1]); self.assertNotIn('set visible of w to true',browser.scripts[-1])
    def test_login_read_does_not_inspect_credentials(self):
        browser=ScriptedChrome(); browser.navigate('https://accounts.google.com/ServiceLogin?continue='+browser.tag,allow_initial=True); browser.login=True
        with self.assertRaises(LoginRequired): browser.evaluate('1')
        self.assertIn('if u does not start with "https://calendar.google.com/" then return',browser.scripts[-1])
        self.assertEqual(browser.creations,1)
    def test_browser_uses_existing_chrome_without_debug_ports_or_private_profiles(self):
        browser=ScriptedChrome(); browser.navigate('https://calendar.google.com/calendar/embed?widget_reader='+browser.tag,allow_initial=True)
        source='\n'.join(browser.scripts)
        for unwanted in ('remote-debugging','user-data-dir','headless','minimized of w to false'): self.assertNotIn(unwanted,source)
    def test_invalid_origin_does_not_touch_an_owned_window(self):
        browser=ScriptedChrome()
        with self.assertRaises(ValueError): browser.navigate('https://calendar.google.com.example.invalid/',allow_initial=True)
        self.assertEqual(browser.scripts,[])
    def test_closed_session_cannot_reopen(self):
        browser=ScriptedChrome(); browser.navigate('https://calendar.google.com/calendar/embed?widget_reader='+browser.tag,allow_initial=True); browser.hide(); browser.close()
        with self.assertRaises(RuntimeError): browser.navigate('https://calendar.google.com/calendar/embed',allow_initial=True)
        self.assertEqual(browser.creations,1)
    def test_window_ownership_checked_before_navigation_and_close(self):
        browser=ScriptedChrome(); browser.navigate('https://calendar.google.com/calendar/embed?widget_reader='+browser.tag,allow_initial=True); browser.hide(); browser.close()
        for script in browser.scripts[1:]: self.assertIn('WIDGET_WINDOW_TAKEN_OVER',script)
    def test_apple_script_quoting_preserves_unicode_as_data(self):
        value='한글 "문장"\n끝\\기호'
        escaped=quote(value); self.assertIn('한글',escaped); self.assertNotIn('\n',escaped); self.assertTrue(escaped.startswith('"') and escaped.endswith('"'))
    def test_script_timeout_is_reported_without_private_script_contents(self):
        browser=GoogleBrowser()
        with patch('google_browser.sys.platform','darwin'), patch('google_browser.subprocess.run',side_effect=subprocess.TimeoutExpired(['osascript','private-script'],35)):
            with self.assertRaises(RuntimeError) as caught: browser.apple('private-script')
        self.assertNotIn('private-script',str(caught.exception)); self.assertIn('자동화',str(caught.exception))

    def test_shared_dom_programs_are_synchronous_for_apple_events(self):
        self.assertEqual(set(SCRIPTS),{'snapshot','expand','expanded','dismiss','catalog','select','route'})
        for program in SCRIPTS.values(): self.assertNotIn('async ',program)


@unittest.skipUnless(sys.platform=='darwin' and Path('/Applications/Google Chrome.app').exists(),'requires native Mac with Chrome')
class NativeMacTests(unittest.TestCase):
    def test_b_compile_real_chrome_scripts(self):
        browser=ScriptedChrome(); browser.navigate('https://calendar.google.com/calendar/embed?widget_reader='+browser.tag,allow_initial=True)
        browser.evaluate('({value:"가상 데이터"})'); browser.hide(); browser.navigate('https://calendar.google.com/calendar/embed?widget_reader='+browser.tag,allow_initial=True); browser.close()
        with tempfile.TemporaryDirectory() as folder:
            for index,script in enumerate(browser.scripts):
                result=subprocess.run(['/usr/bin/osacompile','-o',str(Path(folder)/f'check{index}.scpt')],input=script,text=True,encoding='utf-8',capture_output=True)
                self.assertEqual(result.returncode,0,result.stderr)
    def test_a_read_failure_does_not_launch_stopped_chrome(self):
        probe=lambda:subprocess.run(['/usr/bin/osascript','-e','application "Google Chrome" is running'],capture_output=True,text=True,check=True).stdout.strip()
        if probe()!='false': self.skipTest('Chrome already running; preserve its windows')
        browser=GoogleBrowser(); browser.phase='hidden'; browser.window_id=999999
        with self.assertRaises(RuntimeError): browser.navigate('https://calendar.google.com/calendar/embed?widget_reader='+browser.tag,allow_initial=True)
        self.assertEqual(probe(),'false')

    def test_c_real_hidden_window_reuses_without_visibility_or_recreation(self):
        import os,time,threading,signal
        from urllib.parse import urlencode
        if os.environ.get('CALENDAR_WIDGET_NATIVE_CHROME_TEST')!='1': self.skipTest('native Chrome window test is opt-in')
        state=subprocess.run(['/usr/bin/osascript','-e','application "Google Chrome" is running'],capture_output=True,text=True,check=True).stdout.strip()
        if state!='false': self.skipTest('preserve an existing Chrome session')
        import Quartz,AppKit
        browser=GoogleBrowser(); stop=threading.Event(); exposed=[]; sampler=None
        with tempfile.TemporaryDirectory(prefix='calendar-native-chrome-') as folder:
            application=None; process_id=None; launcher=None
            def onscreen():
                return {w[Quartz.kCGWindowNumber] for w in Quartz.CGWindowListCopyWindowInfo(Quartz.kCGWindowListOptionOnScreenOnly,Quartz.kCGNullWindowID) if w.get(Quartz.kCGWindowOwnerPID)==process_id and w.get(Quartz.kCGWindowLayer)==0}
            def url(month):
                return 'https://calendar.google.com/calendar/embed?'+urlencode(dict(src='en.usa#holiday@group.v.calendar.google.com',hl='ko',dates=month+'01/'+month+'28',widget_reader=browser.tag))
            try:
                print('Native Chrome probe: launching an empty disposable profile',flush=True)
                launcher=subprocess.Popen(['/usr/bin/open','-n','-a','/Applications/Google Chrome.app','--args','--no-first-run','--no-default-browser-check','--disable-background-mode','--user-data-dir='+folder,'about:blank'],stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL)
                for _ in range(100):
                    apps=AppKit.NSRunningApplication.runningApplicationsWithBundleIdentifier_('com.google.Chrome')
                    if apps: application=apps[0]; process_id=application.processIdentifier(); break
                    time.sleep(.1)
                if application is None: self.skipTest('runner has no usable Chrome GUI session; no permission was changed')
                # A freshly installed Chrome may still be undergoing macOS's
                # normal first-launch verification. Wait without changing any
                # Gatekeeper, quarantine, or Automation settings.
                for _ in range(1800):
                    if onscreen(): break
                    time.sleep(.1)
                if not onscreen(): self.skipTest('runner did not expose a Chrome GUI window; no permission was changed')
                print('Native Chrome probe: initial frame exists; checking Apple Events',flush=True)
                try:
                    browser.navigate(url('202611'),allow_initial=True)
                    ready=False
                    deadline=time.monotonic()+10
                    while time.monotonic()<deadline:
                        try:
                            browser.apple(browser.scope()+'return id of w\nend tell'); ready=True; break
                        except RuntimeError as error:
                            if '자동화' in str(error) or 'JavaScript' in str(error): raise
                            time.sleep(.1)
                    self.assertTrue(ready,'initial Chrome document did not become ready')
                    browser.hide()
                except RuntimeError as error:
                    if '자동화' in str(error): self.skipTest('runner Chrome Automation was denied or did not respond; no permission was changed')
                    raise
                owned=browser.window_id
                count=browser.apple('tell application "Google Chrome" to return count of windows')
                time.sleep(.3)
                baseline=onscreen()
                def sample():
                    while not stop.is_set():
                        if onscreen()-baseline: exposed.append(True)
                        time.sleep(.01)
                sampler=threading.Thread(target=sample,daemon=True); sampler.start()
                for month in ('202611','202612','202611'):
                    browser.navigate(url(month)); time.sleep(.5)
                    self.assertEqual(browser.window_id,owned)
                    self.assertEqual(browser.apple(browser.scope()+'return visible of w\nend tell'),'false')
                    self.assertEqual(browser.apple('tell application "Google Chrome" to return count of windows'),count)
                stop.set();sampler.join();sampler=None
                self.assertFalse(exposed,'a hidden Chrome reader became visible during navigation')
                browser.apple(browser.scope()+'close w\nend tell')
                remaining=browser.apple('tell application "Google Chrome" to return count of windows')
                for _ in range(3):
                    with self.assertRaises(RuntimeError): browser.navigate(url('202611'),allow_initial=True)
                self.assertEqual(browser.apple('tell application "Google Chrome" to return count of windows'),remaining)
            except Exception:
                if os.environ.get('GITHUB_ACTIONS')=='true':
                    output=Path('build/qa'); output.mkdir(parents=True,exist_ok=True)
                    subprocess.run(['/usr/sbin/screencapture','-x',str(output/'native-window-debug.png')],capture_output=True,timeout=8)
                    names={w.get(Quartz.kCGWindowOwnerName,'') for w in Quartz.CGWindowListCopyWindowInfo(Quartz.kCGWindowListOptionOnScreenOnly,Quartz.kCGNullWindowID)}
                    print('Native Chrome probe visible owners:',sorted(names))
                raise
            finally:
                stop.set()
                if sampler: sampler.join(timeout=3)
                browser.close()
                if launcher and launcher.poll() is None:
                    launcher.terminate()
                    try: launcher.wait(timeout=3)
                    except subprocess.TimeoutExpired: launcher.kill(); launcher.wait(timeout=3)
                if application is not None and not application.isTerminated():
                    try: os.kill(process_id,signal.SIGTERM)
                    except ProcessLookupError: pass
                    for _ in range(50):
                        if application.isTerminated(): break
                        time.sleep(.1)
                    if not application.isTerminated():
                        try: os.kill(process_id,signal.SIGKILL)
                        except ProcessLookupError: pass


if __name__=='__main__': unittest.main()
