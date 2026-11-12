"""Verify the packaged entry point, MCP framing and private-data exclusions."""
import json
import plistlib
import subprocess
from pathlib import Path

root=Path(__file__).resolve().parent/'dist/Team Calendar Widget.app'
info=plistlib.loads((root/'Contents/Info.plist').read_bytes())
assert info['LSUIElement'] and info['NSAppleEventsUsageDescription']
binary=root/'Contents/MacOS/GoogleCalendarWidget'
requests=[dict(jsonrpc='2.0',id=1,method='initialize'),dict(jsonrpc='2.0',id=2,method='tools/list')]
result=subprocess.run([str(binary),'--mcp'],input='\n'.join(map(json.dumps,requests))+'\n',text=True,encoding='utf-8',capture_output=True,check=True)
lines=[json.loads(line) for line in result.stdout.splitlines()]
assert len(lines)==2 and len(lines[1]['result']['tools'])==26
tls=subprocess.run([str(binary),'--updates-test'],text=True,encoding='utf-8',capture_output=True,check=True)
assert json.loads(tls.stdout)['trusted_roots']>100
for path in root.rglob('*'):
    assert path.name not in {'calendar-data.json','config.toml','settings-v2.xml','ex.png'}
    assert not path.name.startswith('events-')
print('PASS: bundled MCP (26 tools), Finder metadata, no user data/settings/screenshots')
