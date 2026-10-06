#!/usr/bin/env python3
"""Host-side GZCTF updater. Run on the Docker Compose host, not in the web container."""

from __future__ import annotations

import hmac
import json
import logging
import os
import re
import secrets
import shutil
import socket
import socketserver
import stat
import subprocess
import sys
import tarfile
import threading
import time
import urllib.error
import urllib.parse
import urllib.request
from datetime import datetime, timezone
from http.server import BaseHTTPRequestHandler
from pathlib import Path
from typing import Any


SHA_PATTERN = re.compile(r"^[0-9a-f]{40}$")
REPOSITORY_PATTERN = re.compile(r"^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$")
IMAGE_PATTERN = re.compile(r"^ghcr\.io/[a-z0-9._/-]+$")
PULL_IMAGE_PATTERN = re.compile(r"^[a-z0-9][a-z0-9.-]+/[a-z0-9._/-]+$")
LOG = logging.getLogger("gzctf-updater")


def utc_now() -> str:
    return datetime.now(timezone.utc).isoformat()


class UpdateError(Exception):
    def __init__(self, status: int, message: str):
        self.status = status
        super().__init__(message)


class Updater:
    def __init__(self, config_path: Path):
        self.config = json.loads(config_path.read_text(encoding="utf-8"))
        for key in ("compose_file", "env_file", "files_dir", "backup_dir", "socket_path", "token_file"):
            path = Path(self.config[key])
            if not path.is_absolute():
                raise ValueError(f"{key} must be an absolute path")
            self.config[key] = path
        if not REPOSITORY_PATTERN.fullmatch(self.config["repository"]):
            raise ValueError("invalid repository")
        if not IMAGE_PATTERN.fullmatch(self.config["image_repository"]):
            raise ValueError("invalid image repository")
        self.config.setdefault("pull_image_repository", self.config["image_repository"])
        if not PULL_IMAGE_PATTERN.fullmatch(self.config["pull_image_repository"]):
            raise ValueError("invalid pull image repository")
        for key in ("app_service", "db_service"):
            if not re.fullmatch(r"[a-z][a-z0-9_-]*", self.config[key]):
                raise ValueError(f"invalid {key}")
        self.config.setdefault("branch", "main")
        self.config.setdefault("workflow", "self-update-image.yml")
        self.config["backup_dir"].mkdir(mode=0o700, parents=True, exist_ok=True)
        if stat.S_IMODE(self.config["backup_dir"].stat().st_mode) & 0o077:
            raise ValueError("backup directory must not be accessible to other users")
        if stat.S_IMODE(self.config["token_file"].stat().st_mode) & 0o077:
            raise ValueError("updater token must be private")
        self.token = self.config["token_file"].read_text(encoding="utf-8").strip()
        if len(self.token) < 32:
            raise ValueError("updater token must contain at least 32 characters")
        self.state_file = self.config["backup_dir"] / "update-state.json"
        self.state_lock = threading.Lock()
        self.update_lock = threading.Lock()
        self.latest_cache: dict[str, Any] | None = None
        self.latest_until = 0.0
        self.state = self._load_state()

    def _load_state(self) -> dict[str, Any]:
        if self.state_file.exists():
            state = json.loads(self.state_file.read_text(encoding="utf-8"))
            if state.get("phase") in {"accepted", "pulling", "backingUp", "starting"}:
                state.update(phase="interrupted", message="更新程序重启；请检查容器和备份后人工处理。")
            return state
        return {"phase": "idle", "message": None, "targetSha": None, "backupPath": None}

    def _save_state(self, **changes: Any) -> None:
        with self.state_lock:
            self.state.update(changes, changedAtUtc=utc_now())
            temp = self.state_file.with_suffix(".tmp")
            with os.fdopen(os.open(temp, os.O_WRONLY | os.O_CREAT | os.O_TRUNC, 0o600), "w", encoding="utf-8") as output:
                json.dump(self.state, output, ensure_ascii=False)
            os.replace(temp, self.state_file)

    def _run(self, args: list[str], *, timeout: int = 60) -> str:
        result = subprocess.run(args, capture_output=True, text=True, timeout=timeout, check=False)
        if result.returncode:
            LOG.error("Command failed (%s): %s", result.returncode, " ".join(args))
            raise RuntimeError("deployment command failed")
        return result.stdout.strip()

    def _compose(self, *args: str, timeout: int = 60) -> str:
        command = [
            "docker", "compose", "--project-directory", str(self.config["compose_file"].parent),
            "--env-file", str(self.config["env_file"]), "-f", str(self.config["compose_file"]),
            *args,
        ]
        return self._run(command, timeout=timeout)

    def _latest(self, *, force: bool = False) -> dict[str, str]:
        if not force and self.latest_cache and time.monotonic() < self.latest_until:
            return self.latest_cache
        repository = urllib.parse.quote(self.config["repository"], safe="/")
        workflow = urllib.parse.quote(self.config["workflow"], safe="")
        query = urllib.parse.urlencode({"branch": self.config["branch"], "status": "success", "per_page": 1})
        request = urllib.request.Request(
            f"https://api.github.com/repos/{repository}/actions/workflows/{workflow}/runs?{query}",
            headers={"Accept": "application/vnd.github+json", "User-Agent": "gzctf-updater",
                     "X-GitHub-Api-Version": "2022-11-28"},
        )
        github_token_file = self.config.get("github_token_file")
        if github_token_file:
            request.add_header("Authorization", f"Bearer {Path(github_token_file).read_text(encoding='utf-8').strip()}")
        with urllib.request.urlopen(request, timeout=12) as response:
            runs = json.load(response).get("workflow_runs", [])
        if not runs:
            raise UpdateError(503, "GitHub 尚无成功发布的镜像。")
        run = runs[0]
        sha = str(run.get("head_sha", "")).lower()
        if not SHA_PATTERN.fullmatch(sha) or run.get("conclusion") != "success":
            raise UpdateError(503, "GitHub 返回的发布版本无效。")
        self.latest_cache = {"sha": sha, "url": run.get("html_url", ""),
                             "publishedAtUtc": run.get("updated_at", "")}
        self.latest_until = time.monotonic() + 300
        return self.latest_cache

    def _container_id(self) -> str:
        return self._compose("ps", "-q", self.config["app_service"])

    def _current_sha(self) -> str | None:
        container = self._container_id()
        if not container:
            return None
        sha = self._run(["docker", "inspect", "--format",
                         '{{ index .Config.Labels "org.opencontainers.image.revision" }}', container])
        return sha.lower() if SHA_PATTERN.fullmatch(sha.lower()) else None

    def status(self, *, refresh: bool = False) -> dict[str, Any]:
        with self.state_lock:
            snapshot = dict(self.state)
        current_sha = self._current_sha()
        try:
            latest = self._latest(force=refresh)
            check_error = None
        except (UpdateError, urllib.error.URLError, TimeoutError, OSError) as error:
            latest = self.latest_cache
            check_error = str(error) if isinstance(error, UpdateError) else "无法连接 GitHub，请稍后重试。"
        return {
            "configured": True, "currentSha": current_sha,
            "latestSha": latest["sha"] if latest else None,
            "latestUrl": latest["url"] if latest else None,
            "publishedAtUtc": latest["publishedAtUtc"] if latest else None,
            "updateAvailable": bool(latest and latest["sha"] != current_sha and not check_error),
            "checkError": check_error, **snapshot,
        }

    def begin_update(self, target_sha: str) -> None:
        if not SHA_PATTERN.fullmatch(target_sha):
            raise UpdateError(400, "目标提交格式无效。")
        if not self.update_lock.acquire(blocking=False):
            raise UpdateError(409, "已有更新任务正在运行。")
        try:
            if self.state.get("phase") == "interrupted" or (
                self.state.get("phase") == "failed" and self.state.get("backupPath")
            ):
                raise UpdateError(409, "上次更新需要人工恢复，已暂停新的更新任务。")
            latest = self._latest(force=True)
            if latest["sha"] != target_sha:
                raise UpdateError(409, "可用版本已变化，请刷新页面。")
            current_sha = self._current_sha()
            if current_sha is None:
                raise UpdateError(409, "应用容器未运行或缺少版本标注，请先在部署机检查。")
            if current_sha == target_sha:
                raise UpdateError(409, "当前已是该版本。")
            self._save_state(phase="accepted", message="更新任务已接受。", targetSha=target_sha,
                             backupPath=None)
        except Exception:
            self.update_lock.release()
            raise

    def _backup(self, target_sha: str) -> Path:
        name = (datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%SZ") + "-" +
                target_sha[:12] + "-" + secrets.token_hex(3))
        destination = self.config["backup_dir"] / name
        destination.mkdir(mode=0o700)
        database_file = destination / "database.dump"
        command = ["docker", "compose", "--project-directory", str(self.config["compose_file"].parent),
                   "--env-file", str(self.config["env_file"]), "-f", str(self.config["compose_file"]),
                   "exec", "-T", self.config["db_service"], "sh", "-c",
                   'PGPASSWORD="$POSTGRES_PASSWORD" exec pg_dump -U "$POSTGRES_USER" -d "$POSTGRES_DB" -Fc']
        with os.fdopen(os.open(database_file, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600), "wb") as output:
            result = subprocess.run(command, stdout=output, stderr=subprocess.PIPE, timeout=600, check=False)
        if result.returncode or database_file.stat().st_size == 0:
            raise RuntimeError("database backup failed")
        files_dir = self.config["files_dir"]
        if not files_dir.is_dir():
            raise RuntimeError("uploads directory is missing")
        with tarfile.open(destination / "files.tar.gz", "w:gz") as archive:
            archive.add(files_dir, arcname="files")
        shutil.copy2(self.config["env_file"], destination / "deployment.env")
        os.chmod(destination / "deployment.env", 0o600)
        (destination / "metadata.json").write_text(
            json.dumps({"targetSha": target_sha, "createdAtUtc": utc_now()}, indent=2), encoding="utf-8")
        return destination

    def _set_image(self, target_sha: str) -> None:
        env_file = self.config["env_file"]
        original = env_file.read_text(encoding="utf-8")
        lines = original.splitlines(keepends=True)
        positions = [i for i, line in enumerate(lines) if re.match(r"^GZCTF_IMAGE=", line)]
        if len(positions) != 1:
            raise RuntimeError("deployment .env must contain exactly one GZCTF_IMAGE entry")
        image = f"{self.config['image_repository']}:sha-{target_sha}"
        lines[positions[0]] = f"GZCTF_IMAGE={image}\n"
        metadata = env_file.stat()
        temp = env_file.with_suffix(".update-tmp")
        with os.fdopen(os.open(temp, os.O_WRONLY | os.O_CREAT | os.O_TRUNC, 0o600), "w", encoding="utf-8") as output:
            output.writelines(lines)
        os.chmod(temp, stat.S_IMODE(metadata.st_mode))
        if os.geteuid() == 0:
            os.chown(temp, metadata.st_uid, metadata.st_gid)
        os.replace(temp, env_file)

    def _wait_healthy(self, timeout_seconds: int = 300) -> None:
        deadline = time.monotonic() + timeout_seconds
        while time.monotonic() < deadline:
            try:
                self._compose("exec", "-T", self.config["app_service"], "wget", "--quiet",
                              "--tries=1", "--spider", "http://localhost:3000/healthz", timeout=15)
                return
            except (RuntimeError, subprocess.TimeoutExpired):
                time.sleep(5)
        raise RuntimeError("application did not become healthy")

    def run_update(self, target_sha: str) -> None:
        stopped = False
        image_changed = False
        backup: Path | None = None
        try:
            self._save_state(phase="pulling", message="正在拉取并校验新镜像。")
            image = f"{self.config['image_repository']}:sha-{target_sha}"
            pull_image = f"{self.config['pull_image_repository']}:sha-{target_sha}"
            self._run(["docker", "pull", pull_image], timeout=900)
            revision = self._run(["docker", "image", "inspect", "--format",
                                  '{{ index .Config.Labels "org.opencontainers.image.revision" }}', pull_image])
            if revision.lower() != target_sha:
                raise RuntimeError("image revision does not match the requested commit")
            if pull_image != image:
                self._run(["docker", "image", "tag", pull_image, image])
            self._save_state(phase="backingUp", message="正在停止应用并备份数据库和上传文件。")
            self._compose("stop", self.config["app_service"], timeout=120)
            stopped = True
            backup = self._backup(target_sha)
            self._save_state(backupPath=str(backup), phase="starting", message="正在启动新版本。")
            self._set_image(target_sha)
            image_changed = True
            self._compose("up", "-d", "--no-deps", self.config["app_service"], timeout=180)
            self._wait_healthy()
            self._save_state(phase="succeeded", message="更新成功，数据库和上传文件已保留。")
        except Exception:
            LOG.exception("Update failed during phase %s", self.state.get("phase"))
            if image_changed:
                try:
                    self._compose("stop", self.config["app_service"], timeout=120)
                    if backup:
                        shutil.copy2(backup / "deployment.env", self.config["env_file"])
                except Exception:
                    LOG.exception("Could not stop new app or restore deployment image selection")
                message = "新版本未通过健康检查。已保留备份并停止应用；数据库可能已迁移，请人工恢复。"
            else:
                message = "更新未完成；旧版本配置保持不变。"
                if stopped:
                    try:
                        self._compose("up", "-d", "--no-deps", self.config["app_service"], timeout=180)
                    except Exception:
                        LOG.exception("Could not restart old application after backup failure")
                        message = "备份失败且旧应用未能自动重启，请在部署机检查。"
            self._save_state(phase="failed", message=message, backupPath=str(backup) if backup else None)
        finally:
            self.update_lock.release()


