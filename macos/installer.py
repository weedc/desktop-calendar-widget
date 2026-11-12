"""Stage a matching public app bundle, then replace it after the UI exits."""
import hashlib
import os
import plistlib
import posixpath
import re
import shutil
import stat
import subprocess
import sys
import tempfile
import urllib.request
import uuid
import zipfile
from pathlib import Path
from urllib.parse import urlparse
from updates import valid_download, version, tls_context

DOWNLOAD_LIMIT = 600 * 1024 * 1024
BUNDLE = 'Team Calendar Widget.app'
BUNDLE_ID = 'io.github.weedc.desktop-calendar-widget'


class AssetRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        url = urlparse(newurl)
        if (url.scheme != 'https' or url.hostname not in
                {'github.com', 'release-assets.githubusercontent.com', 'objects.githubusercontent.com'}
                or url.port not in (None, 443) or url.username or url.password):
            raise ValueError('업데이트 다운로드 주소를 확인하지 못했습니다.')
        return super().redirect_request(req, fp, code, msg, headers, newurl)


def download(result, target, output):
    name = f"DesktopCalendarWidget-v{result['version']}-{target}.zip"
    if not valid_download(result['url'], name): raise ValueError('업데이트 주소가 올바르지 않습니다.')
    digest = result.get('digest') or ''
    if digest and not re.fullmatch(r'sha256:[a-fA-F0-9]{64}', digest):
        raise ValueError('업데이트 검증 정보를 읽지 못했습니다.')
    opener = urllib.request.build_opener(AssetRedirect(), urllib.request.HTTPSHandler(context=tls_context()))
    request = urllib.request.Request(result['url'], headers={'User-Agent': 'DesktopCalendarWidget'})
    hashed = hashlib.sha256(); size = 0
    with opener.open(request, timeout=30) as response, output.open('wb') as stream:
        while chunk := response.read(128 * 1024):
            size += len(chunk)
            if size > DOWNLOAD_LIMIT: raise ValueError('업데이트 파일이 너무 큽니다.')
            stream.write(chunk); hashed.update(chunk)
    if digest and digest.lower() != 'sha256:' + hashed.hexdigest():
        raise ValueError('다운로드 검증에 실패했습니다. 다시 시도해 주세요.')


def validate_archive(path):
    """ditto preserves framework symlinks; validate both member and link destinations."""
    with zipfile.ZipFile(path) as archive:
        entries = archive.infolist()
        if len(entries) > 100000 or sum(e.file_size for e in entries) > 3 * 1024**3:
            raise ValueError('업데이트 압축 파일이 너무 큽니다.')
        seen = set()
        for entry in entries:
            name = entry.filename
            if ('\\' in name or ':' in name or name.startswith('/') or '..' in name.split('/')
                    or name.split('/')[0] not in {BUNDLE, '__MACOSX'}):
                raise ValueError('업데이트 압축 파일의 경로가 올바르지 않습니다.')
            if name in seen: raise ValueError('업데이트 압축 파일에 중복 경로가 있습니다.')
            seen.add(name)
            if stat.S_ISLNK(entry.external_attr >> 16):
                if entry.file_size > 4096: raise ValueError('업데이트 링크가 올바르지 않습니다.')
                link = archive.read(entry).decode('utf-8')
                resolved = posixpath.normpath(posixpath.join(posixpath.dirname(name), link))
                if link.startswith('/') or '\\' in link or not resolved.startswith(BUNDLE + '/'):
                    raise ValueError('업데이트 링크가 앱 외부를 가리킵니다.')
        if BUNDLE + '/Contents/Info.plist' not in seen:
            raise ValueError('업데이트 앱을 찾지 못했습니다.')


def validate_bundle(bundle, expected):
    info = plistlib.loads((bundle / 'Contents/Info.plist').read_bytes())
    if info.get('CFBundleIdentifier') != BUNDLE_ID or version(info.get('CFBundleShortVersionString')) != version(expected):
        raise ValueError('업데이트 앱의 이름이나 버전이 일치하지 않습니다.')
    executable = bundle / 'Contents/MacOS/GoogleCalendarWidget'
    if not executable.is_file(): raise ValueError('업데이트 실행 파일이 없습니다.')
    return executable


HELPER = r'''#!/bin/sh
parent="$1"; installed="$2"; incoming="$3"; backup="$4"
count=0
while kill -0 "$parent" 2>/dev/null; do
  count=$((count+1))
  [ "$count" -gt 30 ] && exit 1
  sleep 1
done
if ! /bin/mv "$installed" "$backup"; then /usr/bin/open -n -a "$installed"; exit 1; fi
if ! /bin/mv "$incoming" "$installed"; then /bin/mv "$backup" "$installed"; /usr/bin/open -n -a "$installed"; exit 1; fi
if ! /usr/bin/open -n -a "$installed"; then
  /bin/mv "$installed" "$incoming"
  /bin/mv "$backup" "$installed"
  /usr/bin/open -n -a "$installed"
  exit 1
fi
'''


def prepare(result, target):
    if sys.platform != 'darwin' or not getattr(sys, 'frozen', False):
        raise ValueError('설치된 Mac 앱에서 업데이트해 주세요.')
    installed = Path(sys.executable).resolve().parents[2]
    if installed.suffix != '.app' or not os.access(installed.parent, os.W_OK):
        raise ValueError('앱을 쓰기 가능한 응용 프로그램 폴더로 옮긴 뒤 다시 시도해 주세요.')
    from platform_mac import app_folder
    updates = app_folder() / 'updates'; updates.mkdir(parents=True, exist_ok=True)
    work = Path(tempfile.mkdtemp(prefix='release-', dir=updates))
    incoming = installed.parent / ('.TeamCalendarWidget-update-' + uuid.uuid4().hex + '.app')
    try:
        archive = work / 'release.zip'; download(result, target, archive); validate_archive(archive)
        extracted = work / 'extracted'
        subprocess.run(['/usr/bin/ditto', '-x', '-k', str(archive), str(extracted)], check=True, capture_output=True, timeout=180)
        bundle = extracted / BUNDLE; executable = validate_bundle(bundle, result['version'])
        architecture = 'arm64' if target == 'mac-arm64' else 'x86_64'
        subprocess.run(['/usr/bin/lipo', '-verify_arch', architecture, str(executable)], check=True, capture_output=True, timeout=30)
        subprocess.run(['/usr/bin/codesign', '--verify', '--deep', '--strict', str(bundle)], check=True, capture_output=True, timeout=60)
        subprocess.run(['/usr/bin/ditto', str(bundle), str(incoming)], check=True, capture_output=True, timeout=180)
        subprocess.run(['/usr/bin/codesign', '--verify', '--deep', '--strict', str(incoming)], check=True, capture_output=True, timeout=60)
        shutil.rmtree(extracted); archive.unlink()
        helper = work / 'install.sh'; helper.write_text(HELPER, encoding='utf-8'); helper.chmod(0o700)
        backup = installed.parent / ('.TeamCalendarWidget-previous-' + uuid.uuid4().hex + '.app')
        return [str(helper), str(os.getpid()), str(installed), str(incoming), str(backup)]
    except Exception:
        if incoming.is_dir(): shutil.rmtree(incoming)
        shutil.rmtree(work)
        raise


def handoff(plan):
    subprocess.Popen(['/bin/sh', *plan], start_new_session=True,
                     stdin=subprocess.DEVNULL, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
