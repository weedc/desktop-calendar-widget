"""Public package validation without accessing an installed app."""
import plistlib
import stat
import tempfile
import unittest
import zipfile
import subprocess
import sys
import shlex
from pathlib import Path
from unittest.mock import patch
from installer import BUNDLE, BUNDLE_ID, HELPER, validate_archive, validate_bundle, download, prepare


class InstallerTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory(); self.addCleanup(self.tmp.cleanup)
        self.root = Path(self.tmp.name)

    def archive(self, extra=None, link=None):
        path = self.root / 'release.zip'
        with zipfile.ZipFile(path, 'w') as z:
            z.writestr(BUNDLE + '/Contents/Info.plist', b'fixture')
            if extra: z.writestr(extra, b'fixture')
            if link:
                info = zipfile.ZipInfo(BUNDLE + '/Contents/Frameworks/Python.framework/Versions/Current')
                info.create_system = 3; info.external_attr = (stat.S_IFLNK | 0o777) << 16
                z.writestr(info, link)
        return path

    def test_framework_symlinks_preserved(self):
        validate_archive(self.archive(link='3.12'))

    def test_path_traversal_and_foreign_roots_rejected(self):
        for name in ('../outside', '/outside', BUNDLE + '/../outside', 'Other.app/Contents/x', 'C:\\outside'):
            with self.subTest(name=name), self.assertRaises(ValueError): validate_archive(self.archive(extra=name))

    def test_escaping_symlinks_rejected(self):
        for link in ('/etc/passwd', '../../../../../outside', '..\\outside'):
            with self.subTest(link=link), self.assertRaises(ValueError): validate_archive(self.archive(link=link))

    def test_bundle_version_and_identifier(self):
        bundle = self.root / BUNDLE; (bundle / 'Contents/MacOS').mkdir(parents=True)
        (bundle / 'Contents/MacOS/GoogleCalendarWidget').write_bytes(b'fixture')
        (bundle / 'Contents/Info.plist').write_bytes(plistlib.dumps(dict(CFBundleIdentifier=BUNDLE_ID, CFBundleShortVersionString='5.8.0')))
        self.assertTrue(validate_bundle(bundle, '5.8.0').is_file())
        with self.assertRaises(ValueError): validate_bundle(bundle, '5.9.0')
        (bundle / 'Contents/Info.plist').write_bytes(plistlib.dumps(dict(CFBundleIdentifier='other.app', CFBundleShortVersionString='5.8.0')))
        with self.assertRaises(ValueError): validate_bundle(bundle, '5.8.0')

    def test_wrong_architecture_download_rejected_before_network(self):
        result = dict(version='5.8.0', url='https://github.com/weedc/desktop-calendar-widget/releases/download/test/DesktopCalendarWidget-v5.8.0-mac-x86_64.zip')
        with patch('urllib.request.build_opener') as request:
            with self.assertRaises(ValueError): download(result, 'mac-arm64', self.root / 'download.zip')
            request.assert_not_called()

    def test_source_mode_cannot_replace_installed_apps(self):
        with patch('sys.platform', 'darwin'), patch('sys.frozen', False, create=True):
            with self.assertRaises(ValueError): prepare({}, 'mac-arm64')

    @unittest.skipUnless(sys.platform == 'darwin', 'Native Mac bundle swap')
    def test_native_helper_swap_and_launch(self):
        self.run_helper(False)

    @unittest.skipUnless(sys.platform == 'darwin', 'Native Mac bundle rollback')
    def test_native_helper_restores_previous_bundle_if_launch_fails(self):
        self.run_helper(True)

    def run_helper(self, fail_first):
        installed = self.root / 'Installed Calendar.app'
        incoming = self.root / '.TeamCalendarWidget-update-fixture.app'
        backup = self.root / '.TeamCalendarWidget-previous-fixture.app'
        installed.mkdir(); incoming.mkdir()
        (installed / 'version').write_text('old'); (incoming / 'version').write_text('new')
        trace = self.root / 'launches'
        opener = self.root / 'open-fixture.sh'
        opener.write_text('#!/bin/sh\nprintf "launch\\n" >> ' + shlex.quote(str(trace)) + '\n' +
                          ('[ "$(wc -l < ' + shlex.quote(str(trace)) + ')" -eq 1 ] && exit 1\n' if fail_first else '') + 'exit 0\n')
        opener.chmod(0o700)
        helper = self.root / 'install.sh'
        helper.write_text(HELPER.replace('/usr/bin/open', shlex.quote(str(opener))))
        result = subprocess.run(['/bin/sh', str(helper), '999999', str(installed), str(incoming), str(backup)], timeout=10, capture_output=True)
        self.assertEqual(result.returncode, 1 if fail_first else 0, result.stderr)
        self.assertEqual((installed / 'version').read_text(), 'old' if fail_first else 'new')
        self.assertEqual(len(trace.read_text().splitlines()), 2 if fail_first else 1)
        if not fail_first: self.assertEqual((backup / 'version').read_text(), 'old')


if __name__ == '__main__': unittest.main()
