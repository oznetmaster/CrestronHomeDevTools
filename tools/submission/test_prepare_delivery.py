# Copyright (c) 2026 Neil Colvin.
# Licensed under the MIT License. See LICENSE in the repository root.
"""Final delivery preparation uses synthetic approvals; no transport is called."""

from datetime import datetime, timedelta, timezone
import json
from pathlib import Path
import subprocess
import sys
import unittest
from unittest.mock import patch

import prepare_delivery as stage
import test_prepare_signed_review as signing_tests


class DeliveryStageTests(unittest.TestCase):
    def setUp(self, android=False):
        self.signed = f = signing_tests.SignedReviewStageTests()
        f.setUp(android=android)
        self.addCleanup(f.doCleanups)
        self.receipt = f.run_stage()
        self.root = f.root
        self.output = self.root / "final-delivery"
        self.pin = stage.sha((f.output / "signed-review-receipt.json").read_bytes())
        self.approval = {"schemaVersion": 1, "signedReviewSha256": self.pin,
                         **{key: self.receipt[key] for key in ("candidateSha256", "packageSha256", "signedFormSha256")},
                         "sender": "developer@example.org", "recipient": "drivers@crestron.com",
                         "subject": "Driver Submission Package",
                         "expiresUtc": (datetime.now(timezone.utc) + timedelta(minutes=10)).isoformat(),
                         "signedVisualReviewCompleted": True, "deliveryAuthorized": True}
        self.authorization = self.root / "synthetic-delivery-authorization.json"
        self.save_approval()
        self.settings = {"schemaVersion": 1, "signedReviewDirectory": str(f.output),
                         "reviewDirectory": str(f.review.output), "authorization": str(self.authorization),
                         "dotnet": f.settings["dotnet"], "validator": f.settings["validator"], "output": str(self.output)}
        self.settings_path = self.root / "private-delivery-settings.json"
        f.review.fixture.write_json(self.settings_path, self.settings)

    def save_approval(self):
        self.signed.review.fixture.write_json(self.authorization, self.approval)
        self.authorization_pin = stage.sha(self.authorization.read_bytes())

    def run_stage(self):
        return stage.prepare(self.settings_path, self.pin, self.authorization_pin)

    def test_real_cli_revalidates_and_prepares_exact_plan_without_delivery(self):
        run = subprocess.run([sys.executable, str(Path(stage.__file__)), "--settings", str(self.settings_path),
                              "--signed-review-sha256", self.pin, "--authorization-sha256", self.authorization_pin],
                             capture_output=True, timeout=60, check=False)
        self.assertEqual(run.returncode, 0, run.stderr.decode())
        result = json.loads(run.stdout)
        self.assertEqual(result["state"], "DeliveryPlanPrepared")
        self.assertTrue(result["deliveryAuthorized"])
        self.assertFalse(result["deliveryAttempted"])
        self.assertFalse(result["submissionReady"])
        plan_bytes = (self.output / "delivery-plan.json").read_bytes()
        plan = json.loads(plan_bytes)
        self.assertEqual(set(plan), {"candidateSha256", "reviewSha256", "authorizationSha256", "packageSha256",
                                     "signedFormSha256", "packageFileName", "signedFormFileName", "sender", "recipient"})
        self.assertEqual(plan["reviewSha256"], self.pin)
        self.assertEqual(plan["authorizationSha256"], self.authorization_pin)
        self.assertEqual(plan["sender"], self.approval["sender"])
        self.assertEqual(result["planFileSha256"], stage.sha(plan_bytes))
        names = [plan["packageFileName"], plan["signedFormFileName"]]
        self.assertEqual(sorted(item.name for item in (self.output / "delivery").iterdir()), sorted(names))
        for name in names:
            self.assertEqual((self.output / "delivery" / name).read_bytes(),
                             (self.signed.output / "delivery" / name).read_bytes())
        self.assertEqual((self.output / "COMPLETE").read_text().strip(),
                         stage.sha((self.output / "delivery-review-receipt.json").read_bytes()))
        for private in ("evidence.zip", "synthetic-signature.png", "private-delivery-settings.json"):
            self.assertFalse((self.output / private).exists())
        with self.assertRaisesRegex(ValueError, "new delivery"):
            self.run_stage()

    def test_signing_approval_is_not_delivery_approval(self):
        with self.assertRaises(ValueError):
            stage.prepare(self.settings_path, self.pin, self.signed.approval_pin)
        self.authorization.write_bytes(self.signed.authorization.read_bytes())
        with self.assertRaises(ValueError):
            stage.prepare(self.settings_path, self.pin, self.signed.approval_pin)
        self.assertFalse(self.output.exists())

    def test_final_approval_requires_all_exact_artifacts_and_visual_review(self):
        original = self.approval.copy()
        for key, invalid in (("signedReviewSha256", "0" * 64), ("candidateSha256", "0" * 64),
                             ("packageSha256", "0" * 64), ("signedFormSha256", "0" * 64),
                             ("signedVisualReviewCompleted", False), ("deliveryAuthorized", False),
                             ("signedVisualReviewCompleted", "true"), ("schemaVersion", True)):
            self.approval = dict(original, **{key: invalid})
            self.save_approval()
            with self.subTest(key=key, invalid=invalid), self.assertRaises(ValueError):
                self.run_stage()
            self.assertFalse(self.output.exists())

    def test_expired_naive_or_changed_authorization_stops_preparation(self):
        for expires in ("2020-01-01T00:00:00Z", "2099-01-01T00:00:00", None, True):
            self.approval["expiresUtc"] = expires
            self.save_approval()
            with self.subTest(expires=expires), self.assertRaisesRegex(ValueError, "expired|timezone"):
                self.run_stage()
        self.authorization.write_bytes(self.authorization.read_bytes() + b" ")
        with self.assertRaisesRegex(ValueError, "pinned digest"):
            self.run_stage()
        self.assertFalse(self.output.exists())

    def test_expiration_during_validation_is_rechecked(self):
        real_run = stage.run_process
        def expire_after_validation(*args, **kwargs):
            result = real_run(*args, **kwargs)
            clock.now.return_value = datetime.now(timezone.utc) + timedelta(hours=1)
            return result
        with patch.object(stage, "datetime", wraps=datetime) as clock, patch.object(stage, "run_process", side_effect=expire_after_validation):
            clock.now.return_value = datetime.now(timezone.utc)
            with self.assertRaisesRegex(ValueError, "expired"):
                self.run_stage()
        self.assertFalse(self.output.exists())

    def test_sender_destination_and_subject_cannot_inject_or_redirect_mail(self):
        original = self.approval.copy()
        for key, value in (("recipient", "someone@example.org"), ("subject", "other subject"),
                           ("sender", "developer@example.org\r\nBcc: other@example.org"),
                           ("sender", "Name <developer@example.org>"), ("sender", "a@b.org,c@d.org"),
                           ("sender", "a..b@example.org")):
            self.approval = dict(original, **{key: value})
            self.save_approval()
            with self.subTest(key=key, value=value), self.assertRaises(ValueError):
                self.run_stage()
        self.assertFalse(self.output.exists())

    def test_changed_signed_files_and_evidence_are_rejected(self):
        for path in (self.signed.output / "delivery" / self.receipt["packageFileName"],
                     self.signed.output / "delivery" / self.receipt["signedFormFileName"],
                     self.signed.output / "signing-report.json", self.signed.output / "validation-report.json",
                     self.signed.output / "review-receipt.json", self.signed.review.output / "evidence.zip",
                     self.signed.review.output / "form-report.json"):
            original = path.read_bytes()
            try:
                path.write_bytes(original + b" ")
                with self.subTest(path=path.name), self.assertRaises(ValueError):
                    self.run_stage()
                self.assertFalse(self.output.exists())
            finally:
                path.write_bytes(original)

    def test_missing_completion_on_either_review_stops_preparation(self):
        for folder in (self.signed.output, self.signed.review.output):
            path = folder / "COMPLETE"
            original = path.read_bytes()
            try:
                path.unlink()
                with self.subTest(folder=folder.name), self.assertRaises(OSError):
                    self.run_stage()
            finally:
                path.write_bytes(original)
        self.assertFalse(self.output.exists())

    def test_failed_fresh_validation_prevents_prepared_output(self):
        with patch.object(stage, "run_process", return_value=subprocess.CompletedProcess([], 1, "", "stale")):
            with self.assertRaisesRegex(ValueError, "no longer passes"):
                self.run_stage()
        self.assertFalse(self.output.exists())

    def test_mismatched_fresh_validation_cannot_prepare(self):
        result = json.loads((self.signed.output / "validation-report.json").read_bytes())
        result["Validation"]["ObservationsSha256"] = "0" * 64
        with patch.object(stage, "run_process", return_value=subprocess.CompletedProcess([], 0, json.dumps(result), "")):
            with self.assertRaisesRegex(ValueError, "Fresh validation differs"):
                self.run_stage()
        self.assertFalse(self.output.exists())

    def test_interrupted_publication_is_not_complete_and_cannot_be_reused(self):
        original = Path.rename
        def fail_during_publish(path, destination):
            if path.name == "delivery-plan.json":
                raise OSError("synthetic disk failure")
            return original(path, destination)
        with patch.object(Path, "rename", fail_during_publish), self.assertRaisesRegex(OSError, "synthetic"):
            self.run_stage()
        self.assertFalse((self.output / "COMPLETE").exists())
        with self.assertRaisesRegex(ValueError, "new delivery"):
            self.run_stage()


if __name__ == "__main__":
    unittest.main()
