import hashlib
import io
import json
from pathlib import Path
import sys
import tarfile
import tempfile
import unittest
import zipfile
sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import update_sources as sources


class SourceTests(unittest.TestCase):
    def test_github_tree_url_has_explicit_ref(self):
        result = sources.normalize_source({'kind':'github','location':'https://github.com/hougangbei/GZCTF/tree/main'})
        self.assertEqual(result['location'], 'https://github.com/hougangbei/GZCTF')
        self.assertEqual(result['ref'], 'main')

    def test_rejects_url_credentials_queries_and_unsafe_refs(self):
        for value in [
            {'kind':'github','location':'https://github.com/a/b','ref':'--upload-pack=evil'},
            {'kind':'package','location':'file:///etc/passwd'},
            {'kind':'package','location':'https://user:pass@example.com/x.zip'},
            {'kind':'directory','location':'../repo'},
            {'kind':'package','location':'https://example.com/x.zip','sha256':'xyz'}]:
            with self.subTest(value=value), self.assertRaises(sources.SourceError):
                sources.normalize_source(value)

    def test_snapshot_excludes_runtime_secrets_and_dependencies(self):
        with tempfile.TemporaryDirectory() as tmp:
            root=Path(tmp); origin=root/'repo'; origin.mkdir(); target=root/'stage'
            (origin/'code.txt').write_text('safe'); (origin/'appsettings.Development.json').write_text('secret')
            (origin/'node_modules').mkdir(); (origin/'node_modules/x').write_text('huge')
            sources.snapshot_directory(origin,target,[root])
            self.assertEqual((target/'code.txt').read_text(),'safe')
            self.assertFalse((target/'appsettings.Development.json').exists())
            self.assertFalse((target/'node_modules').exists())
            before=sources.tree_digest(target); (target/'code.txt').write_text('changed')
            self.assertNotEqual(before,sources.tree_digest(target))

    def test_snapshot_rejects_symlinks_and_paths_outside_allowed_roots(self):
        with tempfile.TemporaryDirectory() as tmp:
            root=Path(tmp); origin=root/'repo'; origin.mkdir(); (origin/'secret-link').symlink_to('/etc/passwd')
            with self.assertRaises(sources.SourceError): sources.snapshot_directory(origin,root/'stage',[root])
            with self.assertRaises(sources.SourceError): sources.snapshot_directory(root,root/'stage2',[origin])

    def test_zip_and_tar_cannot_escape_destination(self):
        with tempfile.TemporaryDirectory() as tmp:
            root=Path(tmp)
            for filename in ['../escaped','/absolute','folder/../../escaped']:
                archive=root/'bad.zip'
                with zipfile.ZipFile(archive,'w') as z: z.writestr(filename,'x')
                with self.subTest(filename=filename),self.assertRaises(sources.SourceError):
                    sources.extract_archive(archive,root/'output')
            with tarfile.open(root/'bad.tar.gz','w:gz') as tar:
                entry=tarfile.TarInfo('link'); entry.type=tarfile.SYMTYPE; entry.linkname='/etc/passwd'; tar.addfile(entry)
            with self.assertRaises(sources.SourceError): sources.extract_archive(root/'bad.tar.gz',root/'output2')

    def test_release_manifest_requires_identity_and_exact_file_hashes(self):
        with tempfile.TemporaryDirectory() as tmp:
            root=Path(tmp); (root/'GZCTF.dll').write_bytes(b'assembly')
            (root/'wwwroot').mkdir(); (root/'wwwroot/index.html').write_text('<html>hi</html>')
            manifest={'project':'GZCTF','validationProtocol':1,'version':'1.8.8','runtime':'osx-arm64','files':{
                'GZCTF.dll':hashlib.sha256(b'assembly').hexdigest(),
                'wwwroot/index.html':hashlib.sha256(b'<html>hi</html>').hexdigest()}}
            (root/'gzctf-release.json').write_text(json.dumps(manifest))
            self.assertEqual(sources.verify_release(root,'osx-arm64')['version'],'1.8.8')
            (root/'GZCTF.dll').write_bytes(b'mutated')
            with self.assertRaises(sources.SourceError): sources.verify_release(root,'osx-arm64')

    def test_http_archive_download_and_checksum_are_verified_end_to_end(self):
        from http.server import BaseHTTPRequestHandler,ThreadingHTTPServer
        import threading
        stream=io.BytesIO()
        with zipfile.ZipFile(stream,'w') as archive:
            archive.writestr('release/src/GZCTF/GZCTF.csproj','<Project />')
        data=stream.getvalue()
        class Handler(BaseHTTPRequestHandler):
            def do_GET(self):
                self.send_response(200); self.send_header('Content-Length',str(len(data)));self.end_headers();self.wfile.write(data)
            def log_message(self,*args): pass
        server=ThreadingHTTPServer(('127.0.0.1',0),Handler)
        thread=threading.Thread(target=server.serve_forever,daemon=True);thread.start()
        try:
            with tempfile.TemporaryDirectory() as tmp:
                root=Path(tmp)
                source={'kind':'package','location':f'http://127.0.0.1:{server.server_port}/release.zip',
                        'sha256':hashlib.sha256(data).hexdigest()}
                result,_=sources.acquire(source,root/'success',[],allow_private=True)
                self.assertEqual((result/'src/GZCTF/GZCTF.csproj').read_text(),'<Project />')
                with self.assertRaises(sources.SourceError):
                    sources.acquire({**source,'sha256':'0'*64},root/'bad-hash',[],allow_private=True)
        finally:
            server.shutdown();server.server_close();thread.join()

    def test_private_download_addresses_are_blocked(self):
        with self.assertRaises(sources.SourceError): sources.check_download_url('http://127.0.0.1/a.zip')

if __name__=='__main__': unittest.main()
