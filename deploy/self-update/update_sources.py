"""Acquire administrator-selected updates into bounded, immutable staging directories."""
from __future__ import annotations

import hashlib
import http.client
import ipaddress
import json
import os
from pathlib import Path, PurePosixPath
import re
import shutil
import socket
import stat
import subprocess
import tarfile
import urllib.parse
import urllib.request
import zipfile

MAX_ARCHIVE_BYTES = 1024 * 1024 * 1024
MAX_FILES = 100000
MAX_EXPANDED_BYTES = 4 * MAX_ARCHIVE_BYTES
EXCLUDED = {'.git', 'node_modules', 'bin', 'obj', 'publish', 'files', '.local-run', '.worktrees', '.pnpm-store', '__pycache__', 'playwright-report', 'test-results'}


class SourceError(ValueError):
    pass


def normalize_source(value: dict) -> dict:
    kind = value.get('kind', '')
    location = str(value.get('location', '')).strip()
    ref = str(value.get('ref', '')).strip()
    checksum = str(value.get('sha256', '')).strip().lower()
    generation = value.get('generation', '')
    if not isinstance(generation, str) or (generation and not re.fullmatch(r'[0-9a-f]{32}', generation)):
        raise SourceError('来源版本标识无效。')
    generation_field = {'generation': generation} if 'generation' in value else {}
    if kind not in {'github', 'package', 'directory'} or not location or len(location) > 2048:
        raise SourceError('请选择更新来源并填写有效地址。')
    if checksum and not re.fullmatch(r'[0-9a-f]{64}', checksum):
        raise SourceError('SHA-256 必须为 64 位十六进制字符。')
    if kind == 'directory':
        if not Path(location).is_absolute():
            raise SourceError('本机目录必须是服务器上的绝对路径。')
        return {'kind': kind, 'location': location, 'ref': '', 'sha256': '', **generation_field}
    url = urllib.parse.urlsplit(location)
    if url.scheme not in {'http', 'https'} or not url.hostname or url.username or url.password or url.fragment:
        raise SourceError('请输入不含凭据或片段的 HTTP/HTTPS 地址。')
    if kind == 'github':
        parts = url.path.strip('/').split('/')
        if url.hostname.lower() != 'github.com' or url.port or url.query or len(parts) < 2:
            raise SourceError('请输入 GitHub 仓库或分支链接。')
        owner, repo = parts[:2]; repo = repo.removesuffix('.git')
        if not re.fullmatch(r'[A-Za-z0-9_.-]+', owner) or not re.fullmatch(r'[A-Za-z0-9_.-]+', repo):
            raise SourceError('GitHub 仓库地址无效。')
        if len(parts) > 2:
            if parts[2] != 'tree' or len(parts) < 4:
                raise SourceError('支持仓库链接或 /tree/分支 链接。')
            embedded = urllib.parse.unquote('/'.join(parts[3:]))
            if ref and ref != embedded:
                raise SourceError('链接中的分支与填写的分支不一致。')
            ref = embedded
        ref = ref or 'main'
        if not re.fullmatch(r'[A-Za-z0-9][A-Za-z0-9_./-]{0,199}', ref) or '..' in ref or '@{' in ref:
            raise SourceError('分支或标签格式无效。')
        location = f'https://github.com/{owner}/{repo}'
    return {'kind': kind, 'location': location, 'ref': ref if kind == 'github' else '',
            'sha256': checksum if kind == 'package' else '', **generation_field}


def source_digest(source: dict) -> str:
    return hashlib.sha256(json.dumps(normalize_source(source), sort_keys=True).encode()).hexdigest()


def tree_digest(root: Path) -> str:
    digest = hashlib.sha256()
    for path in sorted(root.rglob('*')):
        if path.is_symlink():
            raise SourceError('候选版本中包含符号链接。')
        if path.is_file():
            name = path.relative_to(root).as_posix().encode()
            digest.update(len(name).to_bytes(4, 'big')); digest.update(name)
            digest.update(path.stat().st_size.to_bytes(8, 'big'))
            with path.open('rb') as stream:
                for chunk in iter(lambda: stream.read(1024 * 1024), b''):
                    digest.update(chunk)
    return digest.hexdigest()


def excluded(name: str) -> bool:
    return (name in EXCLUDED or name.startswith('.env') or
            (name.startswith('appsettings') and name != 'appsettings.Template.json') or
            name in {'updater.token', 'updater.json', 'kube-config.yaml'} or name.endswith('.key'))


