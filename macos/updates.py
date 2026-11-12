"""Public release checks, independent of Google synchronization. Never installs code."""
import json
import platform
import re
import ssl
import time
import urllib.request
from urllib.parse import urlparse,unquote

FEED='https://api.github.com/repos/weedc/desktop-calendar-widget/releases?per_page=100'
INTERVAL=6*60*60
LIMIT=2*1024*1024


def version(value):
    if not isinstance(value,str) or not re.fullmatch(r'\d{1,5}\.\d{1,5}\.\d{1,5}(?:\.\d{1,5})?',value): return None
    parts=tuple(map(int,value.split('.')))
    return parts+(0,)*(4-len(parts))


def valid_download(value,name=None):
    try:
        url=urlparse(value)
        return (url.scheme=='https' and url.hostname=='github.com' and url.port in (None,443)
            and not url.username and not url.password and not url.query and not url.fragment
            and url.path.startswith('/weedc/desktop-calendar-widget/releases/download/')
            and bool(re.fullmatch(r'DesktopCalendarWidget-v\d+\.\d+\.\d+(?:\.\d+)?-(?:win-x64|mac-arm64|mac-x86_64)\.zip',unquote(url.path.split('/')[-1])))
            and (name is None or unquote(url.path.split('/')[-1])==name))
    except (ValueError,TypeError): return False


def select_release(releases,current,target):
    installed=version(current)
    if installed is None: raise ValueError('현재 버전을 확인하지 못했습니다.')
    if not isinstance(releases,list): raise ValueError('업데이트 응답을 읽지 못했습니다.')
    best=None; newest=installed
    for release in releases:
        if not isinstance(release,dict) or release.get('draft') or release.get('prerelease'): continue
        for asset in release.get('assets') or []:
            name=asset.get('name','')
            match=re.fullmatch(r'DesktopCalendarWidget-v(\d+\.\d+\.\d+(?:\.\d+)?)-'+re.escape(target)+r'\.zip',name)
            candidate=version(match[1]) if match else None
            if candidate and candidate>newest and valid_download(asset.get('browser_download_url',''),name):
                newest=candidate; best={'version':match[1],'url':asset['browser_download_url']}
    return best


class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self,*args,**kwargs): return None


def tls_context():
    import certifi
    return ssl.create_default_context(cafile=certifi.where())


def fetch(current):
    request=urllib.request.Request(FEED,headers={'User-Agent':'DesktopCalendarWidget/'+current,'Accept':'application/vnd.github+json'})
    with urllib.request.build_opener(NoRedirect,urllib.request.HTTPSHandler(context=tls_context())).open(request,timeout=12) as response:
        data=response.read(LIMIT+1)
    if len(data)>LIMIT: raise ValueError('업데이트 응답이 너무 큽니다.')
    return json.loads(data.decode('utf-8'))


def due(enabled,last,at):
    return enabled and (last is None or at-last>=INTERVAL)


class UpdateState:
    """No Qt/network side effects; notification delivery is acknowledged separately."""
    def __init__(self,store,current,target):
        self.store=store; self.current=current; self.target=target; self.last=None
        self.enabled=store.options['update_notifications']; self.generation=0; self.pending=None
    def configure(self):
        enabled=self.store.options['update_notifications']
        if enabled!=self.enabled:
            self.generation+=1; self.pending=None; self.enabled=enabled
            if enabled:self.last=None
    def begin(self,manual=False,at=None):
        self.configure(); at=time.monotonic() if at is None else at
        if not manual and not due(self.enabled,self.last,at): return None
        self.last=at
        return self.generation
    def finish(self,releases,generation):
        self.configure()
        result=select_release(releases,self.current,self.target)
        notified=self.store.data.get('update_state',{}).get('notified_version','')
        if generation==self.generation and self.enabled and result and result['version']!=notified:
            self.pending=result
        return result
    def acknowledge(self,result):
        self.store.data.setdefault('update_state',{})['notified_version']=result['version']; self.store.save()


def controller(window,demo=False):
    from PySide6.QtCore import QObject,QThreadPool,Slot
    from PySide6.QtWidgets import QDialog,QVBoxLayout,QLabel,QHBoxLayout,QPushButton
    from PySide6.QtGui import QDesktopServices
    from PySide6.QtCore import QUrl
    from core import VERSION
    from widget import Job
    class Controller(QObject):
        def __init__(self):
            super().__init__(window)
            self.state=UpdateState(window.store,VERSION,'mac-arm64' if platform.machine().lower() in ('arm64','aarch64') else 'mac-x86_64')
            self.pool=QThreadPool(self); self.pool.setMaxThreadCount(1); self.busy=False; self.dialog=None; self.demo=demo
            self.reader=lambda:fetch(VERSION)
        def configure(self):
            self.state.configure()
            if not self.state.enabled:
                window.alert_queue[:]=[a for a in window.alert_queue if a.get('kind')!='update']
        def check(self,manual=False):
            self.configure()
            if self.busy:return
            generation=self.state.begin(manual)
            if generation is None:return
            self.busy=True; self.generation=generation
            self.job=Job(self.reader,self.completed,self.failed); self.pool.start(self.job)
        @Slot(object)
        def completed(self,releases):
            self.busy=False
            try: result=self.state.finish(releases,self.generation)
            except Exception:return self.failed('')
            self.render(result,False)
        @Slot(str)
        def failed(self,message):
            self.busy=False; self.render(None,True)
        def render(self,result,failed):
            if self.dialog and self.dialog.isVisible():
                self.message.setText('업데이트를 확인하지 못했습니다. 잠시 후 다시 시도해 주세요.' if failed else '최신 버전을 사용하고 있습니다.' if not result else '새 버전 '+result['version']+'을 사용할 수 있습니다.')
                self.result=result; self.download.setEnabled(bool(result))
        def open(self,owner):
            if self.dialog and self.dialog.isVisible(): self.dialog.raise_(); return
            self.dialog=QDialog(owner); self.dialog.setWindowTitle('업데이트 확인'); self.dialog.resize(430,200)
            layout=QVBoxLayout(self.dialog); layout.addWidget(QLabel('현재 버전  '+VERSION))
            self.message=QLabel('새 버전을 확인하고 있습니다…'); self.message.setWordWrap(True); layout.addWidget(self.message)
            row=QHBoxLayout(); layout.addLayout(row); row.addStretch()
            self.download=QPushButton('다운로드'); self.download.setEnabled(False); row.addWidget(self.download)
            self.download.clicked.connect(lambda:QDesktopServices.openUrl(QUrl(self.result['url'])) if self.result and valid_download(self.result['url']) else None)
            close=QPushButton('닫기'); close.clicked.connect(self.dialog.close); row.addWidget(close)
            self.dialog.show(); self.check(True)
        def tick(self):
            self.configure()
            if not self.demo: self.check()
            result=self.state.pending
            if result:
                self.state.pending=None
                if not any(a.get('kind')=='update' for a in window.alert_queue) and not (window.sending and window.sending.get('kind')=='update'):
                    window.alert_queue.append(dict(id='update-'+result['version'],kind='update',version=result['version'],url=result['url'],date='',title='캘린더 업데이트 · '+result['version'],body='새 버전이 있습니다. 알림을 클릭해 다운로드하세요.'))
    return Controller()
