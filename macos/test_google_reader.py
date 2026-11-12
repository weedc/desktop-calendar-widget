"""Connection, refresh and failure-policy tests without credentials or Google traffic."""
import base64
import copy
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import threading
import unittest
from unittest.mock import Mock, patch

from google_browser import GoogleBrowser, LoginRequired, official_url
from google_reader import GoogleReader, SCRIPTS


class FakeBrowser:
    def __init__(self):
        self.gate=threading.RLock(); self.urls=[]; self.error=None; self.login_calls=0
    def navigate(self,url):
        self.urls.append(url)
        if self.error: raise self.error
    def open_login(self,email=''): self.login_calls+=1
    def close(self): pass


class FixtureReader(GoogleReader):
    def __init__(self):
        super().__init__(FakeBrowser())
        self.snapshot=dict(month='2026-11',busy=False,cells=[dict(label=f'일정 없음, 11월 {day}일',events=[],more=False) for day in range(1,29)])
        self.calls=[]; self.expanded=[]
    def script(self,name,value=None):
        self.calls.append((name,value))
        if name=='snapshot': return copy.deepcopy(self.snapshot)
        if name=='expand': return True
        if name=='expanded': return self.expanded
        if name=='dismiss': return True
        raise AssertionError(name)


class ReaderTests(unittest.TestCase):
    sources=[dict(id='demo@example.invalid',name='가상 캘린더',enabled=True,color='#487F91')]
    def setUp(self): self.sleep=patch('google_reader.time.sleep').start()
    def tearDown(self): patch.stopall()

    def test_refresh_reads_new_documents_without_starting_login(self):
        reader=FixtureReader()
        for _ in range(3): self.assertEqual(reader.read_month('2026-11',self.sources)['errors'],[])
        self.assertEqual(len(set(reader.browser.urls)),3)
        self.assertEqual(reader.browser.login_calls,0)

    def test_multiple_sources_share_one_browser(self):
        reader=FixtureReader(); sources=self.sources+[{**self.sources[0],'id':'other@example.invalid'}]
        self.assertEqual(len(reader.read_month('2026-11',sources)['outcomes']),2)
        self.assertEqual(len(reader.browser.urls),2)

    def test_disabled_sources_never_start_a_read(self):
        reader=FixtureReader(); self.assertEqual(reader.read_month('2026-11',[{**self.sources[0],'enabled':False}])['outcomes'],{})
        self.assertEqual(reader.browser.urls,[])

    def test_expired_login_never_opens_a_window_or_returns_empty_success(self):
        reader=FixtureReader(); reader.browser.error=LoginRequired()
        for _ in range(2):
            result=reader.read_month('2026-11',self.sources)
            self.assertTrue(result['errors']); self.assertEqual(result['outcomes'],{})
        self.assertEqual(reader.browser.login_calls,0)

    def test_network_error_is_failure_and_a_later_read_can_recover(self):
        reader=FixtureReader(); reader.browser.error=OSError('offline')
        self.assertTrue(reader.read_month('2026-11',self.sources)['errors'])
        reader.browser.error=None
        self.assertEqual(reader.read_month('2026-11',self.sources)['errors'],[])
        self.assertEqual(reader.browser.login_calls,0)

    def test_incomplete_page_does_not_clear_cached_events(self):
        reader=FixtureReader(); reader.snapshot['cells']=[]
        result=reader.read_month('2026-11',self.sources)
        self.assertEqual(result['outcomes'],{}); self.assertTrue(result['errors'])

    def test_overflow_events_are_expanded_before_accepting_snapshot(self):
        reader=FixtureReader(); reader.snapshot['cells'][0].update(label='일정 1개, 11월 1일',more=True)
        reader.expanded=['오후 2:30, 가상 미팅, 캘린더: 예시, 위치: 회의실, 2026년 11월 1일']
        result=reader.read_month('2026-11',self.sources)
        self.assertFalse(result['errors']); event=result['outcomes'][self.sources[0]['id']][0]
        self.assertEqual(event['uid'],'DBD6656405DA940F708D'); self.assertEqual(event['sort'],870)
        self.assertIn(('dismiss',None),reader.calls)

    def test_missing_overflow_is_an_error_and_dialog_is_dismissed(self):
        reader=FixtureReader(); reader.snapshot['cells'][0].update(label='일정 2개, 11월 1일',more=True)
        result=reader.read_month('2026-11',self.sources)
        self.assertTrue(result['errors']); self.assertEqual(result['outcomes'],{})
        self.assertIn(('dismiss',None),reader.calls)

    def test_duplicate_calendar_names_keep_distinct_row_indices(self):
        reader=GoogleReader(FakeBrowser()); selected=[]
        def script(name,value=None):
            if name=='catalog': return dict(email='demo@example.invalid',rows=[dict(label='동명 캘린더',index=2),dict(label='동명 캘린더',index=3)])
            if name=='select': selected.append(value['index']); return True
            if name=='route':
                if not selected: return 'https://calendar.google.com/calendar/u/0/r/settings'
                encoded=base64.urlsafe_b64encode(f'calendar{selected[-1]}@example.invalid'.encode()).decode().rstrip('=')
                return 'https://calendar.google.com/calendar/u/0/r/settings/calendar/'+encoded
            raise AssertionError(name)
        reader.script=script
        result=reader.discover('demo@example.invalid')
        self.assertEqual(len(result['calendars']),2); self.assertEqual(selected,[2,3])

    def test_discovery_mismatched_account_does_not_replace_selection(self):
        reader=GoogleReader(FakeBrowser()); reader.script=Mock(return_value=dict(email='other@example.invalid',rows=[{}]))
        with self.assertRaises(RuntimeError): reader.discover('wanted@example.invalid')


