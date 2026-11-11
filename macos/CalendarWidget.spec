# Build on a Mac of the target architecture. No user data is collected.
from pathlib import Path
root = Path(SPECPATH)
a = Analysis([str(root/'main.py')], pathex=[str(root)], binaries=[], datas=[],
             hiddenimports=['AppKit','Foundation','Quartz','UserNotifications'],
             hookspath=[], hooksconfig={}, runtime_hooks=[], excludes=['PySide6.QtWebEngineCore',
             'PySide6.QtWebEngineWidgets','PySide6.QtQml','PySide6.QtQuick'], noarchive=False)
pyz = PYZ(a.pure)
exe = EXE(pyz, a.scripts, [], exclude_binaries=True, name='GoogleCalendarWidget',
          debug=False, bootloader_ignore_signals=False, strip=False, upx=False,
          console=True, argv_emulation=False, target_arch=None, codesign_identity=None,
          entitlements_file=str(root/'entitlements.plist'))
coll = COLLECT(exe, a.binaries, a.datas, strip=False, upx=False, name='GoogleCalendarWidget')
app = BUNDLE(coll, name='Team Calendar Widget.app', icon=str(root/'build/calendar.icns'),
             bundle_identifier='io.github.weedc.desktop-calendar-widget',
             info_plist={'CFBundleName':'Team Calendar Widget','CFBundleDisplayName':'팀 캘린더 위젯',
                 'CFBundleShortVersionString':'5.5.0','CFBundleVersion':'5.5.0',
                 'LSMinimumSystemVersion':'14.0','LSUIElement':True,
                 'NSHighResolutionCapable':True,
                 'NSAppleEventsUsageDescription':'선택한 Google 계정의 캘린더를 Chrome의 공식 Google 화면에서 읽습니다.',
                 'NSHumanReadableCopyright':'Desktop Calendar Widget contributors'})
