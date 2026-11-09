import importlib.util
from pathlib import Path
import sys
import tempfile
import unittest
sys.path.insert(0,str(Path(__file__).resolve().parents[2]/'offline'))
spec=importlib.util.spec_from_file_location('bundle_builder',Path(__file__).resolve().parents[2]/'offline'/'build_bundle.py')
builder=importlib.util.module_from_spec(spec);spec.loader.exec_module(builder)

class BundleAssetTests(unittest.TestCase):
    def test_release_includes_fixed_import_layout_installer_and_operations_doc(self):
        with tempfile.TemporaryDirectory() as temp:
            root=Path(temp)
            builder.copy_updater_assets(root)
            for name in builder.UPDATER_ASSETS:self.assertTrue((root/'self-update'/name).is_file(),name)
            self.assertEqual((root/'offline'/'manage.py').read_bytes(),(Path(builder.__file__).parent/'manage.py').read_bytes())
            self.assertIn('offline_updates',(root/'self-update'/'install-offline-updater.py').read_text())
