import importlib.util
import json
import tempfile
import unittest
from pathlib import Path

spec=importlib.util.spec_from_file_location('manage',Path(__file__).parents[1]/'manage.py')
m=importlib.util.module_from_spec(spec)
spec.loader.exec_module(m)

class SafetyTests(unittest.TestCase):
    def test_bundle_tampering_is_rejected(self):
        with tempfile.TemporaryDirectory() as tmp:
            root=Path(tmp)
            for name in m.PACKAGE_FILES:(root/name).write_text('verified')
            manifest={'schema':1,'images':{k:{'tag':k,'major':v,'fingerprint':'abc'} for k,v in [('app',None),('db',15),('redis',7)]},'files':{n:m.sha256(root/n) for n in m.PACKAGE_FILES}}
            (root/'manifest.json').write_text(json.dumps(manifest))
            self.assertEqual(m.load_manifest(root)['schema'],1)
            (root/'images.tar.gz').write_text('changed')
            with self.assertRaises(m.OfflineError):m.load_manifest(root)

    def test_archive_paths_cannot_escape_root_or_follow_symlinks(self):
        with tempfile.TemporaryDirectory() as tmp, tempfile.TemporaryDirectory() as outside:
            root=Path(tmp);(root/'link').symlink_to(outside,target_is_directory=True)
            for name in ['../secrets','/etc/passwd','link/data']:
                with self.assertRaises(m.OfflineError):m.safe_child(root,name)

    def test_updates_cannot_silently_replace_database_or_redis(self):
        old={'schema':1,'images':{'db':{'major':15,'fingerprint':'old'},'redis':{'major':7,'fingerprint':'redis'}}}
        new=json.loads(json.dumps(old));new['images']['db']['major']=16
        with self.assertRaises(m.OfflineError):m.compatible_update(old,new)
        new=json.loads(json.dumps(old));new['images']['redis']['fingerprint']='different'
        with self.assertRaises(m.OfflineError):m.compatible_update(old,new)
        m.compatible_update(old,old)

    def test_backup_confirmation_and_integrity_are_required(self):
        with tempfile.TemporaryDirectory() as tmp:
            root=Path(tmp);(root/'database.dump').write_bytes(b'backup')
            metadata={'instance':'instance-one','files':{'database.dump':m.sha256(root/'database.dump')}}
            (root/'backup.json').write_text(json.dumps(metadata))
            with self.assertRaises(m.OfflineError):m.load_backup(root,'different-instance')
            self.assertEqual(m.load_backup(root,'instance-one')['instance'],'instance-one')
            (root/'database.dump').write_bytes(b'broken')
            with self.assertRaises(m.OfflineError):m.load_backup(root,'instance-one')

    def test_missing_volume_is_rejected_before_recreation(self):
        seen=[]
        def inspect(name):
            seen.append(name)
            return name=='db-existing'
        with self.assertRaises(m.OfflineError):
            m.require_volumes({'DB_VOLUME':'db-existing','REDIS_VOLUME':'redis-missing'},inspect)
        self.assertEqual(seen,['db-existing','redis-missing'])

    def test_secrets_are_generated_once_and_not_written_over(self):
        with tempfile.TemporaryDirectory() as tmp:
            path=Path(tmp)/'.env';m.write_private(path,'secret=original\n',exclusive=True)
            with self.assertRaises(FileExistsError):m.write_private(path,'secret=replaced\n',exclusive=True)
            self.assertEqual(path.read_text(),'secret=original\n')
            self.assertEqual(path.stat().st_mode & 0o777,0o600)

if __name__=='__main__':unittest.main()

class VolumeIdentityTests(unittest.TestCase):
    def test_recreated_or_foreign_volume_is_rejected(self):
        expected={'instance':'one','volumes':{'db-one':{'created':'original'}}}
        good={'Name':'db-one','CreatedAt':'original','Labels':{'gzctf.offline.instance':'one'}}
        m.validate_volume_identity('db-one',good,expected)
        for patch in [{'CreatedAt':'recreated'},{'Labels':{'gzctf.offline.instance':'other'}}]:
            with self.assertRaises(m.OfflineError):m.validate_volume_identity('db-one',dict(good,**patch),expected)

    def test_site_override_cannot_redirect_database_storage(self):
        env={'DB_VOLUME':'db-one','REDIS_VOLUME':'redis-one','DB_IMAGE':'db','REDIS_IMAGE':'redis','APP_IMAGE':'app'}
        config={'services':{'db':{'image':'db','volumes':[{'type':'volume','source':'db_data','target':'/var/lib/postgresql/data'}]},
                            'redis':{'image':'redis','volumes':[{'type':'volume','source':'redis_data','target':'/data'}]},
                            'app':{'image':'app','volumes':[{'type':'bind','source':'/test/files','target':'/app/files'}]}},
                'volumes':{'db_data':{'external':True,'name':'db-one'},'redis_data':{'external':True,'name':'redis-one'}}}
        m.validate_effective_config(config,env,Path('/test'))
        config['services']['db']['volumes'][0]={'type':'bind','source':'/other/database','target':'/var/lib/postgresql/data'}
        with self.assertRaises(m.OfflineError):m.validate_effective_config(config,env,Path('/test'))

class RestoreRetryTests(unittest.TestCase):
    def test_files_recovery_can_retry_after_interruption_between_renames(self):
        import os
        from unittest import mock
        with tempfile.TemporaryDirectory() as tmp:
            root=Path(tmp);source=root/'snapshot';source.mkdir();(source/'restored').write_text('backup')
            current=root/'files';current.mkdir();(current/'newer').write_text('preserve')
            rename=os.rename;calls=[]
            def interrupted(*args):
                calls.append(args)
                if len(calls)==2:raise OSError('simulated interruption')
                return rename(*args)
            with mock.patch.object(m.os,'rename',side_effect=interrupted):
                with self.assertRaises(OSError):m.restore_files(root,source)
            self.assertFalse(current.exists())
            m.restore_files(root,source)
            self.assertEqual((current/'restored').read_text(),'backup')
            self.assertTrue(any((p/'newer').exists() for p in root.glob('files-before-restore-*')))

class ComposeCompatibilityTests(unittest.TestCase):
    def test_existing_compose_2181_passes_preflight(self):
        from unittest import mock
        with tempfile.TemporaryDirectory() as tmp:
            deployment=m.Deployment(Path(tmp)/'install',test_host=True)
            with mock.patch.object(m,'docker',return_value=b'2.18.1\n'):
                deployment.preflight()
            self.assertTrue(deployment.root.is_dir())

    def test_versions_older_than_verified_baseline_are_rejected(self):
        from unittest import mock
        with tempfile.TemporaryDirectory() as tmp:
            deployment=m.Deployment(Path(tmp)/'install',test_host=True)
            with mock.patch.object(m,'docker',return_value=b'2.17.3\n'):
                with self.assertRaises(m.OfflineError):deployment.preflight()