class Handler(BaseHTTPRequestHandler):
    server: "UpdaterServer"

    def log_message(self, format_string: str, *args: Any) -> None:
        LOG.info(format_string, *args)

    def _send(self, status: int, payload: dict[str, Any]) -> None:
        body = json.dumps(payload, ensure_ascii=False).encode("utf-8")
        self.send_response(status)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Content-Length", str(len(body)))
        self.send_header("Cache-Control", "no-store")
        self.end_headers()
        self.wfile.write(body)

    def _authorized(self) -> bool:
        expected = "Bearer " + self.server.updater.token
        return hmac.compare_digest(self.headers.get("Authorization", ""), expected)

    def do_GET(self) -> None:
        if not self._authorized():
            self._send(401, {"message": "Unauthorized"})
            return
        if self.path not in {"/status", "/status?refresh=1"}:
            self._send(404, {"message": "Not found"})
            return
        try:
            self._send(200, self.server.updater.status(refresh=self.path.endswith("refresh=1")))
        except Exception:
            LOG.exception("Status lookup failed")
            self._send(503, {"message": "更新状态暂不可用。"})

    def do_POST(self) -> None:
        if not self._authorized():
            self._send(401, {"message": "Unauthorized"})
            return
        if self.path != "/apply":
            self._send(404, {"message": "Not found"})
            return
        try:
            size = int(self.headers.get("Content-Length", "0"))
            if size < 1 or size > 2048:
                raise UpdateError(400, "请求内容无效。")
            payload = json.loads(self.rfile.read(size))
            target_sha = payload.get("targetSha")
            if not isinstance(target_sha, str):
                raise UpdateError(400, "目标提交无效。")
            self.server.updater.begin_update(target_sha)
            threading.Timer(0.5, self.server.updater.run_update, args=(target_sha,)).start()
            self._send(202, {"accepted": True, "targetSha": target_sha})
        except UpdateError as error:
            self._send(error.status, {"message": str(error)})
        except (json.JSONDecodeError, ValueError):
            self._send(400, {"message": "请求内容无效。"})
        except Exception:
            LOG.exception("Could not start update")
            self._send(503, {"message": "无法启动更新任务。"})


class UpdaterServer(socketserver.ThreadingMixIn, socketserver.UnixStreamServer):
    daemon_threads = True
    allow_reuse_address = True
    updater: Updater


def main() -> None:
    if len(sys.argv) != 2:
        raise SystemExit("Usage: updater.py /absolute/path/to/updater.json")
    logging.basicConfig(level=logging.INFO, format="%(asctime)s %(levelname)s %(message)s")
    updater = Updater(Path(sys.argv[1]))
    socket_path = updater.config["socket_path"]
    socket_path.parent.mkdir(mode=0o750, parents=True, exist_ok=True)
    if socket_path.exists():
        socket_path.unlink()
    with UpdaterServer(str(socket_path), Handler) as server:
        server.updater = updater
        os.chmod(socket_path, 0o660)
        LOG.info("GZCTF updater listening on %s", socket_path)
        try:
            server.serve_forever(poll_interval=0.5)
        finally:
            socket_path.unlink(missing_ok=True)


if __name__ == "__main__":
    main()
