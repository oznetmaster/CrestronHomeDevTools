# Copyright (c) 2026 Neil Colvin.
# Licensed under the MIT License. See LICENSE in the repository root.
"""Sign only synthetic declared-gap forms after actual bundle reassessment."""

import json
from pathlib import Path
import subprocess
import sys
import unittest
from unittest.mock import patch

from pypdf import PdfReader

import prepare_signed_review as stage
import test_prepare_signed_review as fixtures


class DeclaredGapSigningTests(unittest.TestCase):
    def setUp(self):
        self.f = fixtures.SignedReviewStageTests()
        self.f.setUp(declared_gaps=True)
        self.addCleanup(self.f.doCleanups)

    def save_approval(self):
        self.f.review.fixture.write_json(self.f.authorization, self.f.approval)
        self.f.approval_pin = stage.sha(self.f.authorization.read_bytes())

    def test_public_signing_stage_preserves_gaps_and_original_form(self):
        f = self.f
        result = subprocess.run([sys.executable, str(Path(stage.__file__)), "--settings", str(f.settings_path),
            "--review-sha256", f.review_pin, "--authorization-sha256", f.approval_pin],
            capture_output=True, timeout=60, check=False)
        self.assertEqual(result.returncode, 0, result.stderr.decode())
        receipt = json.loads(result.stdout)
        for key in ("reviewMode", "verificationStatus", "declarationsSha256"):
            self.assertEqual(receipt[key], f.approval[key])
        self.assertFalse(receipt["deliveryAttempted"])
        self.assertFalse(receipt["submissionReady"])
        self.assertTrue(receipt["signatureApplied"])
        signed = PdfReader(f.output / "delivery" / receipt["signedFormFileName"])
        original = PdfReader(f.review.output / "self-test.review.pdf")
        fields = signed.get_fields()
        self.assertEqual(fields["First"]["/V"], "/Yes")
        self.assertEqual(fields["Second"]["/V"], "/Off")
        self.assertEqual(fields["Signature"]["/V"], f.approval["signer"])
        self.assertEqual(signed.metadata.title, "Driver self-test form with declared gaps")
        self.assertIn("Equipment unavailable.", "\n".join(page.extract_text() for page in signed.pages))
        for before, after in zip(original.pages, signed.pages, strict=True):
            self.assertEqual(before.get_contents().get_data(), after.get_contents().get_data())
        self.assertEqual((f.output / "declarations.json").read_bytes(),
                         (f.review.output / "declarations.json").read_bytes())
        self.assertEqual(sorted(path.name for path in (f.output / "delivery").iterdir()),
                         sorted([f.review.fixture.package.name, "Driver-Self-Test.signed.pdf"]))

    def test_generic_or_changed_gap_approval_cannot_sign(self):
        f = self.f
        for key in ("reviewMode", "verificationStatus", "declarationsSha256"):
            for value in (None, "changed"):
                with self.subTest(key=key, value=value):
                    original = f.approval.pop(key)
                    if value is not None:
                        f.approval[key] = value
                    self.save_approval()
                    with patch.object(stage.signing, "sign") as sign, self.assertRaises(ValueError):
                        f.run_stage()
                    sign.assert_not_called()
                    self.assertFalse(f.output.exists())
                    f.approval[key] = original

    def test_changed_declarations_cannot_sign(self):
        path = self.f.review.output / "declarations.json"
        path.write_bytes(path.read_bytes() + b" ")
        with patch.object(stage.signing, "sign") as sign, self.assertRaisesRegex(ValueError, "input changed"):
            self.f.run_stage()
        sign.assert_not_called()
        self.assertFalse(self.f.output.exists())

    def test_direct_signer_also_requires_explicit_gap_authorization(self):
        f = self.f
        for key in ("reviewMode", "verificationStatus", "declarationsSha256"):
            f.approval.pop(key)
        self.save_approval()
        review = f.review.output
        output = f.root / "unapproved.signed.pdf"
        with self.assertRaisesRegex(ValueError, "Signing declared gaps requires authorization"):
            stage.signing.sign(review / "self-test.review.pdf", review / "form-report.json", review / "inventory.json",
                               f.authorization, f.approval_pin, f.image, output)
        self.assertFalse(output.exists())


if __name__ == "__main__":
    unittest.main()
