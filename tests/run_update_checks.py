"""Isolated update policy and staged native controls; no network or live widget."""
import json,subprocess,sys
from pathlib import Path
root=Path(__file__).resolve().parents[1]
exe=Path(sys.argv[1]) if len(sys.argv)>1 else root/'bin/GoogleCalendarWidget.exe'
p=subprocess.run([str(exe),'--updates-test'],capture_output=True,timeout=30)
result=json.loads(p.stdout.decode('utf-8-sig'))
assert p.returncode==0 and result.get('passed')==13,result
print(json.dumps(result,ensure_ascii=True,indent=2))
