import contextlib
import hashlib
import io
import json
from pathlib import Path
import sys
import tarfile
import tempfile
import unittest
from unittest import mock
import uuid
import zipfile
sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import offline_updates as offline
from update_errors import UpdateError


class FakeDeployment:
    def __init__(self, root):
        self.root = Path(root)
        self.updates = []
    @contextlib.contextmanager
    def lock(self): yield
    def installed(self): return {'INSTANCE_ID': 'instance'}
    def verify_application(self): pass
    def release(self): return {'schema': 1, 'version': 'old', 'images': {k: {'fingerprint': 'a'*64, 'tag': 'gzctf-offline/'+k+':'+'a'*16+'-amd64', 'major': 1} for k in ('app','db','redis')}}
    def preflight(self): pass
    def validate_candidate(self, *args): pass
    def import_bundle(self, *args): pass
    def update(self, bundle, progress=None):
        self.updates.append(bundle)
        if progress: progress('backingUp', '备份', '/private/backup')


class OfflineTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory(); self.addCleanup(self.tmp.cleanup)
        self.root = Path(self.tmp.name).resolve()
        self.packages = self.root/'packages'; self.packages.mkdir()
        self.runtime = self.root/'runtime'; self.runtime.mkdir()
        self.config = {'deployment_mode':'offline','runtime_dir':str(self.runtime),'work_dir':str(self.root/'work'),
                       'allowed_package_roots':[str(self.packages)],'package_directory':str(self.packages),'max_upload_bytes':1000000}
        self.dep = FakeDeployment(self.runtime)
        self.updater = offline.OfflineUpdater(self.config, deployment=self.dep)
    def archive(self, name='release.tar.gz', members=None):
        path = self.packages/name
        with tarfile.open(str(path),'w:gz') as archive:
            for filename, content in (members or [('bundle/manifest.json',b'{}')]):
                info=tarfile.TarInfo(filename); info.size=len(content);archive.addfile(info,io.BytesIO(content))
        return path
    def test_scan_flat_bound_and_reject_symlinks(self):
        self.archive(); (self.packages/'bad.tar').symlink_to(self.packages/'release.tar.gz')
        (self.packages/'sub').mkdir(); (self.packages/'sub'/'hidden.tar').write_bytes(b'x')
        items=self.updater.packages()['items'];self.assertEqual([x['name'] for x in items],['release.tar.gz'])
        self.assertFalse(items[0]['checksumProvided'])
    def test_settings_reject_similar_prefix_and_symlink(self):
        sibling=self.root/'packages-evil';sibling.mkdir()
        link=self.packages/'link';link.symlink_to(sibling,target_is_directory=True)
        for path in [sibling,link,self.packages/'..']:
            with self.assertRaises(UpdateError):self.updater.settings(str(path))
    def test_upload_streams_and_truncated_never_registers(self):
        class SmallReads(io.BytesIO):
            def read(self,n=-1):
                if n<0 or n>1024*1024:raise AssertionError('unbounded read')
                return super().read(n)
        package=self.updater.upload(SmallReads(b'abc'),3,'test.tar')
        self.assertEqual(package['sha256'],hashlib.sha256(b'abc').hexdigest())
        with self.assertRaises(UpdateError):self.updater.upload(SmallReads(b'x'),4,'bad.tar')
        self.assertEqual(len(self.updater.packages()['items']),1)
    def test_upload_limit_and_filename(self):
        for size,name in [(1000001,'a.tar'),(3,'../a.tar'),(3,'a.exe')]:
            with self.assertRaises(UpdateError):self.updater.upload(io.BytesIO(b'abc'),size,name)
    def test_extract_traversal_duplicate_symlink_and_bomb(self):
        for members in [[('../escape',b'x')],[('a',b'x'),('a',b'y')]]:
            path=self.archive(members=members)
            with self.assertRaises(UpdateError):offline.extract_archive(path,self.root/uuid.uuid4().hex,100,5)
        path=self.archive(members=[('a',b'x'*101)])
        with self.assertRaises(UpdateError):offline.extract_archive(path,self.root/'bomb',100,5)
        path=self.packages/'link.tar'
        with tarfile.open(str(path),'w') as archive:
            info=tarfile.TarInfo('link');info.type=tarfile.SYMTYPE;info.linkname='/etc';archive.addfile(info)
        with self.assertRaises(UpdateError):offline.extract_archive(path,self.root/'links',100,5)
    def test_zip_special_and_traversal(self):
        path=self.packages/'a.zip'
        with zipfile.ZipFile(str(path),'w') as archive:archive.writestr('../bad','x')
        with self.assertRaises(UpdateError):offline.extract_archive(path,self.root/'zip',100,5)
    def test_checksum_is_optional_and_mismatch_blocks_before_deployment(self):
        self.archive();item=self.updater.packages()['items'][0]
        self.updater.checksum(item['id'],'0'*64)
        result=self.updater.begin_validation(item['id']);self.updater.wait_for_task()
        status=self.updater.status();self.assertEqual(status['phase'],'validationFailed')
        self.assertFalse(status['validation']['canApply']);self.assertEqual(result['accepted'],True)
    def valid_report(self):
        self.archive();item=next(x for x in self.updater.packages()['items'] if x['source']=='directory')
        manifest={'schema':1,'version':'new','platform':'linux/amd64','images':self.dep.release()['images']}
        with mock.patch.object(offline.MANAGE,'load_manifest',return_value=manifest), mock.patch.object(offline,'inspect_image_archive'):
            self.updater.begin_validation(item['id']);self.updater.wait_for_task()
        self.assertEqual(self.updater.status()['phase'],'ready')
        return item,self.updater.status()['validation']
    def test_report_binding_apply_and_idempotency(self):
        item,report=self.valid_report();op=str(uuid.uuid4())
        self.updater.begin_apply(item['id'],{'validationId':report['id'],'operationId':op,'maintenanceConfirmed':True})
        self.updater.wait_for_task();self.assertEqual(len(self.dep.updates),1)
        self.updater.begin_apply(item['id'],{'validationId':report['id'],'operationId':op,'maintenanceConfirmed':True})
        self.assertEqual(len(self.dep.updates),1)
        self.assertEqual(self.updater.status()['backupPath'],'/private/backup')
    def test_completed_report_stays_consumed_after_restart(self):
        item,report=self.valid_report()
        self.updater.begin_apply(item['id'],{'validationId':report['id'],'operationId':str(uuid.uuid4()),'maintenanceConfirmed':True})
        self.updater.wait_for_task()
        restarted=offline.OfflineUpdater(self.config,deployment=self.dep)
        with self.assertRaises(UpdateError):restarted.begin_apply(item['id'],{'validationId':report['id'],'operationId':str(uuid.uuid4()),'maintenanceConfirmed':True})
    def test_audit_records_actor_and_digest_without_token(self):
        self.updater.request_actor.value='12345678-1234-1234-1234-123456789abc'
        result=self.updater.upload(io.BytesIO(b'abc'),3,'a.tar')
        record=json.loads((self.updater.work/'audit.jsonl').read_text().splitlines()[-1])
        self.assertEqual(record['actor'],self.updater.request_actor.value)
        self.assertEqual(record['sha256'],result['sha256'])
    def test_replaced_source_and_deployment_rejected(self):
        item,report=self.valid_report()
        (self.packages/item['name']).write_bytes(b'replaced')
        with self.assertRaises(UpdateError):self.updater.begin_apply(item['id'],{'validationId':report['id'],'operationId':str(uuid.uuid4()),'maintenanceConfirmed':True})
        self.assertEqual(self.dep.updates,[])
    def test_restart_marks_uncertain_task_and_blocks_apply(self):
        self.updater._save_state(phase='starting',backupPath='/backup')
        other=offline.OfflineUpdater(self.config,deployment=self.dep)
        self.assertEqual(other.status()['phase'],'interrupted')
        with self.assertRaises(UpdateError):other.begin_validation('x'*32)
    def test_status_never_returns_internal_error(self):
        self.dep.release=mock.Mock(side_effect=RuntimeError('password=secret'))
        self.assertNotIn('secret',json.dumps(self.updater.status()))