def snapshot_directory(origin: Path, destination: Path, roots: list[Path]) -> None:
    # Resolve containment before reading and reject a symlink as the selected root itself.
    if origin.is_symlink() or not origin.is_dir():
        raise SourceError('更新目录不存在，或不是普通目录。')
    origin = origin.resolve()
    if not any(origin.is_relative_to(root.resolve()) for root in roots):
        raise SourceError('目录不在宿主机允许的更新目录范围内。')
    destination = destination.resolve()
    if destination.is_relative_to(origin) and not any(excluded(p) for p in destination.relative_to(origin).parts):
        raise SourceError('暂存目录不能位于更新源目录内。')
    count = total = 0
    destination.mkdir(parents=True, exist_ok=True)
    for current, directories, filenames in os.walk(origin, followlinks=False):
        directories[:] = [name for name in directories if not excluded(name)]
        for name in [*directories, *filenames]:
            if excluded(name): continue
            path = Path(current) / name
            if path.is_symlink(): raise SourceError('更新源包含符号链接，请提供普通文件。')
            if path.is_dir(): continue
            if not stat.S_ISREG(path.stat().st_mode): raise SourceError('更新源包含特殊文件。')
            count += 1; total += path.stat().st_size
            if count > MAX_FILES or total > MAX_EXPANDED_BYTES: raise SourceError('更新目录超过大小或文件数量限制。')
            target = destination / path.relative_to(origin)
            target.parent.mkdir(parents=True, exist_ok=True); shutil.copy2(path, target)


def safe_member(name: str) -> Path:
    part = PurePosixPath(name)
    if not name or part.is_absolute() or '..' in part.parts or '\\' in name or ':' in name:
        raise SourceError('更新包包含越界路径。')
    return Path(*part.parts)


def extract_archive(archive: Path, destination: Path) -> None:
    destination.mkdir(parents=True, exist_ok=True)
    total = count = 0
    def target(name: str, size: int) -> Path:
        nonlocal total, count
        relative = safe_member(name)
        count += 1; total += size
        if count > MAX_FILES or total > MAX_EXPANDED_BYTES: raise SourceError('更新包解压大小或文件数量超限。')
        output = destination / relative
        if output.exists(): raise SourceError('更新包包含重复文件路径。')
        output.parent.mkdir(parents=True, exist_ok=True)
        return output
    if zipfile.is_zipfile(archive):
        with zipfile.ZipFile(archive) as stream:
            for entry in stream.infolist():
                safe_member(entry.filename)
                mode = entry.external_attr >> 16
                if stat.S_ISLNK(mode) or (stat.S_IFMT(mode) and not (stat.S_ISREG(mode) or stat.S_ISDIR(mode))):
                    raise SourceError('更新包中不允许符号链接或特殊文件。')
                if entry.is_dir(): continue
                output = target(entry.filename, entry.file_size)
                with stream.open(entry) as source, output.open('xb') as dest:
                    shutil.copyfileobj(source, dest)
    else:
        try:
            with tarfile.open(archive, 'r:*') as stream:
                for entry in stream:
                    safe_member(entry.name)
                    if entry.isdir(): continue
                    if not entry.isfile(): raise SourceError('更新包中不允许链接或特殊文件。')
                    output = target(entry.name, entry.size)
                    with stream.extractfile(entry) as source, output.open('xb') as dest:
                        shutil.copyfileobj(source, dest)
        except tarfile.TarError as error:
            raise SourceError('更新包不是可读取的 ZIP 或 TAR 压缩包。') from error


def check_download_url(url: str, allow_private: bool = False) -> None:
    value = urllib.parse.urlsplit(url)
    if value.scheme not in {'https', 'http'} or not value.hostname or value.username or value.password:
        raise SourceError('更新包下载地址无效。')
    try: addresses = socket.getaddrinfo(value.hostname, value.port or (443 if value.scheme == 'https' else 80), type=socket.SOCK_STREAM)
    except OSError as error: raise SourceError('无法解析下载服务器地址。') from error
    if not allow_private and any(not ipaddress.ip_address(address[4][0]).is_global for address in addresses):
        raise SourceError('下载地址指向内网或本机；请改用本机目录，或由运维允许内网下载。')


class CheckedRedirect(urllib.request.HTTPRedirectHandler):
    def __init__(self, allow_private: bool): self.allow_private = allow_private
    def redirect_request(self, request, fp, code, msg, headers, newurl):
        check_download_url(newurl, self.allow_private)
        return super().redirect_request(request, fp, code, msg, headers, newurl)


def approved_connection(address, timeout=socket._GLOBAL_DEFAULT_TIMEOUT, source_address=None, *, all_errors=False, allow_private=False):
    host, port = address
    errors = []
    for family, socktype, protocol, _, sockaddr in socket.getaddrinfo(host, port, type=socket.SOCK_STREAM):
        if not allow_private and not ipaddress.ip_address(sockaddr[0]).is_global:
            raise SourceError('下载连接解析为内网或本机地址，已拒绝。')
        sock = socket.socket(family, socktype, protocol)
        try:
            if timeout is not socket._GLOBAL_DEFAULT_TIMEOUT: sock.settimeout(timeout)
            if source_address: sock.bind(source_address)
            sock.connect(sockaddr)
            return sock
        except OSError as error:
            errors.append(error); sock.close()
    raise OSError('无法连接更新包下载服务器。') from (errors[-1] if errors else None)


