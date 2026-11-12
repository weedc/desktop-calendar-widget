# Bundled runtime components

Certifi supplies Mozilla's CA certificates for update checks (MPL 2.0): https://github.com/certifi/python-certifi

The macOS app includes Python, PySide6 / Qt, Shiboken, PyObjC and the IANA time-zone database. Their licenses remain with their respective authors. License files supplied by the installed distributions are copied into the app's `Contents/Resources/ThirdPartyLicenses` folder.

Qt for Python's community distribution offers LGPLv3/GPLv3 licensing. The app uses the dynamically linked Qt libraries and ships them as separate libraries/frameworks; this packaging does not forbid users from replacing them or debugging modifications. The app is ad-hoc signed and a modified bundle must be signed again locally to run on macOS.

- Qt for Python licensing and source: https://doc.qt.io/qtforpython-6/licenses.html and https://code.qt.io/pyside/pyside-setup.git
- Qt source: https://code.qt.io/qt/qtbase.git
- Python source and license: https://www.python.org/downloads/source/ and https://docs.python.org/3/license.html
- PyObjC source and license: https://github.com/ronaldoussoren/pyobjc
- Time-zone data: https://www.iana.org/time-zones

These third-party notices do not assign a new license to this repository's own source.