if __name__=='__main__': unittest.main()

class OfflineTransportTests(OfflineTests):
    def test_http_routes_settings_scan_upload_and_auth(self):
        import http.client
        import threading
        import updater
        self.updater.token='t'*64
        class QuietHandler(updater.Handler):
            def log_message(self,*args):pass
        server=updater.UpdaterTCPServer(('127.0.0.1',0),QuietHandler);server.updater=self.updater
        thread=threading.Thread(target=server.serve_forever,daemon=True);thread.start()
        self.addCleanup(thread.join);self.addCleanup(server.server_close);self.addCleanup(server.shutdown)
        def request(method,path,body=None,headers=None):
            connection=http.client.HTTPConnection(*server.server_address,timeout=3)
            connection.request(method,path,body=body,headers=headers or {})
            response=connection.getresponse();result=(response.status,json.load(response));connection.close();return result
        auth={'Authorization':'Bearer '+'t'*64}
        self.assertEqual(request('GET','/packages/settings')[0],401)
        status,payload=request('GET','/packages/settings',headers=auth)
        self.assertEqual(status,200);self.assertEqual(payload['mode'],'offline')
        self.assertEqual(request('PUT','/packages/settings',json.dumps({'directory':str(self.packages)}),auth)[0],200)
        status,payload=request('POST','/packages/upload',b'abc',dict(auth,**{'X-File-Name':'update.tar'}))
        self.assertEqual(status,201);self.assertEqual(payload['size'],3)
        self.assertEqual(request('GET','/packages',headers=auth)[1]['items'][0]['id'],payload['id'])