class BrowserTests(unittest.TestCase):
    def test_first_connection_requires_deliberate_login(self):
        with tempfile.TemporaryDirectory() as folder:
            browser=GoogleBrowser(Path(folder)/'profile')
            with patch.object(browser,'launch') as launch, self.assertRaises(LoginRequired): browser.ensure()
            launch.assert_not_called(); self.assertFalse(browser.profile.exists())

    def test_interactive_and_background_modes_use_same_owned_profile(self):
        browser=GoogleBrowser('/tmp/widget-profile',executable='chrome')
        hidden=browser.arguments(True,'https://calendar.google.com/calendar/embed')
        login=browser.arguments(False,'https://calendar.google.com/calendar/u/0/r/settings')
        self.assertIn('--headless=new',hidden); self.assertIn('--remote-debugging-address=127.0.0.1',hidden)
        self.assertFalse(any('--remote-debugging' in arg for arg in login))
        self.assertEqual([a for a in hidden if a.startswith('--user-data-dir=')],[a for a in login if a.startswith('--user-data-dir=')])

    def test_headless_launch_uses_non_gui_lifetime_guard(self):
        browser=GoogleBrowser('/tmp/widget-profile',executable='chrome')
        with patch('google_browser.subprocess.Popen') as popen:
            browser.launch(True,'https://calendar.google.com/calendar/embed')
            args,options=popen.call_args
            self.assertIn('--google-background',args[0]); self.assertEqual(options['stdin'],subprocess.PIPE)
            if sys.platform=='win32': self.assertEqual(options['creationflags'],subprocess.CREATE_NO_WINDOW)
            else: self.assertTrue(options['start_new_session'])

    def test_no_default_profile_or_apple_events_in_background_adapter(self):
        self.assertFalse(official_url('https://calendar.google.com.example.invalid/'))
        self.assertFalse(official_url('http://calendar.google.com/'))
        browser=GoogleBrowser('/tmp/widget-profile',executable='chrome')
        with patch.object(browser,'launch') as launch, self.assertRaises(ValueError): browser.navigate('https://example.invalid/')
        launch.assert_not_called()

    def test_open_login_stops_headless_and_does_not_enable_debugging(self):
        with tempfile.TemporaryDirectory() as folder:
            browser=GoogleBrowser(folder,executable='chrome')
            process=Mock(); process.poll.return_value=None
            with patch.object(browser,'launch',return_value=process) as launch, patch.object(browser,'close') as close:
                browser.open_login('demo@example.invalid')
                close.assert_called_once(); self.assertFalse(launch.call_args.args[0]); self.assertTrue(browser.login_started)
                with self.assertRaises(RuntimeError): browser.open_login()
                self.assertEqual(launch.call_count,1)

    def test_login_origin_is_not_inspected_and_no_headful_fallback(self):
        browser=GoogleBrowser('/tmp/widget-profile')
        with patch.object(browser,'call',return_value={'result':{'value':{'widgetLoginRequired':True}}}) as call, patch.object(browser,'launch') as launch:
            with self.assertRaises(LoginRequired): browser.evaluate('document.title')
            self.assertIn("location.origin !== 'https://calendar.google.com'",call.call_args.args[1]['expression'])
            launch.assert_not_called()

    def test_cdp_ignores_events_and_unrelated_responses(self):
        browser=GoogleBrowser('/tmp/widget-profile'); browser.session='owned-session'; browser.socket=Mock()
        browser.socket.recv.side_effect=[json.dumps({'method':'Page.loadEventFired'}),json.dumps({'id':99,'result':{}}),json.dumps({'id':1,'result':{'value':'ok'}})]
        self.assertEqual(browser.call('Runtime.evaluate',{'expression':'1'}),{'value':'ok'})
        request=json.loads(browser.socket.send.call_args.args[0]); self.assertEqual(request['sessionId'],'owned-session')

    def test_cdp_errors_never_become_success(self):
        browser=GoogleBrowser('/tmp/widget-profile'); browser.socket=Mock(); browser.socket.recv.return_value=json.dumps({'id':1,'error':{'message':'failed'}})
        with self.assertRaises(RuntimeError): browser.call('Page.navigate')

    def test_close_never_terminates_interactive_login(self):
        browser=GoogleBrowser('/tmp/widget-profile'); browser.login_process=Mock(); browser.close()
        browser.login_process.terminate.assert_not_called(); browser.login_process.kill.assert_not_called()

    def test_profile_in_use_blocks_launch_after_widget_restart(self):
        with tempfile.TemporaryDirectory() as folder:
            browser=GoogleBrowser(folder,executable='chrome'); (browser.profile/'widget-login-started').write_text('1')
            process=Mock(); process.info={'name':'Google Chrome','username':'demo'}
            process.cmdline.return_value=['chrome','--user-data-dir='+str(browser.profile)]
            with patch('psutil.Process') as current, patch('psutil.process_iter',return_value=[process]), patch.object(browser,'launch') as launch:
                current.return_value.username.return_value='demo'
                with self.assertRaises(RuntimeError): browser.ensure()
                launch.assert_not_called()

    def test_shared_dom_programs_are_complete(self):
        self.assertEqual(set(SCRIPTS),{'snapshot','expand','expanded','dismiss','catalog','select','route'})
        for program in SCRIPTS.values(): self.assertNotIn('fetch(',program)


if __name__=='__main__': unittest.main()
