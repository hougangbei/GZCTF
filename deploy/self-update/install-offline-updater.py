#!/usr/bin/env python3
"""Install fixed offline updater host files. Default is a read-only dry run.

--apply writes backed-up files; --activate additionally starts systemd and
recreates only the app container. Run from an operator-reviewed release tree.
Python 3.8+, no YAML library or package-provided executable is required.
"""
import argparse
import contextlib
import datetime
import hashlib
import json
import os
from pathlib import Path
import re
import secrets
import shutil
import stat
import subprocess
import sys

sys.path.insert(0, str(Path(__file__).resolve().parent))
from offline_updates import MANAGE, OfflineUpdater

TOOLS = ('updater.py', 'offline_updates.py', 'update_errors.py', 'managed_updates.py', 'update_sources.py', 'update_deployment.py')


def absolute(value):
    path = Path(value)
    if not re.fullmatch(r'/[A-Za-z0-9_./-]+', str(path)) or '..' in path.parts:
        raise ValueError('Use dedicated absolute paths without whitespace or shell metacharacters')
    return OfflineUpdater._no_links(path)


def install(args, deployment=None):
    runtime = absolute(args.runtime)
    host = absolute(args.host_dir)
    packages = absolute(args.packages)
    systemd = absolute(args.systemd_dir)
    if not Path(args.python).is_absolute():
        raise ValueError('Python interpreter must be an absolute path')
    # System Python commonly is an operator-owned symlink to python3.8+.
    python = Path(args.python).resolve()
    if host == runtime or str(host) in ('/', '/etc', '/usr', '/root', '/opt', '/var', '/home') or packages == runtime or packages == host:
        raise ValueError('Choose dedicated, separate runtime, package and host-tool directories')
    if not python.is_file() or not os.access(str(python), os.X_OK):
        raise ValueError('Python interpreter is not executable')
    if args.activate and not args.apply:
        raise ValueError('--activate requires --apply')
    if min(args.app_uid, args.socket_gid) < 0:
        raise ValueError('Application UID and socket GID must be nonnegative integers')
    deployment = deployment or MANAGE.Deployment(runtime, bool(getattr(args, 'test_host', False)))
    # Check installation identity, current config, volume ownership and recovery
    # before making any changes. The outer file lock serializes CLI/web writers.
    deployment.preflight()
    lock = deployment.lock() if hasattr(deployment, 'lock') else contextlib.nullcontext()
    with lock:
        deployment.installed()
        config_path = host / 'updater.json'
        existing = json.loads(config_path.read_text()) if config_path.exists() else None
        if existing and (existing.get('deployment_mode') != 'offline' or existing.get('runtime_dir') != str(runtime)):
            raise ValueError('Existing updater configuration targets a different deployment')
        token_file = host / 'updater.token'
        if token_file.exists():
            if token_file.is_symlink() or token_file.stat().st_mode & 0o077 or len(token_file.read_text().strip()) < 32:
                raise ValueError('Existing updater token must be private and at least 32 characters')
            token = token_file.read_text()
        else:
            token = secrets.token_hex(32) + '\n'
        config = dict(existing or {})
        config.update(deployment_mode='offline', runtime_dir=str(runtime), work_dir=str(host / 'state'),
                      socket_path=str(host / 'run' / 'updater.sock'), token_file=str(token_file), socket_gid=args.socket_gid)
        config.setdefault('package_directory', str(packages))
        config.setdefault('allowed_package_roots', [str(packages)])
        config.setdefault('max_upload_bytes', 1024 ** 3)
        if len(config['socket_path'].encode()) >= 104:
            raise ValueError('Unix socket path is too long; use a shorter host directory')
        overlay = {'services': {'app': {'environment': {'GZCTF_Updater__SocketPath': '/run/gzctf-updater/updater.sock',
                    'GZCTF_Updater__TokenFile': '/run/secrets/gzctf-updater-token', 'GZCTF_Updater__BaseUrl': ''},
                    'volumes': [{'type': 'bind', 'source': str(host / 'run'), 'target': '/run/gzctf-updater', 'read_only': True},
                                {'type': 'bind', 'source': str(token_file), 'target': '/run/secrets/gzctf-updater-token', 'read_only': True}]}}}
        unit = ('[Unit]\nDescription=GZCTF offline host updater\nAfter=docker.service\nRequires=docker.service\n\n'
                '[Service]\nType=simple\nUser=root\nGroup=root\nExecStart=' + str(python) + ' ' + str(host / 'self-update' / 'updater.py') + ' ' + str(config_path) + '\n'
                'WorkingDirectory=' + str(host) + '\nRestart=on-failure\nRestartSec=5\nUMask=0077\n'
                'NoNewPrivileges=true\nProtectHome=false\nKillMode=control-group\nTimeoutStopSec=30\n\n'
                '[Install]\nWantedBy=multi-user.target\n')
        files = {config_path: json.dumps(config, ensure_ascii=False, indent=2) + '\n', token_file: token,
                 runtime / 'compose.updater.yml': json.dumps(overlay, indent=2) + '\n',
                 systemd / 'gzctf-offline-updater.service': unit,
                 host / 'offline' / 'manage.py': Path(MANAGE.__file__).read_text(),
                 runtime / 'manage.py': Path(MANAGE.__file__).read_text()}
        source = Path(__file__).resolve().parent
        for name in TOOLS:
            files[host / 'self-update' / name] = (source / name).read_text()
        changes = []
        for path, content in files.items():
            absolute(str(path))
            if not path.exists() or path.read_text() != content:
                changes.append(path)
        result = {'dryRun': not args.apply, 'runtime': str(runtime), 'changed': [str(path) for path in changes],
                  'service': 'gzctf-offline-updater.service', 'backupPath': None, 'activated': False}
        if not args.apply:
            return result
        for directory in (host, host / 'self-update', host / 'offline', host / 'state', packages, host / 'run', systemd):
            directory.mkdir(mode=0o700, parents=True, exist_ok=True)
        os.chmod(str(host), 0o700); os.chmod(str(host / 'state'), 0o700)
        os.chmod(str(host / 'run'), 0o750)
        if os.geteuid() == 0:
            os.chown(str(host / 'run'), 0, args.socket_gid)
        backup = None
        if changes:
            backup = host / 'bootstrap-backups' / MANAGE.now(); backup.mkdir(mode=0o700, parents=True)
            index = {}
            for i, path in enumerate(changes):
                if path.exists():
                    saved = backup / str(i)
                    shutil.copyfile(str(path), str(saved)); os.chmod(str(saved), 0o600)
                    index[str(path)] = {'file': str(i), 'sha256': MANAGE.sha256(saved), 'mode': stat.S_IMODE(path.stat().st_mode)}
            MANAGE.write_private(backup / 'index.json', json.dumps(index, indent=2))
            result['backupPath'] = str(backup)
        for path in changes:
            MANAGE.write_private(path, files[path])
        if os.geteuid() == 0:
            os.chown(str(token_file), args.app_uid, args.socket_gid)
        if args.activate:
            # A config-only parse checks merge semantics before app recreation.
            deployment.compose('config', '--quiet')
            subprocess.run(['systemctl', 'daemon-reload'], check=True)
            subprocess.run(['systemctl', 'enable', 'gzctf-offline-updater.service'], check=True)
            # enable --now does not reload an already running Python process.
            # The shared deployment lock above prevents replacing a live task.
            # A prior --apply-only invocation may have staged changed modules
            # while the old process remained alive. Explicit activation always
            # reloads the idle host daemon, even when this invocation is a no-op.
            subprocess.run(['systemctl', 'restart', 'gzctf-offline-updater.service'], check=True)
            # Converge runtime config even after a prior --apply-only stage.
            # Compose recreates when its config hash changes and preserves an
            # already matching container; do not force an unnecessary restart.
            deployment.compose('up', '-d', '--no-deps', 'app')
            deployment.healthy('app')
            result['activated'] = True
        return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--runtime', required=True)
    parser.add_argument('--host-dir', required=True)
    parser.add_argument('--packages', required=True)
    parser.add_argument('--systemd-dir', default='/etc/systemd/system')
    parser.add_argument('--python', default='/usr/bin/python3')
    parser.add_argument('--app-uid', type=int, default=0, help='UID of the existing app container process; token owner')
    parser.add_argument('--socket-gid', type=int, default=0, help='GID available to the existing app process')
    parser.add_argument('--apply', action='store_true')
    parser.add_argument('--activate', action='store_true', help='Start host service and recreate app; maintenance required')
    parser.add_argument('--test-host', action='store_true', help=argparse.SUPPRESS)
    args = parser.parse_args()
    try:
        print(json.dumps(install(args), ensure_ascii=False, indent=2))
    except Exception:
        # Never print Docker output, config or token contents.
        print('Bootstrap refused or failed. Review installation identity, permissions and private local backups; secrets are not printed.', file=sys.stderr)
        return 1
    return 0


if __name__ == '__main__':
    sys.exit(main())
