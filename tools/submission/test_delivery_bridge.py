# Copyright (c) 2026 Neil Colvin.
# Licensed under the MIT License. See LICENSE in the repository root.
"""Real pinned Python/.NET revalidation through guarded dispatch; providers are synthetic."""
import json
from pathlib import Path
import shutil
import subprocess
import sys
import unittest

from build_help import sha
import test_prepare_delivery as preparation_tests
import test_prepare_review_request as request_tests


class DeliveryProcessBridgeTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        root = Path(__file__).resolve().parents[2]
        cls.dotnet = shutil.which("dotnet")
        run = subprocess.run([cls.dotnet, "build", str(root / "CrestronHomeDevTools.Tests.Probe/CrestronHomeDevTools.Tests.Probe.csproj"),
                              "-c", "Release", "--verbosity", "quiet"], capture_output=True, timeout=120)
        if run.returncode:
            raise AssertionError(run.stdout.decode(errors="replace") + run.stderr.decode(errors="replace"))
        cls.harness = root / "CrestronHomeDevTools.Tests.Probe/bin/Release/net10.0/CrestronHomeDevTools.Tests.Probe.dll"

    def setUp(self):
        self.fixture = f = preparation_tests.DeliveryStageTests()
        f.setUp()
        self.addCleanup(f.doCleanups)
        f.settings["dotnet"] = str(Path(f.settings["dotnet"]).resolve())
        f.settings_path.write_text(json.dumps(f.settings), encoding="utf-8")
        f.run_stage()
        self.tools = f.root / "pinned-scripts"
        self.tools.mkdir()
        for path in Path(__file__).parent.glob("*.py"):
            shutil.copyfile(path, self.tools / path.name)
        self.attempts = f.root / "bridge-attempts"
        self.attempts.mkdir()
        self.journal = f.root / "dispatch-journal"
        self.journal.mkdir()
        self.plan = json.loads((f.output / "delivery-plan.json").read_bytes())
        validator_root = Path(f.settings["validator"]).parent
        def pins(directory):
            return [{"RelativePath": path.relative_to(directory).as_posix(), "Sha256": sha(path.read_bytes())}
                    for path in directory.rglob("*") if path.is_file()]
        self.settings = {
            "PythonPath": str(Path(sys.executable).resolve()), "PythonSha256": sha(Path(sys.executable).resolve().read_bytes()),
            "DotnetPath": f.settings["dotnet"], "DotnetSha256": sha(Path(f.settings["dotnet"]).read_bytes()),
            "ToolsDirectory": str(self.tools), "ToolFiles": pins(self.tools),
            "ValidatorDirectory": str(validator_root), "ValidatorFiles": pins(validator_root),
            "PreparationSettingsPath": str(f.settings_path), "PreparationSettingsSha256": sha(f.settings_path.read_bytes()),
            "PreparedDirectory": str(f.output), "AttemptsDirectory": str(self.attempts),
            "DeliveryReviewSha256": sha((f.output / "delivery-review-receipt.json").read_bytes()), "Timeout": "00:01:00"}

    def invoke(self):
        return subprocess.run([self.dotnet, str(self.harness), "--real-delivery-bridge"],
            input=json.dumps({"settings": self.settings, "plan": self.plan, "journal": str(self.journal),
                              "package": str(self.fixture.output / "delivery" / self.plan["packageFileName"]),
                              "form": str(self.fixture.output / "delivery" / self.plan["signedFormFileName"])}).encode(),
            capture_output=True, timeout=150)

    def test_both_steps_run_real_revalidation_and_only_simulated_delivery(self):
        run = self.invoke()
        failures = [json.loads(path.read_bytes()) for path in self.attempts.glob("*/finished.json")]
        self.assertEqual(run.returncode, 0, (run.stderr.decode(), failures))
        self.assertEqual(json.loads(run.stdout), {"State": "Submitted", "Uploads": 1, "Sends": 1, "SyntheticTransport": True})
        attempts = list(self.attempts.iterdir())
        folders = [path for path in attempts if path.is_dir()]
        self.assertEqual(len(folders), 2)
        self.assertEqual({path.name.split("-")[0] for path in folders}, {"Upload", "Send"})
        for path in folders:
            self.assertTrue(json.loads((path / "finished.json").read_bytes())["Success"])
            result = json.loads((path / "result/revalidation-receipt.json").read_bytes())
            self.assertEqual(result["plan"], self.plan)
            self.assertFalse(result["deliveryAttempted"])
        again = self.invoke()
        self.assertEqual(again.returncode, 0, again.stderr.decode())
        self.assertEqual(json.loads(again.stdout)["Uploads"], 0)
        self.assertEqual(len([path for path in self.attempts.iterdir() if path.is_dir()]), 2)

    def test_changed_pinned_script_never_attempts_upload(self):
        with (self.tools / "revalidate_delivery.py").open("a") as file:
            file.write("\n# changed after review\n")
        run = self.invoke()
        self.assertEqual(run.returncode, 1)
        receipt = json.loads(next(self.journal.glob("*.json")).read_bytes())
        self.assertEqual(receipt["state"], "Prepared")
        self.assertIsNone(receipt["upload"])
        self.assertFalse(any(path.is_dir() for path in self.attempts.iterdir()))


class ReviewRequestDeliveryBridgeTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        DeliveryProcessBridgeTests.setUpClass.__func__(cls)

    def exercise(self, kind, change):
        fixture = request_tests.ReviewRequestTests()
        fixture.setUp()
        try:
            fixture.disposition["attachmentKind"] = kind
            if kind == "DisclosureOnly":
                fixture.disposition["omissions"].append({"id": "officialSelfTestForm", "reason": "Synthetic form omission."})
            fixture.save()
            prepared = fixture.run_stage()
            run = subprocess.run([self.dotnet, str(self.harness), "--review-request-delivery", str(fixture.output),
                                  str(fixture.root / "synthetic-delivery"), change], capture_output=True, timeout=120)
            expected = 0 if change == "none" else 2
            self.assertEqual(run.returncode, expected, run.stderr.decode(errors="replace"))
            result = json.loads(run.stdout)
            self.assertTrue(result["syntheticTransport"])
            self.assertEqual(result["Uploads"], 1)
            self.assertEqual(result["Sends"], 1 if change == "none" else 0)
            self.assertEqual(result["state"], "Submitted" if change == "none" else "Uploaded")
            if change == "none":
                self.assertEqual(result["verification"], "GapsDeclared")
            self.assertFalse(prepared["signatureApplied"])
        finally:
            fixture.doCleanups()

    def test_real_unsigned_preparation_through_approval_and_simulated_delivery(self):
        self.exercise("UnsignedSelfTest", "none")

    def test_real_disclosure_only_preparation_through_approval_and_simulated_delivery(self):
        self.exercise("DisclosureOnly", "none")

    def test_approval_revoked_after_upload_prevents_email(self):
        self.exercise("UnsignedSelfTest", "approval")

    def test_evidence_changed_after_upload_prevents_email(self):
        self.exercise("UnsignedSelfTest", "evidence")
