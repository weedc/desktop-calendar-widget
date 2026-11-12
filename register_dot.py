"""Register the widget MCP server while preserving existing Codex settings."""
from datetime import datetime
import json
import os
from pathlib import Path
import shutil
import sys
import re
import tomllib

config = Path(os.environ.get('CODEX_HOME', str(Path.home() / '.codex'))) / 'config.toml'
install = Path(os.environ['LOCALAPPDATA']) / 'Programs/GoogleCalendarWidget'
exe = install / (sys.argv[1] if len(sys.argv) > 1 else 'GoogleCalendarWidget-v5.7.0.exe')
if not re.fullmatch(r'GoogleCalendarWidget-v[0-9.]+\.exe', exe.name) or exe.parent.resolve() != install.resolve() or not exe.is_file():
    raise RuntimeError('Expected an installed versioned widget executable.')
original = config.read_bytes()
text = original.decode('utf-8-sig')
parsed = tomllib.loads(text)
existing = parsed.get('mcp_servers', {}).get('team_calendar_widget')
if existing:
    expected_old = install / 'GoogleCalendarWidget.exe'
    old_command = Path(existing.get('command', ''))
    owned = old_command.parent.resolve() == install.resolve() and (old_command.name == 'GoogleCalendarWidget.exe' or re.fullmatch(r'GoogleCalendarWidget-v[0-9.]+\.exe', old_command.name))
    if not owned or existing.get('args') != ['--mcp']:
        raise RuntimeError('A different MCP server already uses this name.')
    if existing.get('command') != str(exe):
        import re
        backup = config.with_name('config.toml.calendar-backup-' + datetime.now().strftime('%Y%m%d-%H%M%S'))
        shutil.copy2(config, backup)
        pattern = r'(\[mcp_servers\.team_calendar_widget\]\s*\ncommand\s*=\s*)[^\n]+'
        updated, count = re.subn(pattern, lambda m: m.group(1) + json.dumps(str(exe)), text)
        if count != 1:
            raise RuntimeError('Widget MCP section could not be updated safely.')
        tomllib.loads(updated)
        config.write_bytes((b'\xef\xbb\xbf' if original.startswith(b'\xef\xbb\xbf') else b'') + updated.encode('utf-8'))
        print('Widget MCP executable updated; previous settings backed up.')
    else:
        print('Widget MCP already registered.')
else:
    backup = config.with_name('config.toml.calendar-backup-' + datetime.now().strftime('%Y%m%d-%H%M%S'))
    shutil.copy2(config, backup)
    addition = '\n\n[mcp_servers.team_calendar_widget]\ncommand = ' + json.dumps(str(exe)) + '\nargs = ["--mcp"]\nenabled = true\nstartup_timeout_sec = 10\ntool_timeout_sec = 45\n'
    updated = text.rstrip() + addition
    tomllib.loads(updated)
    config.write_bytes((b'\xef\xbb\xbf' if original.startswith(b'\xef\xbb\xbf') else b'') + updated.encode('utf-8'))
    print('Widget MCP registered; previous settings preserved and backed up.')
