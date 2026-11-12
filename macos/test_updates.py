import copy
import tempfile
import unittest
from core import Store
from updates import select_release,valid_download,version,due,UpdateState,INTERVAL


def release(value,target,**flags):
    name=f'DesktopCalendarWidget-v{value}-{target}.zip'
    return dict(assets=[dict(name=name,browser_download_url='https://github.com/weedc/desktop-calendar-widget/releases/download/test/'+name)],**flags)


class UpdateTests(unittest.TestCase):
    def setUp(self):
        self.tmp=tempfile.TemporaryDirectory(); self.addCleanup(self.tmp.cleanup)
        self.store=Store(self.tmp.name)
        self.state=UpdateState(self.store,'5.6.0','mac-arm64')
    def test_versions_and_architecture(self):
        feed=[release('5.9.0','mac-arm64'),release('5.10.0','mac-arm64'),release('99.0.0','mac-x86_64'),release('99.0.0','win-x64')]
        self.assertEqual(select_release(feed,'5.6.0','mac-arm64')['version'],'5.10.0')
        self.assertIsNone(select_release(feed,'99.0.0','mac-x86_64'))
    def test_no_preview_draft_or_downgrade(self):
        feed=[release('5.9.0','mac-arm64',prerelease=True),release('5.8.0','mac-arm64',draft=True),release('5.6.0','mac-arm64')]
        self.assertIsNone(select_release(feed,'5.6.0','mac-arm64'))
    def test_repository_download_validation(self):
        good=release('5.9.0','mac-arm64')['assets'][0]
        self.assertTrue(valid_download(good['browser_download_url'],good['name']))
        for value in ['https://github.com.evil.invalid/','http://github.com/weedc/desktop-calendar-widget/releases/download/test/a.zip',good['browser_download_url']+'?anything=yes',good['browser_download_url'].replace('/weedc/','/other/')]:
            self.assertFalse(valid_download(value))
    def test_exact_six_hour_boundary_and_failed_attempt(self):
        generation=self.state.begin(at=0)
        self.assertEqual(generation,0)
        self.assertIsNone(self.state.begin(at=INTERVAL-1))
        self.assertEqual(self.state.begin(at=INTERVAL),0)
        self.assertFalse(due(False,None,0))
    def test_disabled_manual_check_and_stale_result(self):
        self.store.options['update_notifications']=False
        self.assertIsNone(self.state.begin(at=0))
        generation=self.state.begin(manual=True,at=0)
        result=self.state.finish([release('5.9.0','mac-arm64')],generation)
        self.assertEqual(result['version'],'5.9.0'); self.assertIsNone(self.state.pending)
        self.store.options['update_notifications']=True; generation=self.state.begin(at=1)
        self.store.options['update_notifications']=False
        self.state.finish([release('5.9.0','mac-arm64')],generation)
        self.assertIsNone(self.state.pending)
    def test_once_per_version_survives_restart_and_apply(self):
        generation=self.state.begin(at=0); result=self.state.finish([release('5.9.0','mac-arm64')],generation)
        draft=copy.deepcopy(self.store.options)
        self.state.acknowledge(result); self.store.apply_options(draft)
        reopened=UpdateState(Store(self.tmp.name),'5.6.0','mac-arm64')
        reopened.finish([release('5.9.0','mac-arm64')],reopened.begin(at=1))
        self.assertIsNone(reopened.pending)
        reopened.finish([release('5.10.0','mac-arm64')],reopened.begin(at=INTERVAL+2))
        self.assertEqual(reopened.pending['version'],'5.10.0')
    def test_independent_of_calendar_sync(self):
        self.store.options['auto_sync']=False
        result=self.state.finish([release('5.9.0','mac-arm64')],self.state.begin(at=0))
        self.assertEqual(result['version'],'5.9.0')
        self.assertFalse(self.store.options['auto_sync'])
    def test_version_validation(self):
        self.assertEqual(version('5.6.0'),version('5.6.0.0'))
        for value in ['5.6','5.6.0-beta','5.6.0/path','']:self.assertIsNone(version(value))


if __name__=='__main__':unittest.main()
