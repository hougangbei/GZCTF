#!/usr/bin/env python3
"""Add a SHA-256 manifest to an already-published, validation-capable GZCTF release."""
import hashlib
import json
from pathlib import Path
import sys
from update_sources import verify_release

if len(sys.argv)!=4:
    raise SystemExit('Usage: make-release.py /published/directory runtime-id version')
root=Path(sys.argv[1]).resolve(); runtime=sys.argv[2]; version=sys.argv[3]
if not (root/'GZCTF.dll').is_file() or not (root/'wwwroot/index.html').is_file():
    raise SystemExit('Release must include GZCTF.dll and wwwroot/index.html.')
for path in root.rglob('*'):
    if path.is_symlink() or path.name.startswith(('appsettings','.env')) or path.name=='files':
        raise SystemExit('Remove runtime settings, environment files, uploads and symlinks before packaging.')
manifest={'project':'GZCTF','validationProtocol':1,'runtime':runtime,'version':version,
    'files':{path.relative_to(root).as_posix():hashlib.sha256(path.read_bytes()).hexdigest()
        for path in root.rglob('*') if path.is_file() and path!=root/'gzctf-release.json'}}
(root/'gzctf-release.json').write_text(json.dumps(manifest,indent=2))
verify_release(root,runtime)
print('Release manifest verified: '+str(root/'gzctf-release.json'))
