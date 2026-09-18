# Copyright (c) 2026 Neil Colvin.
# Licensed under the MIT License. See LICENSE in the repository root.
"""Recheck synthetic signed handoffs through the real evidence validator; never deliver."""
from datetime import datetime, timedelta, timezone
import json
from pathlib import Path
import subprocess
import sys
import unittest
from unittest.mock import patch

import revalidate_delivery as stage
import test_prepare_delivery as preparation_tests


class DeliveryRevalidationTests(unittest.TestCase):
    def setUp(self):
        self.fixture = f = preparation_tests.DeliveryStageTests()
        f.setUp()
        self.addCleanup(f.doCleanups)
        self.prepared_receipt = f.run_stage()
        self.prepared = f.output
        self.pin = stage.sha((f.output / "delivery-review-receipt.json").read_bytes())
        self.output = f.root / "revalidation"
        self.settings = {"schemaVersion": 1, "preparedDirectory": str(self.prepared),
                         "preparationSettings": str(f.settings_path), "output": str(self.output)}
        self.settings_path = f.root / "private-revalidation-settings.json"
        stage.write_json(self.settings_path, self.settings)

    def run_stage(self):
        return stage.revalidate(self.settings_path, self.pin, self.fixture.pin, self.fixture.authorization_pin)

    def test_cli_rechecks_real_chain_and_preserves_exact_plan(self):
        f = self.fixture
        run = subprocess.run([sys.executable, str(Path(stage.__file__)), "--settings", str(self.settings_path),
                              "--delivery-review-sha256", self.pin, "--signed-review-sha256", f.pin,
                              "--authorization-sha256", f.authorization_pin], capture_output=True, timeout=90)
        self.assertEqual(run.returncode, 0, run.stderr.decode())
        result = json.loads(run.stdout)
        self.assertEqual(result["state"], "DeliveryRevalidated")
        self.assertFalse(result["deliveryAttempted"])
        self.assertFalse(result["submissionReady"])
        self.assertEqual(result["plan"], json.loads((self.prepared / "delivery-plan.json").read_bytes()))
        self.assertEqual(result["expiresUtc"], f.approval["expiresUtc"])
        self.assertEqual((self.output / "COMPLETE").read_text().strip(), stage.sha((self.output / "revalidation-receipt.json").read_bytes()))
        self.assertFalse((self.output / "private-settings.json").exists())
        with self.assertRaisesRegex(ValueError, "new output"):
            self.run_stage()

    def test_altered_prepared_files_or_completion_are_rejected(self):
        plan = json.loads((self.prepared / "delivery-plan.json").read_bytes())
        files = [self.prepared / name for name in ("COMPLETE", "delivery-plan.json", "delivery-review-receipt.json",
                 "signed-review-receipt.json", "delivery-authorization.json", "validation-report.json")]
        files += [self.prepared / "delivery" / plan[key] for key in ("packageFileName", "signedFormFileName")]
        for path in files:
            original = path.read_bytes()
            try:
                path.write_bytes(original + b"altered")
                with self.subTest(path=path.name), self.assertRaises(ValueError):
                    self.run_stage()
                self.assertFalse(self.output.exists())
            finally:
                path.write_bytes(original)

    def test_revoked_original_approval_cannot_use_retained_copy(self):
        self.fixture.authorization.unlink()
        with self.assertRaises(OSError):
            self.run_stage()
        self.assertFalse(self.output.exists())

    def test_original_evidence_is_revalidated_instead_of_trusting_old_report(self):
        evidence = self.fixture.signed.review.output / "evidence.zip"
        evidence.write_bytes(evidence.read_bytes() + b"changed")
        with self.assertRaises(ValueError):
            self.run_stage()
        self.assertFalse(self.output.exists())

    def test_fresh_validator_failure_blocks_dispatch_authorization(self):
        with patch.object(stage.prepare_delivery, "run_process", return_value=subprocess.CompletedProcess([], 1, "", "synthetic failure")):
            with self.assertRaisesRegex(ValueError, "no longer passes"):
                self.run_stage()
        self.assertFalse(self.output.exists())

    def test_expiry_after_full_revalidation_is_rechecked(self):
        real_prepare = stage.prepare_delivery.prepare
        def later(*args):
            result = real_prepare(*args)
            clock.now.return_value = datetime.now(timezone.utc) + timedelta(hours=1)
            return result
        with patch.object(stage, "datetime", wraps=datetime) as clock, patch.object(stage.prepare_delivery, "prepare", side_effect=later):
            clock.now.return_value = datetime.now(timezone.utc)
            with self.assertRaisesRegex(ValueError, "expired"):
                self.run_stage()
        self.assertFalse(self.output.exists())

    def test_handoff_changed_during_validation_is_not_accepted(self):
        real_prepare = stage.prepare_delivery.prepare
        def changed(*args):
            result = real_prepare(*args)
            (self.prepared / "COMPLETE").write_text("0" * 64)
            return result
        with patch.object(stage.prepare_delivery, "prepare", side_effect=changed), self.assertRaises(ValueError):
            self.run_stage()
        self.assertFalse(self.output.exists())

    def test_output_must_not_be_inside_original_review(self):
        self.settings["output"] = str(self.prepared / "new-child")
        self.settings_path.write_text(json.dumps(self.settings), encoding="utf-8")
        with self.assertRaisesRegex(ValueError, "separate"):
            self.run_stage()

    def test_changed_plan_cannot_reuse_unchanged_signed_authorization(self):
        plan_path = self.prepared / "delivery-plan.json"
        receipt_path = self.prepared / "delivery-review-receipt.json"
        plan = json.loads(plan_path.read_bytes())
        plan["recipient"] = "unexpected@example.org"
        plan_path.write_text(json.dumps(plan), encoding="utf-8")
        receipt = json.loads(receipt_path.read_bytes())
        receipt["planFileSha256"] = stage.sha(plan_path.read_bytes())
        receipt_path.write_text(json.dumps(receipt), encoding="utf-8")
        self.pin = stage.sha(receipt_path.read_bytes())
        (self.prepared / "COMPLETE").write_text(self.pin, encoding="ascii")
        with self.assertRaisesRegex(ValueError, "exact authorized delivery plan"):
            self.run_stage()
        self.assertFalse(self.output.exists())

    def test_approval_removed_during_validation_is_rechecked(self):
        real_prepare = stage.prepare_delivery.prepare
        def removed(*args):
            result = real_prepare(*args)
            self.fixture.authorization.unlink()
            return result
        with patch.object(stage.prepare_delivery, "prepare", side_effect=removed), self.assertRaises(OSError):
            self.run_stage()
        self.assertFalse(self.output.exists())

    def test_independent_approval_pin_cannot_be_replaced_by_receipt_value(self):
        with self.assertRaisesRegex(ValueError, "approved pins"):
            stage.revalidate(self.settings_path, self.pin, self.fixture.pin, "0" * 64)
        self.assertFalse(self.output.exists())


    def test_interrupted_publication_has_no_complete_marker(self):
        real_rename = Path.rename
        def fail(path, destination):
            if path.name == "revalidation-receipt.json":
                raise OSError("synthetic interrupted publication")
            return real_rename(path, destination)
        with patch.object(Path, "rename", side_effect=fail, autospec=True), self.assertRaises(OSError):
            self.run_stage()
        self.assertFalse((self.output / "COMPLETE").exists())
        with self.assertRaisesRegex(ValueError, "new output"):
            self.run_stage()
