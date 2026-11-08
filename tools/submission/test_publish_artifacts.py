# Copyright (c) 2026 Neil Colvin. MIT licensed.
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
from publish_artifacts import publish

class ArtifactPublicationTests(unittest.TestCase):
    def test_publication_never_renames_directories_and_preserves_bytes(self):
        with tempfile.TemporaryDirectory() as temporary:
            root=Path(temporary).resolve();staging=root/'staging';staging.mkdir();(staging/'empty').mkdir()
            (staging/'sub').mkdir();(staging/'sub/file').write_bytes(b'original\r\n\x00')
            with patch.object(Path,'rename',side_effect=PermissionError('directory locked')):
                publish(staging,root/'output','a'*64)
            self.assertEqual((root/'output/sub/file').read_bytes(),b'original\r\n\x00')
            self.assertTrue((root/'output/empty').is_dir());self.assertTrue((root/'output/COMPLETE').is_file())
    def test_partial_copy_never_gets_completion_and_cannot_be_overwritten(self):
        with tempfile.TemporaryDirectory() as temporary:
            root=Path(temporary).resolve();staging=root/'staging';staging.mkdir();(staging/'file').write_bytes(b'x')
            real=Path.open
            def fail(path,*args,**kwargs):
                if path==root/'output/file':raise PermissionError('fixture')
                return real(path,*args,**kwargs)
            with patch.object(Path,'open',fail),self.assertRaises(PermissionError):publish(staging,root/'output','a'*64)
            self.assertFalse((root/'output/COMPLETE').exists())
            with self.assertRaises(ValueError):publish(staging,root/'output','a'*64)
