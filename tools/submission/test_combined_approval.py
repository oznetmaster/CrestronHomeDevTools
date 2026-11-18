# Copyright (c) 2026 Neil Colvin.
# Licensed under the MIT License. See LICENSE in the repository root.
"""Offline combined-approval contracts; synthetic signatures and no providers."""

import copy
import json
from pathlib import Path
import unittest

from pypdf import PdfReader, PdfWriter
from pypdf.generic import NameObject

import combined_approval as combined
import sign_reviewed_packet as manual_signing
from build_help import sha
import test_sign_self_test_form as signatures


class CombinedApprovalTests(unittest.TestCase):
    def setUp(self):
        self.signing = signatures.SigningTests()
        self.signing.setUp()
        self.addCleanup(self.signing.doCleanups)
        self.root = self.signing.fixture.root
        self.now = self.signing.now
        self.packet_path = self.root / "combined-packet.json"
        self.auth_path = self.root / "combined-authority.json"
        self.review_path = self.root / "technical-review.json"
        self.visual_path = self.root / "signed-visual.json"
        self.letter_path = self.root / "letter.txt"
        self.letter_path.write_text("To: drivers@example.org\nSubject: Driver Submission Package\n\n"
                                    "Synthetic test only. Package: {{PACKAGE_DOWNLOAD_URL}}\n", encoding="utf-8")
        self.review = {"schemaVersion": 1, "method": "ManualTechnicalReview", "reviewer": "Synthetic reviewer",
                       "packageSha256": self.ref(self.signing.fixture.package)["sha256"],
                       "inventorySha256": self.ref(self.signing.inventory)["sha256"], "rows": [
                           {"id": r["id"], "disposition": "Checked", "rationale": "Synthetic measured scope, not real hardware.",
                            "evidence": [self.ref(self.signing.fixture.evidence / "synthetic.txt")]}
                           for r in self.signing.fixture.inventory["requirements"]]}
        self.write(self.review_path, self.review)
        self.packet = {"schemaVersion": 1, "unsignedForm": self.ref(self.signing.fixture.output),
                       "inventory": self.ref(self.signing.inventory), "package": self.ref(self.signing.fixture.package),
                       "coverLetter": self.ref(self.letter_path), "evidenceReview": self.ref(self.review_path),
                       "signing": {"signer": self.signing.approval["signer"], "date": "2026-09-17", "signatureField": "Signature",
                                   "dateField": "Date", "signatureImageSha256": self.signing.approval["signatureImageSha256"]},
                       "delivery": {"sender": "developer@example.org", "recipient": "drivers@example.org",
                                    "subject": "Driver Submission Package", "urlToken": "{{PACKAGE_DOWNLOAD_URL}}"},
                       "provider": {"uploadHost": "uploader.crestron.com", "formSha256": "d" * 64, "termsSha256": "e" * 64}}
        self.auth = {"schemaVersion": 1, "packetSha256": "", "approvalMode": "CombinedSigningAndSubmission",
                     "signingAuthorized": True, "deliveryAuthorized": True, "visualReviewCompleted": True,
                     "expiresUtc": "2026-09-17T18:00:00Z"}

    def write(self, path, value):
        path.write_text(json.dumps(value), encoding="utf-8")

    def ref(self, path):
        return {"path": str(path), "sha256": sha(path.read_bytes())}

    def freeze(self):
        self.write(self.packet_path, self.packet)
        self.auth["packetSha256"] = self.ref(self.packet_path)["sha256"]
        self.write(self.auth_path, self.auth)
        return self.ref(self.packet_path), self.ref(self.auth_path)

    def check(self, **kwargs):
        return combined.check(*self.freeze(), now=self.now, **kwargs)

    def signed_inputs(self):
        self.signing.sign()
        self.write(self.visual_path, {"schemaVersion": 1, "signedFormSha256": self.ref(self.signing.output)["sha256"],
                   "reviewer": "Synthetic operator", "reviewedUtc": self.now.isoformat(),
                   "signatureAndDateLegible": True, "unchangedOutsideSignatureDate": True})
        return {"signed_reference": self.ref(self.signing.output), "visual_review_reference": self.ref(self.visual_path)}

    def test_one_authority_covers_both_stages_without_creating_new_approval(self):
        checked = self.check()
        self.assertEqual("CombinedAuthorityChecked", checked["state"])
        bound = self.check(**self.signed_inputs())
        self.assertTrue(bound["deliveryAuthorizedUnderOriginalApproval"])
        self.assertEqual(self.auth["expiresUtc"], bound["expiresUtc"])
        self.assertFalse(bound["newHumanApprovalCreated"])
        self.assertFalse(bound["deliveryAttempted"])
        self.assertFalse(bound["producerAuthenticationPerformed"])
        self.assertFalse(bound["testOutcomesReassessed"])
        self.assertEqual("ManualTechnicalReview", bound["evidenceReviewMethod"])

    def test_signing_only_or_unreviewed_or_numeric_flags_do_not_grant_submission(self):
        for name in ("signingAuthorized", "deliveryAuthorized", "visualReviewCompleted"):
            for value in (False, 1, "true"):
                with self.subTest(name=name, value=value):
                    self.auth[name] = value
                    with self.assertRaises(ValueError): self.check()
            self.auth[name] = True

    def test_expired_authority_is_not_renewed_by_a_later_signed_result(self):
        signed = self.signed_inputs()
        self.auth["expiresUtc"] = "2026-09-17T11:00:00Z"
        with self.assertRaises(ValueError): self.check(**signed)

    def test_changed_approved_inputs_are_rejected_before_and_after_signing(self):
        signed = self.signed_inputs()
        for key in ("package", "coverLetter", "evidenceReview", "unsignedForm", "inventory"):
            path = Path(self.packet[key]["path"])
            data = path.read_bytes()
            with self.subTest(key=key):
                path.write_bytes(data + b"tampered")
                with self.assertRaises(ValueError): self.check()
                with self.assertRaises(ValueError): self.check(**signed)
                path.write_bytes(data)

    def test_revised_packet_cannot_reuse_old_independent_approval(self):
        packet, auth = self.freeze()
        self.packet["delivery"]["subject"] = "Different letter"
        self.write(self.packet_path, self.packet)
        with self.assertRaises(ValueError):
            combined.check(self.ref(self.packet_path), auth, now=self.now)

    def test_missing_duplicate_and_unknown_official_rows_fail(self):
        original = copy.deepcopy(self.review["rows"])
        for rows in (original[:-1], [original[0], original[0]], original + [original[0]]):
            self.review["rows"] = rows
            self.write(self.review_path, self.review)
            self.packet["evidenceReview"] = self.ref(self.review_path)
            with self.assertRaises(ValueError): self.check()

    def test_missing_evidence_does_not_become_an_automatic_pass(self):
        self.review["rows"][0]["evidence"] = []
        self.write(self.review_path, self.review)
        self.packet["evidenceReview"] = self.ref(self.review_path)
        with self.assertRaises(ValueError): self.check()

    def test_signed_output_requires_actual_exact_inspection(self):
        signed = self.signed_inputs()
        with self.assertRaises(ValueError): self.check(signed_reference=signed["signed_reference"])
        visual = json.loads(self.visual_path.read_text())
        for key, value in (("signedFormSha256", "0" * 64), ("signatureAndDateLegible", False),
                           ("unchangedOutsideSignatureDate", False), ("reviewedUtc", "2026-09-18T12:00:00Z")):
            altered = dict(visual, **{key: value})
            self.write(self.visual_path, altered)
            with self.assertRaises(ValueError):
                self.check(signed_reference=signed["signed_reference"], visual_review_reference=self.ref(self.visual_path))

    def test_a_changed_checkbox_cannot_be_hidden_by_a_signed_review_flag(self):
        signed = self.signed_inputs()
        writer = PdfWriter(); writer.clone_document_from_reader(PdfReader(self.signing.output))
        writer.update_page_form_field_values(None, {"First": NameObject("/Off")}, auto_regenerate=False)
        with self.signing.output.open("wb") as f: writer.write(f)
        visual = json.loads(self.visual_path.read_text())
        visual["signedFormSha256"] = self.ref(self.signing.output)["sha256"]
        self.write(self.visual_path, visual)
        with self.assertRaises(ValueError): self.check(signed_reference=self.ref(self.signing.output), visual_review_reference=self.ref(self.visual_path))

    def test_recipient_header_injection_unknown_placeholders_and_provider_change_fail(self):
        original = copy.deepcopy(self.packet)
        for edit in (lambda: self.packet["delivery"].update(recipient="other@example.org"),
                     lambda: self.packet["delivery"].update(subject="Subject\nBcc: other@example.org"),
                     lambda: self.packet["provider"].update(uploadHost="other.example.org")):
            self.packet = copy.deepcopy(original); edit()
            with self.assertRaises(ValueError): self.check()
        self.packet = original
        self.letter_path.write_text(self.letter_path.read_text() + "{{OTHER}}", encoding="utf-8")
        self.packet["coverLetter"] = self.ref(self.letter_path)
        with self.assertRaises(ValueError): self.check()

    def test_output_creation_refuses_overwriting_an_approval_record(self):
        self.freeze()
        with self.assertRaises(FileExistsError):
            combined.write_json(self.auth_path, {"not": "approval"})

    def test_manual_route_signs_without_fabricating_an_automatic_report(self):
        packet, auth = self.freeze()
        self.signing.report_path.unlink()
        output, receipt = self.root / "manual.signed.pdf", self.root / "manual-signing.json"
        result = manual_signing.sign(packet, auth, output, receipt, image_bytes=self.signing.image.read_bytes(), now=self.now)
        self.assertEqual("ManualReviewSignedUnderCombinedAuthority", result["state"])
        self.assertEqual("ManualTechnicalReview", result["evidenceReviewMethod"])
        self.assertFalse(result["producerAuthenticationPerformed"])
        self.assertFalse(result["testOutcomesReassessed"])
        self.assertFalse(result["deliveryAttempted"])
        self.assertTrue(result["visualReviewRequired"])
        fields = PdfReader(output).get_fields()
        self.assertEqual(self.signing.approval["signer"], fields["Signature"]["/V"])
        self.assertEqual("2026-09-17", fields["Date"]["/V"])
        self.assertTrue(all(int(field["/Ff"]) & 1 for field in fields.values()))
        self.assertEqual("/Yes", fields["First"]["/V"])
        self.assertEqual(self.auth["expiresUtc"], result["expiresUtc"])
        with self.assertRaises(ValueError):
            manual_signing.sign(packet, auth, output, receipt, image_bytes=self.signing.image.read_bytes(), now=self.now)

    def test_manual_route_refuses_wrong_signature_and_automated_route_downgrade(self):
        packet, auth = self.freeze()
        output, receipt = self.root / "manual.signed.pdf", self.root / "manual-signing.json"
        with self.assertRaises(ValueError):
            manual_signing.sign(packet, auth, output, receipt, image_bytes=b"incorrect private input", now=self.now)
        self.assertFalse(output.exists()); self.assertFalse(receipt.exists())
        self.review["method"] = "AutomatedEvidenceReview"
        self.write(self.review_path, self.review); self.packet["evidenceReview"] = self.ref(self.review_path)
        packet, auth = self.freeze()
        with self.assertRaises(ValueError):
            manual_signing.sign(packet, auth, output, receipt, image_bytes=self.signing.image.read_bytes(), now=self.now)
        self.assertFalse(output.exists()); self.assertFalse(receipt.exists())


if __name__ == "__main__":
    unittest.main()
