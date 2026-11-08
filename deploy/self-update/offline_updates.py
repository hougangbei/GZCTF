"""Private, host-side offline package adapter (Python 3.8+).

Only the sibling, operator-installed offline/manage.py is imported. Package code
is data and is never imported, sourced, or executed.
"""
import contextlib
import datetime
import hashlib
import importlib.util
import json
import logging
import os
from pathlib import Path, PurePosixPath
import re
import secrets
import shutil
import stat
import struct
import tarfile
import threading
import time
import uuid
import zipfile

from update_errors import UpdateError

_SPEC = importlib.util.spec_from_file_location('gzctf_trusted_offline_manage', Path(__file__).resolve().parent.parent / 'offline' / 'manage.py')
MANAGE = importlib.util.module_from_spec(_SPEC)
_SPEC.loader.exec_module(MANAGE)
LOG = logging.getLogger('gzctf-updater.offline')
HEX = re.compile(r'^[0-9a-f]{64}$')
ID = re.compile(r'^[0-9a-f]{32}$')
SUFFIXES = ('.tar.gz', '.tgz', '.tar', '.zip')
ACTIVE = {'validating', 'accepted', 'importing', 'backingUp', 'stopping', 'starting', 'recovering', 'restoring'}


def utc(timestamp=None):
    return datetime.datetime.fromtimestamp(time.time() if timestamp is None else timestamp, datetime.timezone.utc).isoformat()


class BoundedTarInfo(tarfile.TarInfo):
    # tarfile normally reads extended headers into one allocation before a
    # caller sees a member. Bound those as well as the eventual file payload.
    def _proc_pax(self, archive):
        if self.size > 1024 * 1024:
            raise UpdateError(413, '归档扩展头超过限制。')
        return super()._proc_pax(archive)

    def _proc_gnulong(self, archive):
        if self.size > 1024 * 1024:
            raise UpdateError(413, '归档长文件名超过限制。')
        return super()._proc_gnulong(archive)

    def _proc_sparse(self, archive):
        raise UpdateError(400, '不支持稀疏归档文件。')


@contextlib.contextmanager
def open_regular(path):
    """Walk by directory descriptors: ancestor replacement cannot follow links."""
    path = Path(path)
    if not path.is_absolute() or '..' in path.parts:
        raise UpdateError(400, '更新包路径无效。')
    flags = os.O_RDONLY | getattr(os, 'O_DIRECTORY', 0) | getattr(os, 'O_NOFOLLOW', 0)
    directory = os.open(path.anchor, flags)
    try:
        for part in path.parts[1:-1]:
            next_directory = os.open(part, flags, dir_fd=directory)
            os.close(directory); directory = next_directory
        descriptor = os.open(path.name, os.O_RDONLY | getattr(os, 'O_NOFOLLOW', 0) | getattr(os, 'O_NONBLOCK', 0), dir_fd=directory)
        with os.fdopen(descriptor, 'rb') as stream:
            if not stat.S_ISREG(os.fstat(stream.fileno()).st_mode):
                raise UpdateError(400, '更新包必须是普通文件。')
            yield stream
    finally:
        os.close(directory)


def member_name(name):
    if '\\' in name or '\x00' in name:
        raise UpdateError(400, '归档路径无效。')
    while name.startswith('./'):
        name = name[2:]
    path = PurePosixPath(name)
    if not name or path.is_absolute() or '..' in path.parts or ':' in name or len(name) > 1024:
        raise UpdateError(400, '归档路径无效。')
    return str(path)


def check_zip_directory(source, max_files):
    # ZipFile reads the entire central directory in its constructor. Inspect the
    # bounded end record first so a huge metadata directory cannot exhaust RAM.
    size = Path(source).stat().st_size
    with Path(source).open('rb') as stream:
        stream.seek(max(0, size - 65557))
        tail = stream.read(65557)
    offset = tail.rfind(b'PK\x05\x06')
    if offset < 0 or len(tail) - offset < 22:
        raise UpdateError(400, 'ZIP 目录记录无效。')
    _, disk, directory_disk, disk_count, count, directory_size, directory_offset, comment_size = struct.unpack('<4s4H2LH', tail[offset:offset + 22])
    if disk or directory_disk or disk_count != count or count > max_files or count == 65535 or directory_size > 16 * 1024 * 1024 or directory_offset + directory_size > size or len(tail) - offset != 22 + comment_size:
        raise UpdateError(413, 'ZIP 目录超过限制；不支持分卷或 ZIP64。')


