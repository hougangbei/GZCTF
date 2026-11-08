import copy
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest
from unittest import mock
spec=importlib.util.spec_from_file_location('fixed_manage',Path(__file__).resolve().parents[2]/'offline'/'manage.py')
manage=importlib.util.module_from_spec(spec);spec.loader.exec_module(manage)

class CandidateTests(unittest.TestCase):
    def test_changed_commands_and_privileged_mounts_rejected_before_execution(self):
        with tempfile.TemporaryDirectory() as temp:
            root=Path(temp);(root/'.env').write_text('INSTANCE_ID=x\nCOMPOSE_PROJECT=x\n')
            deployment=manage.Deployment(root,True)
            baseline={'services':{'app':{'image':'old','volumes':[]},'db':{'image':'db'},'redis':{'image':'redis'}}}
            manifest={'images':{k:{'tag':k} for k in ('app','db','redis')}}
            for malicious in [{'privileged':True},{'command':['sh','-c','evil']},{'volumes':[{'type':'bind','source':'/','target':'/host'}]}]:
                candidate=copy.deepcopy(baseline)
                for kind in ('app','db','redis'):candidate['services'][kind]['image']=kind
                candidate['services']['app'].update(malicious)
                with mock.patch.object(deployment,'compose',return_value=json.dumps(baseline).encode()),mock.patch.object(manage,'command',return_value=json.dumps(candidate).encode()),mock.patch.object(manage,'validate_effective_config'):
                    with self.assertRaises(manage.OfflineError):deployment.validate_candidate(root,manifest)
    def test_save_release_never_installs_package_python(self):
        with tempfile.TemporaryDirectory() as temp:
            root=Path(temp)/'runtime';root.mkdir();bundle=Path(temp)/'bundle';bundle.mkdir()
            (root/'.env').write_text('INSTANCE_ID=x\n');(root/'manage.py').write_text('TRUSTED')
            (bundle/'manage.py').write_text('EVIL');(bundle/'compose.yml').write_text('services: {}')
            manage.Deployment(root,True).save_release(bundle,{'images':{kind:{'tag':kind} for kind in ('app','db','redis')}})
            self.assertNotEqual((root/'manage.py').read_text(),'EVIL')
if __name__=='__main__':unittest.main()

class OverlayTests(unittest.TestCase):
    def test_optional_updater_overlay_used_without_rewriting_site(self):
        with tempfile.TemporaryDirectory() as temp:
            root=Path(temp);(root/'.env').write_text('INSTANCE_ID=x\nCOMPOSE_PROJECT=x\n')
            (root/'compose.updater.yml').write_text('{}')
            with mock.patch.object(manage,'command',return_value=b'{}') as command:
                manage.Deployment(root,True).compose('config')
                self.assertIn(root.resolve()/'compose.updater.yml',command.call_args[0][0])

class ManifestBoundsTests(unittest.TestCase):
    def test_manifest_too_large_rejected_before_json_decode(self):
        with tempfile.TemporaryDirectory() as temp:
            path=Path(temp)/'manifest.json'
            with path.open('wb') as stream:stream.truncate(1024*1024+1)
            with mock.patch.object(manage.json,'loads',side_effect=AssertionError('must reject before allocation')):
                with self.assertRaises(manage.OfflineError):manage.load_manifest(temp)

class ApplicationIdentityTests(unittest.TestCase):
    def test_replaced_running_application_image_rejects_validation_binding(self):
        with tempfile.TemporaryDirectory() as temp:
            root=Path(temp);(root/'.env').write_text('INSTANCE_ID=x\nCOMPOSE_PROJECT=x\nAPP_IMAGE=app\n')
            deployment=manage.Deployment(root,True)
            container={'Image':'different-id','Config':{'Labels':{'com.docker.compose.project':'x','com.docker.compose.service':'app'}}}
            image={'Os':'linux','Architecture':'amd64','Config':{},'RootFS':{'Layers':[]}}
            with mock.patch.object(deployment,'compose',return_value=b'container'),mock.patch.object(deployment,'release',return_value={'images':{'app':{'fingerprint':'0'*64}}}),mock.patch.object(manage,'docker',side_effect=[json.dumps([container]).encode(),json.dumps([image]).encode()]):
                with self.assertRaises(manage.OfflineError):deployment.verify_application()
