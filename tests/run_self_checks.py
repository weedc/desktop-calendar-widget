"""Run isolated native checks without opening the desktop widget."""
from pathlib import Path
import json
import subprocess
import sys

root = Path(__file__).resolve().parents[1]
result = subprocess.run([str(Path(sys.argv[1]) if len(sys.argv) > 1 else root / "bin/GoogleCalendarWidget.exe"), "--self-test"], capture_output=True, timeout=30)
try:
    data = json.loads(result.stdout.decode("utf-8-sig"))
except (ValueError, UnicodeError):
    raise SystemExit("The native self-check did not return valid JSON")
if result.returncode or "error" in data:
    raise SystemExit(data.get("error", "Native self-check failed"))
assert data["passed"] == 82, data
print(json.dumps(data, ensure_ascii=True, indent=2))