def extract_archive(source, destination, max_bytes, max_files):
    """Never use extractall: no links, devices, duplicate paths, or unbounded reads."""
    destination = Path(destination)
    destination.mkdir(mode=0o700, parents=True, exist_ok=False)
    names = set()
    total = 0

    def target(name, size, directory):
        nonlocal total
        name = member_name(name)
        if name in names or len(names) >= max_files or size < 0:
            raise UpdateError(400, '归档有重复路径或文件数量超限。')
        names.add(name)
        total += size
        if total > max_bytes:
            raise UpdateError(413, '归档解压大小超过限制。')
        if size > shutil.disk_usage(str(destination)).free - 64 * 1024 * 1024:
            raise UpdateError(413, '暂存磁盘空间不足。')
        path = destination.joinpath(*PurePosixPath(name).parts)
        if directory:
            path.mkdir(mode=0o700, parents=True, exist_ok=True)
        else:
            path.parent.mkdir(mode=0o700, parents=True, exist_ok=True)
        return path

    def copy(source_stream, path, size):
        with path.open('xb') as output:
            os.chmod(str(path), 0o600)
            remaining = size
            while remaining:
                chunk = source_stream.read(min(1024 * 1024, remaining))
                if not chunk:
                    raise UpdateError(400, '归档被截断。')
                output.write(chunk)
                remaining -= len(chunk)

    if str(source).lower().endswith('.zip'):
        check_zip_directory(source, max_files)
        with zipfile.ZipFile(str(source)) as archive:
            for entry in archive.infolist():
                mode = entry.external_attr >> 16
                kind = stat.S_IFMT(mode)
                directory = entry.is_dir()
                if kind not in (0, stat.S_IFREG, stat.S_IFDIR) or entry.flag_bits & 1:
                    raise UpdateError(400, '归档包含特殊文件或加密文件。')
                path = target(entry.filename.rstrip('/') if directory else entry.filename, entry.file_size, directory)
                if not directory:
                    with archive.open(entry) as stream:
                        copy(stream, path, entry.file_size)
    else:
        with tarfile.open(str(source), 'r|*', tarinfo=BoundedTarInfo) as archive:
            for entry in archive:
                if entry.isdir() and entry.name in ('.', './'):
                    continue
                if not (entry.isfile() or entry.isdir()) or entry.sparse is not None:
                    raise UpdateError(400, '归档包含链接或特殊文件。')
                path = target(entry.name.rstrip('/') if entry.isdir() else entry.name, entry.size, entry.isdir())
                if entry.isfile():
                    with archive.extractfile(entry) as stream:
                        copy(stream, path, entry.size)
    manifests = list(destination.glob('manifest.json')) + list(destination.glob('*/manifest.json'))
    if len(manifests) != 1:
        raise UpdateError(400, '归档必须包含唯一的发布包根目录。')
    return manifests[0].parent


