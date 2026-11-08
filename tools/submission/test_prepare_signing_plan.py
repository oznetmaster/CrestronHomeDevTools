# Copyright (c) 2026 Neil Colvin. MIT licensed.
import json
import unittest
from pathlib import Path
from unittest.mock import patch
import prepare_signing_plan as plan
import test_prepare_signed_review as fixtures

class SigningPlanTests(unittest.TestCase):
    def setUp(self):
        self.f = fixtures.SignedReviewStageTests(); self.f.setUp(declared_gaps=True)
        self.addCleanup(self.f.doCleanups)
    def prepare(self):
        f=self.f
        return plan.prepare(f.review.output, f.review_pin, f.authorization, f.approval_pin,
            f.root / "signing-plan", f.output, f.root / "deliberately-unreadable-credentials.setup")
    def test_exact_receipt_drives_arguments_without_accessing_signature(self):
        result=self.prepare()
        self.assertEqual(result["arguments"][result["arguments"].index("--review-sha256")+1], self.f.review_pin)
        self.assertFalse(result["signatureRead"])
        self.assertFalse(self.f.output.exists())
        self.assertTrue((self.f.root / "signing-plan/COMPLETE").is_file())
    def test_old_receipt_refused_before_output_or_signature_access(self):
        self.f.review_pin="0"*64
        with self.assertRaises(ValueError): self.prepare()
        self.assertFalse((self.f.root / "signing-plan").exists())
    def test_different_form_authority_refused(self):
        self.f.approval["formSha256"]="0"*64
        self.f.review.fixture.write_json(self.f.authorization,self.f.approval)
        self.f.approval_pin=plan.sha(self.f.authorization.read_bytes())
        with self.assertRaisesRegex(ValueError,"different review"): self.prepare()
    def test_changed_bytes_refused_even_with_original_receipt(self):
        with (self.f.review.output / "self-test.review.pdf").open("ab") as stream: stream.write(b"changed")
        with self.assertRaisesRegex(ValueError,"input bytes changed"): self.prepare()
