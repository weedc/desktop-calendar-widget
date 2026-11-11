import copy
import json
import os
import subprocess
import sys
import tempfile
import unittest
from datetime import datetime,timedelta
from pathlib import Path
from unittest.mock import patch
from core import *


class CalendarTests(unittest.TestCase):
    def setUp(self):
        self.tmp=tempfile.TemporaryDirectory(); self.addCleanup(self.tmp.cleanup)
        self.store=Store(self.tmp.name)
        from demo import seed
        seed(self.store)

    def test_grid_year_boundary(self):
        self.assertEqual(days_for_month('2027-01')[0],'2026-12-27')
        self.assertEqual(len(days_for_month('2024-02')),42)

    def test_validation(self):
        for s in ['2026-00','2026-13','2026-1','hello']: self.assertFalse(month_valid(s))
        for s in ['24:00','-1:00','2:30','12:60']: self.assertFalse(time_valid(s))
        self.assertTrue(day_valid('2024-02-29')); self.assertFalse(day_valid('2026-02-29'))

    def test_context_not_time(self):
        e=self.store.events('2026-11')[0]; changed={**e,'date':'2027-01-01','start':'2027-01-01T13:00:00+09:00'}
        self.assertEqual(context_key(e),context_key(changed))
        self.assertNotEqual(context_key(e),context_key({**e,'title':'새로운 제목'}))
        self.assertNotEqual(context_key(e),context_key({**e,'calendarId':'another@example.invalid'}))

    def test_failed_and_disabled_sources_preserved(self):
        old=copy.deepcopy(self.store.data['months']['2026-11']['events'])
        self.store.merge('2026-11',{})
        self.assertEqual(self.store.data['months']['2026-11']['events'],old)
        self.store.options['id_calendars'][0]['enabled']=False
        self.assertEqual(self.store.events('2026-11'),[])
        self.assertEqual(self.store.data['months']['2026-11']['events'],old)

    def test_partial_merge(self):
        self.store.data['months']['2026-11']['events'].append({**self.store.events('2026-11')[0],'calendarId':'failed@example.invalid'})
        self.store.merge('2026-11',{'demo@example.invalid':[]})
        self.assertEqual(len(self.store.data['months']['2026-11']['events']),1)
        self.assertEqual(self.store.data['months']['2026-11']['events'][0]['calendarId'],'failed@example.invalid')

    def test_staged_options_preserve_background_writes(self):
        draft=copy.deepcopy(self.store.options); draft['opacity']=50
        self.store.data['delivered'].append('new-delivery'); self.store.data['notes'].append({'id':'new'})
        self.store.apply_options(draft)
        loaded=Store(self.tmp.name)
        self.assertEqual(loaded.options['opacity'],50)
        self.assertEqual(loaded.data['delivered'],['new-delivery']); self.assertEqual(loaded.data['notes'],[{'id':'new'}])

    def test_mode_exclusive_retains_other(self):
        draft=copy.deepcopy(self.store.options); draft['mode']='account'
        self.store.apply_options(draft)
        self.assertEqual(self.store.events('2026-11'),[])
        self.assertTrue(self.store.options['id_calendars'])

    def test_manual_and_project_protection(self):
        e=self.store.events('2026-11')[0]; key=context_key(e)
        self.store.classify(key,'🌴',project='팀',manual=True,whole_project=True)
        self.store.classify(key,'💬')
        self.assertEqual(self.store.resolved(e)['icon'],'🌴')
        with self.assertRaises(ValueError): self.store.classify('stale','💬')
        with self.assertRaises(ValueError): self.store.classify(key,'hello')

    def test_summary_exact_time_no_catchup(self):
        self.store.options.update(daily_summary=True,summary_time='09:00',hour_reminders=False)
        at=datetime(2026,11,2,9,0,tzinfo=SEOUL)
        alerts=self.store.notifications(at); self.assertEqual(len(alerts),1)
        self.assertEqual(self.store.notifications(at),[])
        self.store.data['summary_checked']=''
        self.assertEqual(self.store.notifications(at+timedelta(minutes=1)),[])
        self.assertEqual(self.store.notifications(datetime(2026,11,4,9,0,tzinfo=SEOUL)),[])

    def test_summary_time_change_reschedules_but_same_save_does_not(self):
        self.store.data['summary_checked']='2026-11-02'
        draft=copy.deepcopy(self.store.options)
        self.store.apply_options(draft)
        self.assertEqual(self.store.data['summary_checked'],'2026-11-02')
        draft['summary_time']='09:30'; self.store.apply_options(draft)
        self.assertEqual(self.store.data['summary_checked'],'')

    def test_hour_notification_actual_remaining(self):
        self.store.options.update(hour_reminders=True,daily_summary=False)
        at=datetime(2026,11,2,9,7,tzinfo=SEOUL)
        alerts=self.store.notifications(at)
        self.assertEqual(len(alerts),1); self.assertIn('53분',alerts[0]['title'])
        self.store.acknowledge(alerts[0]); self.assertEqual(self.store.notifications(at),[])

    def test_empty_or_wrong_month_is_not_success(self):
        with self.assertRaises(ValueError): parse_snapshot({'month':'2026-11','cells':[]},'2026-11',{})

    def test_korean_accessibility_parse(self):
        cells=[{'label':f'일정 없음, 11월 {i}일','events':[]} for i in range(1,29)]
        raw='오후 2:30~오후 3시, 가상 디자인 회의, 캘린더: 예시, 위치: 온라인, 2026년 11월 2일'
        cells[1]['events']=[raw,raw]
        e=parse_snapshot({'month':'2026-11','cells':cells},'2026-11',self.store.sources()[0])
        self.assertEqual(len(e),1); self.assertEqual(e[0]['title'],'가상 디자인 회의')
        self.assertEqual(e[0]['start'],'2026-11-02T14:30:00+09:00'); self.assertEqual(e[0]['location'],'온라인')

    def test_incomplete_hidden_events_keep_cache(self):
        cells=[{'label':f'일정 없음, 11월 {i}일','events':[]} for i in range(1,29)]
        cells[0]['label']='일정 5개, 11월 1일'
        with self.assertRaises(ValueError): parse_snapshot({'month':'2026-11','cells':cells},'2026-11',self.store.sources()[0])

    def test_codex_config_preservation(self):
        from bridge import codex_registration
        with patch.dict(os.environ,{'CODEX_HOME':self.tmp.name}):
            path=Path(self.tmp.name)/'config.toml'; original='model = "example"\n\n[mcp_servers.other]\ncommand = "keep"\n'
            path.write_text(original,encoding='utf-8')
            codex_registration(True); self.assertIn(original.strip(),path.read_text(encoding='utf-8'))
            codex_registration(True); self.assertEqual(path.read_text(encoding='utf-8').count('[mcp_servers.team_calendar_widget]'),1)
            codex_registration(False); self.assertIn(original.strip(),path.read_text(encoding='utf-8'))
            self.assertNotIn('[mcp_servers.team_calendar_widget]',path.read_text(encoding='utf-8'))

    def test_mcp_stdio_without_running_widget(self):
        requests=[{'jsonrpc':'2.0','id':1,'method':'initialize','params':{'protocolVersion':'2024-11-05'}},
                  {'jsonrpc':'2.0','id':2,'method':'tools/list'}, {'jsonrpc':'2.0','method':'notifications/initialized'},
                  {'jsonrpc':'2.0','id':3,'method':'tools/call','params':{'name':'calendar_widget_opacity','arguments':{'percent':'bad'}}}]
        p=subprocess.run([sys.executable,str(Path(__file__).with_name('main.py')),'--mcp'],input='\n'.join(map(json.dumps,requests))+'\n',text=True,encoding='utf-8',capture_output=True,check=True)
        lines=[json.loads(s) for s in p.stdout.splitlines()]
        self.assertEqual(len(lines),3); self.assertEqual(len(lines[1]['result']['tools']),26)
        self.assertIn('error',lines[2])

    def test_corrupt_data_not_overwritten(self):
        self.store.path.write_text('broken',encoding='utf-8')
        with self.assertRaises(json.JSONDecodeError): Store(self.tmp.name)
        self.assertEqual(self.store.path.read_text(encoding='utf-8'),'broken')


if __name__=='__main__': unittest.main()
