# Copyright (c) 2026 Neil Colvin.
# Licensed under the MIT License. See LICENSE in the repository root.
"""Keep the independently selected Android run inventory complete at review."""

import copy
import json
import unittest

from review_android import audit_runs
import test_audit_android as fixtures


class ReviewAndroidTests(unittest.TestCase):
    def setUp(self):
        self.fixtures = []
        for index in range(2):
            fixture = fixtures.AndroidEvidenceTests()
            fixture.setUp()
            self.addCleanup(fixture.doCleanups)
            fixture.run = ("a" if index == 0 else "b") * 32
            for name in ("context.json", "completion.json", "producer-pin.json", "coverage.json", "check/observation.json"):
                value = json.loads((fixture.root / name).read_bytes())
                value["RunId"] = fixture.run
                fixture.write(name, value)
            self.fixtures.append(fixture)
        first = self.fixtures[0]
        self.settings = {"candidate": str(first.root / "candidate.json"), "androidEvidence": [
            {"runId": f.run, "path": str(f.root)} for f in self.fixtures]}
        self.pins = {"schemaVersion": 1, "candidateSha256": first.candidate_hash, "runs": [
            {"runId": f.run, "assembly": f.assembly, "assemblySha256": f.assembly_hash,
             "discoverySha256": f.discovery_hash, "producerManifestSha256": f.manifest_hash} for f in self.fixtures]}
        self.path = first.root / "independent-pins.json"

    def run_audit(self):
        first = self.fixtures[0]
        pin = first.write(self.path.name, self.pins)
        return audit_runs(self.settings, self.path, pin, first.candidate_hash)

    def test_every_selected_run_is_audited_regardless_of_location_order(self):
        self.settings["androidEvidence"].reverse()
        result = self.run_audit()
        self.assertEqual([run["runId"] for run in result["runs"]], [f.run for f in self.fixtures])
        self.assertFalse(result["producerAuthenticated"])

    def test_missing_duplicate_or_unselected_runs_are_rejected(self):
        original = copy.deepcopy(self.settings["androidEvidence"])
        for invalid in ([], original[:1], [original[0], original[0]],
                        [original[0], {**original[1], "runId": "c" * 32}]):
            with self.subTest(locations=invalid), self.assertRaises(ValueError):
                self.settings["androidEvidence"] = invalid
                self.run_audit()

    def test_duplicate_or_empty_trusted_run_inventory_is_rejected(self):
        original = copy.deepcopy(self.pins["runs"])
        for invalid in ([], [original[0], original[0]], None):
            with self.subTest(inventory=invalid), self.assertRaises(ValueError):
                self.pins["runs"] = invalid
                self.run_audit()

    def test_bad_second_run_cannot_hide_behind_a_passing_first(self):
        (self.fixtures[1].root / "check/screen.png").write_bytes(b"Changed second run")
        with self.assertRaises(ValueError):
            self.run_audit()

    def test_other_candidate_and_unknown_fields_are_rejected(self):
        original = copy.deepcopy(self.pins)
        for invalid in ({**original, "candidateSha256": "0" * 64}, {**original, "approved": True}):
            with self.subTest(pins=invalid), self.assertRaises(ValueError):
                self.pins = invalid
                self.run_audit()

    def test_private_paths_are_only_locations_not_sources_of_trusted_pins(self):
        self.settings["androidEvidence"][0]["path"] = "relative/evidence"
        with self.assertRaises(ValueError):
            self.run_audit()
        self.settings["androidEvidence"][0]["path"] = str(self.fixtures[0].root)
        self.run_audit()
        with self.assertRaises(ValueError):
            audit_runs(self.settings, self.path, "0" * 64, self.fixtures[0].candidate_hash)


if __name__ == "__main__":
    unittest.main()
