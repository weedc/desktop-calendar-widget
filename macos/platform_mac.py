"""Small macOS adapters; no private APIs or hidden browser preferences."""
import os
import plistlib
import sys
from pathlib import Path

BUNDLE_ID = 'io.github.weedc.desktop-calendar-widget'


def app_folder():
    return Path.home() / 'Library' / 'Application Support' / 'DesktopCalendarWidget'


def native_window(widget):
    import objc
    view = objc.objc_object(c_void_p=int(widget.winId()))
    return view.window()


def attach_desktop(widget, pinned):
    if sys.platform != 'darwin':
        return
    import AppKit
    from Quartz import CGWindowLevelForKey, kCGDesktopWindowLevelKey
    window = native_window(widget)
    window.setLevel_(CGWindowLevelForKey(kCGDesktopWindowLevelKey) + 1 if pinned else AppKit.NSNormalWindowLevel)
    window.setCollectionBehavior_(AppKit.NSWindowCollectionBehaviorCanJoinAllSpaces |
                                 AppKit.NSWindowCollectionBehaviorStationary |
                                 AppKit.NSWindowCollectionBehaviorIgnoresCycle)
    window.setHidesOnDeactivate_(False)
    window.setCanHide_(False)


def startup(enabled):
    # A user-owned LaunchAgent supports the same opt-in login launch as Windows.
    # Keep the exact executable location; never run an arbitrary shell command.
    path = Path.home() / 'Library' / 'LaunchAgents' / (BUNDLE_ID + '.plist')
    if not enabled:
        path.unlink(missing_ok=True)
        return
    if not getattr(sys, 'frozen', False):
        raise ValueError('자동 실행은 /Applications에 설치한 앱에서 켜 주세요.')
    path.parent.mkdir(parents=True, exist_ok=True)
    content = {'Label': BUNDLE_ID, 'ProgramArguments': [sys.executable], 'RunAtLoad': True,
               'ProcessType': 'Interactive'}
    tmp = path.with_suffix('.tmp')
    tmp.write_bytes(plistlib.dumps(content))
    os.chmod(tmp, 0o600)
    os.replace(tmp, path)


class Notifications:
    def __init__(self, fallback):
        self.fallback = fallback
        self.center = None
        if sys.platform == 'darwin':
            from UserNotifications import UNUserNotificationCenter
            self.center = UNUserNotificationCenter.currentNotificationCenter()

    def install_click_handler(self,callback):
        if not self.center: return
        import objc
        from Foundation import NSObject
        from UserNotifications import (UNNotificationPresentationOptionBanner,
                                       UNNotificationPresentationOptionList,
                                       UNNotificationPresentationOptionSound)
        class CalendarNotificationDelegate(NSObject, protocols=[objc.protocolNamed('UNUserNotificationCenterDelegate')]):
            def userNotificationCenter_didReceiveNotificationResponse_withCompletionHandler_(self,center,response,complete):
                info=response.notification().request().content().userInfo()
                callback({'date':str(info.get('date','')),'url':str(info.get('update_url',''))}); complete()
            def userNotificationCenter_willPresentNotification_withCompletionHandler_(self,center,notification,complete):
                complete(UNNotificationPresentationOptionBanner | UNNotificationPresentationOptionList | UNNotificationPresentationOptionSound)
        self.delegate=CalendarNotificationDelegate.alloc().init()
        self.center.setDelegate_(self.delegate)

    def authorize(self):
        if self.center:
            from UserNotifications import UNAuthorizationOptionAlert, UNAuthorizationOptionSound
            self.center.requestAuthorizationWithOptions_completionHandler_(
                UNAuthorizationOptionAlert | UNAuthorizationOptionSound, lambda granted, error: None)

    def send(self, alert, done):
        if not self.center:
            self.fallback(alert['title'], alert['body'])
            done(True)
            return
        from UserNotifications import UNMutableNotificationContent, UNNotificationRequest, UNNotificationSound
        content = UNMutableNotificationContent.alloc().init()
        content.setTitle_(alert['title'])
        content.setBody_(alert['body'])
        content.setUserInfo_({'date': alert['date'],'update_url':alert.get('url','')})
        content.setSound_(UNNotificationSound.defaultSound())
        request = UNNotificationRequest.requestWithIdentifier_content_trigger_(alert['id'], content, None)
        self.center.addNotificationRequest_withCompletionHandler_(request, lambda error: done(error is None))