def inspect_image_archive(path, manifest, max_bytes=16 * 1024 ** 3, max_files=100000):
    """Verify Docker-save JSON and image fingerprints without executing containers.

    Reading the tar stream also bounds its expanded size. Layers are never
    extracted on the host. Docker will validate layer digests again on load.
    """
    metadata = {}
    seen = set()
    total = 0
    with tarfile.open(str(path), 'r|*', tarinfo=BoundedTarInfo) as archive:
        for entry in archive:
            if entry.isdir() and entry.name in ('.', './'):
                continue
            name = member_name(entry.name.rstrip('/') if entry.isdir() else entry.name)
            if name in seen or len(seen) >= max_files or not (entry.isfile() or entry.isdir()) or entry.sparse is not None:
                raise UpdateError(400, '镜像归档包含重复路径或特殊文件。')
            seen.add(name)
            total += entry.size
            if total > max_bytes:
                raise UpdateError(413, '镜像展开大小超过限制。')
            if entry.isfile() and (name == 'manifest.json' or re.fullmatch(r'[0-9a-f]{64}\.json', name) or name.startswith('blobs/sha256/')):
                # OCI blobs include large layers: only small JSON config candidates are retained.
                if entry.size <= 1024 * 1024:
                    with archive.extractfile(entry) as stream:
                        raw = stream.read(entry.size)
                    if name == 'manifest.json' or raw.lstrip().startswith(b'{'):
                        metadata[name] = raw
                        if sum(len(value) for value in metadata.values()) > 16 * 1024 * 1024:
                            raise UpdateError(413, '镜像 JSON 元数据超过限制。')
    try:
        saved = json.loads(metadata['manifest.json'])
        expected = {item['tag']: item for item in manifest['images'].values()}
        gateway = manifest.get('gatewayImage')
        if gateway is not None:
            if not HEX.fullmatch(str(gateway.get('fingerprint', ''))) or not re.fullmatch(r'gzctf-offline/traefik:[A-Za-z0-9_.-]+-amd64', str(gateway.get('tag', ''))):
                raise ValueError('gateway identity')
            expected[gateway['tag']] = gateway
        if not isinstance(saved, list) or not 1 <= len(saved) <= len(expected):
            raise ValueError('image count')
        found = set()
        for image in saved:
            tags = image['RepoTags']
            if len(tags) != 1 or tags[0] not in expected or tags[0] in found:
                raise ValueError('image tag')
            tag = tags[0]; found.add(tag)
            raw = metadata[image['Config']]
            config = json.loads(raw)
            digest = hashlib.sha256(raw).hexdigest()
            if image['Config'] not in (digest + '.json', 'blobs/sha256/' + digest):
                raise ValueError('config digest')
            info = {'Os': config['os'], 'Architecture': config['architecture'],
                    'Config': config['config'], 'RootFS': {'Layers': config['rootfs']['diff_ids']}}
            if info['Os'] != 'linux' or info['Architecture'] != 'amd64' or MANAGE.fingerprint(info) != expected[tag]['fingerprint']:
                raise ValueError('image fingerprint')
            if any(member_name(layer) not in seen for layer in image['Layers']):
                raise ValueError('missing layer')
        # Application-only update exports reuse the verified, compatible db and
        # Redis images already owned by this installation. Undeclared images
        # are always rejected, including unrelated host service images.
        if manifest['images']['app']['tag'] not in found:
            raise ValueError('missing application image')
    except (KeyError, TypeError, ValueError):
        raise UpdateError(400, '镜像平台、内容指纹或归档清单不匹配。')


