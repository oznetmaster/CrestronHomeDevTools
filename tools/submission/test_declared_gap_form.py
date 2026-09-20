# Copyright (c) 2026 Neil Colvin.
# Licensed under the MIT License. See LICENSE in the repository root.
"""Candidate-bound gap assessments must never check unsupported official form items."""

import copy
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import unittest

from pypdf import PdfReader

import self_test_form as forms
import test_self_test_form as fixtures


class DeclaredGapFormTests(unittest.TestCase):
    write_json = staticmethod(fixtures.SelfTestFormTests.write_json)

    def setUp(self):
        fixtures.SelfTestFormTests.setUp(self)
        self.candidate = {"schemaVersion": 1, "identity": self.identity, "packageRequirements": {
            "driverId": self.driver_id, "driverVersion": "1.0.000.0000", "kind": "NewDriver",
            "developerFilenameToken": "Example", "publicSupportEmail": "support@example.org"}}
        self.candidate_path = self.root / "candidate.json"
        self.mapping_path = self.root / "mapping.json"
        self.observations_path = self.root / "observations.json"
        self.declarations_path = self.root / "declarations.json"
        self.inventory_path = self.root / "inventory.json"
        self.dotnet = os.environ.get("SUBMISSION_TEST_DOTNET") or shutil.which("dotnet")
        self.validator = Path(os.environ.get("SUBMISSION_TEST_VALIDATOR", str(Path(__file__).resolve().parents[2] /
                              "CrestronHomeDevTools.Console/bin/Release/net10.0/CrestronHomeDevTools.Console.dll")))
        self.assertTrue(self.validator.is_file(), "Build the current DevTools console before running integration tests")
        self.gaps = []

    def prepare(self):
        for path, value in ((self.candidate_path, self.candidate), (self.mapping_path, self.mapping),
                            (self.observations_path, self.observations), (self.inventory_path, self.inventory)):
            self.write_json(path, value)
        self.write_json(self.declarations_path, {"schemaVersion": 1, "identity": self.identity,
                                               "mode": "DeclaredGaps", "declarations": self.gaps})
        self.declarations_digest = forms.sha(self.declarations_path.read_bytes())

    def validate(self):
        self.prepare()
        return forms.validate_evidence(self.inventory, self.inventory_digest, self.mapping_path,
                                      forms.sha(self.mapping_path.read_bytes()), self.candidate_path,
                                      forms.sha(self.candidate_path.read_bytes()), self.policy_path, self.observations_path,
                                      self.package, self.template, self.evidence, self.dotnet, self.validator,
                                      declarations_path=self.declarations_path, declarations_digest=self.declarations_digest)

    def write(self, rows, identity):
        return forms.write_form(self.source, self.inventory, self.output, "SYNTHETIC DECLARED-GAP REVIEW",
                                "Example Developer", rows, identity, False, declared_gaps=True)

    def test_missing_duration_is_disclosed_and_only_fully_verified_item_is_checked(self):
        self.observations["observations"].pop()
        self.gaps = [{"requirementId": "second.duration", "reason": "The developer declined the observation period."}]
        rows, identity, raw = self.validate()
        report = self.write(rows, identity)
        self.assertEqual(report["checkedRequirements"], ["first"])
        self.assertEqual(report["declaredGapRequirements"], ["second"])
        self.assertEqual(report["verificationStatus"], "GapsDeclared")
        self.assertFalse(json.loads(raw)["validation"]["validationChecksPassed"])
        reader = PdfReader(self.output)
        forms.inspect_form(reader, self.inventory, {"First": "/Yes", "Second": "/Off"}, report["companionPages"])
        text = "\n".join(page.extract_text() for page in reader.pages)
        self.assertIn("No observation: The developer declined", text)
        self.assertIn("our interpretation", text)
        self.assertIn("Nothing in this report implies", text)
        self.assertNotIn("second.duration", text)
        self.assertNotIn("first.a", text)
        for name in ("Signature", "Date"):
            self.assertEqual(reader.get_fields()[name].get("/V", ""), "")

    def test_failed_partial_inconclusive_untested_and_missing_outcomes_remain_distinct(self):
        for outcome in ("Failed", "Partial", "Inconclusive", "NotTested", None):
            with self.subTest(outcome=outcome):
                original = copy.deepcopy(self.observations)
                if outcome is None:
                    self.observations["observations"].pop(0)
                else:
                    self.observations["observations"][0]["outcome"] = outcome
                self.gaps = [{"requirementId": "first.a", "reason": "Equipment unavailable for further testing."}]
                rows, _, raw = self.validate()
                self.assertEqual(rows[0]["state"], "GapDeclared")
                self.assertEqual(rows[1]["state"], "Passed")
                self.assertEqual(json.loads(raw)["assessment"]["requirements"][0]["observedOutcome"], outcome)
                self.assertIn("Verified passing portions: 1", rows[0]["rationale"])
                self.observations = original

    def test_short_claimed_pass_is_a_gap_not_a_checked_duration(self):
        self.observations["observations"][-1]["startedUtc"] = "2026-01-01T12:00:00Z"
        self.gaps = [{"requirementId": "second.duration", "reason": "Only half the required time was observed."}]
        rows, identity, _ = self.validate()
        report = self.write(rows, identity)
        self.assertEqual(report["checkedRequirements"], ["first"])
        self.assertIn("Claimed pass with incomplete verification", rows[1]["rationale"])

    def test_shared_explanation_is_printed_once_without_hiding_scoped_gaps(self):
        self.observations["observations"][0]["outcome"] = "Partial"
        self.observations["observations"][1]["outcome"] = "Partial"
        reason = "Only the documented portion could be observed."
        self.gaps = [{"requirementId": identifier, "reason": reason} for identifier in ("first.a", "first.b")]
        rows, identity, raw = self.validate()
        self.assertEqual(rows[0]["rationale"].count(reason), 1)
        self.assertIn("Declared gaps: 2", rows[0]["rationale"])
        assessed = json.loads(raw)["assessment"]["requirements"]
        self.assertEqual(sum(item["status"] == "GapDeclared" for item in assessed), 2)
        report = self.write(rows, identity)
        self.assertEqual(report["declaredGapRequirements"], ["first"])
        self.assertEqual(PdfReader(self.output).get_fields()["First"]["/V"], "/Off")

    def test_empty_observations_require_every_gap_and_leave_all_boxes_unchecked(self):
        self.observations["observations"] = []
        self.gaps = [{"requirementId": rule["id"], "reason": "Hardware unavailable."} for rule in self.policy["requirements"]]
        rows, identity, _ = self.validate()
        report = self.write(rows, identity)
        self.assertEqual(report["checkedRequirements"], [])
        self.assertEqual(report["declaredGapRequirements"], ["first", "second"])
        self.output.unlink()
        self.gaps.pop()
        with self.assertRaisesRegex(ValueError, "validation failed"):
            self.validate()
        self.assertFalse(self.output.exists())

    def test_invalid_evidence_cannot_be_rendered_as_explained_gap(self):
        self.gaps = [{"requirementId": rule["id"], "reason": "Trying to excuse corruption."} for rule in self.policy["requirements"]]
        (self.evidence / "synthetic.txt").write_text("Changed measurement", encoding="utf-8")
        with self.assertRaisesRegex(ValueError, "validation failed"):
            self.validate()
        self.assertFalse(self.output.exists())

    def test_mapping_must_cover_all_official_items_and_scopes_even_when_missing(self):
        self.observations["observations"].pop()
        self.gaps = [{"requirementId": "second.duration", "reason": "Not observed."}]
        self.mapping["requirements"].pop()
        with self.assertRaisesRegex(ValueError, "cover every"):
            self.validate()

    def test_minimum_official_duration_cannot_be_reduced_in_gaps_mode(self):
        rows, _, raw = self.validate()
        self.assertEqual(len(rows), 2)
        self.policy["requirements"][-1]["minimumDuration"] = "00:00:01"
        with self.assertRaisesRegex(ValueError, "official observation duration"):
            forms.decisions(self.inventory, self.inventory_digest, self.mapping, self.policy, self.observations,
                            json.loads(raw)["assessment"])

    def test_non_applicability_is_not_renamed_a_gap(self):
        self.observations["observations"][1].update(outcome="NotApplicable", rationale="No optional control exists.")
        self.observations["observations"].pop()
        self.gaps = [{"requirementId": "second.duration", "reason": "Not observed."}]
        rows, identity, _ = self.validate()
        report = self.write(rows, identity)
        self.assertEqual(report["notApplicableRequirements"], [])
        self.assertEqual(report["declaredGapRequirements"], ["second"])
        self.assertEqual(report["checkedRequirements"], ["first"])

    def test_assessment_cannot_hide_scope_failures_or_change_original_outcomes(self):
        _, _, raw = self.validate()
        for defect in ("missing", "outcome", "invalid", "summary"):
            with self.subTest(defect=defect):
                assessment = json.loads(raw)["assessment"]
                if defect == "missing": assessment["requirements"].pop()
                if defect == "outcome": assessment["requirements"][0]["observedOutcome"] = "Failed"
                if defect == "invalid": assessment["requirements"][0]["status"] = "InvalidEvidence"
                if defect == "summary": assessment["verificationStatus"] = "GapsDeclared"
                with self.assertRaises(ValueError):
                    forms.decisions(self.inventory, self.inventory_digest, self.mapping, self.policy, self.observations, assessment)

    def test_declared_gap_form_cannot_be_prepared_as_a_complete_form(self):
        self.observations["observations"].pop()
        self.gaps = [{"requirementId": "second.duration", "reason": "Not observed."}]
        rows, identity, _ = self.validate()
        for signing, gaps in ((True, False), (False, False)):
            with self.subTest(signing=signing, gaps=gaps):
                with self.assertRaises(ValueError):
                    forms.write_form(self.source, self.inventory, self.output, "Synthetic", "Example", rows, identity,
                                     False, signing_copy=signing, declared_gaps=gaps)
                self.assertFalse(self.output.exists())

    def test_command_mode_evaluates_actual_files_and_rejects_declared_gaps_in_other_modes(self):
        self.observations["observations"].pop()
        self.gaps = [{"requirementId": "second.duration", "reason": "Not observed."}]
        self.prepare()
        args = [sys.executable, str(Path(forms.__file__)), "declared-gaps"]
        values = {"template": self.template, "inventory": self.inventory_path, "inventory-sha256": self.inventory_digest,
                  "output": self.output, "title": "Synthetic CLI review", "author": "Example",
                  "report": self.root / "report.json", "mapping": self.mapping_path,
                  "mapping-sha256": forms.sha(self.mapping_path.read_bytes()), "candidate": self.candidate_path,
                  "candidate-sha256": forms.sha(self.candidate_path.read_bytes()), "policy": self.policy_path,
                  "observations": self.observations_path, "package": self.package, "evidence": self.evidence,
                  "dotnet": self.dotnet, "validator": self.validator, "declarations": self.declarations_path,
                  "declarations-sha256": self.declarations_digest}
        for key, value in values.items(): args.extend(("--" + key, str(value)))
        result = subprocess.run(args, capture_output=True, text=True, timeout=120)
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(json.loads(result.stdout)["declaredGapRequirements"], ["second"])
        self.output.unlink()
        (self.root / "report.json").unlink()
        args[2] = "from-evidence"
        result = subprocess.run(args, capture_output=True, text=True, timeout=120)
        self.assertNotEqual(result.returncode, 0)
        self.assertIn("explicit declared-gaps mode", result.stderr)
        self.assertFalse(self.output.exists())

    def test_long_official_item_explanation_continues_across_pages_without_losing_text(self):
        self.observations["observations"].pop()
        reason = "\n".join(f"Explanation paragraph {index:03}: Required equipment was unavailable." for index in range(120))
        self.gaps = [{"requirementId": "second.duration", "reason": reason}]
        rows, identity, _ = self.validate()
        report = self.write(rows, identity)
        self.assertGreater(report["companionPages"], 2)
        reader = PdfReader(self.output)
        text = "\n".join(page.extract_text() for page in reader.pages)
        for index in range(120): self.assertIn(f"Explanation paragraph {index:03}", text)
        forms.inspect_form(reader, self.inventory, {"First": "/Yes", "Second": "/Off"}, report["companionPages"])


if __name__ == "__main__":
    unittest.main()
