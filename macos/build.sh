#!/bin/bash
set -euo pipefail
cd "$(dirname "$0")/.."
python3 -m pip install -r macos/requirements.txt pyinstaller==6.17.0 Pillow==11.3.0
mkdir -p macos/build/calendar.iconset
python3 - <<'PY'
from pathlib import Path
from PIL import Image
image=Image.open('calendar.ico').convert('RGBA')
folder=Path('macos/build/calendar.iconset')
for size in (16,32,128,256,512):
    for scale in (1,2):
        image.resize((size*scale,size*scale),Image.Resampling.LANCZOS).save(folder/f'icon_{size}x{size}{"@2x" if scale==2 else ""}.png')
PY
iconutil -c icns macos/build/calendar.iconset -o macos/build/calendar.icns
python3 -m PyInstaller --noconfirm --clean --distpath macos/dist --workpath macos/build/pyinstaller macos/CalendarWidget.spec
codesign --verify --deep --strict 'macos/dist/Team Calendar Widget.app'
mkdir -p dist
arch="$(uname -m)"
ditto -c -k --sequesterRsrc --keepParent 'macos/dist/Team Calendar Widget.app' "dist/DesktopCalendarWidget-v5.8.1-mac-${arch}.zip"
shasum -a 256 "dist/DesktopCalendarWidget-v5.8.1-mac-${arch}.zip" > "dist/SHA256SUMS-mac-${arch}.txt"
