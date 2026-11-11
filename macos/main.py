"""Finder app entry and headless MCP/command entry share one signed executable."""
import json
import os
import socket
import sys
import threading
from pathlib import Path


def main():
    for stream in (sys.stdin,sys.stdout,sys.stderr):
        if stream is not None and hasattr(stream,'reconfigure'): stream.reconfigure(encoding='utf-8')
    from bridge import mcp,send
    if '--mcp' in sys.argv: mcp(); return
    if '--command' in sys.argv:
        try: result=send(json.loads(sys.stdin.readline()))
        except Exception as error: result={'error':str(error)}
        print(json.dumps(result,ensure_ascii=False),flush=True); return
    if sys.platform!='darwin' and '--smoke-test' not in sys.argv and '--demo' not in sys.argv:
        raise SystemExit('Use the existing native Windows application on Windows.')
    from PySide6.QtCore import QObject,Signal,Slot,QTimer
    from PySide6.QtWidgets import QApplication,QMessageBox
    from core import Store,VERSION
    from widget import CalendarWindow,STYLE
    import platform_mac
    from bridge import endpoint,command
    demo='--demo' in sys.argv or '--smoke-test' in sys.argv
    channel=None
    if not demo:
        try: send({'command':'show'}); return
        except (OSError,ValueError): pass
        channel=socket.socket(socket.AF_UNIX,socket.SOCK_STREAM)
        path=endpoint()
        # Lock before removing stale socket; simultaneous launches can't orphan a server.
        import fcntl
        lock=open(path.with_suffix('.lock'),'a')
        try: fcntl.flock(lock,fcntl.LOCK_EX|fcntl.LOCK_NB)
        except BlockingIOError:
            channel.close(); raise SystemExit('Calendar is starting. Try the icon again in a moment.')
        path.unlink(missing_ok=True); channel.bind(str(path)); os.chmod(path,0o600); channel.listen(4)
    app=QApplication(sys.argv); app.setApplicationName('팀 캘린더 위젯'); app.setQuitOnLastWindowClosed(False); app.setStyleSheet(STYLE)
    if sys.platform=='darwin' and not demo:
        import AppKit
        AppKit.NSApplication.sharedApplication().setActivationPolicy_(AppKit.NSApplicationActivationPolicyAccessory)
    try:
        folder=Path(os.environ['CALENDAR_WIDGET_TEST_DATA']) if demo and 'CALENDAR_WIDGET_TEST_DATA' in os.environ else platform_mac.app_folder()
        if demo and 'CALENDAR_WIDGET_TEST_DATA' not in os.environ:
            import tempfile
            temporary=tempfile.TemporaryDirectory(); folder=Path(temporary.name)
        store=Store(folder)
    except Exception as error:
        QMessageBox.critical(None,'저장 데이터 확인','기존 데이터를 덮어쓰지 않았습니다.\n'+str(error)); return
    if demo:
        from demo import seed
        seed(store)
    window=CalendarWindow(store,demo=demo)
    if demo:
        if '--smoke-test' in sys.argv:
            from smoke import check
            QTimer.singleShot(200,lambda:check(app,window))
        elif '--screenshot' in sys.argv:
            output=sys.argv[sys.argv.index('--screenshot')+1]
            def shot():
                window.resize(700,410); window.repaint(); window.grab().save(output); app.quit()
            QTimer.singleShot(300,shot)
        return app.exec()
    class Relay(QObject):
        received=Signal(object,object)
        @Slot(object,object)
        def dispatch(self,args,reply):
            try: reply['result']=command(window,args)
            except Exception as error: reply['result']={'error':str(error)}
            reply['ready'].set()
    relay=Relay(); relay.received.connect(relay.dispatch)
    def serve():
        while True:
            try:
                client,_=channel.accept()
                with client:
                    client.settimeout(12)
                    request=json.loads(client.makefile('rb').readline(1024*1024))
                    reply={'ready':threading.Event()}; relay.received.emit(request,reply)
                    if reply['ready'].wait(10): client.sendall(json.dumps(reply['result'],ensure_ascii=False).encode()+b'\n')
            except (OSError,ValueError):
                if channel.fileno()<0: return
    threading.Thread(target=serve,daemon=True).start()
    app.exec()
    channel.close(); path.unlink(missing_ok=True)


if __name__=='__main__': sys.exit(main() or 0)
