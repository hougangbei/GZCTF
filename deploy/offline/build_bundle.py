#!/usr/bin/env python3
"""Export already-built amd64 application/PostgreSQL/Redis images into a verified bundle."""
import argparse
import gzip
import json
from pathlib import Path
import shutil
import subprocess
import sys
import manage

parser=argparse.ArgumentParser(description=__doc__)
parser.add_argument('--app',required=True)
parser.add_argument('--db',default='postgres:15-alpine')
parser.add_argument('--redis',default='redis:7-alpine')
parser.add_argument('--output',required=True)
args=parser.parse_args()
root=Path(args.output).resolve()
if root.exists():raise SystemExit('Output directory already exists; choose a new directory')
root.mkdir(parents=True)
source=Path(__file__).resolve().parent
images={}
for kind,tag,major in [('app',args.app,None),('db',args.db,15),('redis',args.redis,7)]:
    info=json.loads(manage.docker('image','inspect',tag))[0]
    if info['Os']!='linux' or info['Architecture']!='amd64':raise SystemExit(kind+' is not linux/amd64')
    if kind=='db':
        value=subprocess.check_output(['docker','run','--rm','--platform','linux/amd64','--network','none','--entrypoint','postgres',tag,'--version'],text=True)
        if not value.startswith('postgres (PostgreSQL) 15.'):raise SystemExit('Expected PostgreSQL 15')
    if kind=='redis':
        value=subprocess.check_output(['docker','run','--rm','--platform','linux/amd64','--network','none','--entrypoint','redis-server',tag,'--version'],text=True)
        if 'v=7.' not in value:raise SystemExit('Expected Redis 7')
    identity=manage.fingerprint(info)
    fixed='gzctf-offline/'+kind+':'+identity[:16]+'-amd64'
    manage.docker('tag',tag,fixed)
    images[kind]={'tag':fixed,'fingerprint':identity,'major':major,'source':tag,'repoDigests':info.get('RepoDigests',[])}
    print(kind+': '+fixed,flush=True)
with (root/'images.tar.gz').open('wb') as output:
    proc=subprocess.Popen(['docker','save']+[item['tag'] for item in images.values()],stdout=subprocess.PIPE)
    with gzip.GzipFile(fileobj=output,mode='wb',mtime=0) as compressed:
        shutil.copyfileobj(proc.stdout,compressed,1024*1024)
    proc.stdout.close()
    if proc.wait()!=0:raise SystemExit('Docker image export failed')
for name in manage.PACKAGE_FILES:
    if name!='images.tar.gz':shutil.copyfile(str(source/name),str(root/name))
manifest={'schema':1,'platform':'linux/amd64','images':images,
          'files':{name:manage.sha256(root/name) for name in manage.PACKAGE_FILES}}
(root/'manifest.json').write_text(json.dumps(manifest,indent=2))
manage.load_manifest(root)
manage.verify_images(manifest)
print('Bundle verified: '+str(root))
