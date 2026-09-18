# Copyright (c) 2026 Neil Colvin.
# Licensed under the MIT License. See LICENSE in the repository root.
"""Reject mixed, incomplete and changed candidate evidence before review."""

import copy
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

import audit_android
from audit_android import audit, Evidence
from build_help import sha


class AndroidEvidenceTests(unittest.TestCase):
    def setUp(self):
        self.scratch = tempfile.TemporaryDirectory()
        self.addCleanup(self.scratch.cleanup)
        self.root = Path(self.scratch.name)
        self.run = "a" * 32
        self.assembly = "Example.AndroidTests.dll"
        self.identity = {"packageSha256": "b" * 64, "sourceCommit": "c" * 40,
                         "policySha256": "d" * 64, "templateSha256": "e" * 64}
        self.candidate = {"schemaVersion": 1, "identity": self.identity, "packageRequirements": {
            "driverId": "12345678-1234-1234-1234-123456789012", "driverVersion": "1.2.003.0000",
            "kind": "NewDriver", "developerFilenameToken": "Example", "publicSupportWebsite": "https://example.org"}}
        self.candidate_hash = self.write("candidate.json", self.candidate)
        self.context = {"SchemaVersion": 1, "RunId": self.run, "PackageSha256": self.identity["packageSha256"],
                        "ReleaseSourceCommit": self.identity["sourceCommit"], "SourceSha256": "f" * 64,
                        "DriverGuid": self.candidate["packageRequirements"]["driverId"], "DriverVersion": "1.2.003.0000", "InstalledDriverId": 123}
        self.write("context.json", self.context)
        self.write("completion.json", {"SchemaVersion": 1, "RunId": self.run, "PackageSha256": self.identity["packageSha256"], "RestorationConfirmed": True})
        (self.root / "assembly").mkdir()
        (self.root / "assembly" / self.assembly).write_bytes(b"Synthetic producer, never a hardware acceptance claim")
        self.assembly_hash = sha((self.root / "assembly" / self.assembly).read_bytes())
        (self.root / "assembly/nunit.framework.dll").write_bytes(b"Synthetic pinned framework dependency")
        (self.root / "assembly/Example.AndroidTests.deps.json").write_text("{}", encoding="utf-8")
        self.manifest = {"schemaVersion": 1, "files": [
            {"relativePath": path.name, "sha256": sha(path.read_bytes())}
            for path in sorted((self.root / "assembly").iterdir())]}
        self.manifest_hash = self.write("producer-manifest.json", self.manifest)
        self.discovery = ('<NUnitXml><test-run testcasecount="2" runstate="Runnable">'
                          '<test-suite type="Assembly" name="Example.AndroidTests.dll" runstate="Runnable">'
                          '<test-case id="1" fullname="Example.Fixture.Inspect" runstate="Runnable"/>'
                          '<test-case id="2" fullname="Example.Fixture.Inspect" runstate="Runnable"/>'
                          '</test-suite></test-run></NUnitXml>')
        self.discovery_hash = sha(self.discovery.encode())
        self.pin = {"SchemaVersion": 1, "RunId": self.run, "PackageSha256": self.identity["packageSha256"],
                    "ProducerManifestSha256": self.manifest_hash, "DiscoverySha256": self.discovery_hash}
        self.write("producer-pin.json", self.pin)
        (self.root / "discovery.dump").write_text(self.discovery, encoding="utf-8")
        self.coverage = {"RunId": self.run, "PackageSha256": self.identity["packageSha256"], "ReleaseSourceCommit": self.identity["sourceCommit"],
                         "DiscoverySha256": self.discovery_hash, "ProducerManifestSha256": self.manifest_hash,
                         "ExpectedTests": ["Example.Fixture.Inspect"] * 2,
                         "Results": {"Passed": 2, "Failed": 0, "Skipped": 0, "Complete": True, "MeetsGate": True}}
        self.write("coverage.json", self.coverage)
        self.trx = '''<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
          <Times start="2026-01-01T10:00:00Z" finish="2026-01-01T10:01:00Z"/>
          <TestDefinitions><UnitTest id="1" name="Inspect"><TestMethod className="Example.Fixture" codeBase="C:\\CI\\Example.AndroidTests.dll" adapterTypeName="executor://nunit3testexecutor/"/></UnitTest>
          <UnitTest id="2" name="Inspect"><TestMethod className="Example.Fixture" codeBase="C:\\CI\\Example.AndroidTests.dll" adapterTypeName="executor://nunit3testexecutor/"/></UnitTest></TestDefinitions>
          <Results><UnitTestResult testId="1" executionId="a" outcome="Passed" startTime="2026-01-01T10:00:01Z" endTime="2026-01-01T10:00:20Z"/>
          <UnitTestResult testId="2" executionId="b" outcome="Passed" startTime="2026-01-01T10:00:21Z" endTime="2026-01-01T10:00:40Z"/></Results>
          <ResultSummary outcome="Completed"><Counters total="2" passed="2" failed="0"/></ResultSummary></TestRun>'''
        (self.root / "TestResult.trx").write_text(self.trx, encoding="utf-8")
        (self.root / "check").mkdir()
        (self.root / "check/hierarchy.xml").write_bytes(b"<hierarchy/>")
        (self.root / "check/screen.png").write_bytes(b"Synthetic screenshot")
        self.capture = {**self.context, "CheckId": "check", "StartedUtc": "2026-01-01T10:00:10Z", "FinishedUtc": "2026-01-01T10:00:12Z",
                        "HierarchySha256": sha(b"<hierarchy/>"), "ScreenshotSha256": sha(b"Synthetic screenshot"), "Outcome": "Passed"}
        self.write("check/observation.json", self.capture)

    def write(self, relative, value):
        data = json.dumps(value).encode()
        (self.root / relative).write_bytes(data)
        return sha(data)

    def audit(self):
        return audit(self.root / "candidate.json", self.candidate_hash, self.root, self.run,
                     self.assembly, self.assembly_hash, self.discovery_hash, self.manifest_hash)

    def test_retains_each_input_digest_without_asserting_official_coverage(self):
        report = self.audit()
        self.assertEqual(2, report["executedTests"])
        self.assertEqual(1, report["retainedCaptures"])
        self.assertFalse(report["submissionReady"])
        self.assertFalse(report["producerAuthenticated"])
        self.assertEqual([], report["officialRequirementsSatisfied"])
        self.assertTrue(report["producerInventoryPinned"])
        self.assertEqual(3, report["producerFiles"])
        self.assertEqual(self.manifest_hash, report["producerManifestSha256"])
        self.assertEqual(13, len(report["files"]))
        for item in report["files"]:
            self.assertEqual(sha((self.root / item["relativePath"]).read_bytes()), item["sha256"])

    def test_independent_pins_cannot_be_replaced_by_worker_values(self):
        for field in ("candidate_hash", "assembly_hash", "discovery_hash", "manifest_hash"):
            with self.subTest(field=field), patch.object(self, field, "0" * 64), self.assertRaises(ValueError):
                self.audit()

    def test_modified_sibling_dependency_is_rejected(self):
        (self.root / "assembly/nunit.framework.dll").write_bytes(b"Changed dependency beside the unchanged test assembly")
        with self.assertRaises(ValueError):
            self.audit()

    def test_runtime_settings_and_missing_or_unlisted_files_are_rejected(self):
        dependency = self.root / "assembly/Example.AndroidTests.deps.json"
        original = dependency.read_bytes()
        for operation in ("changed", "missing", "unlisted"):
            with self.subTest(operation=operation):
                if operation == "changed":
                    dependency.write_bytes(b'{"changed":true}')
                elif operation == "missing":
                    dependency.unlink()
                else:
                    (self.root / "assembly/unlisted.dll").write_bytes(b"Unreviewed dependency")
                with self.assertRaises(ValueError):
                    self.audit()
                dependency.write_bytes(original)
                (self.root / "assembly/unlisted.dll").unlink(missing_ok=True)

    def test_manifest_cannot_be_replaced_alongside_changed_dependency(self):
        (self.root / "assembly/nunit.framework.dll").write_bytes(b"Replacement")
        manifest = copy.deepcopy(self.manifest)
        next(pin for pin in manifest["files"] if pin["relativePath"] == "nunit.framework.dll")["sha256"] = sha(b"Replacement")
        self.write("producer-manifest.json", manifest)
        with self.assertRaisesRegex(ValueError, "independent pin"):
            self.audit()

    def test_duplicate_ambiguous_and_escaping_manifest_paths_fail_even_with_matching_pin(self):
        for name in ("../context.json", "C:/outside.dll", "/outside.dll", "folder\\dependency.dll", "folder//dependency.dll",
                     "folder/./dependency.dll", "nunit.framework.dll.", "nunit.framework.dll ", "nunit.framework.dll", "NUNIT.FRAMEWORK.DLL"):
            manifest = copy.deepcopy(self.manifest)
            manifest["files"].append({"relativePath": name, "sha256": "a" * 64})
            self.manifest_hash = self.write("producer-manifest.json", manifest)
            with self.subTest(name=name), self.assertRaises(ValueError):
                self.audit()

    def test_nested_dependencies_are_retained_and_audited(self):
        folder = self.root / "assembly/fr"
        folder.mkdir()
        (folder / "Example.resources.dll").write_bytes(b"Synthetic satellite resource")
        self.manifest["files"].append({"relativePath": "fr/Example.resources.dll", "sha256": sha(b"Synthetic satellite resource")})
        self.manifest_hash = self.write("producer-manifest.json", self.manifest)
        self.pin["ProducerManifestSha256"] = self.manifest_hash
        self.write("producer-pin.json", self.pin)
        self.coverage["ProducerManifestSha256"] = self.manifest_hash
        self.write("coverage.json", self.coverage)
        report = self.audit()
        self.assertEqual(4, report["producerFiles"])
        self.assertIn("assembly/fr/Example.resources.dll", {f["relativePath"] for f in report["files"]})

    def test_coordinator_receipt_must_match_independent_run_and_producer_pins(self):
        for field, value in (("SchemaVersion", True), ("RunId", "0" * 32), ("PackageSha256", "0" * 64),
                             ("ProducerManifestSha256", "0" * 64), ("DiscoverySha256", "0" * 64)):
            pin = {**self.pin, field: value}
            self.write("producer-pin.json", pin)
            with self.subTest(field=field), self.assertRaisesRegex(ValueError, "Coordinator producer receipt"):
                self.audit()

    def test_redirected_producer_subdirectory_is_rejected_before_traversal(self):
        (self.root / "assembly/redirected").mkdir()
        original = Path.is_junction
        with patch.object(Path, "is_junction", lambda path: path.name == "redirected" or original(path)), self.assertRaisesRegex(ValueError, "junction"):
            self.audit()

    def test_missing_or_empty_producer_manifest_is_rejected(self):
        (self.root / "producer-manifest.json").unlink()
        with self.assertRaises(ValueError):
            self.audit()
        self.manifest_hash = self.write("producer-manifest.json", {"schemaVersion": 1, "files": []})
        with self.assertRaises(ValueError):
            self.audit()

    def test_debug_candidate_rejected_even_when_all_records_agree(self):
        self.candidate["packageRequirements"]["driverVersion"] = "1.2.003.0004"
        self.candidate_hash = self.write("candidate.json", self.candidate)
        self.context["DriverVersion"] = self.capture["DriverVersion"] = "1.2.003.0004"
        self.write("context.json", self.context)
        self.write("check/observation.json", self.capture)
        with self.assertRaisesRegex(ValueError, "Debug"):
            self.audit()

    def test_rejects_missing_or_wrong_release_run_identity(self):
        for field, value in (("ReleaseSourceCommit", None), ("RunId", "1" * 32), ("PackageSha256", "0" * 64),
                             ("InstalledDriverId", True), ("DriverVersion", "1.2.3.4")):
            with self.subTest(field=field):
                context = {**self.context, field: value}
                self.write("context.json", context)
                with self.assertRaises(ValueError):
                    self.audit()

    def test_restoration_must_match_this_run(self):
        for change in ({"RestorationConfirmed": False}, {"RunId": "1" * 32}, {"PackageSha256": "0" * 64}):
            self.write("completion.json", {"SchemaVersion": 1, "RunId": self.run, "PackageSha256": self.identity["packageSha256"],
                                           "RestorationConfirmed": True, **change})
            with self.subTest(change=change), self.assertRaises(ValueError):
                self.audit()

    def test_rejects_changed_capture_identity_time_or_bytes(self):
        for change in ({"RunId": "1" * 32}, {"ReleaseSourceCommit": None}, {"PackageSha256": "0" * 64},
                       {"CheckId": "other"}, {"Outcome": "Failed"}, {"ScreenshotSha256": "0" * 64},
                       {"StartedUtc": "2025-01-01T10:00:10Z"}, {"FinishedUtc": "2026-01-01T10:00:09Z"}):
            self.write("check/observation.json", {**self.capture, **change})
            with self.subTest(change=change), self.assertRaises(ValueError):
                self.audit()

    def test_interrupted_capture_cannot_be_silently_ignored(self):
        (self.root / "unfinished").mkdir()
        (self.root / "unfinished/hierarchy.xml").write_bytes(b"<hierarchy/>")
        with self.assertRaises(ValueError):
            self.audit()

    def test_summary_does_not_override_skips_duplicates_missing_or_wrong_tests(self):
        for before, after in (("outcome=\"Passed\"", "outcome=\"NotExecuted\""), ("executionId=\"b\"", "executionId=\"a\""),
                              ("className=\"Example.Fixture\"", "className=\"Other.Fixture\""),
                              ("adapterTypeName=\"executor://nunit3testexecutor/\"", "adapterTypeName=\"other\""),
                              ("passed=\"2\"", "passed=\"1\""), ("outcome=\"Completed\"", "outcome=\"Aborted\""),
                              ("testId=\"2\"", "testId=\"missing\"")):
            (self.root / "TestResult.trx").write_text(self.trx.replace(before, after, 1), encoding="utf-8")
            with self.subTest(before=before), self.assertRaises(ValueError):
                self.audit()

    def test_incomplete_coverage_and_multiple_result_files_fail(self):
        coverage = copy.deepcopy(self.coverage)
        coverage["Results"]["Complete"] = False
        self.write("coverage.json", coverage)
        with self.assertRaises(ValueError):
            self.audit()
        self.write("coverage.json", self.coverage)
        (self.root / "TestResult-second.trx").write_text(self.trx)
        with self.assertRaises(ValueError):
            self.audit()

    def test_xml_dtd_rejected(self):
        (self.root / "TestResult.trx").write_text('<!DOCTYPE TestRun [<!ENTITY x "secret">]>' + self.trx)
        with self.assertRaises(ValueError):
            self.audit()

    def test_paths_and_junctions_are_rejected(self):
        evidence = Evidence(self.root)
        for relative in ("../candidate.json", "/context.json", "C:/context.json", "check\\screen.png"):
            with self.subTest(relative=relative), self.assertRaises(ValueError):
                evidence.read(relative)
        with patch.object(Path, "is_junction", return_value=True), self.assertRaises(ValueError):
            evidence.read("context.json")

    def test_duplicate_json_properties_rejected(self):
        (self.root / "context.json").write_text('{"RunId":"a","RunId":"b"}')
        with self.assertRaises(ValueError):
            self.audit()

    def test_cli_writes_only_new_success_report_and_keeps_failures_private(self):
        output = self.root / "audit.json"
        args = ["audit_android.py", "--candidate", str(self.root / "candidate.json"), "--candidate-sha256", self.candidate_hash,
                "--evidence", str(self.root), "--run-id", self.run, "--assembly", self.assembly,
                "--assembly-sha256", self.assembly_hash, "--discovery-sha256", self.discovery_hash,
                "--producer-manifest-sha256", self.manifest_hash, "--output", str(output)]
        with patch("sys.argv", args), patch("builtins.print"):
            self.assertEqual(0, audit_android.main())
            original = output.read_bytes()
            self.assertEqual(1, audit_android.main())
            self.assertEqual(original, output.read_bytes())
        output.unlink()
        (self.root / "completion.json").unlink()
        with patch("sys.argv", args), patch("builtins.print") as printed:
            self.assertEqual(1, audit_android.main())
            self.assertFalse(output.exists())
            self.assertNotIn(str(self.root), str(printed.call_args_list))

    def test_nonrunnable_discovery_is_rejected_even_with_matching_pin(self):
        changed = self.discovery.replace('runstate="Runnable"', 'runstate="Explicit"', 1)
        (self.root / "discovery.dump").write_text(changed, encoding="utf-8")
        self.discovery_hash = sha(changed.encode())
        self.pin["DiscoverySha256"] = self.discovery_hash
        self.write("producer-pin.json", self.pin)
        with self.assertRaisesRegex(ValueError, "non-runnable"):
            self.audit()


if __name__ == "__main__":
    unittest.main()
