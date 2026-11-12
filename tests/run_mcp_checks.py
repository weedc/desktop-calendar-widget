"""Check native MCP framing; --live also checks an already-running widget."""
from pathlib import Path
import json
import subprocess
import sys

root = Path(__file__).resolve().parents[1]
exe = root / 'bin/GoogleCalendarWidget.exe'
requests = [
    {'jsonrpc': '2.0', 'id': 1, 'method': 'initialize', 'params': {'protocolVersion': '2024-11-05'}},
    {'jsonrpc': '2.0', 'method': 'notifications/initialized'},
    {'jsonrpc': '2.0', 'id': 2, 'method': 'ping'},
    {'jsonrpc': '2.0', 'id': 3, 'method': 'tools/list'},
    {'jsonrpc': '2.0', 'id': 4, 'method': 'unknown/method'},
]
if '--live' in sys.argv:
    for name, args in [
        ('calendar_widget_status', {}),
        ('calendar_widget_sync_settings', {}),
        ('calendar_widget_daily_summary', {}),
        ('calendar_widget_emoji_pending', {}),
        ('calendar_widget_emoji_values', {}),
        ('calendar_widget_events', {'month': '2099-04', 'cached_only': True}),
        ('calendar_widget_set_emoji', {'key': 'stale-test-key', 'icon': '🎨'}),
    ]:
        requests.append({'jsonrpc': '2.0', 'id': len(requests), 'method': 'tools/call', 'params': {'name': name, 'arguments': args}})
result = subprocess.run([str(exe), '--mcp'], input=''.join(json.dumps(x, ensure_ascii=True)+'\n' for x in requests).encode(), capture_output=True, timeout=30)
assert result.returncode == 0, 'MCP process failed'
rows = [json.loads(line) for line in result.stdout.decode('utf-8-sig').splitlines()]
assert len(rows) == len(requests)-1, 'Notification must not receive a response'
by_id = {x['id']: x for x in rows}
assert by_id[1]['result']['serverInfo']['version'] == '5.7.1'
assert by_id[2]['result'] == {}
assert by_id[4]['error']['code'] == -32601
tools = {x['name']: x for x in by_id[3]['result']['tools']}
assert len(tools) == 26
assert tools['calendar_widget_events']['inputSchema']['properties']['cached_only']['type'] == 'boolean'
assert tools['calendar_widget_set_emoji']['inputSchema']['required'] == ['key', 'icon']
assert tools['calendar_widget_emoji_pending']['annotations']['readOnlyHint']
if '--live' in sys.argv:
    values = []
    for row in rows[4:]:
        response = row['result']
        values.append((response, json.loads(response['content'][0]['text'])))
    status = values[0][1]
    assert values[1][1]['intervalMinutes'] == 60
    assert values[2][1]['delivery'] == 'Windows'
    assert isinstance(values[3][1]['items'], list)
    assert isinstance(values[4][1]['items'], list)
    assert values[5][1]['available'] is False
    assert values[6][0]['isError'] and 'error' in values[6][1]
    assert 'autoSyncEnabled' in status
print('PASS: MCP framing, 26 tool schemas' + (', seven live routes and stale-key rejection' if '--live' in sys.argv else ''))
