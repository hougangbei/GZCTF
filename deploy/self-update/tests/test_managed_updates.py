import json
from pathlib import Path
import sys
import tempfile
import time
import unittest
sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
from managed_updates import ManagedUpdater, UpdateError

SOURCE={'kind':'directory','location':'/server/release','ref':'','sha256':''}

class FakeDeployment:
    mode='native'; runtime='osx-arm64'
    def __init__(self): self.calls=[]; self.fail_start=False; self.fail_restore=False
    def preflight(self): return '目录可写，空间充足'
    def build(self, source, artifact, revision):
        artifact.mkdir(); (artifact/'GZCTF.dll').write_text('assembly'); return '1.8.8'
    def validate(self, artifact, workspace): return ['old-migration'], '已在隔离数据库通过健康检查'
    def migrations(self): return ['old-migration']
    def backup(self, destination): destination.mkdir(); self.calls.append('backup'); return {'old':'release'}
    def stop(self): self.calls.append('stop')
    def activate(self, artifact): self.calls.append('activate')
    def start(self):
        self.calls.append('start')
        if self.fail_start and self.calls.count('start')==1: raise RuntimeError('start failed')
    def restore(self, backup, metadata):
        self.calls.append('restore')
        if self.fail_restore: raise RuntimeError('restore failed')
    def health(self): self.calls.append('health')
    def current(self): return 'previous'

class ManagedTests(unittest.TestCase):
    def make(self, root, deployment=None):
        return ManagedUpdater({'work_dir':str(root/'work'),'backup_dir':str(root/'backups'),
            'allowed_source_roots':[str(root)]}, deployment or FakeDeployment())
    def prepare(self, updater, root):
        # A realistic prepared artifact without external build tools.
        import managed_updates
        original=managed_updates.acquire
        def fake(source,workspace,roots,**kwargs):
            origin=workspace/'source'; origin.mkdir(parents=True); return origin,'abc'
        managed_updates.acquire=fake
        try: updater.begin_validation(SOURCE, asynchronous=False)
        finally: managed_updates.acquire=original
        return updater.status()['validation']
    def test_preparation_is_isolated_and_apply_requires_immutable_artifact(self):
        with tempfile.TemporaryDirectory() as tmp:
            root=Path(tmp); agent=self.make(root); report=self.prepare(agent,root)
            self.assertTrue(report['canApply']); self.assertEqual(agent.deployment.calls,[])
            (agent.candidate_path/'GZCTF.dll').write_text('tampered')
            with self.assertRaises(UpdateError): agent.begin_apply(report['id'],SOURCE,asynchronous=False)
            self.assertEqual(agent.deployment.calls,[])
    def test_expired_or_changed_source_cannot_apply(self):
        with tempfile.TemporaryDirectory() as tmp:
            root=Path(tmp); agent=self.make(root); report=self.prepare(agent,root)
            with self.assertRaises(UpdateError): agent.begin_apply(report['id'],{**SOURCE,'location':'/other'},asynchronous=False)
            agent.expires_at=time.time()-1
            with self.assertRaises(UpdateError): agent.begin_apply(report['id'],SOURCE,asynchronous=False)
    def test_source_generation_prevents_reusing_report_after_save_away_and_back(self):
        with tempfile.TemporaryDirectory() as tmp:
            root=Path(tmp); agent=self.make(root)
            source={**SOURCE,'generation':'a'*32}
            import managed_updates
            original=managed_updates.acquire
            def fake(source,workspace,roots,**kwargs):
                origin=workspace/'source'; origin.mkdir(parents=True); return origin,'abc'
            managed_updates.acquire=fake
            try: agent.begin_validation(source,asynchronous=False)
            finally: managed_updates.acquire=original
            report=agent.status()['validation']
            with self.assertRaises(UpdateError):
                agent.begin_apply(report['id'],{**source,'generation':'b'*32},asynchronous=False)

    def test_updated_database_migrations_require_validation_again(self):
        with tempfile.TemporaryDirectory() as tmp:
            root=Path(tmp); agent=self.make(root); report=self.prepare(agent,root)
            agent.deployment.migrations=lambda:['old-migration','new-migration']
            with self.assertRaises(UpdateError): agent.begin_apply(report['id'],SOURCE,asynchronous=False)
    def test_failed_start_restores_database_and_old_version_before_restart(self):
        with tempfile.TemporaryDirectory() as tmp:
            root=Path(tmp); adapter=FakeDeployment(); agent=self.make(root,adapter); report=self.prepare(agent,root)
            adapter.fail_start=True; agent.begin_apply(report['id'],SOURCE,asynchronous=False)
            self.assertEqual(adapter.calls,['stop','backup','activate','start','stop','restore','start','health'])
            self.assertEqual(agent.status()['phase'],'failed')
    def test_failed_restore_blocks_further_updates(self):
        with tempfile.TemporaryDirectory() as tmp:
            root=Path(tmp); adapter=FakeDeployment(); agent=self.make(root,adapter); report=self.prepare(agent,root)
            adapter.fail_start=True; adapter.fail_restore=True; agent.begin_apply(report['id'],SOURCE,asynchronous=False)
            self.assertEqual(agent.status()['phase'],'recoveryRequired')
            with self.assertRaises(UpdateError): agent.begin_validation(SOURCE,asynchronous=False)
    def test_restart_invalidates_ready_candidate(self):
        with tempfile.TemporaryDirectory() as tmp:
            root=Path(tmp); agent=self.make(root); self.prepare(agent,root)
            restarted=self.make(root)
            self.assertFalse(restarted.status().get('validation',{}).get('canApply',False))
    def test_overlapping_operations_rejected(self):
        with tempfile.TemporaryDirectory() as tmp:
            root=Path(tmp); agent=self.make(root); agent.operation_lock.acquire()
            try:
                with self.assertRaises(UpdateError): agent.begin_validation(SOURCE,asynchronous=False)
            finally: agent.operation_lock.release()
if __name__=='__main__': unittest.main()
