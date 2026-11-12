"""Exercise Windows handoff with disposable executables, never the live widget."""
import json
import os
import subprocess
import tempfile
import time
from pathlib import Path

root = Path(__file__).resolve().parents[1]
compiler = Path(os.environ['WINDIR']) / 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
powershell = Path(os.environ['WINDIR']) / 'System32/WindowsPowerShell/v1.0/powershell.exe'
with tempfile.TemporaryDirectory(prefix='calendar-install-check-') as folder:
    folder = Path(folder)
    source = folder / 'Fixture.cs'
    source.write_text('''using System; using System.IO; using System.Threading;
class Fixture { static void Main(string[] args) {
 if (args.Length > 0) { Thread.Sleep(1000); return; }
 File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"started.txt"), "updated");
} }''', encoding='utf-8')
    old = folder / 'GoogleCalendarWidget-v0.0.1.exe'
    new = folder / 'GoogleCalendarWidget-v0.0.2.exe'
    subprocess.run([str(compiler), '/nologo', '/target:winexe', '/out:' + str(new), str(source)], check=True, capture_output=True)
    old.write_bytes(new.read_bytes())
    helper = folder / 'UpdateInstall.ps1'
    helper.write_text((root / 'UpdateInstall.ps1').read_text(encoding='utf-8-sig'), encoding='utf-8-sig')
    previous = subprocess.Popen([str(old), '--wait'])
    plan = folder / 'plan.json'
    plan.write_text(json.dumps(dict(parent=previous.pid, installed=str(old), target=str(new), version='0.0.2')), encoding='utf-8')
    subprocess.run([str(powershell), '-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass', '-File', str(helper), '-PlanPath', str(plan)], check=True, capture_output=True, timeout=15)
    previous.wait(timeout=5)
    deadline = time.monotonic() + 5
    while not (folder / 'started.txt').exists() and time.monotonic() < deadline: time.sleep(.05)
    assert (folder / 'started.txt').read_text() == 'updated'
    assert old.exists(), 'Previous version must stay available to existing MCP servers'
print('PASS: actual Windows helper waits, launches new executable, preserves old MCP binary')