class OfflineUpdater:
    offline_packages = True

    def __init__(self, config, deployment=None):
        self.config = dict(config)
        for key in ('runtime_dir', 'work_dir'):
            if not Path(config[key]).is_absolute():
                raise ValueError(key + ' must be absolute')
        self.work = Path(config['work_dir'])
        self.work.mkdir(mode=0o700, parents=True, exist_ok=True)
        if self.work.is_symlink() or self.work.stat().st_mode & 0o077:
            raise ValueError('offline work directory must be private and not a symlink')
        self.uploads = self.work / 'uploads'; self.uploads.mkdir(mode=0o700, exist_ok=True)
        self.candidates = self.work / 'candidates'; self.candidates.mkdir(mode=0o700, exist_ok=True)
        roots = config.get('allowed_package_roots', [])
        if not roots or any(not Path(root).is_absolute() for root in roots):
            raise ValueError('explicit absolute allowed_package_roots required')
        self.roots = [str(self._no_links(Path(root))) for root in roots]
        self.max_upload = int(config.get('max_upload_bytes', 1024 ** 3))
        if not 1 <= self.max_upload <= 1024 ** 3:
            raise ValueError('max_upload_bytes must be between 1 and 1GiB')
        self.max_extract = int(config.get('max_extracted_bytes', 8 * 1024 ** 3))
        self.max_files = int(config.get('max_archive_files', 20000))
        self.max_images = int(config.get('max_image_bytes', 16 * 1024 ** 3))
        if min(self.max_extract, self.max_files, self.max_images) < 1:
            raise ValueError('archive bounds must be positive')
        self.deployment = deployment if deployment is not None else MANAGE.Deployment(config['runtime_dir'], bool(config.get('test_host', False)))
        self.state_lock = threading.RLock()
        self.update_lock = threading.Lock()
        self.task = None
        self.request_actor = threading.local()
        self.state_file = self.work / 'offline-state.json'
        self.data_file = self.work / 'offline-packages.json'
        self.data = json.loads(self.data_file.read_text()) if self.data_file.exists() else {'directory': config['package_directory'], 'packages': {}, 'operations': {}}
        self.directory = self._directory(self.data['directory'])
        self.state = json.loads(self.state_file.read_text()) if self.state_file.exists() else {'phase': 'idle', 'message': None, 'validation': None, 'backupPath': None, 'operationId': None, 'packageId': None, 'changedAtUtc': utc()}
        if self.state.get('phase') in ACTIVE:
            self._save_state(phase='interrupted', message='更新服务曾中断，请运维检查备份、恢复标记和部署状态后人工恢复。')
        self.report = json.loads((self.work / 'validation.json').read_text()) if (self.work / 'validation.json').exists() else None

    @staticmethod
    def _no_links(path):
        if not path.is_absolute() or '..' in path.parts:
            raise UpdateError(400, '仅支持允许范围内的绝对路径。')
        current = Path(path.anchor)
        for part in path.parts[1:]:
            current = current / part
            if current.is_symlink():
                raise UpdateError(400, '路径不能包含符号链接。')
        return path.resolve()

    def _directory(self, value):
        if not isinstance(value, str):
            raise UpdateError(400, '目录无效。')
        path = self._no_links(Path(value))
        if not path.is_dir():
            raise UpdateError(400, '目录不存在。')
        for root in self.roots:
            try:
                path.relative_to(root)
                return path
            except ValueError:
                pass
        raise UpdateError(400, '目录不在运维允许范围内。')

    def _save_data(self):
        MANAGE.write_private(self.data_file, json.dumps(self.data, ensure_ascii=False))

    def _save_state(self, **changes):
        with self.state_lock:
            self.state.update(changes, changedAtUtc=utc())
            MANAGE.write_private(self.state_file, json.dumps(self.state, ensure_ascii=False))

    def _audit(self, action, package=None, outcome='accepted'):
        actor = getattr(self.request_actor, 'value', 'authenticated-admin')
        if not re.fullmatch(r'[A-Za-z0-9@._:-]{1,128}', actor): actor = 'authenticated-admin'
        item = self.data['packages'].get(package, {})
        report = self.state.get('validation') or {}
        digest = report.get('sourceDigest') if report.get('packageId') == package else item.get('sha256')
        record = {'atUtc': utc(), 'action': action, 'actor': actor, 'packageId': package, 'sha256': digest, 'outcome': outcome}
        if action == 'settings': record['directory'] = str(self.directory)
        with (self.work / 'audit.jsonl').open('a') as target:
            os.chmod(str(self.work / 'audit.jsonl'), 0o600)
            target.write(json.dumps(record, ensure_ascii=False) + '\n')

    @contextlib.contextmanager
    def _mutation(self):
        if not self.update_lock.acquire(False):
            raise UpdateError(409, '已有更新任务正在运行。')
        try:
            yield
        finally:
            self.update_lock.release()

    def settings(self, directory=None):
        if directory is not None:
            with self._mutation():
                path = self._directory(directory)
                self.directory = path; self.data['directory'] = str(path)
                self._save_data(); self._audit('settings')
        return {'mode': 'offline', 'directory': str(self.directory), 'maxUploadBytes': self.max_upload, 'allowedRoots': self.roots}

    def _public_item(self, item):
        return {key: item[key] for key in ('id', 'name', 'size', 'modifiedAtUtc', 'source', 'checksumProvided', 'sha256') if key in item}

    def packages(self):
        with self.state_lock:
            directory = self._directory(str(self.directory))
            results = []
            # Bound directory enumeration as well as output. No recursion.
            with os.scandir(str(directory)) as entries:
                for index, entry in enumerate(entries):
                    if index >= 1000 or len(results) >= 100:
                        break
                    if not entry.is_file(follow_symlinks=False) or not entry.name.lower().endswith(SUFFIXES):
                        continue
                    info = entry.stat(follow_symlinks=False)
                    path = str(directory / entry.name)
                    package_id = hashlib.sha256(path.encode()).hexdigest()[:32]
                    item = self.data['packages'].get(package_id, {})
                    # Retain an explicitly attached digest across a rescan. Source digest is checked again.
                    item.update(id=package_id, name=entry.name, size=info.st_size, modifiedAtUtc=utc(info.st_mtime), source='directory', path=path)
                    external = self._companion(Path(path))
                    if external is not None:
                        item['companion'] = external
                    else:
                        item.pop('companion', None)
                    item['checksumProvided'] = bool(item.get('expected') or external is not None)
                    self.data['packages'][package_id] = item
                    results.append(self._public_item(item))
            for item in self.data['packages'].values():
                if item['source'] == 'upload' and Path(item['path']).is_file():
                    results.append(self._public_item(item))
            self._save_data()
            return {'directory': str(directory), 'items': sorted(results, key=lambda item: item['name'])}

    def _companion(self, package):
        for path in (Path(str(package) + '.sha256'), Path(str(package) + '.sha256sum')):
            if path.exists() or path.is_symlink():
                if path.is_symlink() or not path.is_file() or path.stat().st_size > 4096:
                    return 'invalid'
                with open_regular(path) as stream:
                    raw = stream.read(4097)
                if len(raw) > 4096: return 'invalid'
                content = raw.decode('utf-8').strip().split()
                if content and HEX.fullmatch(content[0].lower()):
                    return content[0].lower()
                return 'invalid'
        return None

    def _package(self, package_id):
        if not isinstance(package_id, str) or not ID.fullmatch(package_id) or package_id not in self.data['packages']:
            raise UpdateError(404, '更新包不存在，请重新扫描。')
        item = self.data['packages'][package_id]
        path = self._no_links(Path(item['path']))
        if not path.is_file() or not stat.S_ISREG(path.stat().st_mode):
            raise UpdateError(404, '更新包不存在。')
        if item['source'] == 'directory':
            self._directory(str(path.parent))
        else:
            try: path.relative_to(self.uploads.resolve())
            except ValueError: raise UpdateError(400, '上传路径无效。')
        if path.stat().st_size > self.max_upload:
            raise UpdateError(413, '更新包超过大小限制。')
        return item, path

    def upload(self, stream, length, name):
        if not isinstance(length, int) or length <= 0:
            raise UpdateError(400, '必须提供有效的 Content-Length。')
        if length > self.max_upload:
            raise UpdateError(413, '更新包超过上传大小限制。')
        if not isinstance(name, str) or len(name) > 200 or name in ('.', '..') or '/' in name or '\\' in name or any(ord(c) < 32 for c in name) or not name.lower().endswith(SUFFIXES):
            raise UpdateError(400, '更新包文件名无效。')
        with self._mutation():
            package_id = secrets.token_hex(16)
            suffix = next(ext for ext in SUFFIXES if name.lower().endswith(ext))
            pending = self.uploads / (package_id + '.partial')
            destination = self.uploads / (package_id + suffix)
            digest = hashlib.sha256()
            try:
                with pending.open('xb') as output:
                    os.chmod(str(pending), 0o600)
                    remaining = length
                    while remaining:
                        chunk = stream.read(min(1024 * 1024, remaining))
                        if not chunk:
                            raise UpdateError(400, '上传中断或文件被截断，请重试。')
                        output.write(chunk); digest.update(chunk); remaining -= len(chunk)
                    output.flush(); os.fsync(output.fileno())
                os.replace(str(pending), str(destination))
                item = {'id': package_id, 'name': name, 'size': length, 'modifiedAtUtc': utc(), 'source': 'upload', 'sha256': digest.hexdigest(), 'checksumProvided': False, 'path': str(destination)}
                with self.state_lock:
                    self.data['packages'][package_id] = item; self._save_data()
                self._audit('upload', package_id)
                return self._public_item(item)
            finally:
                if pending.exists(): pending.unlink()

    def checksum(self, package_id, digest):
        if not isinstance(digest, str) or not HEX.fullmatch(digest.lower()):
            raise UpdateError(400, 'SHA-256 格式无效。')
        with self._mutation():
            item, _ = self._package(package_id)
            item.update(expected=digest.lower(), checksumProvided=True)
            # Changing trust input always invalidates earlier approval.
            if self.report and self.report['packageId'] == package_id:
                self.report = None
                (self.work / 'validation.json').unlink(missing_ok=True)
                self._save_state(validation=None)
            self._save_data(); self._audit('checksum', package_id)
            return self._public_item(item)

    def _binding(self):
        installed = self.deployment.installed()
        self.deployment.verify_application()
        value = {'instance': installed['INSTANCE_ID'], 'release': self.deployment.release()}
        for name in ('.env', 'installation.json', 'compose.yml', 'compose.site.yml', 'compose.updater.yml', 'appsettings.json'):
            path = self.deployment.root / name
            if path.exists(): value[name] = MANAGE.sha256(path)
        return hashlib.sha256(json.dumps(value, sort_keys=True).encode()).hexdigest()

    def _gated(self):
        if self.state['phase'] == 'interrupted' or (self.deployment.root / 'recovery-required.json').exists():
            raise UpdateError(409, '上次任务需要人工恢复；请检查宿主机恢复标记及备份。')

    def _task_lock(self):
        if not self.update_lock.acquire(False):
            raise UpdateError(409, '已有更新任务正在运行。')
        lock = self.deployment.lock()
        entered = False
        try:
            lock.__enter__()
            entered = True
            self._gated()
            return lock
        except Exception as error:
            if entered: lock.__exit__(None, None, None)
            self.update_lock.release()
            if isinstance(error, UpdateError): raise
            raise UpdateError(409, '部署正在被其他任务使用，或需要恢复。')

    def _launch(self, function, lock):
        actor = getattr(self.request_actor, 'value', 'authenticated-admin')
        def worker():
            self.request_actor.value = actor
            try: function()
            finally:
                lock.__exit__(None, None, None)
                self.update_lock.release()
        self.task = threading.Thread(target=worker, daemon=False)
        self.task.start()

    def wait_for_task(self):
        if self.task is not None: self.task.join(30)

    def begin_validation(self, package_id):
        lock = self._task_lock()
        try:
            self._package(package_id)
            identifier = secrets.token_hex(16)
            report = {'id': identifier, 'packageId': package_id, 'sourceDigest': None, 'version': None, 'artifactHash': None,
                      'expiresAtUtc': utc(time.time() + 1800), 'canApply': False, 'checks': []}
            self.report = None
            (self.work / 'validation.json').unlink(missing_ok=True)
            self._save_state(phase='validating', message='正在复制并检查更新包。', packageId=package_id, validation=report, operationId=None, backupPath=None)
            self._launch(lambda: self._validate(package_id, report), lock)
            return {'accepted': True, 'validationId': identifier, 'packageId': package_id}
        except Exception:
            lock.__exit__(None, None, None); self.update_lock.release(); raise

    def _check(self, report, key, label, function):
        check = {'key': key, 'label': label, 'status': 'running', 'message': '正在检查。'}
        report['checks'].append(check)
        self._save_state(validation=report)
        try:
            result = function()
            check.update(status='passed', message='检查通过。')
            self._save_state(validation=report)
            return result
        except Exception:
            check.update(status='failed', message='检查失败；请核对发行包、架构和部署状态。')
            self._save_state(validation=report)
            raise

    def _copy(self, path, destination):
        with open_regular(path) as source:
            before = os.fstat(source.fileno())
            if not stat.S_ISREG(before.st_mode) or before.st_size > self.max_upload:
                raise UpdateError(400, '更新包不是受支持的普通文件。')
            if before.st_size > shutil.disk_usage(str(self.work)).free - 64 * 1024 * 1024:
                raise UpdateError(413, '暂存磁盘空间不足。')
            digest = hashlib.sha256(); total = 0
            with destination.open('xb') as target:
                os.chmod(str(destination), 0o400)
                while True:
                    chunk = source.read(1024 * 1024)
                    if not chunk: break
                    total += len(chunk)
                    if total > self.max_upload: raise UpdateError(413, '更新包大小超过限制。')
                    target.write(chunk); digest.update(chunk)
                target.flush(); os.fsync(target.fileno())
            after = os.fstat(source.fileno())
            if (before.st_size, before.st_mtime_ns, before.st_ino) != (after.st_size, after.st_mtime_ns, after.st_ino) or total != before.st_size:
                raise UpdateError(409, '更新包在复制期间发生变化。')
        return digest.hexdigest()

    def _source_digest(self, path):
        with open_regular(path) as source:
            before = os.fstat(source.fileno())
            if before.st_size > self.max_upload: raise UpdateError(413, '更新包超过大小限制。')
            digest = hashlib.sha256(); total = 0
            while True:
                chunk = source.read(1024 * 1024)
                if not chunk: break
                total += len(chunk)
                if total > self.max_upload: raise UpdateError(413, '更新包超过大小限制。')
                digest.update(chunk)
            after = os.fstat(source.fileno())
            if total != before.st_size or (before.st_mtime_ns, before.st_size) != (after.st_mtime_ns, after.st_size):
                raise UpdateError(409, '更新包在检查期间发生变化。')
            return digest.hexdigest()

    def _validate(self, package_id, report):
        try:
            item, path = self._package(package_id)
            candidate = self.candidates / report['id']; candidate.mkdir(mode=0o700)
            suffix = next(ext for ext in SUFFIXES if item['name'].lower().endswith(ext))
            immutable = candidate / ('package' + suffix)
            digest = self._check(report, 'copy', '不可变副本与 SHA-256', lambda: self._copy(path, immutable))
            report.update(sourceDigest=digest, artifactHash=digest)
            expected = item.get('expected')
            companion = self._companion(path) if item['source'] == 'directory' else None
            def external():
                if expected and expected != digest or companion and companion != digest:
                    raise UpdateError(400, '外部 SHA-256 与更新包不匹配。')
            self._check(report, 'checksum', '外部 SHA-256', external)
            report['checks'][-1]['message'] = '外部 SHA-256 匹配；摘要并非签名认证。' if expected or companion else '未提供外部校验值；自算 SHA-256 不代表来源认证。'
            bundle = self._check(report, 'archive', '安全归档', lambda: extract_archive(immutable, candidate / 'extracted', self.max_extract, self.max_files))
            def check_manifest():
                manifest = MANAGE.load_manifest(bundle)
                if manifest.get('platform') != 'linux/amd64': raise UpdateError(400, '只支持 linux/amd64 更新包。')
                for kind, image in manifest['images'].items():
                    if not HEX.fullmatch(str(image.get('fingerprint', ''))) or not re.fullmatch(r'gzctf-offline/' + kind + r':[0-9a-f]{16}-amd64', str(image.get('tag', ''))):
                        raise UpdateError(400, '离线镜像身份无效。')
                    if image['tag'] != 'gzctf-offline/' + kind + ':' + image['fingerprint'][:16] + '-amd64':
                        raise UpdateError(400, '离线镜像标签和内容指纹不匹配。')
                return manifest
            manifest = self._check(report, 'manifest', '内部文件清单与镜像声明', check_manifest)
            self._check(report, 'installation', '当前安装身份与恢复状态', self.deployment.preflight)
            binding = self._check(report, 'identity', '安装版本绑定', self._binding)
            self._check(report, 'compatibility', '数据库及 Redis 兼容性', lambda: MANAGE.compatible_update(self.deployment.release(), manifest))
            self._check(report, 'compose', '部署命令、权限和挂载不变', lambda: self.deployment.validate_candidate(bundle, manifest))
            self._check(report, 'images', '镜像架构与内容身份', lambda: inspect_image_archive(bundle / 'images.tar.gz', manifest, self.max_images))
            app = manifest['images']['app']
            report['version'] = str(manifest.get('version') or manifest.get('revision') or app['tag'])
            report['targetImage'] = app['tag']
            expires = time.time() + 1800
            report['expiresAtUtc'] = utc(expires)
            report['canApply'] = True
            self.report = {'public': report, 'packageId': package_id, 'sourceDigest': digest, 'candidate': str(immutable), 'bundle': str(bundle), 'binding': binding, 'expires': expires,
                           'expected': expected, 'companion': companion, 'files': {str(p.relative_to(bundle)): MANAGE.sha256(p) for p in bundle.rglob('*') if p.is_file()}}
            MANAGE.write_private(self.work / 'validation.json', json.dumps(self.report, ensure_ascii=False))
            self._save_state(phase='ready', message='检查通过，请确认维护后备份并更新。', validation=report)
            self._audit('validate', package_id, 'passed')
        except Exception:
            LOG.exception('Offline validation failed')
            report['canApply'] = False
            self._save_state(phase='validationFailed', message='检查未通过；请核对发行包、校验文件和宿主机部署。', validation=report)
            self._audit('validate', package_id, 'failed')

    def begin_apply(self, package_id, payload):
        if not isinstance(payload, dict) or payload.get('maintenanceConfirmed') is not True:
            raise UpdateError(400, '请先确认外部入口已进入维护状态。')
        operation = payload.get('operationId')
        try:
            if not isinstance(operation, str) or str(uuid.UUID(operation)) != operation: raise ValueError()
        except ValueError:
            raise UpdateError(400, '操作标识必须是标准 UUID。')
        previous = self.data['operations'].get(operation)
        if previous:
            if previous != {'packageId': package_id, 'validationId': payload.get('validationId')}:
                raise UpdateError(409, '操作标识已用于另一更新任务。')
            return {'accepted': True, 'operationId': operation, 'packageId': package_id}
        lock = self._task_lock()
        try:
            report = self.report
            if not report or report['packageId'] != package_id or report['public']['id'] != payload.get('validationId') or not report['public']['canApply'] or time.time() >= report['expires']:
                raise UpdateError(409, '检查报告无效或已过期，请重新检查更新包。')
            item, source = self._package(package_id)
            if self._source_digest(source) != report['sourceDigest'] or self._binding() != report['binding'] or MANAGE.sha256(Path(report['candidate'])) != report['sourceDigest']:
                raise UpdateError(409, '更新包或当前部署已变化，请重新检查。')
            companion = self._companion(source) if item['source'] == 'directory' else None
            if item.get('expected') != report.get('expected') or companion != report.get('companion'):
                raise UpdateError(409, '外部校验值已变化，请重新检查。')
            bundle = Path(report['bundle'])
            actual_files = {str(p.relative_to(bundle)) for p in bundle.rglob('*') if p.is_file()}
            if actual_files != set(report['files']):
                raise UpdateError(409, '候选文件发生变化，请重新检查。')
            for name, digest in report['files'].items():
                file = MANAGE.safe_child(bundle, name)
                if MANAGE.sha256(file) != digest: raise UpdateError(409, '候选文件发生变化，请重新检查。')
            # Consume approval durably before acknowledging work. A restart must
            # never resurrect an old successful or failed application's report.
            report['public']['canApply'] = False
            MANAGE.write_private(self.work / 'validation.json', json.dumps(report, ensure_ascii=False))
            self.data['operations'][operation] = {'packageId': package_id, 'validationId': payload['validationId']}
            self._save_data()
            self._save_state(phase='accepted', message='更新任务已接受。', operationId=operation, packageId=package_id, backupPath=None, validation=report['public'])
            self._launch(lambda: self._apply(report), lock)
            return {'accepted': True, 'operationId': operation, 'packageId': package_id}
        except Exception:
            lock.__exit__(None, None, None); self.update_lock.release(); raise

    def _apply(self, report):
        def progress(phase, message, backup=None):
            changes = {'phase': phase, 'message': message}
            if backup is not None: changes['backupPath'] = str(backup)
            self._save_state(**changes)
        try:
            # Deployment repeats installation, compatibility, Compose and image checks
            # under the same CLI file lock, then performs its full backup/recovery flow.
            self.deployment.update(Path(report['bundle']), progress=progress)
            report['public']['canApply'] = False
            self._save_state(phase='succeeded', message='更新成功，数据库、文件、Redis 和配置已保留。', validation=report['public'])
            self._audit('apply', report['packageId'], 'succeeded')
        except Exception:
            LOG.exception('Offline application failed')
            recovery = (self.deployment.root / 'recovery-required.json').exists()
            report['public']['canApply'] = False
            self._save_state(phase='interrupted' if recovery else 'failed', message='更新失败；需要运维按备份恢复。' if recovery else '更新失败；已尝试恢复原部署，请检查宿主机状态。', validation=report['public'])
            self._audit('apply', report['packageId'], 'recoveryRequired' if recovery else 'failed')

    def status(self, refresh=False):
        with self.state_lock:
            snapshot = json.loads(json.dumps(self.state))
        if snapshot.get('validation') and self.report and time.time() >= self.report['expires']:
            snapshot['validation']['canApply'] = False
        try:
            app = self.deployment.release()['images']['app']
            current = app.get('revision') or app.get('fingerprint')
        except Exception:
            current = None
        try:
            current_image = self.deployment.env().get('APP_IMAGE')
        except Exception:
            current_image = None
        return {'mode': 'offline', 'configured': True, 'currentSha': current, 'currentImage': current_image,
                'backupDirectory': str(self.deployment.root / 'backups'), **snapshot}
