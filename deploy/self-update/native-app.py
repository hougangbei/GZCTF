#!/usr/bin/env python3
"""Start/stop a configured native app without exposing process commands to the web UI."""
import json
from pathlib import Path
import sys
from update_deployment import NativeDeployment

if len(sys.argv)!=3 or sys.argv[2] not in {'start','stop','status'}:
    raise SystemExit('Usage: native-app.py /absolute/path/to/updater.json start|stop|status')
config_path=Path(sys.argv[1])
if config_path.stat().st_mode & 0o077: raise SystemExit('Configuration must be private (chmod 600).')
adapter=NativeDeployment(json.loads(config_path.read_text()))
if sys.argv[2]=='start': adapter.preflight(); adapter.start(); adapter.health()
elif sys.argv[2]=='stop': adapter.stop()
else: print(adapter.current())
