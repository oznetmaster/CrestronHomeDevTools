# Copyright (c) 2026 Neil Colvin. MIT licensed.
"""Portable evidence integrity checks; synthetic inputs and no equipment."""
import json
from pathlib import Path
import tempfile
import unittest

import test_prepare_review as fixtures
from build_help import sha
from freeze_review_inputs import freeze, load, relative


class FrozenReviewInputTests(unittest.TestCase):
    def setUp(self):
        self.fixture = f = fixtures.ReviewStageTests()
        f.setUp()
        self.addCleanup(f.doCleanups)
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        self.root = Path(temporary.name)
        self.output = self.root / "inputs"

    def freeze(self, **options):
        f = self.fixture
        return freeze(f.settings_path, self.output, *f.pins, "a" * 40, **options)

    def test_manifest_has_relative_inputs_and_no_tool_or_credential_settings(self):
        report = self.freeze()
        document, settings, options = load(self.output, report["inputsSha256"], completed=True)
        self.assertNotIn(str(self.fixture.root), json.dumps(document))
        self.assertNotIn("validator", document["settings"])
        self.assertNotIn("dotnet", document["settings"])
        self.assertFalse(report["testsExecuted"])
        self.assertFalse(report["submissionReady"])
        self.assertEqual(self.fixture.pins[0], options["candidateSha256"])
        self.assertTrue(Path(settings["package"]).is_file())

    def test_unreferenced_private_files_are_not_copied(self):
        (self.fixture.fixture.evidence / "private-credentials.json").write_text("SYNTHETIC SECRET")
        self.freeze()
        self.assertFalse(any(p.name == "private-credentials.json" for p in self.output.rglob("*")))

    def test_changed_referenced_evidence_cannot_be_repinned(self):
        (self.fixture.fixture.evidence / "synthetic.txt").write_text("changed")
        with self.assertRaises(ValueError):
            self.freeze()
        self.assertFalse(self.output.exists())

    def test_changed_missing_extra_and_incomplete_snapshots_are_rejected(self):
        report = self.freeze()
        pin = report["inputsSha256"]
        evidence = self.output / "evidence/synthetic.txt"
        original = evidence.read_bytes()
        for action in ("change", "delete", "extra", "incomplete", "manifest"):
            with self.subTest(action=action):
                extra = self.output / "unlisted.txt"
                marker = self.output / "COMPLETE"
                manifest = self.output / "review-inputs.json"
                saved = manifest.read_bytes()
                if action == "change": evidence.write_text("changed")
                elif action == "delete": evidence.unlink()
                elif action == "extra": extra.write_text("unlisted")
                elif action == "incomplete": marker.unlink()
                else: manifest.write_bytes(saved + b" ")
                with self.assertRaises((ValueError, OSError)):
                    load(self.output, pin, completed=True)
                evidence.write_bytes(original)
                if extra.exists(): extra.unlink()
                marker.write_text(pin + "\n")
                manifest.write_bytes(saved)

    def test_manifest_cannot_reference_paths_outside_inventory(self):
        self.freeze()
        manifest = self.output / "review-inputs.json"
        doc = json.loads(manifest.read_bytes())
        doc["settings"]["candidate"] = "../outside.json"
        manifest.write_text(json.dumps(doc))
        pin = sha(manifest.read_bytes())
        (self.output / "COMPLETE").write_text(pin)
        with self.assertRaises(ValueError): load(self.output, pin, completed=True)

    def test_output_is_create_once_and_original_snapshot_is_unchanged(self):
        self.freeze()
        before = {p.relative_to(self.output): p.read_bytes() for p in self.output.rglob("*") if p.is_file()}
        with self.assertRaises(ValueError): self.freeze()
        self.assertEqual(before, {p.relative_to(self.output): p.read_bytes() for p in self.output.rglob("*") if p.is_file()})

    def test_paths_are_portable_and_unambiguous(self):
        for path in ("../outside", "/root", "a//b", "a/./b", "C:/x", "a\\b", "x.", "x ", "NUL.txt", "x:stream", "a?b"):
            with self.subTest(path=path), self.assertRaises(ValueError): relative(path)

    def test_android_snapshot_uses_the_raw_audited_inventory(self):
        android, options = self.fixture.android_run()
        report = self.freeze(**options)
        _, settings, frozen = load(self.output, report["inputsSha256"], completed=True)
        from review_android import audit_runs
        checked = audit_runs(settings, frozen["androidPins"], frozen["androidPinsSha256"], self.fixture.pins[0])
        self.assertEqual(android.run, checked["runs"][0]["runId"])
        self.assertFalse(checked["producerAuthenticated"])
        self.assertTrue(any(item["relativePath"].startswith("assembly/") for item in checked["runs"][0]["files"]))

    def test_failed_observation_is_preserved_and_cannot_prepare_a_complete_review(self):
        f = self.fixture
        path = Path(f.settings["observations"])
        document = json.loads(path.read_bytes())
        document["observations"][0]["outcome"] = "Failed"
        path.write_text(json.dumps(document))
        report = self.freeze()
        retained = json.loads((self.output / "documents/observations.json").read_bytes())
        self.assertEqual("Failed", retained["observations"][0]["outcome"])
        from prepare_frozen_review import prepare_frozen
        with self.assertRaises(ValueError):
            prepare_frozen(self.output, report["inputsSha256"], self.root / "failed-review",
                           validator_settings={key: f.settings[key] for key in ("dotnet", "validator")})
        self.assertFalse((self.root / "failed-review/COMPLETE").exists())

    def test_output_cannot_modify_inputs_through_parent_directory_segments(self):
        report = self.freeze()
        other = self.root / "other"
        other.mkdir()
        from prepare_frozen_review import prepare_frozen
        with self.assertRaises(ValueError):
            prepare_frozen(self.output, report["inputsSha256"], other / ".." / "inputs" / "review")
        load(self.output, report["inputsSha256"], completed=True)


if __name__ == "__main__":
    unittest.main()
