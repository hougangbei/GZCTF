import importlib.util
import json
import tempfile
import unittest
from pathlib import Path

spec = importlib.util.spec_from_file_location('configure_pool', Path(__file__).resolve().parents[1] / 'configure.py')
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)

class PoolConfigurationTests(unittest.TestCase):
    def test_full_pool_has_exact_same_port_tcp_routes(self):
        static, dynamic, compose = module.configuration('192.168.1.160', 30000, 30199)
        self.assertEqual(len(static['entryPoints']), 200)
        self.assertEqual(len(dynamic['tcp']['routers']), 200)
        for port in (30000, 30199):
            key = 'p' + str(port)
            self.assertEqual(static['entryPoints'][key]['address'], ':' + str(port) + '/tcp')
            self.assertEqual(dynamic['tcp']['services'][key]['loadBalancer']['servers'], [{'address': '192.168.1.160:' + str(port)}])
            self.assertEqual(dynamic['tcp']['routers'][key]['entryPoints'], [key])
        self.assertEqual(compose['services']['traefik']['network_mode'], 'host')
        self.assertNotIn('ports', compose['services']['traefik'])

    def test_invalid_inputs_do_not_generate_configuration(self):
        for start, end in ((0, 100), (30199, 30000), (30000, 65536), (30000, 32000)):
            with self.assertRaises(ValueError): module.configuration('192.168.1.160', start, end)
        with self.assertRaises(ValueError): module.configuration('http://bad/path', 30000, 30199)

    def test_merge_preserves_mail_registry_socket_and_other_settings(self):
        with tempfile.TemporaryDirectory() as temp:
            path = Path(temp) / 'appsettings.json'
            original = {'EmailConfig': {'Password': 'private-mail-value'}, 'Registries': {'harbor.example': {'Password': 'private-registry-value'}}, 'ContainerProvider': {'Type': 'Docker', 'DockerConfig': {'Uri': 'unix:///run/gzctf-challenges/docker.sock', 'ChallengeNetwork': 'training'}}}
            path.write_text(json.dumps(original))
            backup = module.merge_appsettings(path, '10.22.130.99', 30000, 30199)
            actual = json.loads(path.read_text())
            self.assertEqual(json.loads(backup.read_text()), original)
            self.assertEqual(actual['EmailConfig'], original['EmailConfig'])
            self.assertEqual(actual['Registries'], original['Registries'])
            self.assertEqual(actual['ContainerProvider']['DockerConfig']['Uri'], original['ContainerProvider']['DockerConfig']['Uri'])
            self.assertEqual(actual['ContainerProvider']['DockerConfig']['HostPortRange'], '30000-30199')
            self.assertEqual(actual['ContainerProvider']['PublicEntry'], '10.22.130.99')
            self.assertEqual(path.stat().st_mode & 0o777, 0o600)

    def test_bad_entry_or_bad_json_never_overwrites_appsettings(self):
        with tempfile.TemporaryDirectory() as temp:
            path = Path(temp) / 'appsettings.json'
            original = '{broken'
            path.write_text(original)
            with self.assertRaises(ValueError): module.merge_appsettings(path, '10.22.130.99', 30000, 30199)
            self.assertEqual(path.read_text(), original)
            path.write_text('{}')
            for entry in ('http://entry.example', 'entry.example:30000', '', 'bad host', '192.168.1.999'):
                with self.assertRaises(ValueError): module.merge_appsettings(path, entry, 30000, 30199)
                self.assertEqual(path.read_text(), '{}')

if __name__ == '__main__': unittest.main()
