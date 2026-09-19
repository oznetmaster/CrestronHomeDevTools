# Copyright (c) 2026 Neil Colvin.
# Licensed under the MIT License. See LICENSE in the repository root.
"""Synthetic outbound requests only; no signature, authorization or external delivery."""

import copy
import json
from pathlib import Path
import subprocess
import sys
import unittest
from unittest.mock import patch
import zipfile

from pypdf import PdfReader

import prepare_review_request as stage
import test_declared_gap_review as fixtures


class ReviewRequestTests(unittest.TestCase):
    def setUp(self):
        self.base = fixtures.DeclaredGapReviewTests()
        self.base.setUp()
        self.addCleanup(self.base.doCleanups)
        self.root = self.base.f.root
        self.review_receipt = self.base.run_stage()
        self.review = self.base.output
        self.review_pin = stage.sha((self.review / "review-receipt.json").read_bytes())
        self.disposition_path = self.root / "disposition.json"
        self.disposition = {"schemaVersion": 1, "reviewReceiptSha256": self.review_pin,
                            "candidateSha256": self.review_receipt["candidateSha256"],
                            "declarationsSha256": self.review_receipt["declarationsSha256"], "reviewMode": "DeclaredGaps",
                            "attachmentKind": "UnsignedSelfTest", "omissions": [
                                {"id": "signature", "reason": "The developer declined to sign this incomplete request."}]}
        self.output = self.root / "request"
        self.settings = {"schemaVersion": 1, "reviewDirectory": str(self.review), "disposition": str(self.disposition_path),
                         "output": str(self.output), "title": "SYNTHETIC OUTBOUND REQUEST - NEVER SUBMIT", "author": "Example Developer",
                         "dotnet": self.base.base.settings["dotnet"], "validator": self.base.base.settings["validator"]}
        self.settings_path = self.root / "request-settings.json"
        self.save()

    def save(self):
        self.base.f.write_json(self.disposition_path, self.disposition)
        self.base.f.write_json(self.settings_path, self.settings)
        self.disposition_pin = stage.sha(self.disposition_path.read_bytes())

    def run_stage(self):
        return stage.prepare(self.settings_path, self.review_pin, self.disposition_pin)

    def test_unsigned_request_retains_exact_candidate_and_honest_checkbox_states(self):
        result = self.run_stage()
        self.assertEqual(result["state"], "UnsignedRequestWithDeclaredGapsPrepared")
        self.assertEqual(result["verificationStatus"], "GapsDeclared")
        self.assertFalse(result["signatureApplied"])
        self.assertFalse(result["deliveryAuthorized"])
        self.assertFalse(result["deliveryAttempted"])
        self.assertEqual(result["dispositionSha256"], self.disposition_pin)
        self.assertEqual((self.output / "COMPLETE").read_text().strip(), stage.sha((self.output / "request-receipt.json").read_bytes()))
        self.assertEqual({p.name for p in (self.output / "delivery").iterdir()}, {result["packageFileName"], result["attachmentFileName"]})
        pdf = self.output / "delivery" / result["attachmentFileName"]
        self.assertEqual(stage.sha(pdf.read_bytes()), result["attachmentSha256"])
        reader = PdfReader(pdf)
        self.assertEqual(reader.get_fields()["First"]["/V"], "/Yes")
        self.assertEqual(reader.get_fields()["Second"]["/V"], "/Off")
        for field in ("Signature", "Date"):
            self.assertEqual(reader.get_fields()[field].get("/V", ""), "")
        text = "\n".join(page.extract_text() for page in reader.pages)
        self.assertIn("REQUEST FOR REVIEW WITH DECLARED GAPS", text)
        self.assertIn("The developer declined", text)
        self.assertIn("No observation:", text)
        self.assertIn("Only Crestron can decide", text)
        self.assertNotIn("NOT FOR SUBMISSION", text)
        bundle = json.loads((self.output / "bundle-report.json").read_text())
        self.assertFalse(bundle["review"]["validation"]["validationChecksPassed"])
        self.assertEqual((self.output / "evidence.zip").read_bytes(), (self.review / "evidence.zip").read_bytes())
        self.assertNotIn(str(self.root), text)
        with self.assertRaisesRegex(ValueError, "new request output"):
            self.run_stage()

    def test_form_omission_produces_disclosure_only_without_inventing_a_form_or_signature(self):
        self.disposition["attachmentKind"] = "DisclosureOnly"
        self.disposition["omissions"].insert(0, {"id": "officialSelfTestForm", "reason": "The developer declined to supply the official form."})
        self.save()
        result = self.run_stage()
        reader = PdfReader(self.output / "delivery" / result["attachmentFileName"])
        self.assertFalse(reader.get_fields())
        text = "\n".join(page.extract_text() for page in reader.pages)
        self.assertIn("DISCLOSURE REPORT ONLY", text)
        self.assertIn("official form omitted", text)
        self.assertNotIn("checkbox checked", text)
        self.assertNotIn("UNSIGNED REVIEW", text)
        report = json.loads((self.output / "request-form-report.json").read_text())
        self.assertEqual(report["checkedRequirements"], [])
        self.assertEqual(report["verifiedRequirements"], ["first"])
        self.assertFalse(report["officialFormIncluded"])

    def test_every_missing_document_has_exactly_one_reason_and_mode_cannot_be_downgraded(self):
        original = copy.deepcopy(self.disposition)
        mutations = [dict(reviewMode="Complete"), dict(candidateSha256="0" * 64), dict(reviewReceiptSha256="0" * 64),
                     dict(declarationsSha256="0" * 64), dict(attachmentKind="SignedSelfTest"), dict(attachmentKind="DisclosureOnly"),
                     dict(omissions=[]), dict(omissions=[{"id": "signature", "reason": " "}]),
                     dict(omissions=[{"id": "unknown", "reason": "Not supported"}])]
        for mutation in mutations:
            with self.subTest(mutation=mutation):
                self.disposition = {**copy.deepcopy(original), **mutation}
                self.save()
                with patch.object(stage, "run_process") as execute, self.assertRaises(ValueError):
                    self.run_stage()
                execute.assert_not_called()
                self.assertFalse(self.output.exists())

    def test_changed_review_and_disposition_pins_prevent_preparation(self):
        self.disposition_path.write_text(self.disposition_path.read_text() + " ", encoding="utf-8")
        with self.assertRaisesRegex(ValueError, "pinned digest"):
            self.run_stage()
        self.save()
        (self.review / "review-receipt.json").write_text("{}", encoding="utf-8")
        with self.assertRaisesRegex(ValueError, "pinned digest"):
            self.run_stage()
        self.assertFalse(self.output.exists())

    def test_changed_archive_does_not_reach_extraction_or_create_output(self):
        with (self.review / "evidence.zip").open("ab") as archive:
            archive.write(b"changed")
        with patch.object(stage, "extract_validated_archive") as extract, self.assertRaisesRegex(ValueError, "changed"):
            self.run_stage()
        extract.assert_not_called()
        self.assertFalse(self.output.exists())
        self.assertEqual(list(self.root.glob(".submission-request-*")), [])

    def test_unknown_review_schema_is_refused_even_with_a_matching_file_pin(self):
        self.review_receipt["schemaVersion"] = 2
        self.base.f.write_json(self.review / "review-receipt.json", self.review_receipt)
        self.review_pin = stage.sha((self.review / "review-receipt.json").read_bytes())
        (self.review / "COMPLETE").write_text(self.review_pin, encoding="ascii")
        self.disposition["reviewReceiptSha256"] = self.review_pin
        self.save()
        with patch.object(stage, "run_process") as execute, self.assertRaisesRegex(ValueError, "intact completed"):
            self.run_stage()
        execute.assert_not_called()
        self.assertFalse(self.output.exists())

    def test_expired_or_inconsistent_assessment_is_not_relabelled_as_permitted(self):
        original = stage.run_process
        def changed(arguments, timeout):
            result = original(arguments, timeout)
            parsed = json.loads(result.stdout)
            parsed["review"]["assessment"]["verificationStatus"] = "CompleteAgainstInterpretedRequirements"
            return subprocess.CompletedProcess(arguments, 0, json.dumps(parsed).encode(), b"")
        with patch.object(stage, "run_process", changed), self.assertRaisesRegex(ValueError, "differs"):
            self.run_stage()
        self.assertFalse(self.output.exists())

    def test_disposition_changed_during_generation_prevents_completion(self):
        original = stage.write_request
        def changed(*arguments, **options):
            result = original(*arguments, **options)
            self.disposition_path.write_text("{}", encoding="utf-8")
            return result
        with patch.object(stage, "write_request", changed), self.assertRaisesRegex(ValueError, "pinned digest"):
            self.run_stage()
        self.assertFalse(self.output.exists())
        self.assertEqual(list(self.root.glob(".submission-request-*")), [])

    def test_original_external_evidence_can_be_unavailable_after_review(self):
        self.base.f.package.unlink()
        for path in self.base.f.evidence.iterdir():
            if path.is_file():
                path.unlink()
        result = self.run_stage()
        self.assertEqual(result["evidenceVerificationStatus"], "GapsDeclared")

    def test_verified_tests_do_not_hide_a_missing_signature(self):
        self.base.f.observations["observations"].append({**self.base.f.observations["observations"][0], "requirementId": "second.duration"})
        self.base.gaps = []
        self.base.save()
        self.review = self.root / "all-tests-verified"
        self.base.base.settings["output"] = str(self.review)
        self.base.f.write_json(self.base.base.settings_path, self.base.base.settings)
        reviewed = self.base.run_stage()
        self.assertEqual(reviewed["verificationStatus"], "CompleteAgainstInterpretedRequirements")
        self.review_pin = stage.sha((self.review / "review-receipt.json").read_bytes())
        self.settings["reviewDirectory"] = str(self.review)
        self.disposition.update(reviewReceiptSha256=self.review_pin, declarationsSha256=reviewed["declarationsSha256"])
        self.save()
        result = self.run_stage()
        self.assertEqual(result["verificationStatus"], "GapsDeclared")
        self.assertEqual(result["evidenceVerificationStatus"], "CompleteAgainstInterpretedRequirements")
        text = "\n".join(page.extract_text() for page in PdfReader(self.output / "delivery" / result["attachmentFileName"]).pages)
        self.assertIn("verificationStatus: GapsDeclared", text)
        self.assertIn("evidenceVerificationStatus: CompleteAgainstInterpretedRequirements", text)
        self.assertIn("no signature is supplied", text)

    def test_cli_prepares_request_without_disclosing_private_path_on_error(self):
        args = [sys.executable, stage.__file__, "--settings", str(self.settings_path), "--review-sha256", self.review_pin,
                "--disposition-sha256", self.disposition_pin]
        result = subprocess.run(args, capture_output=True, timeout=60)
        self.assertEqual(result.returncode, 0, result.stderr.decode())
        self.assertEqual(json.loads(result.stdout)["state"], "UnsignedRequestWithDeclaredGapsPrepared")
        bad = subprocess.run(args, capture_output=True, timeout=60)
        self.assertEqual(bad.returncode, 1)
        self.assertNotIn(str(self.root).encode(), bad.stderr)

    def test_extractor_rejects_escaping_duplicate_and_link_entries(self):
        for name in ("../escape", "C:/escape", "/escape", "evidence/../escape", "evidence\\escape"):
            with self.subTest(name=name):
                archive = self.root / "bad.zip"
                with zipfile.ZipFile(archive, "w") as writer:
                    entry = zipfile.ZipInfo("safe")
                    entry.filename = name  # Preserve malformed raw backslashes; Windows ZipInfo normally normalizes them.
                    writer.writestr(entry, "private")
                with self.assertRaises(ValueError):
                    stage.extract_validated_archive(archive, self.root / "extraction")

    def test_extractor_rejects_case_collisions_and_symbolic_links(self):
        for kind in ("duplicate", "link"):
            with self.subTest(kind=kind):
                archive = self.root / (kind + ".zip")
                with zipfile.ZipFile(archive, "w") as writer:
                    if kind == "duplicate":
                        writer.writestr("evidence/value", "one")
                        writer.writestr("evidence/VALUE", "two")
                    else:
                        entry = zipfile.ZipInfo("evidence/link")
                        entry.external_attr = 0xA1FF << 16
                        writer.writestr(entry, "../../outside")
                with self.assertRaises(ValueError):
                    stage.extract_validated_archive(archive, self.root / kind)


if __name__ == "__main__":
    unittest.main()
