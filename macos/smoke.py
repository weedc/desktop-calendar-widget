"""Exercise actual widgets using synthetic data. No Google account or runtime data."""
import copy
from PySide6.QtCore import Qt
from PySide6.QtWidgets import QDialog
from core import context_key
from widget import Settings,Advanced
from bridge import command


def check(app,window):
    try:
        store=window.store; window.month='2026-11'; window.resize(700,410); window.repaint()
        assert len(window.hits)>0
        import os
        from pathlib import Path
        qa=os.environ.get('CALENDAR_WIDGET_QA_DIR')
        if qa:
            Path(qa).mkdir(parents=True,exist_ok=True)
            window.grab().save(str(Path(qa)/'compact-synthetic.png'))
        assert window.date_at(__import__('PySide6.QtCore',fromlist=['QPointF']).QPointF(10,70))=='2026-11-01'
        settings=Settings(window); before=copy.deepcopy(store.options)
        item=settings.list.item(0); item.setCheckState(Qt.CheckState.Unchecked); settings.capture()
        assert store.options==before and settings.draft['id_calendars'][0]['enabled']==False
        settings.reject(); assert store.options==before
        if qa:
            settings=Settings(window); settings.show(); app.processEvents()
            settings.grab().save(str(Path(qa)/'settings-synthetic.png')); settings.reject()
        advanced=Advanced(before,window); advanced.checks['auto_sync'].setChecked(True); advanced.finish()
        assert advanced.result()==QDialog.DialogCode.Accepted and store.options==before
        window.detail.set_day('2026-11-02'); original=window.detail
        window.detail.set_day('2026-11-03'); assert original is window.detail and original.day=='2026-11-03'
        command(window,dict(command='upsert_note',date='2026-11-03',text='가상 테스트 메모',time='09:30'))
        note=store.data['notes'][-1]; assert note['text']=='가상 테스트 메모'
        command(window,dict(command='highlight',date='2026-11-03',color='#607eb5'))
        assert store.data['highlights']['2026-11-03']=='#607eb5'
        event=store.events('2026-11')[0]
        store.classify(context_key(event),'🌴',manual=True)
        command(window,dict(command='set_emoji',key=context_key(event),icon='💬'))
        assert store.resolved(event)['icon']=='🌴'
        window.resize(1400,650); window.repaint(); assert len(window.hits)>0
        if qa: window.grab().save(str(Path(qa)/'full-synthetic.png'))
        command(window,dict(command='sync_settings',enabled=False)); assert not store.options['auto_sync']
        command(window,dict(command='daily_summary',enabled=True,time='09:15')); assert store.options['summary_time']=='09:15'
        command(window,dict(command='delete_note',id=note['id'])); assert not store.data['notes']
        assert command(window,{'command':'due_reminders'})['alerts']==[]
        import sys
        if sys.platform=='darwin':
            import platform_mac
            from Quartz import CGWindowLevelForKey,kCGDesktopWindowLevelKey
            import AppKit
            platform_mac.attach_desktop(window,True)
            native=platform_mac.native_window(window)
            assert native.level()==CGWindowLevelForKey(kCGDesktopWindowLevelKey)+1
            assert native.collectionBehavior() & AppKit.NSWindowCollectionBehaviorCanJoinAllSpaces
            platform_mac.attach_desktop(window,False)
            assert native.level()==AppKit.NSNormalWindowLevel
            window.tray.show(); assert window.tray.isVisible(); window.hide(); window.show()
            window.tray.hide()
            if getattr(sys,'frozen',False):
                notifications=platform_mac.Notifications(lambda title,body:None)
                notifications.install_click_handler(lambda day:None)
                assert notifications.center.delegate() is not None
        print('PASS: Qt rendering, staged settings/cancel, advanced draft, reused details, notes, highlights, emoji protection, sync and summary commands',flush=True)
        app.exit(0)
    except Exception:
        import traceback
        traceback.print_exc(); app.exit(1)
