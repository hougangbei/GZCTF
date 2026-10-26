#!/usr/bin/env python3
"""Offline GZCTF deployment. Python 3.8+, Docker Engine and Compose v2.18.1+."""
import argparse
import contextlib
import datetime
import fcntl
import hashlib
import ipaddress
import json
import os
from pathlib import Path
import platform
import secrets
import shutil
import subprocess
import sys
import tarfile
import time

PACKAGE_FILES = ('manage.py', 'compose.yml', 'README.md', 'images.tar.gz')
SNAPSHOT_FILES = ('.env', 'appsettings.json', 'compose.yml', 'release.json', 'manage.py', 'compose.site.yml', 'installation.json')

class OfflineError(RuntimeError):
    pass

def sha256(path):
    digest = hashlib.sha256()
    with Path(path).open('rb') as source:
        for chunk in iter(lambda: source.read(1024 * 1024), b''):
            digest.update(chunk)
    return digest.hexdigest()

def safe_child(root, relative):
    root = Path(root).resolve()
    path = Path(relative)
    if path.is_absolute() or not path.parts or any(p in ('..', '.') for p in path.parts):
        raise OfflineError('Unsafe relative path: ' + str(relative))
    current = root
    for part in path.parts:
        current = current / part
        if current.is_symlink():
            raise OfflineError('Symlinks are not allowed: ' + str(relative))
    try:
        current.resolve().relative_to(root)
    except ValueError:
        raise OfflineError('Path escapes deployment directory')
    return current

def check_hashes(root, files):
    if not isinstance(files, dict) or not files:
        raise OfflineError('Missing integrity manifest')
    for name, expected in files.items():
        path = safe_child(root, name)
        if not path.is_file() or sha256(path) != expected:
            raise OfflineError('Integrity check failed: ' + name)

def load_manifest(root):
    root = Path(root)
    manifest = json.loads((root / 'manifest.json').read_text())
    if manifest.get('schema') != 1 or set(manifest.get('images', {})) != {'app', 'db', 'redis'}:
        raise OfflineError('Unsupported bundle manifest')
    if set(manifest.get('files', {})) != set(PACKAGE_FILES):
        raise OfflineError('Incomplete bundle file list')
    check_hashes(root, manifest['files'])
    return manifest

def load_backup(root, instance):
    root = Path(root)
    metadata = json.loads((root / 'backup.json').read_text())
    if metadata.get('instance') != instance:
        raise OfflineError('Backup belongs to a different installation')
    check_hashes(root, metadata.get('files'))
    return metadata

def compatible_update(old, new):
    if old.get('schema') != 1 or new.get('schema') != 1:
        raise OfflineError('Unsupported deployment schema')
    for key in ('db', 'redis'):
        for field in ('major', 'fingerprint'):
            if old['images'][key].get(field) != new['images'][key].get(field):
                raise OfflineError('Application updates cannot replace database/cache images; plan a separate service upgrade')

def require_volumes(env, exists):
    for key in ('DB_VOLUME', 'REDIS_VOLUME'):
        if not env.get(key) or not exists(env[key]):
            raise OfflineError('Required persistent volume is missing: ' + env.get(key, key))

def validate_volume_identity(name, info, identity):
    recorded = identity.get('volumes', {}).get(name, {})
    if (info.get('Name') != name or (info.get('Labels') or {}).get('gzctf.offline.instance') != identity.get('instance')
            or not recorded.get('created') or info.get('CreatedAt') != recorded['created']):
        raise OfflineError('Persistent volume identity changed: ' + name)