class ImageMetadataTests(unittest.TestCase):
    def test_application_only_archive_uses_existing_pinned_database_and_cache(self):
        with tempfile.TemporaryDirectory() as temp:
            path=Path(temp)/'images.tar.gz'
            config={'os':'linux','architecture':'amd64','rootfs':{'diff_ids':[]},'config':{}}
            raw=json.dumps(config).encode();digest=hashlib.sha256(raw).hexdigest()
            info={'Os':'linux','Architecture':'amd64','RootFS':{'Layers':[]},'Config':{}}
            fingerprint=offline.MANAGE.fingerprint(info)
            app={'tag':'gzctf-offline/app:'+fingerprint[:16]+'-amd64','fingerprint':fingerprint}
            manifest={'images':{'app':app,'db':{'tag':'db','fingerprint':'a'*64},'redis':{'tag':'redis','fingerprint':'b'*64}}}
            entries=[('manifest.json',json.dumps([{'Config':digest+'.json','RepoTags':[app['tag']],'Layers':[]}]).encode()),(digest+'.json',raw)]
            with tarfile.open(str(path),'w:gz') as archive:
                for name,value in entries:
                    member=tarfile.TarInfo(name);member.size=len(value);archive.addfile(member,io.BytesIO(value))
            offline.inspect_image_archive(path,manifest)
            manifest['images']['app']['fingerprint']='0'*64
            with self.assertRaises(UpdateError):offline.inspect_image_archive(path,manifest)

class BindingTests(OfflineTests):
    def test_current_installation_change_and_expiry_reject_apply(self):
        for change in ('identity','expiry'):
            item,report=self.valid_report()
            if change=='identity':self.dep.installed=lambda:{'INSTANCE_ID':'different'}
            else:self.updater.report['expires']=0
            with self.assertRaises(UpdateError):self.updater.begin_apply(item['id'],{'validationId':report['id'],'operationId':str(uuid.uuid4()),'maintenanceConfirmed':True})
            self.dep.installed=lambda:{'INSTANCE_ID':'instance'}
    def test_shared_cli_file_lock_blocks_validation(self):
        self.archive();item=self.updater.packages()['items'][0]
        real=offline.MANAGE.Deployment(self.runtime,True)
        self.dep.lock=real.lock
        with real.lock():
            with self.assertRaises(UpdateError) as error:self.updater.begin_validation(item['id'])
            self.assertEqual(error.exception.status,409)
        self.assertTrue(self.updater.update_lock.acquire(False));self.updater.update_lock.release()
    def test_changed_candidate_rejected_and_upload_survives_restart(self):
        uploaded=self.updater.upload(io.BytesIO(b'abc'),3,'a.tar')
        other=offline.OfflineUpdater(self.config,deployment=self.dep)
        self.assertIn(uploaded['id'],[x['id'] for x in other.packages()['items']])
        item,report=self.valid_report()
        (Path(self.updater.report['bundle'])/'manifest.json').write_text('changed')
        with self.assertRaises(UpdateError):self.updater.begin_apply(item['id'],{'validationId':report['id'],'operationId':str(uuid.uuid4()),'maintenanceConfirmed':True})
    def test_copy_rejects_symlink_in_ancestor_at_open_time(self):
        other=self.root/'outside';other.mkdir();(other/'secret.tar').write_bytes(b'secret')
        alias=self.packages/'alias';alias.symlink_to(other,target_is_directory=True)
        with self.assertRaises((UpdateError,OSError)):self.updater._copy(alias/'secret.tar',self.root/'copy.tar')

class ArchiveMetadataBoundsTests(unittest.TestCase):
    def test_large_pax_header_rejected_before_tarfile_allocates_payload(self):
        with tempfile.TemporaryDirectory() as temp:
            root=Path(temp);path=root/'pax.tar'
            member=tarfile.TarInfo('pax');member.type=tarfile.XHDTYPE;member.size=2*1024*1024
            path.write_bytes(member.tobuf()+b'\0'*1024)
            with self.assertRaises(UpdateError):offline.extract_archive(path,root/'out',1024*1024,10)
    def test_zip_entry_count_rejected_before_opening_central_directory(self):
        with tempfile.TemporaryDirectory() as temp:
            root=Path(temp);path=root/'count.zip'
            with zipfile.ZipFile(str(path),'w') as archive:
                for n in range(5):archive.writestr(str(n),'x')
            with mock.patch.object(zipfile,'ZipFile',side_effect=AssertionError('central directory should not be allocated')):
                with self.assertRaises(UpdateError):offline.extract_archive(path,root/'out',100,2)

class StructuredChecksTests(OfflineTests):
    def test_bad_manifest_has_a_failed_structured_check(self):
        self.archive()
        item=self.updater.packages()['items'][0]
        self.updater.begin_validation(item['id']);self.updater.wait_for_task()
        status=self.updater.status()
        self.assertEqual(status['phase'],'validationFailed')
        self.assertTrue(any(check['status']=='failed' for check in status['validation']['checks']))
