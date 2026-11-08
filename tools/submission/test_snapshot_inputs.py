# Copyright (c) 2026 Neil Colvin. MIT licensed.
import json
from pathlib import Path
import tempfile
import unittest
from snapshot_inputs import snapshot, sha

class SnapshotInputsTests(unittest.TestCase):
    def setUp(self):
        t=tempfile.TemporaryDirectory();self.addCleanup(t.cleanup);self.root=Path(t.name).resolve()
        self.source=self.root/'source';self.source.write_bytes(b'line1\r\nline2\r\n\x00\xff')
        self.manifest=self.root/'manifest.json';self.output=self.root/'snapshot'
    def prepare(self,name='fixture/source.bin',pin=None):
        self.manifest.write_text(json.dumps({'schemaVersion':1,'files':[{'source':str(self.source),'relativePath':name,'sha256':pin or sha(self.source.read_bytes())}]}))
        return snapshot(self.manifest,sha(self.manifest.read_bytes()),self.output)
    def test_binary_and_crlf_preserved_and_later_source_change_does_not_change_snapshot(self):
        original=self.source.read_bytes();result=self.prepare();self.source.write_bytes(b'new revision')
        self.assertEqual((self.output/'fixture/source.bin').read_bytes(),original)
        self.assertEqual(result['files'][0]['sha256'],sha(original))
        self.assertFalse(result['authorizationGranted'])
        self.assertTrue((self.output/'COMPLETE').is_file())
    def test_stale_pin_is_not_repaired_by_rehashing_current_checkout(self):
        with self.assertRaisesRegex(ValueError,'Source changed'):self.prepare(pin='0'*64)
        self.assertFalse(self.output.exists())
    def test_traversal_and_reserved_names_refused_before_copy(self):
        for name in ('../outside','COMPLETE','inputs.json','a/../../b'):
            with self.subTest(name=name),self.assertRaises(ValueError):self.prepare(name)
        self.assertFalse(self.output.exists())
    def test_existing_output_never_overwritten(self):
        self.prepare()
        with self.assertRaises(ValueError):self.prepare()
