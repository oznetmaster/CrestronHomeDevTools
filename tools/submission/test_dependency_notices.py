# Copyright (c) 2026 Neil Colvin.
# Licensed under the MIT License. See LICENSE in the repository root.
"""Verify actual merge coverage, retained notice text and package inspection."""

import copy
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
from zipfile import ZipFile

from build_help import sha
import dependency_notices as notices


class DependencyNoticeTests(unittest.TestCase):
    def setUp(self):
        self.scratch = tempfile.TemporaryDirectory()
        self.addCleanup(self.scratch.cleanup)
        self.root = Path(self.scratch.name)
        self.driver = self.root / "Driver.dll"
        self.driver.write_bytes(b"synthetic driver")
        self.dependency = self.root / "Library.dll"
        self.dependency.write_bytes(b"synthetic library")
        self.license = self.root / "LICENSE.txt"
        self.license.write_bytes(b"Synthetic license\nCopyright Original Author\nPreserve this notice.\n")
        self.plan = {"schemaVersion": 1, "driverNoticeIds": ["license"], "documents": [
            {"id": "license", "path": "LICENSE.txt", "sha256": sha(self.license.read_bytes()), "source": "Synthetic fixture, not actual licensing"}],
            "components": [{"assembly": "Library.dll", "sha256": sha(self.dependency.read_bytes()), "packageId": "Example",
                            "packageVersion": "1.0.0", "license": "MIT", "copyright": "Copyright Original Author", "noticeIds": ["license"]}]}
        self.manifest = self.root / "inventory.json"
        self.save()
        self.inputs = self.root / "merge_inputs.txt"
        self.inputs.write_text(str(self.driver) + "\n" + str(self.dependency) + "\n")
        self.include = self.root / "include"
        self.include.mkdir()
        self.receipt = self.root / "receipt.json"
        self.package = self.root / "Driver.pkg"

    def save(self):
        self.manifest.write_text(json.dumps(self.plan), encoding="utf-8")

    def prepare(self):
        return notices.prepare(self.manifest, self.inputs, self.driver)

    def stage(self):
        return notices.stage(self.manifest, self.inputs, self.driver, self.include, self.receipt)

    def verify(self):
        return notices.verify(self.manifest, self.inputs, self.driver, self.receipt, self.package)

    def pack(self, name=notices.FILENAME, data=None):
        with ZipFile(self.package, "w") as archive:
            archive.writestr(name, (self.include / notices.FILENAME).read_bytes() if data is None else data)

    def test_exact_sources_are_staged_and_verified_without_private_paths(self):
        report = self.stage()
        rendered = (self.include / notices.FILENAME).read_bytes()
        self.assertIn(self.license.read_bytes(), rendered)
        self.assertNotIn(str(self.root), rendered.decode())
        self.assertNotIn(str(self.root), json.dumps(report))
        self.pack()
        checked = self.verify()
        self.assertTrue(checked["packagedNoticesVerified"])
        self.assertEqual(sha(self.package.read_bytes()), checked["packageSha256"])

    def test_changed_dependency_or_license_is_rejected(self):
        self.dependency.write_bytes(b"updated library")
        with self.assertRaisesRegex(ValueError, "Merged dependencies"):
            self.prepare()
        self.dependency.write_bytes(b"synthetic library")
        self.license.write_bytes(b"Changed license")
        with self.assertRaisesRegex(ValueError, "notice changed"):
            self.prepare()

    def test_missing_and_extra_merge_inputs_are_rejected(self):
        self.inputs.write_text(str(self.driver))
        with self.assertRaises(ValueError):
            self.prepare()
        extra = self.root / "Extra.dll"
        extra.write_bytes(b"new unreviewed dependency")
        self.inputs.write_text("\n".join(map(str, [self.driver, self.dependency, extra])))
        with self.assertRaises(ValueError):
            self.prepare()

    def test_missing_files_are_not_silently_filtered(self):
        self.dependency.unlink()
        with self.assertRaises((ValueError, OSError)):
            self.prepare()

    def test_explicit_build_alias_resolves_to_the_same_driver(self):
        alias = self.root / "Alias.dll"
        self.inputs.write_text(str(alias) + "\n" + str(self.dependency))
        original = Path.resolve
        def resolve(path, *args, **kwargs):
            return original(self.driver if path == alias else path, *args, **kwargs)
        with patch.object(Path, "resolve", resolve):
            _, report = self.prepare()
        self.assertEqual({"library.dll": sha(self.dependency.read_bytes())}, report["mergedDependencies"])

    def test_driver_and_duplicate_inputs_are_checked(self):
        for paths in ([self.dependency], [self.driver, self.driver, self.dependency], [self.driver, self.dependency, self.dependency]):
            self.inputs.write_text("\n".join(map(str, paths)))
            with self.subTest(paths=paths), self.assertRaises(ValueError):
                self.prepare()

    def test_unsafe_and_unreviewed_sources_fail(self):
        original = copy.deepcopy(self.plan)
        for change in ({"path": "../LICENSE.txt"}, {"path": str(self.license)}, {"sha256": "0" * 64}):
            self.plan = copy.deepcopy(original)
            self.plan["documents"][0].update(change)
            self.save()
            with self.subTest(change=change), self.assertRaises(ValueError):
                self.prepare()

    def test_missing_document_reference_or_duplicate_component_fails(self):
        self.plan["components"][0]["noticeIds"] = ["unknown"]
        self.save()
        with self.assertRaises(ValueError):
            self.prepare()
        self.plan["components"][0]["noticeIds"] = ["license"]
        self.plan["components"].append(copy.deepcopy(self.plan["components"][0]))
        self.save()
        with self.assertRaises(ValueError):
            self.prepare()

    def test_existing_stage_is_never_overwritten(self):
        self.stage()
        before = (self.include / notices.FILENAME).read_bytes()
        with self.assertRaises(ValueError):
            self.stage()
        self.assertEqual(before, (self.include / notices.FILENAME).read_bytes())

    def test_archive_missing_altered_or_wrong_case_notices_fail(self):
        self.stage()
        for name, data in (("missing.txt", b"irrelevant"), (notices.FILENAME, b"altered"), ("third-party-notices.txt", None)):
            self.pack(name, data)
            with self.subTest(name=name), self.assertRaises(ValueError):
                self.verify()

    def test_changed_review_after_staging_cannot_reuse_old_receipt(self):
        self.stage()
        self.pack()
        self.plan["components"][0]["packageVersion"] = "2.0.0"
        self.save()
        with self.assertRaisesRegex(ValueError, "no longer match"):
            self.verify()


if __name__ == "__main__":
    unittest.main()
