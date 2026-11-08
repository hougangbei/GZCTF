import importlib.util
import json
from pathlib import Path
import tempfile
from types import SimpleNamespace
import unittest
from unittest import mock

spec=importlib.util.spec_from_file_location('bootstrap',Path(__file__).resolve().parents[1]/'install-offline-updater.py')
bootstrap=importlib.util.module_from_spec(spec);spec.loader.exec_module(bootstrap)

class FakeDeployment:
    def __init__(self,root):self.root=Path(root);self.checks=0
    def preflight(self):self.checks+=1
    def installed(self):return {'INSTANCE_ID':'test'}
    def compose(self,*args):return b'container'
    def healthy(self,*args):pass

class BootstrapTests(unittest.TestCase):
    def setUp(self):
        self.tmp=tempfile.TemporaryDirectory();self.addCleanup(self.tmp.cleanup)
        self.root=Path(self.tmp.name).resolve();self.runtime=self.root/'runtime';self.runtime.mkdir()
        for name,content in [('compose.site.yml','services:\n  app:\n    environment:\n      MAIL: secret\n'),('appsettings.json','{"Registries":{"Harbor":"private"}}'),('.env','INSTANCE_ID=test\n')]:
            (self.runtime/name).write_text(content)
        self.args=SimpleNamespace(runtime=str(self.runtime),host_dir=str(self.root/'host'),packages=str(self.root/'packages'),systemd_dir=str(self.root/'systemd'),python='/usr/bin/python3',apply=False,activate=False,socket_gid=0,app_uid=0)
        self.dep=FakeDeployment(self.runtime)
    def test_dry_run_is_read_only(self):
        plan=bootstrap.install(self.args,deployment=self.dep)
        self.assertTrue(plan['dryRun']);self.assertFalse((self.root/'host').exists());self.assertFalse((self.runtime/'compose.updater.yml').exists())
    def test_install_idempotent_preserves_site_secrets_and_socket_mounts(self):
        originals={p.name:p.read_bytes() for p in self.runtime.iterdir()}
        self.args.apply=True
        result=bootstrap.install(self.args,deployment=self.dep)
        for name,content in originals.items():self.assertEqual((self.runtime/name).read_bytes(),content)
        overlay=json.loads((self.runtime/'compose.updater.yml').read_text())
        self.assertTrue(all(v['read_only'] for v in overlay['services']['app']['volumes']))
        self.assertNotIn('docker.sock',json.dumps(overlay))
        token=(self.root/'host'/'updater.token').read_bytes()
        self.assertNotIn(token.decode().strip(),json.dumps(result))
        self.assertFalse((self.root/'host'/'updater.token').stat().st_mode&0o077)
        result2=bootstrap.install(self.args,deployment=self.dep)
        self.assertEqual((self.root/'host'/'updater.token').read_bytes(),token)
        self.assertEqual(result2['changed'],[])
        unit=(self.root/'systemd'/'gzctf-offline-updater.service').read_text()
        self.assertIn('ProtectHome=false',unit)
    def test_bootstrap_rejects_symlinks_and_wrong_identity(self):
        self.args.host_dir=str(self.root/'alias');Path(self.args.host_dir).symlink_to(self.runtime,target_is_directory=True)
        with self.assertRaises(Exception):bootstrap.install(self.args,deployment=self.dep)

    def test_reactivation_restarts_service_after_host_code_changes(self):
        self.args.apply=True;self.args.activate=True
        with mock.patch.object(bootstrap.subprocess,'run'):
            bootstrap.install(self.args,deployment=self.dep)
        installed=self.root/'host'/'self-update'/'offline_updates.py'
        installed.write_text(installed.read_text()+'\n# stale tool\n')
        with mock.patch.object(bootstrap.subprocess,'run') as run:
            bootstrap.install(self.args,deployment=self.dep)
        calls=[call.args[0] for call in run.call_args_list]
        self.assertIn(['systemctl','restart','gzctf-offline-updater.service'],calls)
        self.assertNotIn(['systemctl','enable','--now','gzctf-offline-updater.service'],calls)

    def test_staged_apply_then_activate_converges_app_even_when_files_unchanged(self):
        self.args.apply=True
        bootstrap.install(self.args,deployment=self.dep)
        self.args.activate=True
        with mock.patch.object(bootstrap.subprocess,'run') as run,mock.patch.object(self.dep,'compose',return_value=b'') as compose:
            result=bootstrap.install(self.args,deployment=self.dep)
        self.assertEqual(result['changed'],[])
        self.assertIn(mock.call('up','-d','--no-deps','app'),compose.call_args_list)
        self.assertFalse(any('--force-recreate' in call.args for call in compose.call_args_list))
        self.assertIn(mock.call(['systemctl','restart','gzctf-offline-updater.service'],check=True),run.call_args_list)
