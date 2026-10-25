from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import patch
from collections import namedtuple
sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
from update_deployment import NativeDeployment, Database, validate_database_identity, free_port
from update_sources import SourceError

class DeploymentTests(unittest.TestCase):
    def test_database_names_cannot_target_postgres_or_inject_sql(self):
        for name in ['postgres','template1','template0','x; DROP DATABASE prod','']:
            with self.subTest(name=name),self.assertRaises(ValueError): validate_database_identity(name,'gzctf')
    def test_native_running_ports_can_avoid_existing_services(self):
        config={'deployment_mode':'native','work_dir':'/tmp/work','files_dir':'/tmp/files',
            'database':{'container':'db','name':'gzctf','user':'gzctf'},
            'native':{'current_link':'/tmp/current','pid_file':'/tmp/pid.json','settings_file':'/tmp/settings.json',
                      'server_port':18087,'metric_port':53007}}
        env=NativeDeployment(config).environment()
        self.assertEqual(env['GZCTF_SERVER_PORT'],'18087')
        self.assertEqual(env['GZCTF_METRIC_PORT'],'53007')
        self.assertEqual(env['GZCTF_DataProtection__ApplicationName'],'/tmp/current/')

    def test_backup_filesystem_capacity_is_checked_separately(self):
        with tempfile.TemporaryDirectory() as tmp:
            root=Path(tmp)
            for name in ['work','backup','files']: (root/name).mkdir()
            (root/'settings.json').write_text('{}')
            config={'deployment_mode':'native','work_dir':str(root/'work'),'backup_dir':str(root/'backup'),
                'files_dir':str(root/'files'),'database':{'container':'db','name':'gzctf','user':'gzctf'},
                'native':{'current_link':str(root/'current'),'pid_file':str(root/'pid.json'),'settings_file':str(root/'settings.json')}}
            adapter=NativeDeployment(config)
            adapter.database.command=lambda *args: b''
            adapter.database.size=lambda: 1024
            usage=namedtuple('Usage','total used free')
            def capacity(path):
                return usage(10*1024**3,0,1 if Path(path)==root/'backup' else 9*1024**3)
            with patch('shutil.which',return_value='/bin/tool'),patch('shutil.disk_usage',side_effect=capacity):
                with self.assertRaises(SourceError): adapter.preflight()

    def test_validation_uses_local_storage_even_if_deployment_uses_object_storage(self):
        config={'deployment_mode':'native','work_dir':'/tmp/work','files_dir':'/tmp/files',
            'database':{'container':'db','name':'gzctf','user':'gzctf','password':'secret'},
            'native':{'current_link':'/tmp/current','pid_file':'/tmp/pid.json','settings_file':'/tmp/settings.json'}}
        env=NativeDeployment(config).environment(validation=True,database='gzctf_check_test',server_port=50001,metric_port=50002)
        self.assertEqual(env.get('GZCTF_ConnectionStrings__Storage'),'disk://path=./files')

    def test_connection_password_delimiters_are_quoted(self):
        database=Database({'container':'db','name':'gzctf','user':'gzctf','password':'a;b"c'})
        self.assertIn('Password="a;b""c"',database.connection('gzctf'))

    def test_start_does_not_overwrite_existing_owned_process(self):
        with tempfile.TemporaryDirectory() as tmp:
            root=Path(tmp); release=root/'release'; release.mkdir(); (release/'GZCTF.dll').write_text('fake')
            (root/'current').symlink_to(release)
            import json,os
            pidfile=root/'pid.json'; assembly=str((release/'GZCTF.dll').resolve())
            pidfile.write_text(json.dumps({'pid':os.getpid(),'assembly':assembly}))
            config={'deployment_mode':'native','work_dir':str(root/'work'),'files_dir':str(root/'files'),
                'database':{'container':'db','name':'gzctf','user':'gzctf'},
                'native':{'current_link':str(root/'current'),'pid_file':str(pidfile),'settings_file':str(root/'settings.json')}}
            adapter=NativeDeployment(config)
            with patch('update_deployment.run',return_value=assembly.encode()),patch('os.getpgid',return_value=os.getpid()),patch('subprocess.Popen') as launch:
                adapter.start()
                launch.assert_not_called()
            self.assertEqual(json.loads(pidfile.read_text())['pid'],os.getpid())

    def test_real_process_start_twice_keeps_original_pid_and_stop_releases_ports(self):
        import json
        with tempfile.TemporaryDirectory() as tmp:
            root=Path(tmp); release=root/'release'; release.mkdir()
            (release/'GZCTF.dll').write_text("""import os,threading
from http.server import HTTPServer,BaseHTTPRequestHandler
class Handler(BaseHTTPRequestHandler):
 def do_GET(self):
  self.send_response(200);self.end_headers();self.wfile.write(b'Healthy')
 def log_message(self,*args): pass
for key in ['GZCTF_SERVER_PORT','GZCTF_METRIC_PORT']:
 server=HTTPServer(('127.0.0.1',int(os.environ[key])),Handler)
 threading.Thread(target=server.serve_forever,daemon=True).start()
threading.Event().wait()
""")
            (root/'current').symlink_to(release)
            config={'deployment_mode':'native','work_dir':str(root/'work'),'files_dir':str(root/'files'),
                'tools':{'dotnet':sys.executable},'database':{'container':'db','name':'gzctf','user':'gzctf'},
                'native':{'current_link':str(root/'current'),'pid_file':str(root/'pid.json'),'settings_file':str(root/'settings.json'),
                          'server_port':free_port(),'metric_port':free_port()}}
            adapter=NativeDeployment(config)
            try:
                adapter.start(); adapter.health(); before=(root/'pid.json').read_text()
                with self.assertRaises(SourceError): adapter._ports_available()
                adapter.start(); adapter.health()
                self.assertEqual((root/'pid.json').read_text(),before)
            finally:
                adapter.stop()
                if adapter.process: adapter.process.wait(timeout=10)
            self.assertFalse((root/'pid.json').exists())
            adapter._ports_available()

    def test_native_refuses_non_symlink_active_directory(self):
        with tempfile.TemporaryDirectory() as tmp:
            root=Path(tmp); (root/'current').mkdir()
            config={'deployment_mode':'native','work_dir':str(root/'work'),'backup_dir':str(root/'backup'),
                'files_dir':str(root/'files'),'database':{'container':'db','name':'gzctf','user':'gzctf'},
                'native':{'current_link':str(root/'current'),'pid_file':str(root/'pid.json'),
                          'settings_file':str(root/'settings.json'),'dotnet':'/missing/dotnet'}}
            adapter=NativeDeployment(config)
            with self.assertRaises((SourceError,ValueError,RuntimeError)): adapter.preflight()
if __name__=='__main__': unittest.main()
