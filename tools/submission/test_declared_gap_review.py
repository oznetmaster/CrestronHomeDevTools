# Copyright (c) 2026 Neil Colvin.
# Licensed under the MIT License. See LICENSE in the repository root.
"""Exercise the public review stage with declared gaps, actual validation and retained archives."""

import json
from pathlib import Path
import subprocess
import sys
import unittest
from unittest.mock import patch
from zipfile import ZipFile

from pypdf import PdfReader

import prepare_review as stage
import test_prepare_review as fixtures


class DeclaredGapReviewTests(unittest.TestCase):
    def setUp(self):
        self.base = fixtures.ReviewStageTests()
        self.base.setUp()
        self.addCleanup(self.base.doCleanups)
        self.f = self.base.fixture
        self.output = self.base.output
        self.declarations = self.f.root / "declarations.json"
        self.f.observations["observations"].pop()
        self.gaps = [{"requirementId": "second.duration", "reason": "Developer could not complete the observation period."}]
        self.save()

    def save(self):
        self.f.write_json(Path(self.base.settings["observations"]), self.f.observations)
        self.f.write_json(self.declarations, {"schemaVersion": 1, "identity": self.f.identity,
                                            "mode": "DeclaredGaps", "declarations": self.gaps})
        self.digest = stage.sha(self.declarations.read_bytes())

    def run_stage(self, **changes):
        options = dict(review_mode="declared-gaps", declarations=self.declarations, declarations_sha256=self.digest)
        options.update(changes)
        return self.base.run_stage(**options)

    def test_complete_review_attempt_retains_original_failures_and_exact_explanations(self):
        receipt = self.run_stage()
        self.assertEqual(receipt["state"], "UnsignedReviewWithDeclaredGapsPrepared")
        self.assertEqual(receipt["verificationStatus"], "GapsDeclared")
        self.assertFalse(receipt["submissionReady"])
        self.assertFalse(receipt["deliveryAttempted"])
        self.assertEqual((self.output / "declarations.json").read_bytes(), self.declarations.read_bytes())
        self.assertEqual(receipt["declarationsSha256"], self.digest)
        self.assertEqual((self.output / "COMPLETE").read_text().strip(), stage.sha((self.output / "review-receipt.json").read_bytes()))
        form = json.loads((self.output / "form-report.json").read_text())
        bundle = json.loads((self.output / "bundle-report.json").read_text())
        self.assertEqual(form["checkedRequirements"], ["first"])
        self.assertEqual(form["declaredGapRequirements"], ["second"])
        self.assertFalse(bundle["review"]["validation"]["validationChecksPassed"])
        with ZipFile(self.output / "evidence.zip") as archive:
            self.assertEqual(archive.read("declarations.json"), self.declarations.read_bytes())
            self.assertEqual(archive.read("observations.json"), Path(self.base.settings["observations"]).read_bytes())
        reader = PdfReader(self.output / "self-test.review.pdf")
        self.assertEqual(reader.get_fields()["First"]["/V"], "/Yes")
        self.assertEqual(reader.get_fields()["Second"]["/V"], "/Off")
        self.assertEqual(reader.get_fields()["Signature"].get("/V", ""), "")

    def test_declarations_cannot_silently_change_complete_mode(self):
        with self.assertRaises(ValueError):
            self.run_stage(review_mode="complete")
        self.assertFalse(self.output.exists())

    def test_declared_gap_signing_copy_preserves_gaps_and_has_no_signature(self):
        receipt = self.run_stage(signing_copy=True)
        self.assertTrue(receipt["signingCopy"])
        self.assertEqual(receipt["verificationStatus"], "GapsDeclared")
        reader = PdfReader(self.output / "self-test.review.pdf")
        self.assertEqual(reader.get_fields()["Second"]["/V"], "/Off")
        self.assertEqual(reader.get_fields()["Signature"].get("/V", ""), "")
        self.assertIn("Synthetic official-form fixture", reader.pages[0].extract_text())
        self.assertIn("Disclosed limitations remain identified below", "\n".join(page.extract_text() for page in reader.pages[2:]))

    def test_unexplained_missing_scope_cannot_publish_review(self):
        self.gaps = []
        self.save()
        with self.assertRaisesRegex(ValueError, "validation failed"):
            self.run_stage()
        self.assertFalse(self.output.exists())

    def test_changed_declarations_between_form_and_bundle_publish_nothing(self):
        original = stage.run_process
        def changed(arguments, timeout):
            if "submission-review-bundle-create" in arguments:
                self.declarations.write_text(self.declarations.read_text() + " ", encoding="utf-8")
            return original(arguments, timeout)
        with patch.object(stage, "run_process", changed), self.assertRaisesRegex(ValueError, "bundle validation failed"):
            self.run_stage()
        self.assertFalse(self.output.exists())
        self.assertEqual(list(self.f.root.glob(".submission-review-*")), [])

    def test_changed_declarations_after_archive_cannot_replace_retained_copy(self):
        original = stage.run_process
        def changed(arguments, timeout):
            result = original(arguments, timeout)
            self.declarations.write_text(self.declarations.read_text() + " ", encoding="utf-8")
            return result
        with patch.object(stage, "run_process", changed), self.assertRaisesRegex(ValueError, "pinned digest"):
            self.run_stage()
        self.assertFalse(self.output.exists())

    def test_changed_evidence_between_form_and_archive_cannot_be_excused(self):
        original = stage.run_process
        def changed(arguments, timeout):
            (self.f.evidence / "synthetic.txt").write_text("Altered measurement", encoding="utf-8")
            return original(arguments, timeout)
        with patch.object(stage, "run_process", changed), self.assertRaisesRegex(ValueError, "bundle validation failed"):
            self.run_stage()
        self.assertFalse(self.output.exists())

    def test_archive_assessment_must_match_generated_form(self):
        original = stage.run_process
        def changed(arguments, timeout):
            result = original(arguments, timeout)
            report = json.loads(result.stdout)
            report["review"]["assessment"]["verificationStatus"] = "CompleteAgainstInterpretedRequirements"
            return subprocess.CompletedProcess(arguments, 0, json.dumps(report).encode(), b"")
        with patch.object(stage, "run_process", changed), self.assertRaisesRegex(ValueError, "does not match"):
            self.run_stage()
        self.assertFalse(self.output.exists())

    def android_policy(self):
        self.f.policy["requirements"][0]["execution"] = {"target": "room/control", "method": "android",
            "requiredOutcome": "Passed", "responseLimitSeconds": None, "restore": False}
        self.f.write_json(self.f.policy_path, self.f.policy)
        self.f.identity["policySha256"] = stage.sha(self.f.policy_path.read_bytes())
        self.f.mapping["policySha256"] = self.f.identity["policySha256"]
        candidate = json.loads(Path(self.base.settings["candidate"]).read_text())
        candidate["identity"] = self.f.identity
        for name, value in (("candidate", candidate), ("mapping", self.f.mapping)):
            self.f.write_json(Path(self.base.settings[name]), value)
        self.base.pins = [stage.sha(Path(self.base.settings[name]).read_bytes()) for name in ("candidate", "inventory", "mapping")]

    def test_wholly_unperformed_android_scope_can_be_explained_without_an_emulator(self):
        self.android_policy()
        self.f.observations["observations"].pop(0)
        self.gaps.append({"requirementId": "first.a", "reason": "No Android test environment is available."})
        self.save()
        receipt = self.run_stage()
        self.assertEqual(receipt["verificationStatus"], "GapsDeclared")
        report = json.loads((self.output / "form-report.json").read_text())
        self.assertEqual(report["checkedRequirements"], [])
        self.assertNotIn("androidAuditSha256", receipt)

    def test_supplied_android_observation_still_requires_its_raw_run_audit(self):
        self.android_policy()
        self.f.observations["observations"][0]["outcome"] = "Failed"
        self.gaps.append({"requirementId": "first.a", "reason": "Android test failed."})
        self.save()
        with self.assertRaisesRegex(ValueError, "Android policy requirements"):
            self.run_stage()
        self.assertFalse(self.output.exists())

    def test_cli_prepares_explicit_gap_review_and_rejects_changed_pin(self):
        args = [sys.executable, str(Path(stage.__file__)), "--settings", str(self.base.settings_path),
                "--artifact-kind", "driver", "--source-commit", "a" * 40,
                "--candidate-sha256", self.base.pins[0], "--inventory-sha256", self.base.pins[1],
                "--mapping-sha256", self.base.pins[2], "--review-mode", "declared-gaps",
                "--declarations", str(self.declarations), "--declarations-sha256", self.digest]
        result = subprocess.run(args, capture_output=True, timeout=120)
        self.assertEqual(result.returncode, 0, result.stderr.decode())
        self.assertEqual(json.loads(result.stdout)["state"], "UnsignedReviewWithDeclaredGapsPrepared")
        args[-1] = "0" * 64
        failed = subprocess.run(args, capture_output=True, timeout=120)
        self.assertNotEqual(failed.returncode, 0)
        self.assertNotIn(str(self.f.root), failed.stderr.decode())


if __name__ == "__main__":
    unittest.main()
