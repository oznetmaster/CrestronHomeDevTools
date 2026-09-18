# Copyright (c) 2026 Neil Colvin.
# Licensed under the MIT License. See LICENSE in the repository root.
"""Protected signing-stage checks use synthetic evidence and a synthetic signature."""

from datetime import datetime, timedelta, timezone
import json
from pathlib import Path
import subprocess
import sys
import unittest
from unittest.mock import patch

from PIL import Image, ImageDraw
from pypdf import PdfReader

import prepare_signed_review as stage
import test_prepare_review as review_tests


class SignedReviewStageTests(unittest.TestCase):
    def setUp(self, android=False):
        self.review = f = review_tests.ReviewStageTests()
        f.setUp()
        self.addCleanup(f.doCleanups)
        options = f.android_run()[1] if android else {}
        self.receipt = f.run_stage(signing_copy=True, **options)
        self.root = f.root
        self.output = f.root / "signed-review"
        self.review_pin = stage.sha((f.output / "review-receipt.json").read_bytes())
        self.image = f.root / "synthetic-signature.png"
        bitmap = Image.new("RGB", (500, 70), "white")
        ImageDraw.Draw(bitmap).text((10, 15), "SYNTHETIC TEST SIGNATURE", fill="black", font_size=20)
        bitmap.save(self.image)
        now = datetime.now(timezone.utc)
        self.approval = {"schemaVersion": 1, "formSha256": self.receipt["formSha256"],
                        "formReportSha256": self.receipt["formReportSha256"],
                        "inventorySha256": f.pins[1], "candidateSha256": f.pins[0],
                        "packageSha256": stage.sha(f.fixture.package.read_bytes()),
                        "signatureImageSha256": stage.sha(self.image.read_bytes()),
                        "signer": "Synthetic Example Developer", "signingDate": now.date().isoformat(),
                        "expiresUtc": (now + timedelta(minutes=10)).isoformat(),
                        "signatureField": "Signature", "dateField": "Date",
                        "visualReviewCompleted": True, "signatureAuthorized": True}
        self.authorization = f.root / "synthetic-authorization.json"
        f.fixture.write_json(self.authorization, self.approval)
        self.approval_pin = stage.sha(self.authorization.read_bytes())
        self.settings = {"schemaVersion": 1, "reviewDirectory": str(f.output),
                         "authorization": str(self.authorization), "signatureImage": str(self.image),
                         "dotnet": f.settings["dotnet"], "validator": f.settings["validator"],
                         "output": str(self.output)}
        self.settings_path = f.root / "private-signing-settings.json"
        f.fixture.write_json(self.settings_path, self.settings)

    def run_stage(self):
        return stage.prepare(self.settings_path, self.review_pin, self.approval_pin)

    def test_real_cli_revalidates_and_publishes_only_intended_delivery_files(self):
        result = subprocess.run([sys.executable, str(Path(stage.__file__)), "--settings", str(self.settings_path),
                                 "--review-sha256", self.review_pin, "--authorization-sha256", self.approval_pin],
                                capture_output=True, timeout=60, check=False)
        self.assertEqual(result.returncode, 0, result.stderr.decode())
        receipt = json.loads(result.stdout)
        self.assertEqual(receipt["state"], "SignedReviewPrepared")
        self.assertTrue(receipt["signatureApplied"])
        self.assertTrue(receipt["visualReviewRequired"])
        self.assertFalse(receipt["submissionReady"])
        self.assertFalse(receipt["deliveryAttempted"])
        self.assertFalse(receipt["deliveryAuthorized"])
        self.assertEqual(sorted(item.name for item in (self.output / "delivery").iterdir()),
                         sorted([self.review.fixture.package.name, "Driver-Self-Test.signed.pdf"]))
        self.assertEqual((self.output / "delivery" / self.review.fixture.package.name).read_bytes(),
                         self.review.fixture.package.read_bytes())
        form = self.output / "delivery" / "Driver-Self-Test.signed.pdf"
        self.assertEqual(PdfReader(form).get_fields()["Signature"]["/V"], self.approval["signer"])
        self.assertEqual(receipt["signedFormSha256"], stage.sha(form.read_bytes()))
        self.assertEqual((self.output / "COMPLETE").read_text().strip(),
                         stage.sha((self.output / "signed-review-receipt.json").read_bytes()))
        self.assertFalse((self.output / "evidence.zip").exists())
        self.assertFalse((self.output / self.image.name).exists())
        self.assertFalse((self.output / self.authorization.name).exists())
        with self.assertRaisesRegex(ValueError, "new signed-review"):
            self.run_stage()

    def test_changed_retained_inputs_are_rejected_before_signing(self):
        for name in ("self-test.review.pdf", "form-report.json", "inventory.json", "mapping.json", "evidence.zip"):
            path = self.review.output / name
            original = path.read_bytes()
            try:
                path.write_bytes(original + b"changed")
                with self.subTest(name=name), patch.object(stage.signing, "sign") as sign:
                    with self.assertRaisesRegex(ValueError, "input changed"):
                        self.run_stage()
                    sign.assert_not_called()
                    self.assertFalse(self.output.exists())
            finally:
                path.write_bytes(original)

    def test_uncleared_or_wrong_review_pin_cannot_prepare(self):
        self.review_pin = "0" * 64
        with self.assertRaises(ValueError):
            self.run_stage()
        self.review_pin = stage.sha((self.review.output / "review-receipt.json").read_bytes())
        (self.review.output / "COMPLETE").unlink()
        with self.assertRaises(OSError):
            self.run_stage()
        self.assertFalse(self.output.exists())

    def test_ordinary_review_is_not_a_signing_copy(self):
        receipt = dict(self.receipt, signingCopy=False)
        path = self.review.output / "review-receipt.json"
        self.review.fixture.write_json(path, receipt)
        self.review_pin = stage.sha(path.read_bytes())
        (self.review.output / "COMPLETE").write_text(self.review_pin + "\n")
        with self.assertRaisesRegex(ValueError, "signing-copy"):
            self.run_stage()
        self.assertFalse(self.output.exists())

    def test_review_source_must_match_retained_candidate(self):
        receipt = dict(self.receipt, sourceCommit="b" * 40)
        path = self.review.output / "review-receipt.json"
        self.review.fixture.write_json(path, receipt)
        self.review_pin = stage.sha(path.read_bytes())
        (self.review.output / "COMPLETE").write_text(self.review_pin + "\n")
        with patch.object(stage.signing, "sign") as sign:
            with self.assertRaisesRegex(ValueError, "candidate source"):
                self.run_stage()
            sign.assert_not_called()
        self.assertFalse(self.output.exists())

    def test_current_validation_failure_prevents_signature(self):
        with patch.object(stage, "run_process", return_value=subprocess.CompletedProcess([], 1, b"", b"")), \
                patch.object(stage.signing, "sign") as sign:
            with self.assertRaisesRegex(ValueError, "no longer passes"):
                self.run_stage()
            sign.assert_not_called()
        self.assertFalse(self.output.exists())

    def test_mismatched_current_validation_prevents_signature(self):
        original = stage.run_process

        def wrong_identity(arguments, timeout):
            result = original(arguments, timeout)
            report = json.loads(result.stdout)
            report["Validation"]["ObservationsSha256"] = "0" * 64
            result.stdout = json.dumps(report).encode()
            return result

        with patch.object(stage, "run_process", wrong_identity), patch.object(stage.signing, "sign") as sign:
            with self.assertRaisesRegex(ValueError, "does not match"):
                self.run_stage()
            sign.assert_not_called()
        self.assertFalse(self.output.exists())

    def test_different_authorization_or_expired_signature_approval_is_rejected(self):
        original = self.authorization.read_bytes()
        self.approval["candidateSha256"] = "0" * 64
        self.review.fixture.write_json(self.authorization, self.approval)
        self.approval_pin = stage.sha(self.authorization.read_bytes())
        with self.assertRaisesRegex(ValueError, "exact review"):
            self.run_stage()
        self.approval = json.loads(original)
        self.approval["expiresUtc"] = "2000-01-01T00:00:00Z"
        self.review.fixture.write_json(self.authorization, self.approval)
        self.approval_pin = stage.sha(self.authorization.read_bytes())
        with self.assertRaisesRegex(ValueError, "expired"):
            self.run_stage()
        self.assertFalse(self.output.exists())

    def test_interrupted_publication_leaves_no_completion_and_cannot_be_reused(self):
        original = Path.rename

        def interrupt(source, target):
            if Path(target).parent == self.output:
                raise OSError("synthetic interrupted signing-stage publication")
            return original(source, target)

        with patch.object(Path, "rename", interrupt):
            with self.assertRaisesRegex(OSError, "interrupted"):
                self.run_stage()
        self.assertFalse((self.output / "COMPLETE").exists())
        with self.assertRaisesRegex(ValueError, "new signed-review"):
            self.run_stage()


if __name__ == "__main__":
    unittest.main()
