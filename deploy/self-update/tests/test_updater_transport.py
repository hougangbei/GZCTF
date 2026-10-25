import json
from pathlib import Path
import sys
import threading
import unittest
import urllib.request
import urllib.error
from types import SimpleNamespace
sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
import updater

class TransportTests(unittest.TestCase):
    def test_tcp_transport_keeps_bearer_authentication_and_json_status(self):
        self.assertTrue(hasattr(updater,'UpdaterTCPServer'),'Loopback TCP transport must be available for Docker Desktop')
        class QuietHandler(updater.Handler):
            def log_message(self,*args):pass
        server=updater.UpdaterTCPServer(('127.0.0.1',0),QuietHandler)
        server.updater=SimpleNamespace(token='t'*64,status=lambda **kwargs:{'configured':True,'mode':'compose','phase':'idle'})
        thread=threading.Thread(target=server.serve_forever,daemon=True);thread.start()
        url=f'http://127.0.0.1:{server.server_address[1]}/status'
        try:
            with self.assertRaises(urllib.error.HTTPError) as context:urllib.request.urlopen(url)
            self.assertEqual(context.exception.code,401)
            context.exception.close()
            request=urllib.request.Request(url,headers={'Authorization':'Bearer '+'t'*64})
            with urllib.request.urlopen(request) as response:
                self.assertEqual(json.load(response)['mode'],'compose')
        finally:server.shutdown();server.server_close();thread.join()
if __name__=='__main__':unittest.main()