class PinnedHTTPHandler(urllib.request.HTTPHandler):
    def __init__(self, allow_private):
        super().__init__(); self.allow_private=allow_private
    def http_open(self, request):
        def connection(host, **kwargs):
            conn=http.client.HTTPConnection(host, **kwargs)
            conn._create_connection=lambda *a, **k: approved_connection(*a, **k, allow_private=self.allow_private)
            return conn
        return self.do_open(connection, request)


class PinnedHTTPSHandler(urllib.request.HTTPSHandler):
    def __init__(self, allow_private):
        super().__init__(); self.allow_private=allow_private
    def https_open(self, request):
        def connection(host, **kwargs):
            conn=http.client.HTTPSConnection(host, **kwargs)
            conn._create_connection=lambda *a, **k: approved_connection(*a, **k, allow_private=self.allow_private)
            return conn
        return self.do_open(connection, request, context=self._context)


def acquire(source: dict, workspace: Path, roots: list[Path], *, allow_private: bool = False) -> tuple[Path, str | None]:
    source = normalize_source(source); workspace.mkdir(parents=True, exist_ok=True)
    snapshot = workspace / 'source'; revision = None
    if source['kind'] == 'directory':
        snapshot_directory(Path(source['location']), snapshot, roots)
    elif source['kind'] == 'github':
        clone = workspace / 'checkout'
        result = subprocess.run(['git', '-c', 'core.hooksPath=/dev/null', 'clone', '--depth=1', '--single-branch',
            '--branch', source['ref'], '--', source['location'], str(clone)], capture_output=True, timeout=300)
        if result.returncode: raise SourceError('无法下载 GitHub 分支，请检查仓库和分支是否公开可访问。')
        revision = subprocess.check_output(['git','-C',str(clone),'rev-parse','HEAD'],text=True).strip()
        snapshot_directory(clone, snapshot, [clone])
        shutil.rmtree(clone)
    else:
        check_download_url(source['location'], allow_private)
        opener = urllib.request.build_opener(urllib.request.ProxyHandler({}), CheckedRedirect(allow_private),
                                              PinnedHTTPHandler(allow_private), PinnedHTTPSHandler(allow_private))
        archive = workspace / 'download'
        request = urllib.request.Request(source['location'], headers={'User-Agent':'gzctf-updater'})
        total = 0; digest = hashlib.sha256()
        with opener.open(request, timeout=30) as response, archive.open('xb') as output:
            while chunk := response.read(1024 * 1024):
                total += len(chunk)
                if total > MAX_ARCHIVE_BYTES: raise SourceError('更新包超过 1 GiB 下载限制。')
                digest.update(chunk); output.write(chunk)
        if source['sha256'] and digest.hexdigest() != source['sha256']:
            raise SourceError('更新包 SHA-256 与配置值不一致。')
        extracted = workspace / 'extracted'; extract_archive(archive, extracted)
        root = find_root(extracted)
        snapshot_directory(root, snapshot, [extracted])
    return find_root(snapshot), revision


def find_root(root: Path) -> Path:
    for _ in range(4):
        if (root/'src/GZCTF/GZCTF.csproj').is_file() or (root/'gzctf-release.json').is_file(): return root
        entries = list(root.iterdir())
        if len(entries) != 1 or not entries[0].is_dir(): break
        root = entries[0]
    raise SourceError('来源不是 GZCTF 源码或带 gzctf-release.json 的发布包。')


def verify_release(root: Path, runtime: str) -> dict:
    try: manifest = json.loads((root/'gzctf-release.json').read_text())
    except (OSError, ValueError) as error: raise SourceError('发布包缺少有效的 gzctf-release.json。') from error
    if manifest.get('project') != 'GZCTF' or manifest.get('runtime') != runtime or not manifest.get('version') or manifest.get('validationProtocol') != 1:
        raise SourceError('发布包项目标识、版本、校验协议或系统架构与当前部署不匹配。')
    files = manifest.get('files')
    if not isinstance(files, dict) or not {'GZCTF.dll','wwwroot/index.html'}.issubset(files):
        raise SourceError('发布包缺少后端程序或前端首页校验信息。')
    actual = {p.relative_to(root).as_posix() for p in root.rglob('*') if p.is_file() and p.name != 'gzctf-release.json'}
    if actual != set(files): raise SourceError('发布包文件列表与完整性清单不一致。')
    for name, expected in files.items():
        path = root / safe_member(name)
        if path.is_symlink() or not re.fullmatch(r'[0-9a-f]{64}', str(expected)):
            raise SourceError('发布包文件校验信息无效。')
        if hashlib.sha256(path.read_bytes()).hexdigest() != expected:
            raise SourceError(f'发布包文件完整性校验失败：{name}')
    return manifest
