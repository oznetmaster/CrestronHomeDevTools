# Copyright (c) 2026 Neil Colvin.
# Licensed under the MIT License. See LICENSE in the repository root.
"""Synthetic signatures only; never an authorization for a real submission."""

from datetime import datetime, timezone
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import unittest

from PIL import Image, ImageDraw
from pypdf import PdfReader

import self_test_form as forms
import sign_self_test_form as signing
import test_self_test_form as form_tests


class SigningTests(unittest.TestCase):
    def setUp(self):
        self.fixture = form_tests.SelfTestFormTests()
        self.fixture.setUp()
        self.addCleanup(self.fixture.doCleanups)
        f = self.fixture
        self.now = datetime(2026, 9, 17, 12, tzinfo=timezone.utc)
        self.inventory = f.root / "inventory.json"
        f.write_json(self.inventory, f.inventory)
        # Identity values are synthetic, confined to this fixture.
        validation = json.dumps({"ValidationChecksPassed": True, "CandidateSha256": "a" * 64,
                                 "ObservationsSha256": "b" * 64, "Package": {"Sha256": "c" * 64}})
        identity = {"inventorySha256": forms.sha(self.inventory.read_bytes()), "candidateSha256": "a" * 64,
                    "observationsSha256": "b" * 64, "packageSha256": "c" * 64,
                    "validationReportSha256": forms.sha(validation.encode())}
        self.report = forms.write_form(f.source, f.inventory, f.output, "SYNTHETIC SIGNING FIXTURE",
                                       "Example Developer", f.decisions(), identity, False, True)
        self.report["validationReportJson"] = validation
        self.report_path = f.root / "form-report.json"
        f.write_json(self.report_path, self.report)
        self.image = f.root / "synthetic-signature.png"
        image = Image.new("RGBA", (500, 70), "white")
        ImageDraw.Draw(image).text((10, 15), "SYNTHETIC TEST - NOT A REAL SIGNATURE", fill="black", font_size=20)
        image.save(self.image)
        self.approval = {"schemaVersion": 1, "formSha256": self.report["formSha256"],
                         "formReportSha256": forms.sha(self.report_path.read_bytes()),
                         "inventorySha256": identity["inventorySha256"], "candidateSha256": "a" * 64,
                         "packageSha256": "c" * 64, "signatureImageSha256": forms.sha(self.image.read_bytes()),
                         "signer": "Synthetic Example Developer", "signingDate": "2026-09-17",
                         "expiresUtc": "2026-09-17T18:00:00Z", "signatureField": "Signature", "dateField": "Date",
                         "visualReviewCompleted": True, "signatureAuthorized": True}
        self.authorization = f.root / "authorization.json"
        self.output = f.root / "fixture.signed.pdf"

    def sign(self):
        self.fixture.write_json(self.authorization, self.approval)
        return signing.sign(self.fixture.output, self.report_path, self.inventory, self.authorization,
                            forms.sha(self.authorization.read_bytes()), self.image, self.output, self.now)

    def test_signature_preserves_pages_checkbox_values_and_appearance(self):
        report = self.sign()
        result = PdfReader(self.output)
        fields = result.get_fields()
        self.assertEqual(fields["Signature"]["/V"], self.approval["signer"])
        self.assertEqual(fields["Date"]["/V"], "2026-09-17")
        self.assertEqual(fields["First"]["/V"], "/Yes")
        self.assertTrue(all(int(field["/Ff"]) & 1 for field in fields.values()))
        self.assertFalse(report["submissionReady"])
        self.assertFalse(report["cryptographicSignature"])
        self.assertEqual(report["signedFormSha256"], forms.sha(self.output.read_bytes()))
        self.assertIn("Synthetic official-form fixture", result.pages[0].extract_text())
        self.assertIn("Checklist notes", result.pages[self.report['notesStartPage']].extract_text())
        self.assertNotIn("UNSIGNED REVIEW", result.pages[0].extract_text())

    def test_changed_inputs_are_refused(self):
        for field in ("formSha256", "formReportSha256", "inventorySha256", "candidateSha256", "packageSha256", "signatureImageSha256"):
            with self.subTest(field=field):
                original = self.approval[field]
                self.approval[field] = "0" * 64
                with self.assertRaises(ValueError):
                    self.sign()
                self.assertFalse(self.output.exists())
                self.approval[field] = original

    def test_missing_or_expired_authorization_is_refused(self):
        for key, value in (("signatureAuthorized", False), ("visualReviewCompleted", False),
                           ("expiresUtc", "2026-09-17T10:00:00Z"), ("signingDate", "2026-09-16"),
                           ("signatureField", "First"), ("dateField", "Signature")):
            with self.subTest(key=key):
                original = self.approval[key]
                self.approval[key] = value
                with self.assertRaises(ValueError):
                    self.sign()
                self.assertFalse(self.output.exists())
                self.approval[key] = original

    def test_ordinary_review_cannot_be_signed(self):
        self.report["signingCopy"] = False
        self.fixture.write_json(self.report_path, self.report)
        self.approval["formReportSha256"] = forms.sha(self.report_path.read_bytes())
        with self.assertRaises(ValueError):
            self.sign()

    def test_incomplete_decisions_cannot_be_signed(self):
        self.report["checkedRequirements"].pop()
        self.fixture.write_json(self.report_path, self.report)
        self.approval["formReportSha256"] = forms.sha(self.report_path.read_bytes())
        with self.assertRaises(ValueError):
            self.sign()

    def test_original_approval_pin_is_required(self):
        self.fixture.write_json(self.authorization, self.approval)
        with self.assertRaises(ValueError):
            signing.sign(self.fixture.output, self.report_path, self.inventory, self.authorization, "0" * 64,
                         self.image, self.output, self.now)

    def test_output_cannot_be_overwritten(self):
        self.sign()
        original = self.output.read_bytes()
        with self.assertRaises(FileExistsError):
            self.sign()
        self.assertEqual(self.output.read_bytes(), original)

    def test_blank_draft_cannot_be_a_signing_copy(self):
        with self.assertRaises(ValueError):
            forms.write_form(self.fixture.source, self.fixture.inventory, self.fixture.output, "Draft", "Example",
                             [], {}, True, True)

    def test_cli_validates_evidence_before_preparing_a_signing_copy(self):
        f = self.fixture
        candidate = {"schemaVersion": 1, "identity": f.identity, "packageRequirements": {"driverId": f.driver_id,
                     "driverVersion": "1.0.000.0000", "kind": "NewDriver", "developerFilenameToken": "Example",
                     "publicSupportEmail": "support@example.org"}}
        inputs = {"candidate": candidate, "mapping": f.mapping, "observations": f.observations}
        for name, value in inputs.items():
            f.write_json(f.root / (name + ".json"), value)
        validator = Path(os.environ.get("SUBMISSION_TEST_VALIDATOR", str(Path(__file__).resolve().parents[2] /
                         "CrestronHomeDevTools.Console/bin/Release/net10.0/CrestronHomeDevTools.Console.dll")))
        dotnet = os.environ.get("SUBMISSION_TEST_DOTNET") or shutil.which("dotnet")
        self.assertTrue(validator.is_file())
        output, report = f.root / "validated.review.pdf", f.root / "validated-report.json"
        command = [sys.executable, str(Path(forms.__file__)), "for-signing", "--template", str(f.template),
                   "--inventory", str(self.inventory), "--inventory-sha256", forms.sha(self.inventory.read_bytes()),
                   "--output", str(output), "--report", str(report), "--title", "SYNTHETIC CLI FIXTURE", "--author", "Example Developer",
                   "--mapping", str(f.root / "mapping.json"), "--mapping-sha256", forms.sha((f.root / "mapping.json").read_bytes()),
                   "--candidate", str(f.root / "candidate.json"), "--candidate-sha256", forms.sha((f.root / "candidate.json").read_bytes()),
                   "--policy", str(f.policy_path), "--observations", str(f.root / "observations.json"),
                   "--package", str(f.package), "--evidence", str(f.evidence), "--dotnet", dotnet, "--validator", str(validator)]
        result = subprocess.run(command, capture_output=True, text=True, timeout=120)
        self.assertEqual(result.returncode, 0, result.stderr)
        actual = json.loads(report.read_text())
        self.fixture.output = output
        self.report_path = report
        self.approval.update(formSha256=actual["formSha256"], formReportSha256=forms.sha(report.read_bytes()),
                             candidateSha256=actual["identity"]["candidateSha256"], packageSha256=actual["identity"]["packageSha256"])
        self.assertTrue(self.sign()["signatureApplied"])


if __name__ == "__main__":
    unittest.main()
