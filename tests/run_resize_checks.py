"""Real WinForms control drags on normal and parented fixture windows."""
import json
import subprocess
from pathlib import Path

root = Path(__file__).resolve().parents[1]
process = subprocess.run([str(root / 'bin/GoogleCalendarWidget.exe'), '--resize-test'], capture_output=True, timeout=30)
result = json.loads(process.stdout.decode('utf-8-sig'))
assert process.returncode == 0 and result.get('passed') == (27 if result.get('desktopAvailable') else 18), result
print(json.dumps(result, indent=2))
