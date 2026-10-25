"""Serialized, persisted validation and replacement of staged GZCTF releases."""
from __future__ import annotations

from datetime import datetime, timezone
import json
import logging
import os
from pathlib import Path
import secrets
import threading
import time

from update_errors import UpdateError
from update_sources import acquire, normalize_source, source_digest, tree_digest

LOG = logging.getLogger('gzctf-updater')
ACTIVE = {'validating', 'accepted', 'backingUp', 'starting'}
BLOCKED = {'interrupted', 'recoveryRequired'}


def utc(timestamp=None):
    return datetime.fromtimestamp(timestamp or time.time(), timezone.utc).isoformat()


class ManagedUpdater:
    def __init__(self, config: dict, deployment=None):
        self.config = config
        if deployment is None:
            from update_deployment import create_deployment
            deployment = create_deployment(config)
        self.deployment = deployment
        self.work_dir = Path(config['work_dir']).resolve()
        self.backup_dir = Path(config['backup_dir']).resolve()
        for path in (self.work_dir, self.backup_dir):
            path.mkdir(parents=True, exist_ok=True, mode=0o700)
            if path.stat().st_mode & 0o077: raise ValueError('Update directories must be private (chmod 700).')
        self.state_file = self.backup_dir / 'managed-update-state.json'
        self.lock = threading.RLock()
        self.operation_lock = threading.Lock()
        self.state = {'phase':'idle', 'message':None, 'validation':None}
        self.candidate_path = None
        self.expires_at = 0
        self.validated_migrations = None
        if self.state_file.exists():
            self.state = json.loads(self.state_file.read_text())
            if self.state.get('phase') == 'validating':
                self.state.update(phase='idle',message='更新服务已重启，请重新校验来源。')
            elif self.state.get('phase') in ACTIVE:
                self.state.update(phase='interrupted',message='更新程序已重启，正在进行的任务已中断。请检查备份后由运维解除锁定。')
            elif self.state.get('phase') not in BLOCKED:
                self.state['phase'] = 'idle'
            if self.state.get('validation'): self.state['validation']['canApply'] = False

    def _save(self, **changes):
        with self.lock:
            self.state.update(changes, changedAtUtc=utc())
            temporary = self.state_file.with_suffix('.tmp')
            with os.fdopen(os.open(temporary,os.O_CREAT|os.O_TRUNC|os.O_WRONLY,0o600),'w') as output:
                json.dump(self.state,output,ensure_ascii=False)
            os.replace(temporary,self.state_file)

    def _check(self, key, label, status='running', message=''):
        with self.lock:
            report = self.state['validation']
            checks = report['checks']
            check = {'key':key,'label':label,'status':status,'message':message}
            for index, entry in enumerate(checks):
                if entry['key']==key: checks[index]=check; break
            else: checks.append(check)
            self._save(validation=report)

    def status(self, *, refresh=False):
        with self.lock:
            result = json.loads(json.dumps(self.state))
        report = result.get('validation')
        if report:
            report['canApply'] = bool(report.get('canApply') and self.candidate_path and
                                      self.expires_at > time.time() and result['phase']=='ready')
        result.update(configured=True,mode=self.deployment.mode,currentSha=self.deployment.current())
        return result

    def _acquire_lock(self):
        if not self.operation_lock.acquire(blocking=False): raise UpdateError(409,'已有校验或更新任务正在运行。')
        if self.state['phase'] in BLOCKED:
            self.operation_lock.release()
            raise UpdateError(409,'上次更新需要人工恢复，请检查备份和部署状态后再操作。')

    def begin_validation(self, source, *, asynchronous=True):
        try: source = normalize_source(source)
        except ValueError as error: raise UpdateError(400,str(error)) from error
        self._acquire_lock()
        identifier = secrets.token_hex(16)
        self.candidate_path = None
        report = {'id':identifier,'sourceDigest':source_digest(source),'canApply':False,'checks':[]}
        try:
            self._save(phase='validating',message='正在准备并校验候选版本。',validation=report,backupPath=None)
        except Exception:
            self.operation_lock.release(); raise
        if asynchronous:
            threading.Thread(target=self._validate,args=(source,identifier),daemon=True).start()
        else: self._validate(source,identifier)
        return self.status()

    def _validate(self, source, identifier):
        workspace = self.work_dir / identifier
        current_check = ('environment','部署环境')
        try:
            self._check(*current_check)
            message = self.deployment.preflight()
            self._check(*current_check,'passed',message)
            current_check = ('source','来源与项目识别'); self._check(*current_check)
            origin, revision = acquire(source,workspace,[Path(p) for p in self.config.get('allowed_source_roots',[])],
                allow_private=self.config.get('allow_private_downloads',False))
            self._check(*current_check,'passed','来源已复制到独立暂存目录。')
            current_check = ('build','构建与文件完整性'); self._check(*current_check)
            artifact = workspace / 'artifact'
            version = self.deployment.build(origin,artifact,revision)
            digest = tree_digest(artifact)
            self._check(*current_check,'passed','项目、系统架构、前后端文件已校验。')
            current_check = ('database','数据库兼容性与隔离试运行'); self._check(*current_check)
            migrations, detail = self.deployment.validate(artifact,workspace)
            if sorted(self.deployment.migrations()) != sorted(migrations):
                raise UpdateError(409,'校验期间数据库结构发生变化，请重新校验。')
            self._check(*current_check,'passed',detail)
            self.deployment.preflight()
            self.candidate_path = artifact
            self.validated_migrations = sorted(migrations)
            self.expires_at = time.time()+3600
            with self.lock:
                report=self.state['validation']
                report.update(version=version,artifactHash=digest,revision=revision,canApply=True,
                              expiresAtUtc=utc(self.expires_at))
                self._save(phase='ready',message='全部校验通过，可以替换当前版本。',validation=report)
        except Exception as error:
            LOG.exception('Candidate validation failed')
            self._check(*current_check,'failed',str(error))
            self._save(phase='invalid',message='候选版本未通过校验，当前平台保持运行。')
        finally: self.operation_lock.release()

    def begin_apply(self, identifier, source, *, asynchronous=True):
        self._acquire_lock()
        try:
            report=self.state.get('validation') or {}
            if self.state['phase']!='ready' or not report.get('canApply') or identifier!=report.get('id'):
                raise UpdateError(409,'没有可用的校验结果，请先校验来源。')
            if self.expires_at <= time.time(): raise UpdateError(409,'校验结果已过期，请重新校验。')
            if report['sourceDigest']!=source_digest(source): raise UpdateError(409,'更新来源已变化，请重新校验。')
            if not self.candidate_path or tree_digest(self.candidate_path)!=report['artifactHash']:
                raise UpdateError(409,'暂存文件发生变化，请重新校验。')
            if sorted(self.deployment.migrations())!=self.validated_migrations:
                raise UpdateError(409,'数据库结构已变化，请重新校验。')
            self.deployment.preflight()
            report['canApply']=False
            self._save(phase='accepted',message='更新任务已接受。',validation=report)
        except Exception:
            self.operation_lock.release(); raise
        if asynchronous:
            threading.Timer(0.5,self._apply).start()
        else: self._apply()
        return {'accepted':True,'validationId':identifier}

    def _apply(self):
        backup = self.backup_dir / (datetime.now(timezone.utc).strftime('%Y%m%dT%H%M%SZ')+'-'+secrets.token_hex(5))
        metadata = None
        stopped = switched = False
        try:
            self._save(phase='backingUp',message='正在停止应用并备份数据库、配置和上传文件。',backupPath=str(backup))
            self.deployment.stop(); stopped=True
            metadata = self.deployment.backup(backup)
            (backup/'deployment.json').write_text(json.dumps(metadata,indent=2))
            # Verify again after backup: no mutable candidate can be installed using an old report.
            if tree_digest(self.candidate_path)!=self.state['validation']['artifactHash']:
                raise UpdateError(409,'备份期间暂存文件发生变化，已取消替换。')
            self._save(phase='starting',message='正在切换并启动新版本。')
            switched=True
            self.deployment.activate(self.candidate_path)
            self.deployment.start(); self.deployment.health()
            self._save(phase='succeeded',message='更新成功，配置和业务数据已保留。')
        except Exception:
            LOG.exception('Replacement failed')
            try:
                if switched and metadata:
                    self.deployment.stop()
                    self.deployment.restore(backup,metadata)
                if stopped:
                    self.deployment.start(); self.deployment.health()
                self._save(phase='failed',message='更新失败，已恢复原版本及数据。请重新校验后再试。')
            except Exception:
                LOG.exception('Recovery failed')
                self._save(phase='recoveryRequired',message='恢复未完成，已暂停更新。请使用备份目录由运维恢复后解除锁定。')
        finally: self.operation_lock.release()
