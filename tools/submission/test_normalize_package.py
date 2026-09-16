# Copyright (c) 2026 Neil Colvin.
# Licensed under the MIT License. See LICENSE in the repository root.
"""Verify safe normalization, unchanged payloads and failure preservation."""

from pathlib import Path
import stat
import tempfile
import unittest
from unittest.mock import patch
from zipfile import ZIP_DEFLATED, ZipFile, ZipInfo

import normalize_package


class NormalizePackageTests(unittest.TestCase):
    def setUp(self):
        folder = tempfile.TemporaryDirectory()
        self.addCleanup(folder.cleanup)
        self.package = Path(folder.name) / 'Example.pkg'

    def create(self, names):
        with ZipFile(self.package, 'w', compression=ZIP_DEFLATED) as archive:
            archive.comment = b'preserve archive comment'
            for index, name in enumerate(names):
                if isinstance(name, str):
                    info = ZipInfo(name)
                    info.filename = name
                    info.orig_filename = name
                    info.compress_type = ZIP_DEFLATED
                else:
                    info = name
                archive.writestr(info, bytes(range(256)) * (index + 1))
        return self.package.read_bytes()

    def test_realistic_windows_paths_preserve_every_payload_and_metadata(self):
        before = self.create(['Driver.dll', 'Driver.dat', 'Driver.pdf', 'Translations\\en-US.json', 'room\\UiDefinitions\\UiDefinition.xml'])
        with ZipFile(self.package) as archive:
            expected = [(i.filename.replace('\\', '/'), archive.read(i), i.date_time, i.compress_type, i.external_attr) for i in archive.infolist()]
        report = normalize_package.normalize(self.package)
        self.assertEqual(report['inputSha256'], normalize_package.digest(before))
        self.assertEqual(len(report['renamedEntries']), 2)
        with ZipFile(self.package) as archive:
            self.assertEqual(archive.comment, b'preserve archive comment')
            observed = [(i.filename, archive.read(i), i.date_time, i.compress_type, i.external_attr) for i in archive.infolist()]
        self.assertEqual(observed, expected)
        self.assertEqual(report['packageSha256'], normalize_package.digest(self.package.read_bytes()))

    def test_already_normalized_package_retains_exact_archive_bytes(self):
        before = self.create(['Driver.dll', 'Translations/en-US.json'])
        report = normalize_package.normalize(self.package)
        self.assertEqual(self.package.read_bytes(), before)
        self.assertEqual(report['renamedEntries'], [])
        self.assertEqual(report['inputSha256'], report['packageSha256'])

    def test_collision_traversal_and_directory_conflict_leave_original_untouched(self):
        for names in (['dir\\item', 'dir/item'], ['A', 'a'], ['dir', 'dir/file'], ['dir/', 'dir'],
                      ['../item'], ['dir\\..\\item'], ['C:\\item'], ['\\server\\item'], ['a//b']):
            with self.subTest(names=names):
                before = self.create(names)
                with self.assertRaises(ValueError):
                    normalize_package.normalize(self.package)
                self.assertEqual(self.package.read_bytes(), before)

    def test_symlink_entry_is_rejected(self):
        info = ZipInfo('link')
        info.create_system = 3
        info.external_attr = (stat.S_IFLNK | 0o777) << 16
        before = self.create([info])
        with self.assertRaises(ValueError):
            normalize_package.normalize(self.package)
        self.assertEqual(self.package.read_bytes(), before)

    def test_replace_failure_leaves_original_and_removes_own_temporary(self):
        before = self.create(['dir\\file'])
        with patch.object(normalize_package.os, 'replace', side_effect=OSError('locked')):
            with self.assertRaises(OSError):
                normalize_package.normalize(self.package)
        self.assertEqual(self.package.read_bytes(), before)
        self.assertEqual(list(self.package.parent.glob('*.tmp')), [])

    def test_changed_build_output_is_not_overwritten(self):
        self.create(['dir\\file'])
        original_read = Path.read_bytes
        def changed_read(path):
            if path == self.package:
                path.write_bytes(b'new build output')
            return original_read(path)
        with patch.object(Path, 'read_bytes', changed_read):
            with self.assertRaisesRegex(ValueError, 'changed during'):
                normalize_package.normalize(self.package)
        self.assertEqual(self.package.read_bytes(), b'new build output')
        self.assertEqual(list(self.package.parent.glob('*.tmp')), [])


if __name__ == '__main__':
    unittest.main()
