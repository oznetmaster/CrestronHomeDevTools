# Copyright (c) 2026 Neil Colvin.
# Licensed under the MIT License. See LICENSE in the repository root.
"""Exercise the review stage with synthetic evidence and the real .NET validator."""

from datetime import datetime, timezone
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import unittest
from unittest.mock import patch

from PIL import Image, ImageDraw
from pypdf import PdfReader

import prepare_review as stage
import sign_self_test_form as signing
import test_self_test_form as fixtures


class ReviewStageTests(unittest.TestCase):
    def setUp(self):
        self.fixture = f = fixtures.SelfTestFormTests()
        f.setUp()
        self.addCleanup(f.doCleanups)
        self.root = f.root
        self.output = f.root / "review"
        candidate = {"schemaVersion": 1, "identity": f.identity,
                     "packageRequirements": {"driverId": f.driver_id, "driverVersion": "1.0.000.0000",
                                             "kind": "NewDriver", "developerFilenameToken": "Example",
                                             "publicSupportEmail": "support@example.org"}}
        for name, value in (("candidate", candidate), ("mapping", f.mapping),
                            ("observations", f.observations), ("inventory", f.inventory)):
            f.write_json(f.root / (name + ".json"), value)
        dotnet = os.environ.get("SUBMISSION_TEST_DOTNET") or shutil.which("dotnet")
        validator = os.environ.get("SUBMISSION_TEST_VALIDATOR", str(Path(__file__).resolve().parents[2] /
                      "CrestronHomeDevTools.Console/bin/Release/net10.0/CrestronHomeDevTools.Console.dll"))
        self.assertTrue(dotnet and Path(validator).is_file(), "Build the Release console before running review tests")
        self.settings = {"schemaVersion": 1, "dotnet": dotnet, "validator": validator,
                         "policy": str(f.policy_path), "package": str(f.package), "template": str(f.template),
                         "evidence": str(f.evidence), "output": str(self.output),
                         "title": "SYNTHETIC REVIEW STAGE", "author": "Example Developer"}
        for name in ("candidate", "inventory", "mapping", "observations"):
            self.settings[name] = str(f.root / (name + ".json"))
        self.settings_path = f.root / "private-settings.json"
        f.write_json(self.settings_path, self.settings)
        self.pins = [stage.sha((f.root / (name + ".json")).read_bytes()) for name in ("candidate", "inventory", "mapping")]

    def run_stage(self, kind="driver", commit="a" * 40, *, signing_copy=False):
        return stage.prepare(self.settings_path, *self.pins, commit, kind, signing_copy=signing_copy)

    def test_real_stage_retains_consistent_unsigned_form_bundle_and_completion(self):
        result = self.run_stage()
        self.assertEqual(result["state"], "UnsignedReviewPrepared")
        self.assertFalse(result["submissionReady"])
        self.assertFalse(result["deliveryAttempted"])
        self.assertFalse(result["signingCopy"])
        self.assertEqual((self.output / "COMPLETE").read_text().strip(),
                         stage.sha((self.output / "review-receipt.json").read_bytes()))
        self.assertEqual(result["formSha256"], stage.sha((self.output / "self-test.review.pdf").read_bytes()))
        self.assertEqual(result["bundleSha256"].lower(), stage.sha((self.output / "evidence.zip").read_bytes()))
        report = json.loads((self.output / "form-report.json").read_text())
        self.assertTrue(report["signatureBlank"])
        self.assertFalse(report["signingCopy"])
        self.assertEqual(result["formReportSha256"], stage.sha((self.output / "form-report.json").read_bytes()))
        self.assertEqual(report["checkedRequirements"], ["first", "second"])
        self.assertFalse((self.output / "private-settings.json").exists())
        with self.assertRaisesRegex(ValueError, "new review"):
            self.run_stage()

    def test_signing_copy_handoff_uses_exact_reviewed_form_without_regeneration(self):
        completed = subprocess.run([sys.executable, str(Path(stage.__file__)), "--settings", str(self.settings_path),
                                    "--artifact-kind", "driver", "--source-commit", "a" * 40,
                                    "--candidate-sha256", self.pins[0], "--inventory-sha256", self.pins[1],
                                    "--mapping-sha256", self.pins[2], "--prepare-for-signing"],
                                   capture_output=True, check=False, timeout=60)
        self.assertEqual(completed.returncode, 0, completed.stderr.decode())
        receipt = json.loads(completed.stdout)
        self.assertTrue(receipt["signingCopy"])
        form = self.output / "self-test.review.pdf"
        original = form.read_bytes()
        reader = PdfReader(form)
        self.assertIn("SELF-TEST EVIDENCE SUMMARY", reader.pages[0].extract_text())
        self.assertEqual(reader.get_fields()["Signature"].get("/V", ""), "")
        self.assertEqual(reader.get_fields()["Date"].get("/V", ""), "")
        image = self.root / "synthetic-signature.png"
        bitmap = Image.new("RGB", (500, 70), "white")
        ImageDraw.Draw(bitmap).text((10, 15), "SYNTHETIC TEST SIGNATURE", fill="black", font_size=20)
        bitmap.save(image)
        approval = {"schemaVersion": 1, "formSha256": receipt["formSha256"],
                    "formReportSha256": receipt["formReportSha256"], "inventorySha256": self.pins[1],
                    "candidateSha256": self.pins[0], "packageSha256": stage.sha(self.fixture.package.read_bytes()),
                    "signatureImageSha256": stage.sha(image.read_bytes()), "signer": "Synthetic Example Developer",
                    "signingDate": "2026-09-17", "expiresUtc": "2026-09-17T18:00:00Z",
                    "signatureField": "Signature", "dateField": "Date",
                    "visualReviewCompleted": True, "signatureAuthorized": True}
        authorization = self.root / "synthetic-authorization.json"
        self.fixture.write_json(authorization, approval)
        signed = self.root / "synthetic.signed.pdf"
        result = signing.sign(form, self.output / "form-report.json", self.output / "inventory.json",
                              authorization, stage.sha(authorization.read_bytes()), image, signed,
                              datetime(2026, 9, 17, 12, tzinfo=timezone.utc))
        self.assertEqual(form.read_bytes(), original)
        self.assertEqual(PdfReader(signed).get_fields()["Signature"]["/V"], approval["signer"])
        self.assertFalse(result["submissionReady"])
        self.assertFalse(receipt["submissionReady"])
        self.assertFalse(receipt["deliveryAttempted"])

    def test_signing_copy_does_not_bypass_changed_evidence(self):
        (self.fixture.evidence / "synthetic.txt").write_text("changed before signing-copy preparation")
        with self.assertRaises(ValueError):
            self.run_stage(signing_copy=True)
        self.assertFalse(self.output.exists())

    def test_signing_copy_requires_boolean_selection(self):
        with self.assertRaisesRegex(ValueError, "boolean"):
            self.run_stage(signing_copy="false")
        self.assertFalse(self.output.exists())

    def test_non_driver_or_different_release_cannot_start_review(self):
        for kind in ("library", "processor-test", "client", ""):
            with self.subTest(kind=kind), self.assertRaisesRegex(ValueError, "driver release"):
                self.run_stage(kind)
        with self.assertRaisesRegex(ValueError, "release commit"):
            self.run_stage(commit="b" * 40)
        self.assertFalse(self.output.exists())

    def test_bad_pin_and_debug_revision_are_rejected(self):
        self.pins[0] = "0" * 64
        with self.assertRaisesRegex(ValueError, "pinned digest"):
            self.run_stage()
        path = Path(self.settings["candidate"])
        candidate = json.loads(path.read_text())
        candidate["packageRequirements"]["driverVersion"] = "1.0.000.0001"
        self.fixture.write_json(path, candidate)
        self.pins[0] = stage.sha(path.read_bytes())
        with self.assertRaisesRegex(ValueError, "revision zero"):
            self.run_stage()
        self.assertFalse(self.output.exists())

    def test_changed_evidence_between_form_and_bundle_publishes_no_review(self):
        original = stage.run_process

        def change_before_bundle(arguments, timeout):
            if "submission-bundle-create" in arguments:
                (self.fixture.evidence / "synthetic.txt").write_text("changed after form validation")
            return original(arguments, timeout)

        with patch.object(stage, "run_process", change_before_bundle):
            with self.assertRaisesRegex(ValueError, "bundle validation failed"):
                self.run_stage()
        self.assertFalse(self.output.exists())
        self.assertEqual(list(self.root.glob(".submission-review-*")), [])

    def test_interrupted_output_has_no_completion_marker_and_cannot_be_reused(self):
        original = Path.rename

        def interrupted(path, target):
            if Path(target).parent == self.output:
                raise OSError("synthetic interrupted publication")
            return original(path, target)

        with patch.object(Path, "rename", interrupted):
            with self.assertRaisesRegex(OSError, "interrupted publication"):
                self.run_stage()
        self.assertTrue(self.output.is_dir())
        self.assertFalse((self.output / "COMPLETE").exists())
        with self.assertRaisesRegex(ValueError, "new review"):
            self.run_stage()

    def test_inconsistent_bundle_report_is_rejected(self):
        original = stage.run_process

        def mismatched(arguments, timeout):
            result = original(arguments, timeout)
            if "submission-bundle-create" in arguments:
                report = json.loads(result.stdout)
                report["Validation"]["ObservationsSha256"] = "0" * 64
                result.stdout = json.dumps(report).encode()
            return result

        with patch.object(stage, "run_process", mismatched):
            with self.assertRaisesRegex(ValueError, "same candidate snapshot"):
                self.run_stage()
        self.assertFalse(self.output.exists())


if __name__ == "__main__":
    unittest.main()
