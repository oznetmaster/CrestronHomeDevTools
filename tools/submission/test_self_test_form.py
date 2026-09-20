# Copyright (c) 2026 Neil Colvin.
# Licensed under the MIT License. See LICENSE in the repository root.
"""Synthetic forms/evidence exercise canonical fields and the actual offline validator."""

import copy
import io
import json
import os
from pathlib import Path
import shutil
import tempfile
import unittest
from zipfile import ZipFile

from pypdf import PdfReader, PdfWriter
from pypdf.generic import NameObject
from reportlab.pdfgen.canvas import Canvas

import self_test_form as forms


class SelfTestFormTests(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        self.root = Path(temporary.name)
        self.template = self.root / "official-fixture.pdf"
        canvas = Canvas(str(self.template), pagesize=(612, 792))
        canvas.drawString(80, 730, "Synthetic official-form fixture; never submitted")
        canvas.acroForm.checkbox(name="First", x=50, y=700, checked=False)
        canvas.showPage()
        canvas.drawString(80, 730, "Synthetic second page")
        canvas.acroForm.checkbox(name="Second", x=50, y=700, checked=False)
        canvas.acroForm.textfield(name="Signature", x=100, y=100, width=200, height=20)
        canvas.acroForm.textfield(name="Date", x=100, y=50, width=200, height=20)
        canvas.save()
        self.source = self.template.read_bytes()
        self.inventory = {"schemaVersion": 1, "templateSha256": forms.sha(self.source), "templatePages": 2,
                          "requirements": [{"id": "first", "field": "First", "label": "First requirement", "page": 1,
                                            "checkedAppearance": ["/Yes"], "minimumObservationSeconds": 0},
                                           {"id": "second", "field": "Second", "label": "Continuous observation", "page": 2,
                                            "checkedAppearance": ["/Yes"], "minimumObservationSeconds": 86400}],
                          "signingFields": [{"field": "Signature", "page": 2}, {"field": "Date", "page": 2}]}
        self.inventory_digest = forms.sha(json.dumps(self.inventory).encode())
        self.policy = {"schemaVersion": 1, "requirements": [
            {"id": "first.a", "minimumDuration": "00:00:00", "allowNotApplicable": False},
            {"id": "first.b", "minimumDuration": "00:00:00", "allowNotApplicable": True},
            {"id": "second.duration", "minimumDuration": "1.00:00:00", "allowNotApplicable": False}]}
        self.policy_path = self.root / "policy.json"
        self.write_json(self.policy_path, self.policy)
        self.package = self.root / "Example_Platform_Test_IP.pkg"
        self.driver_id = "75b3457d-70f3-4c02-940e-22fbd970ef94"
        metadata = {"driverId": self.driver_id, "driverVersion": "1.0.000.0000", "baseModel": "Synthetic test fixture",
                    "manufacturer": "Example", "assemblyFileName": self.package.stem + ".dll",
                    "developer": "Example", "developerContact": {"company": "Example", "email": "support@example.org"},
                    "dependencyGroup": ""}
        with ZipFile(self.package, "w") as archive:
            archive.writestr(self.package.stem + ".dll", b"synthetic; never executed or deployed")
            archive.writestr(self.package.stem + ".dat", json.dumps(metadata))
            archive.writestr(self.package.stem + ".pdf", self.source)
        self.identity = {"packageSha256": forms.sha(self.package.read_bytes()), "sourceCommit": "a" * 40,
                         "policySha256": forms.sha(self.policy_path.read_bytes()), "templateSha256": forms.sha(self.source)}
        self.evidence = self.root / "evidence"
        self.evidence.mkdir()
        evidence_file = self.evidence / "synthetic.txt"
        evidence_file.write_text("Synthetic validator fixture; not hardware evidence.", encoding="utf-8")
        self.observations = {"schemaVersion": 1, "observations": [
            {"requirementId": rule["id"], "identity": self.identity, "outcome": "Passed",
             "startedUtc": "2026-01-01T00:00:00Z", "finishedUtc": "2026-01-02T00:00:00Z",
             "files": [{"relativePath": "synthetic.txt", "sha256": forms.sha(evidence_file.read_bytes())}], "rationale": ""}
            for rule in self.policy["requirements"]]}
        self.mapping = {"schemaVersion": 1, "inventorySha256": self.inventory_digest,
                        "policySha256": self.identity["policySha256"], "requirements": [
                            {"id": "first", "observationIds": ["first.a", "first.b"]},
                            {"id": "second", "observationIds": ["second.duration"]}]}
        self.output = self.root / "fixture.review.pdf"

    @staticmethod
    def write_json(path, value):
        path.write_text(json.dumps(value), encoding="utf-8")

    def decisions(self):
        return forms.decisions(self.inventory, self.inventory_digest, self.mapping, self.policy, self.observations)

    def write(self, rows, draft=False):
        return forms.write_form(self.source, self.inventory, self.output, "SYNTHETIC TEST FIXTURE", "Example Developer", rows, {}, draft)

    def test_first_page_identifies_submission_without_changing_official_content(self):
        self.write(self.decisions())
        original, result = PdfReader(io.BytesIO(self.source)), PdfReader(self.output)
        headings = [item.get_object() for item in result.pages[0]['/Annots']
                    if item.get_object().get('/NM') == 'submission-identification']
        self.assertEqual(len(headings), 1)
        self.assertEqual(headings[0]['/Contents'], 'SYNTHETIC TEST FIXTURE\nDeveloper: Example Developer')
        self.assertTrue(headings[0]['/AP']['/N'].get_object().get_data())
        for before, after in zip(original.pages, result.pages):
            self.assertEqual(before.get_contents().get_data(), after.get_contents().get_data())

    def test_checked_boxes_agree_in_widgets_canonical_fields_and_appearances(self):
        result = self.write(self.decisions())
        reader = PdfReader(self.output)
        self.assertEqual(result["checkedRequirements"], ["first", "second"])
        self.assertFalse(result["submissionReady"])
        for name in ("First", "Second"):
            self.assertEqual(reader.get_fields()[name]["/V"], "/Yes")
        for page in reader.pages:
            for reference in page.get("/Annots", []):
                widget = reference.get_object()
                if widget.get("/FT") == "/Btn":
                    checked = widget["/AP"]["/N"]["/Yes"].get_object().get_data()
                    self.assertNotIn(b" Tf", checked)
                    self.assertIn(b" l S Q", checked)
        forms.inspect_form(reader, self.inventory, {"First": "/Yes", "Second": "/Yes"}, result["officialStartPage"], trailing_pages=result["companionPages"])
        for name in ("Signature", "Date"):
            self.assertEqual(reader.get_fields()[name].get("/V", ""), "")
        self.assertEqual(self.source, self.template.read_bytes())

    def test_numbered_notes_follow_checklist_and_link_both_directions(self):
        report = self.write(self.decisions())
        reader = PdfReader(self.output)
        first_notes = report['notesStartPage']
        self.assertEqual(first_notes, self.inventory['templatePages'])
        self.assertEqual(report['officialStartPage'], 0)
        self.assertIn('1. First requirement', reader.pages[first_notes].extract_text())
        self.assertIn('2. Continuous observation', reader.pages[first_notes].extract_text())
        page_refs = [p.indirect_reference for p in reader.pages]
        forwards, backwards = [], []
        for index, page in enumerate(reader.pages):
            for annotation in page.get('/Annots', []):
                link = annotation.get_object()
                if link.get('/Subtype') != '/Link':
                    continue
                destination = page_refs.index(link['/Dest'][0])
                (forwards if index < first_notes else backwards).append((index,destination))
        self.assertEqual(len(forwards), 2)
        self.assertEqual(len(backwards), 2)
        self.assertTrue(all(dest >= first_notes for _,dest in forwards))
        self.assertEqual(sorted(dest for _,dest in backwards), [0,1])

    def test_draft_has_no_attestations_and_original_pages_are_unchanged(self):
        rows = self.decisions()
        for row in rows:
            row.update(state="NotTested", observationIds=[])
        report = self.write(rows, draft=True)
        self.assertEqual(report["checkedRequirements"], [])
        reader = PdfReader(self.output)
        for i, page in enumerate(PdfReader(self.template).pages):
            actual = reader.pages[i + report["officialStartPage"]]
            self.assertEqual(page.get_contents().get_data(), actual.get_contents().get_data())

    def test_all_applicable_subconditions_pass_with_optional_absence_disclosed(self):
        self.observations["observations"][1].update(outcome="NotApplicable", rationale="This synthetic device has no optional control.")
        rows = self.decisions()
        report = self.write(rows)
        self.assertEqual(report["checkedRequirements"], ["first", "second"])
        self.assertEqual(report["notApplicableRequirements"], [])
        reader = PdfReader(self.output)
        self.assertEqual(reader.get_fields()["First"]["/V"], "/Yes")
        self.assertIn("no optional control", "".join(p.extract_text() for p in reader.pages))

    def test_wholly_non_applicable_item_remains_unchecked(self):
        self.policy['requirements'][0]['allowNotApplicable'] = True
        for row in self.observations['observations'][:2]:
            row.update(outcome='NotApplicable', rationale='This entire synthetic control family is absent.')
        report = self.write(self.decisions())
        self.assertEqual(report['checkedRequirements'], ['second'])
        self.assertEqual(report['notApplicableRequirements'], ['first'])
        reader = PdfReader(self.output)
        self.assertEqual(reader.get_fields()['First']['/V'], '/Off')
        notes = [a.get_object() for page in reader.pages for a in page.get('/Annots', [])
                 if a.get_object().get('/NM', '').startswith('submission-note:')]
        self.assertEqual([a['/Contents'] for a in notes], ['N/A 1', '[2]'])
        self.assertEqual(notes[0]['/NM'], 'submission-note:First')
        self.assertIn(b'(N/A 1) Tj', notes[0]['/AP']['/N'].get_object().get_data())
        self.assertEqual(notes[0]['/F'], 4)  # Printed as well as displayed.
        for i, page in enumerate(PdfReader(self.template).pages):
            self.assertEqual(page.get_contents().get_data(),
                             reader.pages[i + report['officialStartPage']].get_contents().get_data())

    def test_qualified_item_is_labelled_notes_without_a_pass_mark(self):
        rows = self.decisions()
        rows[0].update(state='GapDeclared', rationale='Synthetic missing observation.')
        report = forms.write_form(self.source, self.inventory, self.output, 'SYNTHETIC', 'Example', rows,
            {'reviewMode': 'DeclaredGaps', 'verificationStatus': 'GapsDeclared', 'declarationsSha256': 'a' * 64},
            False, declared_gaps=True)
        reader = PdfReader(self.output)
        notes = [a.get_object() for page in reader.pages for a in page.get('/Annots', [])
                 if a.get_object().get('/NM', '').startswith('submission-note:')]
        self.assertEqual(reader.get_fields()['First']['/V'], '/Off')
        self.assertEqual([a['/Contents'] for a in notes], ['[1]', '[2]'])
        self.assertEqual(report['checkedRequirements'], ['second'])

    def test_failed_partial_and_missing_observations_are_rejected(self):
        for outcome in ("Failed", "Partial", "NotTested", "Inconclusive"):
            with self.subTest(outcome=outcome):
                self.observations["observations"][0]["outcome"] = outcome
                with self.assertRaisesRegex(ValueError, "Incomplete"):
                    self.decisions()
        self.observations["observations"].pop()
        with self.assertRaisesRegex(ValueError, "cover every"):
            self.decisions()

    def test_every_checkbox_and_every_policy_requirement_must_be_mapped(self):
        original = copy.deepcopy(self.mapping)
        self.mapping["requirements"].pop()
        with self.assertRaisesRegex(ValueError, "cover every"):
            self.decisions()
        self.mapping = original
        self.mapping["requirements"][0]["observationIds"] = ["first.a"]
        with self.assertRaisesRegex(ValueError, "unused"):
            self.decisions()

    def test_observation_cannot_be_reused_for_another_checkbox(self):
        self.mapping["requirements"][1]["observationIds"].append("first.a")
        with self.assertRaisesRegex(ValueError, "distinct"):
            self.decisions()

    def test_24_hour_item_cannot_map_to_a_short_duration_policy(self):
        self.policy["requirements"][-1]["minimumDuration"] = "23:59:59"
        with self.assertRaisesRegex(ValueError, "official observation duration"):
            self.decisions()

    def test_changed_inventory_pin_and_duplicate_mappings_are_rejected(self):
        self.mapping["inventorySha256"] = "0" * 64
        with self.assertRaisesRegex(ValueError, "pinned inventory"):
            self.decisions()
        self.mapping["inventorySha256"] = self.inventory_digest
        self.mapping["requirements"].append(copy.deepcopy(self.mapping["requirements"][0]))
        with self.assertRaisesRegex(ValueError, "Duplicate"):
            self.decisions()

    def test_existing_signature_or_checked_template_is_not_overwritten(self):
        for name, value in (("Signature", "Existing signer"), ("First", NameObject("/Yes"))):
            with self.subTest(field=name):
                writer = PdfWriter(clone_from=io.BytesIO(self.source))
                writer.update_page_form_field_values(None, {name: value}, auto_regenerate=False)
                data = io.BytesIO()
                writer.write(data)
                with self.assertRaises(ValueError):
                    forms.inspect_form(PdfReader(data), self.inventory)

    def test_orphaned_or_mismatched_canonical_fields_are_not_repaired_by_guessing(self):
        reader = PdfReader(io.BytesIO(self.source))
        reader.trailer["/Root"]["/AcroForm"]["/Fields"].pop()
        with self.assertRaises(ValueError):
            forms.inspect_form(reader, self.inventory)

    def test_existing_output_is_preserved(self):
        self.output.write_bytes(b"keep")
        with self.assertRaises(ValueError):
            self.write(self.decisions())
        self.assertEqual(self.output.read_bytes(), b"keep")

    def test_draft_cannot_include_passing_claims(self):
        with self.assertRaisesRegex(ValueError, "incompatible"):
            self.write(self.decisions(), draft=True)
        self.assertFalse(self.output.exists())

    def test_real_dotnet_validator_accepts_bound_fixture_and_rejects_changed_evidence(self):
        dotnet = os.environ.get("SUBMISSION_TEST_DOTNET") or shutil.which("dotnet")
        if not dotnet and os.name == "nt":
            dotnet = str(Path(os.environ.get("ProgramFiles", "C:/Program Files")) / "dotnet/dotnet.exe")
        validator = Path(os.environ.get("SUBMISSION_TEST_VALIDATOR", str(Path(__file__).resolve().parents[2] /
                         "CrestronHomeDevTools.Console/bin/Release/net10.0/CrestronHomeDevTools.Console.dll")))
        self.assertTrue(validator.is_file(), "Build the DevTools console in Release before running form integration tests")
        candidate = {"schemaVersion": 1, "identity": self.identity, "packageRequirements": {"driverId": self.driver_id,
                     "driverVersion": "1.0.000.0000", "kind": "NewDriver", "developerFilenameToken": "Example",
                     "publicSupportEmail": "support@example.org"}}
        candidate_path, mapping_path, observations_path = [self.root / name for name in ("candidate.json", "mapping.json", "observations.json")]
        for path, value in ((candidate_path, candidate), (mapping_path, self.mapping), (observations_path, self.observations)):
            self.write_json(path, value)
        args = (self.inventory, self.inventory_digest, mapping_path, forms.sha(mapping_path.read_bytes()),
                candidate_path, forms.sha(candidate_path.read_bytes()), self.policy_path, observations_path,
                self.package, self.template, self.evidence, dotnet, validator)
        rows, identity, validation_json = forms.validate_evidence(*args)
        report = forms.write_form(self.source, self.inventory, self.output, "SYNTHETIC TEST FIXTURE", "Example Developer", rows, identity, False)
        self.assertEqual(report["checkedRequirements"], ["first", "second"])
        self.assertEqual(report["identity"]["packageSha256"], self.identity["packageSha256"])
        self.assertEqual(forms.sha(validation_json.encode("utf-8")), identity["validationReportSha256"])
        (self.evidence / "synthetic.txt").write_text("Changed after validation", encoding="utf-8")
        with self.assertRaisesRegex(ValueError, "validation failed"):
            forms.validate_evidence(*args)


if __name__ == "__main__":
    unittest.main()
