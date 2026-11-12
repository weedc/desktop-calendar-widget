# Build on a Mac of the target architecture. No user data is collected.
from pathlib import Path
from importlib import metadata
root = Path(SPECPATH)
licenses=[(str(root/'THIRD_PARTY_NOTICES.md'),'ThirdPartyLicenses')]
for name in ('PySide6','PySide6_Essentials','PySide6_Addons','shiboken6','pyobjc-core',
             'pyobjc-framework-Cocoa','pyobjc-framework-Quartz','pyobjc-framework-UserNotifications','tzdata','websocket-client','psutil','certifi'):
    distribution=metadata.distribution(name)
    for file in distribution.files or []:
        if '/licenses/' in str(file).lower() or file.name.upper().startswith(('LICENSE','COPYING','COPYRIGHT')):
            source=distribution.locate_file(file)
            if source.is_file(): licenses.append((str(source),'ThirdPartyLicenses/'+name+'/'+str(file.parent)))
a = Analysis([str(root/'main.py')], pathex=[str(root)], binaries=[], datas=licenses+[(str(root.parent/'GoogleCalendarDom.json'),'.')],
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
                 'CFBundleShortVersionString':'5.6.1','CFBundleVersion':'5.6.1',
                 'LSMinimumSystemVersion':'14.0','LSUIElement':True,
                 'NSHighResolutionCapable':True,
                 'NSHumanReadableCopyright':'Desktop Calendar Widget contributors'})