def validate_effective_config(config, env, root):
    for service, alias, key, target in [('db', 'db_data', 'DB_VOLUME', '/var/lib/postgresql/data'),
                                       ('redis', 'redis_data', 'REDIS_VOLUME', '/data')]:
        volume = config.get('volumes', {}).get(alias, {})
        mounts = config['services'][service].get('volumes', [])
        if volume.get('name') != env[key] or not volume.get('external') or not any(
                v.get('type') == 'volume' and v.get('source') == alias and v.get('target') == target for v in mounts):
            raise OfflineError('Effective Compose storage changed: ' + service)
    for service, key in [('app', 'APP_IMAGE'), ('db', 'DB_IMAGE'), ('redis', 'REDIS_IMAGE')]:
        if config['services'][service].get('image') != env[key]:
            raise OfflineError('Effective Compose image changed: ' + service)
    if not any(v.get('type') == 'bind' and Path(v.get('source', '')).resolve() == root / 'files'
               and v.get('target') == '/app/files' for v in config['services']['app'].get('volumes', [])):
        raise OfflineError('Effective application files mount changed')

def write_private(path, content, exclusive=False):
    path = Path(path)
    if exclusive:
        fd = os.open(str(path), os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
        with os.fdopen(fd, 'w') as target:
            target.write(content)
    else:
        temp = path.with_name(path.name + '.tmp-' + secrets.token_hex(4))
        write_private(temp, content, exclusive=True)
        os.replace(str(temp), str(path))

def env_read(path):
    return dict(line.split('=', 1) for line in Path(path).read_text().splitlines()
                if line and not line.startswith('#') and '=' in line)

def fingerprint(info):
    config = info['Config']
    value = {'os': info['Os'], 'arch': info['Architecture'], 'layers': info['RootFS']['Layers']}
    for key in ('Entrypoint', 'Cmd', 'Env', 'WorkingDir', 'User', 'Labels', 'ExposedPorts', 'Volumes', 'Healthcheck'):
        value[key] = config.get(key)
    return hashlib.sha256(json.dumps(value, sort_keys=True, separators=(',', ':')).encode()).hexdigest()

def command(args, **kwargs):
    result = subprocess.run([str(x) for x in args], stdout=kwargs.pop('stdout', subprocess.PIPE),
                            stderr=subprocess.PIPE, **kwargs)
    if result.returncode:
        # Do not echo command arguments or configuration, which may include secrets.
        raise OfflineError('Command failed (' + str(args[0]) + '). Check service logs locally; credentials are not printed.')
    return result.stdout

def docker(*args, **kwargs):
    return command(['docker'] + list(args), **kwargs)

def verify_images(manifest):
    for kind, image in manifest['images'].items():
        info = json.loads(docker('image', 'inspect', image['tag']))[0]
        if info['Os'] != 'linux' or info['Architecture'] != 'amd64' or fingerprint(info) != image['fingerprint']:
            raise OfflineError('Image platform/content mismatch: ' + kind)

def now():
    return datetime.datetime.now(datetime.timezone.utc).strftime('%Y%m%dT%H%M%SZ') + '-' + secrets.token_hex(3)

def safe_copytree(source, target):
    for item in Path(source).rglob('*'):
        if item.is_symlink():
            raise OfflineError('Symlinks cannot be included in file backups: ' + str(item))
    shutil.copytree(str(source), str(target))

def restore_files(root, source):
    staging = root / ('files-restore-staging-' + now())
    safe_copytree(source, staging)
    expected = {str(p.relative_to(source)): sha256(p) for p in source.rglob('*') if p.is_file()}
    if expected:
        check_hashes(staging, expected)
    current = root / 'files'
    if current.exists():
        os.rename(str(current), str(root / ('files-before-restore-' + now())))
    # A retry also works when interruption happened after moving the original directory.
    os.rename(str(staging), str(current))

class Deployment:
    def __init__(self, root, test_host=False):
        self.root = Path(root).resolve()
        if str(self.root) in ('/', '/etc', '/usr', '/opt', '/var', '/home', str(Path.home())):
            raise OfflineError('Use a dedicated installation directory')
        self.test_host = test_host

    def preflight(self):
        if not self.test_host and (platform.system() != 'Linux' or platform.machine() not in ('x86_64', 'amd64')):
            raise OfflineError('Production installation requires Linux x86_64')
        version = docker('compose', 'version', '--short').decode().strip().lstrip('v').split('.')
        if tuple(map(int, version[:3])) < (2, 18, 1):
            raise OfflineError('Docker Compose v2.18.1+ is required')
        self.root.mkdir(parents=True, exist_ok=True)
        if shutil.disk_usage(str(self.root)).free < 2 * 1024 ** 3:
            raise OfflineError('At least 2 GiB free space is required, plus backup space')

    @contextlib.contextmanager
    def lock(self):
        with (self.root / '.operation.lock').open('a') as handle:
            try:
                fcntl.flock(handle, fcntl.LOCK_EX | fcntl.LOCK_NB)
            except BlockingIOError:
                raise OfflineError('Another deployment operation is in progress')
            yield

    def env(self):
        result = env_read(self.root / '.env')
        if not result.get('INSTANCE_ID'):
            raise OfflineError('Missing installation identity')
        return result

    def compose(self, *args, **kwargs):
        return command(['docker', 'compose', '--project-directory', self.root, '--env-file', self.root / '.env',
                        '-p', self.env()['COMPOSE_PROJECT'], '-f', self.root / 'compose.yml', '-f', self.root / 'compose.site.yml'] + list(args), **kwargs)

    def volume_exists(self, name):
        return subprocess.run(['docker', 'volume', 'inspect', name], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL).returncode == 0

    def installed(self, allow_recovery=False):
        env = self.env()
        require_volumes(env, self.volume_exists)
        if (self.root / 'recovery-required.json').exists() and not allow_recovery:
            raise OfflineError('Recovery is required; inspect recovery-required.json and restore before updating')
        config = json.loads(self.compose('config', '--format', 'json'))
        validate_effective_config(config, env, self.root)
        identity = json.loads((self.root / 'installation.json').read_text())
        if identity.get('instance') != env['INSTANCE_ID']:
            raise OfflineError('Installation identity changed')
        for service, key, target in [('db', 'DB_VOLUME', '/var/lib/postgresql/data'), ('redis', 'REDIS_VOLUME', '/data')]:
            info = json.loads(docker('volume', 'inspect', env[key]))[0]
            validate_volume_identity(env[key], info, identity)
            ids = self.compose('ps', '-a', '-q', service).decode().split()
            if not ids:
                raise OfflineError('Expected service container is missing: ' + service)
            container = json.loads(docker('inspect', ids[0]))[0]
            if not any(v.get('Type') == 'volume' and v.get('Name') == env[key] and v.get('Destination') == target
                       for v in container['Mounts']):
                raise OfflineError('Running service storage changed: ' + service)
            image = json.loads(docker('image', 'inspect', container['Image']))[0]
            if fingerprint(image) != self.release()['images'][service]['fingerprint']:
                raise OfflineError('Running service image changed: ' + service)
        return env

    def release(self):
        return json.loads((self.root / 'release.json').read_text())

    def save_release(self, bundle, manifest):
        for name in ('compose.yml', 'manage.py'):
            write_private(self.root / name, (Path(bundle) / name).read_text())
        write_private(self.root / 'release.json', json.dumps(manifest, indent=2))
        env = self.env()
        for key, kind in [('APP_IMAGE', 'app'), ('DB_IMAGE', 'db'), ('REDIS_IMAGE', 'redis')]:
            env[key] = manifest['images'][kind]['tag']
        write_private(self.root / '.env', ''.join(k + '=' + v + '\n' for k, v in env.items()))

    def validate_candidate(self, bundle, manifest):
        env = self.env()
        for key, kind in [('APP_IMAGE', 'app'), ('DB_IMAGE', 'db'), ('REDIS_IMAGE', 'redis')]:
            env[key] = manifest['images'][kind]['tag']
        candidate = self.root / ('.candidate-env-' + secrets.token_hex(5))
        write_private(candidate, ''.join(k + '=' + v + '\n' for k, v in env.items()), exclusive=True)
        try:
            output = command(['docker', 'compose', '--project-directory', self.root, '--env-file', candidate,
                              '-p', env['COMPOSE_PROJECT'], '-f', Path(bundle) / 'compose.yml',
                              '-f', self.root / 'compose.site.yml', 'config', '--format', 'json'])
            validate_effective_config(json.loads(output), env, self.root)
        finally:
            candidate.unlink(missing_ok=True)

    def recovery_targets(self, previous, identity, release):
        require_volumes(previous, self.volume_exists)
        targets = {}
        for service, key, target in [('db', 'DB_VOLUME', '/var/lib/postgresql/data'), ('redis', 'REDIS_VOLUME', '/data')]:
            validate_volume_identity(previous[key], json.loads(docker('volume', 'inspect', previous[key]))[0], identity)
            ids = docker('ps', '-a', '-q', '--filter', 'label=com.docker.compose.project=' + previous['COMPOSE_PROJECT'],
                         '--filter', 'label=com.docker.compose.service=' + service).decode().split()
            if len(ids) != 1:
                raise OfflineError('Cannot identify the original service container: ' + service)
            container = json.loads(docker('inspect', ids[0]))[0]
            if not any(v.get('Type') == 'volume' and v.get('Name') == previous[key] and v.get('Destination') == target
                       for v in container['Mounts']):
                raise OfflineError('Recovery service storage changed: ' + service)
            if fingerprint(json.loads(docker('image', 'inspect', container['Image']))[0]) != release['images'][service]['fingerprint']:
                raise OfflineError('Recovery service image changed: ' + service)
            targets[service] = ids[0]
        return targets

    def import_bundle(self, bundle):
        manifest = load_manifest(bundle)
        print('Bundle checksums verified. Importing fixed amd64 images...', flush=True)
        docker('load', '--input', Path(bundle) / 'images.tar.gz')
        verify_images(manifest)
        return manifest

    def healthy(self, service, attempts=100):
        for _ in range(attempts):
            ids = self.compose('ps', '-q', service).decode().split()
            if ids:
                info = json.loads(docker('inspect', ids[0]))[0]
                if info['State'].get('Health', {}).get('Status') == 'healthy':
                    return
                if info['State'].get('Status') in ('exited', 'dead', 'restarting'):
                    break
            time.sleep(2)
        raise OfflineError('Service did not become healthy: ' + service)

    def start_app(self):
        self.compose('up', '-d', '--no-deps', '--force-recreate', 'app')
        self.healthy('app')
        # Health checks alone do not prove the SPA is being served.
        body = self.compose('exec', '-T', 'app', 'wget', '-qO-', 'http://127.0.0.1:8080/leaderboard')
        if b'<html' not in body.lower():
            raise OfflineError('Application frontend check failed')

    def install(self, bundle, port, bind, prefix, data_subnet=None, frontend_subnet=None):
        ipaddress.ip_address(bind)
        if not (1 <= port <= 65535) or not prefix.isalnum() or len(prefix) > 24:
            raise OfflineError('Invalid port or installation prefix (letters/numbers only)')
        if any(p.name != '.operation.lock' for p in self.root.iterdir()):
            raise OfflineError('Installation directory is not empty; refusing to overwrite files or secrets')
        os.chmod(str(self.root), 0o700)
        names = [prefix + '-postgres', prefix + '-redis']
        if any(self.volume_exists(name) for name in names):
            raise OfflineError('Volume name already exists; use another prefix or explicitly migrate the existing installation')
        manifest = self.import_bundle(bundle)
        instance = secrets.token_hex(12)
        env = {'INSTANCE_ID': instance, 'COMPOSE_PROJECT': prefix, 'DB_VOLUME': names[0], 'REDIS_VOLUME': names[1],
               'POSTGRES_PASSWORD': secrets.token_hex(24), 'GZCTF_XOR_KEY': secrets.token_hex(32),
               'ADMIN_PASSWORD': 'Aa1!' + secrets.token_hex(20), 'BIND_ADDRESS': bind, 'HTTP_PORT': str(port)}
        write_private(self.root / '.env', ''.join(k + '=' + v + '\n' for k, v in env.items()), exclusive=True)
        for directory in ('files', 'backups', 'challenge-control'):
            (self.root / directory).mkdir(mode=0o700, exist_ok=True)
        settings = {'ContainerProvider': {'Type': 'Docker', 'DockerConfig': {'Uri': 'unix:///run/gzctf-challenges/docker.sock'},
                                          'PublicEntry': '', 'EnableTrafficCapture': False}, 'Registries': {}}
        write_private(self.root / 'appsettings.json', json.dumps(settings, indent=2), exclusive=True)
        site = {'services': {}}
        networks = {}
        for key, subnet in [('data', data_subnet), ('frontend', frontend_subnet)]:
            if subnet:
                value = ipaddress.ip_network(subnet, strict=True)
                if value.version != 4:
                    raise OfflineError('Use IPv4 Docker subnets')
                networks[key] = {'ipam': {'config': [{'subnet': str(value)}]}}
        if networks: site['networks'] = networks
        write_private(self.root / 'compose.site.yml', json.dumps(site, indent=2), exclusive=True)
        self.save_release(bundle, manifest)
        for name in names:
            docker('volume', 'create', '--label', 'gzctf.offline.instance=' + instance, name)
        identity = {'instance': instance, 'volumes': {name: {'created': json.loads(docker('volume', 'inspect', name))[0]['CreatedAt']} for name in names}}
        write_private(self.root / 'installation.json', json.dumps(identity, indent=2), exclusive=True)
        self.compose('config', '--quiet')
        self.compose('up', '-d', 'db', 'redis')
        self.healthy('db'); self.healthy('redis')
        self.start_app()
        print('Installed. Instance: ' + instance + '. Admin password is in the private .env; it is not printed.')

    def stop_writers(self):
        self.compose('stop', 'app')
        self.compose('exec', '-T', 'redis', 'redis-cli', 'SAVE')
        self.compose('stop', 'redis')

    def resume(self):
        self.compose('up', '-d', '--no-deps', 'redis')
        self.healthy('redis')
        self.start_app()

    def backup_stopped(self):
        env = self.env()
        files_size = sum(p.stat().st_size for p in (self.root / 'files').rglob('*') if p.is_file())
        db_size = int(self.compose('exec', '-T', 'db', 'psql', '-U', 'gzctf', '-d', 'gzctf', '-Atc', 'SELECT pg_database_size(current_database());').decode().strip())
        if shutil.disk_usage(str(self.root)).free < max(2 * 1024 ** 3, 3 * db_size + 3 * files_size):
            raise OfflineError('Insufficient free space for backup and recovery')
        backup = self.root / 'backups' / now()
        backup.mkdir(mode=0o700)
        with (backup / 'database.dump').open('wb') as target:
            self.compose('exec', '-T', 'db', 'pg_dump', '-U', 'gzctf', '-d', 'gzctf', '-Fc', stdout=target)
        with (backup / 'database.dump').open('rb') as source:
            self.compose('exec', '-T', 'db', 'pg_restore', '--list', stdin=source)
        safe_copytree(self.root / 'files', backup / 'files')
        with (backup / 'redis.tar').open('wb') as target:
            docker('run', '--rm', '--network', 'none', '--entrypoint', 'tar', '--mount',
                   'type=volume,src=' + env['REDIS_VOLUME'] + ',dst=/data,readonly', env['REDIS_IMAGE'], '-C', '/data', '-cf', '-', '.', stdout=target)
        for name in SNAPSHOT_FILES:
            shutil.copyfile(str(self.root / name), str(backup / name))
            os.chmod(str(backup / name), 0o600)
        hashes = {str(p.relative_to(backup)): sha256(p) for p in backup.rglob('*') if p.is_file()}
        write_private(backup / 'backup.json', json.dumps({'instance': env['INSTANCE_ID'], 'files': hashes}, indent=2))
        load_backup(backup, env['INSTANCE_ID'])
        print('Verified backup: ' + str(backup), flush=True)
        return backup

    def backup(self):
        self.installed()
        try:
            self.stop_writers()
            return self.backup_stopped()
        finally:
            self.resume()

    def recovery_marker(self, backup, reason):
        write_private(self.root / 'recovery-required.json', json.dumps({'backup': str(backup), 'reason': reason}, indent=2))

    def restore_stopped(self, backup):
        identity = json.loads((self.root / 'installation.json').read_text())
        metadata = load_backup(backup, identity['instance'])
        required = set(SNAPSHOT_FILES) | {'database.dump', 'redis.tar'}
        if not required.issubset(metadata['files']):
            raise OfflineError('Backup does not contain every recovery artifact')
        if not (backup / 'files').is_dir():
            raise OfflineError('Backup files directory is missing')
        previous = env_read(backup / '.env')
        identity = json.loads((self.root / 'installation.json').read_text())
        if identity != json.loads((backup / 'installation.json').read_text()) or previous['INSTANCE_ID'] != identity['instance']:
            raise OfflineError('Backup installation identity differs')
        release = json.loads((backup / 'release.json').read_text())
        verify_images(release)
        targets = self.recovery_targets(previous, identity, release)
        effective = command(['docker', 'compose', '--project-directory', self.root, '--env-file', backup / '.env',
                             '-p', previous['COMPOSE_PROJECT'], '-f', backup / 'compose.yml', '-f', backup / 'compose.site.yml',
                             'config', '--format', 'json'])
        validate_effective_config(json.loads(effective), previous, self.root)
        # Validate the entire Redis archive before removing cache volume contents.
        with tarfile.open(str(backup / 'redis.tar')) as archive:
            for member in archive.getmembers():
                name = member.name
                while name.startswith('./'): name = name[2:]
                if name in ('', '.'): continue
                safe_child(backup / 'redis-archive-validation', name)
                if not (member.isfile() or member.isdir()):
                    raise OfflineError('Unsupported Redis archive member')
        # Stop containers by independently verified project labels; do not parse a broken candidate Compose.
        apps = docker('ps', '-a', '-q', '--filter', 'label=com.docker.compose.project=' + previous['COMPOSE_PROJECT'],
                      '--filter', 'label=com.docker.compose.service=app').decode().split()
        if apps: docker('stop', *apps)
        docker('stop', targets['redis'])
        # Restore the verified old control files before any database operation. Retry does not depend on candidate configuration.
        for name in SNAPSHOT_FILES:
            write_private(self.root / name, (backup / name).read_text())
        env = previous
        self.installed(allow_recovery=True)
        self.compose('exec', '-T', 'db', 'dropdb', '-U', 'gzctf', '--force', '--if-exists', 'gzctf')
        self.compose('exec', '-T', 'db', 'createdb', '-U', 'gzctf', '-O', 'gzctf', 'gzctf')
        with (backup / 'database.dump').open('rb') as source:
            self.compose('exec', '-T', 'db', 'pg_restore', '-U', 'gzctf', '-d', 'gzctf', '--exit-on-error', '--no-owner', '--no-privileges', stdin=source)
        # Retain failed-version files for operator inspection instead of deleting them.
        restore_files(self.root, backup / 'files')
        with (backup / 'redis.tar').open('rb') as source:
            docker('run', '--rm', '-i', '--network', 'none', '--entrypoint', 'sh', '--mount',
                   'type=volume,src=' + env['REDIS_VOLUME'] + ',dst=/data', env['REDIS_IMAGE'], '-c',
                   'find /data -mindepth 1 -maxdepth 1 -exec rm -rf {} + && tar -C /data -xf -', stdin=source)
        for name in SNAPSHOT_FILES:
            write_private(self.root / name, (backup / name).read_text())
        self.resume()
        (self.root / 'recovery-required.json').unlink(missing_ok=True)
        print('Database, files, Redis and application version restored.', flush=True)

    def restore(self, backup, confirmation):
        identity = json.loads((self.root / 'installation.json').read_text())
        if confirmation != identity['instance']:
            raise OfflineError('Restore requires --confirm-instance matching the private .env INSTANCE_ID; it rewinds all data')
        backup = Path(backup).resolve()
        try: backup.relative_to((self.root / 'backups').resolve())
        except ValueError: raise OfflineError('Restore source must be inside this installation backups directory')
        load_backup(backup, identity['instance'])
        self.recovery_marker(backup, 'Operator requested full recovery')
        self.restore_stopped(backup)

    def update(self, bundle):
        self.installed()
        new = load_manifest(bundle)
        compatible_update(self.release(), new)
        self.validate_candidate(bundle, new)
        self.import_bundle(bundle)
        backup = None
        try:
            self.stop_writers()
            backup = self.backup_stopped()
            self.recovery_marker(backup, 'Update in progress; do not bypass recovery after interruption')
            self.save_release(bundle, new)
            self.resume()
            (self.root / 'recovery-required.json').unlink()
            print('Application updated; persistent volumes and secrets retained.')
        except Exception:
            if backup is not None:
                print('Candidate failed. Restoring the verified pre-update snapshot...', flush=True)
                self.restore_stopped(backup)
            else:
                self.resume()
            raise

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--root', required=True, help='Absolute installation directory, e.g. /opt/gzctf')
    parser.add_argument('--test-host', action='store_true', help=argparse.SUPPRESS)
    sub = parser.add_subparsers(dest='action', required=True)
    install = sub.add_parser('install'); install.add_argument('--bundle', required=True)
    install.add_argument('--port', type=int, default=18080); install.add_argument('--bind', default='127.0.0.1')
    install.add_argument('--prefix', default='gzctfintranet'); install.add_argument('--data-subnet'); install.add_argument('--frontend-subnet')
    sub.add_parser('backup')
    update = sub.add_parser('update'); update.add_argument('--bundle', required=True); update.add_argument('--maintenance-confirmed', action='store_true', required=True, help='Confirm external ingress is in maintenance mode; keep it closed until success')
    restore = sub.add_parser('restore'); restore.add_argument('--backup', required=True); restore.add_argument('--confirm-instance', required=True); restore.add_argument('--maintenance-confirmed', action='store_true', required=True)
    sub.add_parser('status')
    args = parser.parse_args()
    if not Path(args.root).is_absolute(): raise OfflineError('--root must be an absolute path')
    deployment = Deployment(args.root, args.test_host)
    deployment.preflight()
    with deployment.lock():
        if args.action == 'install': deployment.install(Path(args.bundle).resolve(), args.port, args.bind, args.prefix, args.data_subnet, args.frontend_subnet)
        elif args.action == 'backup': deployment.backup()
        elif args.action == 'update': deployment.update(Path(args.bundle).resolve())
        elif args.action == 'restore': deployment.restore(Path(args.backup).resolve(), args.confirm_instance)
        elif args.action == 'status':
            deployment.installed(allow_recovery=True)
            verify_images(deployment.release())
            print(deployment.compose('ps').decode())
            print('Recovery required:', (deployment.root / 'recovery-required.json').exists())

if __name__ == '__main__':
    try:
        main()
    except (OfflineError, OSError, ValueError, KeyError) as error:
        print('ERROR: ' + str(error), file=sys.stderr)
        sys.exit(1)
