"""Local helper for inspecting/testing the widget's own supported command interface."""
import json
import sys
sys.stdout.reconfigure(encoding='utf-8')

command = {'command': sys.argv[1] if len(sys.argv) > 1 else 'status'}
for arg in sys.argv[2:]:
    key, value = arg.split('=', 1)
    command[key] = value
with open(r'\\.\pipe\DesktopCalendarWidget.v2', 'r+b', buffering=0) as pipe:
    pipe.write(json.dumps(command).encode('utf-8') + b'\n')
    response = bytearray()
    while not response.endswith(b'\n'):
        chunk = pipe.read(1)
        if not chunk:
            break
        response.extend(chunk)
print(response.decode('utf-8').strip())
