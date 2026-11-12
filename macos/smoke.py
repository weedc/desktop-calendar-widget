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
        assert window.previous_month.parent() is window.month_navigation
        assert window.next_month.parent() is window.month_navigation
        assert window.month_button.width()<window.month_button.fontMetrics().horizontalAdvance(window.month_button.text())+20
        assert not any(b.text()=='오늘' for b in window.header.findChildren(__import__('PySide6.QtWidgets',fromlist=['QPushButton']).QPushButton))
        from unittest.mock import patch
        with patch('widget.QInputDialog.getText',return_value=('2027-02',True)) as jump:
            window.month_button.click(); assert window.month=='2027-02' and jump.call_count==1
        window.set_month('2026-11')
        # Send mouse events through every real handle without moving the user's pointer.
        from PySide6.QtCore import QEvent,QPoint,QPointF,QRect
        from PySide6.QtGui import QMouseEvent
        from resize import calculate
        original=QRect(window.geometry())
        for handle in window.resize_handles.handles:
            window.setGeometry(original); app.processEvents()
            local=QPointF(handle.width()/2,handle.height()/2)
            screen=QPointF(handle.mapToGlobal(local.toPoint())); delta=QPoint(31,23)
            press=QMouseEvent(QEvent.Type.MouseButtonPress,local,screen,Qt.MouseButton.LeftButton,Qt.MouseButton.LeftButton,Qt.KeyboardModifier.NoModifier)
            app.sendEvent(handle,press)
            screen+=QPointF(delta); at=QPointF(handle.mapFromGlobal(screen.toPoint()))
            move=QMouseEvent(QEvent.Type.MouseMove,at,screen,Qt.MouseButton.NoButton,Qt.MouseButton.LeftButton,Qt.KeyboardModifier.NoModifier)
            app.sendEvent(handle,move)
            app.sendEvent(handle,QMouseEvent(QEvent.Type.MouseButtonRelease,at,screen,Qt.MouseButton.LeftButton,Qt.MouseButton.NoButton,Qt.KeyboardModifier.NoModifier))
            assert window.geometry()==calculate(original,handle.edges,delta,window.minimumSize()),str(handle.edges)
        minimum=calculate(original,Qt.Edge.LeftEdge|Qt.Edge.TopEdge,QPoint(99999,99999),window.minimumSize())
        assert minimum.size()==window.minimumSize() and minimum.bottomRight()==original.bottomRight()
        window.setGeometry(original); app.processEvents()
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
        # Exercise the real button and asynchronous completion while alerts are off.
        from PySide6.QtWidgets import QPushButton
        from test_updates import release
        import time
        from widget import Job
        delivered=[]
        window.pool.start(Job(lambda:'fixture',lambda value:delivered.append(value),lambda error:delivered.append('error')))
        deadline=time.monotonic()+2
        while not delivered and time.monotonic()<deadline:app.processEvents();time.sleep(0.01)
        assert delivered==['fixture'], 'background calendar callbacks must reach the UI thread'
        store.options['update_notifications']=False
        advanced=Advanced(store.options,window)
        original_reader=window.updater.reader
        window.updater.reader=lambda:[release('99.0.0',window.updater.state.target)]
        next(b for b in advanced.findChildren(QPushButton) if b.text()=='지금 확인').click()
        deadline=time.monotonic()+3
        while window.updater.busy and time.monotonic()<deadline: app.processEvents(); time.sleep(0.01)
        assert not window.updater.busy and window.updater.download.isEnabled(), (window.updater.busy,window.updater.message.text(),window.updater.dialog.isVisible(),window.updater.state.target)
        assert '99.0.0' in window.updater.message.text() and not store.options['update_notifications']
        assert window.updater.state.pending is None and not store.options['auto_sync']
        assert window.updater.download.text()=='업데이트하기'
        # The actual button calls the installer once; failure leaves the widget running.
        from unittest.mock import patch
        calls=[]
        def failed_install(result,target): calls.append(target); raise ValueError('synthetic failure')
        with patch('installer.prepare',failed_install):
            window.updater.download.click(); window.updater.download.click()
            deadline=time.monotonic()+3
            while window.updater.installing and time.monotonic()<deadline: app.processEvents(); time.sleep(0.01)
        assert calls==[window.updater.state.target] and not window.updater.installing
        assert window.updater.download.isEnabled() and 'synthetic failure' in window.updater.message.text()
        window.updater.dialog.close(); advanced.reject(); window.updater.reader=original_reader
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
