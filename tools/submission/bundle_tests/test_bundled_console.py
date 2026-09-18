# Copyright (c) 2026 Neil Colvin.
# Licensed under the MIT License. See LICENSE in the repository root.
"""Acceptance checks against the actual self-contained Windows download; no delivery."""

import json
import os
from pathlib import Path
import subprocess
import tempfile
import unittest
from xml.etree import ElementTree as ET

import test_prepare_review as review
import test_prepare_signed_review as signed
import test_prepare_delivery as delivery
import test_revalidate_delivery as revalidation
import test_msbuild_help as build
from build_help import sha


class BundledConsoleTests(unittest.TestCase):
    def setUp(self):
        self.console = Path(os.environ["SUBMISSION_TEST_BUNDLE"]).resolve()
        self.assertTrue(self.console.is_file())
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        self.hostile = Path(temporary.name)
        # These would break imports if the runtime used PATH, cwd or Python configuration.
        (self.hostile / "json.py").write_text("raise RuntimeError('SHADOW_MODULE_LOADED')")
        self.env = dict(os.environ, PATH="", PYTHONHOME=str(self.hostile), PYTHONPATH=str(self.hostile),
                        CRESTRON_DEVTOOLS_BUNDLED_VALIDATOR='["UNTRUSTED_VALIDATOR"]')

    def invoke(self, command, *args, success=True):
        result = subprocess.run([str(self.console), "submission", command, *map(str, args)],
                                cwd=self.hostile, env=self.env, capture_output=True, timeout=90)
        output = result.stdout.decode("utf-8")
        error = result.stderr.decode("utf-8")
        self.assertNotIn("Traceback", error)
        self.assertNotIn("Unhandled exception", error)
        self.assertNotIn("SHADOW_MODULE_LOADED", output + error)
        if success:
            self.assertEqual(result.returncode, 0, error)
        else:
            self.assertNotEqual(result.returncode, 0)
        return output, error

    def portable_settings(self, fixture):
        data = json.loads(fixture.settings_path.read_text())
        data.pop("dotnet", None)
        data.pop("validator", None)
        fixture.settings_path.write_text(json.dumps(data))

    def fixture(self, kind):
        fixture = kind()
        fixture.setUp()
        self.addCleanup(fixture.doCleanups)
        self.portable_settings(fixture)
        return fixture

    def test_runtime_and_every_public_command_help_without_installed_runtime(self):
        result = json.loads(self.invoke("runtime-check")[0])
        self.assertTrue(result["ready"])
        self.assertTrue(result["isolated"])
        commands = json.loads((self.console.parent / "submission-tools/scripts/commands.json").read_text())
        for command in commands:
            with self.subTest(command=command):
                output, _ = self.invoke(command, "--help")
                self.assertIn("CrestronHomeDevTools.Console submission " + command, output)
        self.invoke("not-a-command", success=False)

    def test_review_creates_real_form_and_bundle_with_bundled_validator(self):
        f = self.fixture(review.ReviewStageTests)
        output, _ = self.invoke("prepare-review", "--settings", f.settings_path,
                               "--candidate-sha256", f.pins[0], "--inventory-sha256", f.pins[1],
                               "--mapping-sha256", f.pins[2], "--source-commit", "a" * 40,
                               "--artifact-kind", "driver")
        receipt = json.loads(output)
        self.assertEqual(receipt["state"], "UnsignedReviewPrepared")
        self.assertFalse(receipt["deliveryAttempted"])
        self.assertFalse(receipt["submissionReady"])
        self.assertTrue((f.output / "self-test.review.pdf").is_file())
        self.assertTrue((f.output / "evidence.zip").is_file())

    def test_synthetic_signing_uses_bundled_validator(self):
        f = self.fixture(signed.SignedReviewStageTests)
        output, _ = self.invoke("prepare-signed-review", "--settings", f.settings_path,
                               "--review-sha256", f.review_pin, "--authorization-sha256", f.approval_pin)
        receipt = json.loads(output)
        self.assertEqual(receipt["state"], "SignedReviewPrepared")
        self.assertFalse(receipt["deliveryAttempted"])

    def test_delivery_preparation_uses_bundled_validator_without_sending(self):
        f = self.fixture(delivery.DeliveryStageTests)
        output, _ = self.invoke("prepare-delivery", "--settings", f.settings_path,
                               "--signed-review-sha256", f.pin, "--authorization-sha256", f.authorization_pin)
        receipt = json.loads(output)
        self.assertEqual(receipt["state"], "DeliveryPlanPrepared")
        self.assertFalse(receipt["deliveryAttempted"])

    def test_revalidation_preserves_existing_pins_without_sending(self):
        f = self.fixture(revalidation.DeliveryRevalidationTests)
        self.portable_settings(f.fixture)
        output, _ = self.invoke("revalidate-delivery", "--settings", f.settings_path,
                               "--delivery-review-sha256", f.pin, "--signed-review-sha256", f.fixture.pin,
                               "--authorization-sha256", f.fixture.authorization_pin)
        receipt = json.loads(output)
        self.assertEqual(receipt["state"], "DeliveryRevalidated")
        self.assertFalse(receipt["deliveryAttempted"])

    def test_packaged_input_cannot_override_validator(self):
        f = review.ReviewStageTests()
        f.setUp()
        self.addCleanup(f.doCleanups)
        self.invoke("prepare-review", "--settings", f.settings_path,
                    "--candidate-sha256", f.pins[0], "--inventory-sha256", f.pins[1],
                    "--mapping-sha256", f.pins[2], "--source-commit", "a" * 40,
                    "--artifact-kind", "driver", success=False)
        self.assertFalse(f.output.exists())

    def test_unexpected_module_returns_an_actionable_error_without_crashing(self):
        unexpected = self.console.parent / "submission-tools/scripts/acceptance_unexpected.py"
        with unexpected.open("x") as stream:
            stream.write("raise RuntimeError('MUST_NOT_EXECUTE')")
        try:
            _, error = self.invoke("runtime-check", success=False)
            self.assertIn("Unexpected file in submission tools", error)
            self.assertNotIn("MUST_NOT_EXECUTE", error)
        finally:
            unexpected.unlink()

    def test_real_msbuild_packages_help_using_console_without_interpreter_property(self):
        f = build.MsBuildHelpTests()
        f.setUp()
        self.addCleanup(f.doCleanups)
        tree = ET.parse(f.project)
        properties = tree.getroot().find("PropertyGroup")
        properties.remove(properties.find("SubmissionPython"))
        ET.SubElement(properties, "SubmissionConsole").text = str(self.console)
        tree.write(f.project, encoding="utf-8", xml_declaration=True)
        result = f.run_build()
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        reports = list((f.root / "obj").rglob("packaged-help.json"))
        self.assertEqual(len(reports), 1)

    def dispatch_fixture(self):
        f = self.fixture(delivery.DeliveryStageTests)
        self.invoke("prepare-delivery", "--settings", f.settings_path,
                    "--signed-review-sha256", f.pin, "--authorization-sha256", f.authorization_pin)
        directories = {}
        for key in ("journalDirectory", "attemptsDirectory", "uploadReceiptDirectory", "mailReceiptDirectory"):
            path = f.root / key
            path.mkdir()
            directories[key] = str(path)
        settings = {"schemaVersion": 1, "preparedDirectory": str(f.output),
                    "preparationSettingsPath": str(f.settings_path),
                    "deliveryReviewSha256": sha((f.output / "delivery-review-receipt.json").read_bytes()),
                    **directories, "reviewedUploadFormSha256": "a" * 64, "acceptedUploadTermsSha256": "b" * 64,
                    "smtpHost": "smtp.example.test", "smtpPort": 587, "revalidationTimeoutSeconds": 60,
                    "uploadTimeoutSeconds": 60, "mailTimeoutSeconds": 60}
        source = f.root / "dispatch-setup.json"
        source.write_text(json.dumps(settings))
        return f, source, f.root / "dispatch.json", settings

    def prepare_dispatch(self, source, destination, success=True):
        result = subprocess.run([str(self.console), "submission-delivery-settings", "--settings", str(source),
                                 "--output", str(destination)], cwd=self.hostile, env=self.env, capture_output=True, timeout=90)
        self.assertNotIn(b"Unhandled exception", result.stderr)
        self.assertNotIn(str(source).encode(), result.stdout + result.stderr)
        self.assertEqual(result.returncode, 0 if success else 2, result.stderr.decode())
        if success:
            receipt = json.loads(result.stdout)
            self.assertEqual(receipt["settingsSha256"], sha(destination.read_bytes()))
            self.assertTrue(receipt["approvalRequired"])
            self.assertFalse(receipt["deliveryAttempted"])
            actual = json.loads(destination.read_bytes())
            self.assertEqual(actual["schemaVersion"], 2)
            self.assertIsNone(actual["revalidation"])
            self.assertNotIn("pythonPath", actual["bundledRevalidation"])
        return result

    def dispatch(self, path, pin, revoke=False):
        flag = "--delivery-command-revoke-after-upload" if revoke else "--real-delivery-command"
        result = subprocess.run([os.environ["SUBMISSION_TEST_DOTNET"], os.environ["SUBMISSION_TEST_PROBE"], flag,
                                 "--settings", str(path), "--settings-sha256", pin, "--execute-approved"],
                                input=json.dumps({"uploadUserName": "synthetic-uploader", "uploadPassword": "synthetic-upload-secret",
                                                  "smtpUserName": "synthetic-mailbox", "smtpPassword": "synthetic-mail-secret"}).encode(),
                                cwd=self.hostile, env=self.env, capture_output=True, timeout=180)
        output = result.stdout + result.stderr
        for private in (b"synthetic-upload-secret", b"synthetic-mail-secret", str(path.parent).encode()):
            self.assertNotIn(private, output)
        return result.returncode, json.loads(result.stdout)

    def test_generated_dispatch_runs_bundled_revalidation_for_both_steps_and_replay_does_not_send(self):
        f, source, path, settings = self.dispatch_fixture()
        self.prepare_dispatch(source, path)
        pin = sha(path.read_bytes())
        code, result = self.dispatch(path, pin)
        self.assertEqual(code, 0, result)
        self.assertEqual((result["Uploads"], result["Sends"], result["SyntheticTransport"]), (1, 1, True))
        attempts = list(Path(settings["attemptsDirectory"]).glob("*/finished.json"))
        self.assertEqual(len(attempts), 2)
        self.assertTrue(all(json.loads(p.read_bytes())["Success"] for p in attempts))
        code, result = self.dispatch(path, pin)
        self.assertEqual(code, 0, result)
        self.assertEqual((result["Uploads"], result["Sends"]), (0, 0))
        self.assertEqual(len(list(Path(settings["attemptsDirectory"]).glob("*/finished.json"))), 2)

    def test_bundled_dispatch_revocation_after_upload_preserves_upload_and_blocks_email(self):
        f, source, path, settings = self.dispatch_fixture()
        self.prepare_dispatch(source, path)
        pin = sha(path.read_bytes())
        code, result = self.dispatch(path, pin, revoke=True)
        self.assertEqual((code, result["Uploads"], result["Sends"]), (2, 1, 0))
        receipt = json.loads(next(Path(settings["journalDirectory"]).glob("*.json")).read_bytes())
        self.assertEqual(receipt["state"], "Uploaded")
        code, result = self.dispatch(path, pin)
        self.assertEqual((code, result["Uploads"], result["Sends"]), (2, 0, 0))

    def test_changed_console_inventory_blocks_dispatch_before_any_provider(self):
        f, source, path, settings = self.dispatch_fixture()
        self.prepare_dispatch(source, path)
        extra = self.console.parent / "unexpected-after-review.txt"
        with extra.open("x") as file:
            file.write("Changed after independent review")
        try:
            code, result = self.dispatch(path, sha(path.read_bytes()))
            self.assertEqual((code, result["Uploads"], result["Sends"]), (2, 0, 0))
            self.assertFalse(list(Path(settings["attemptsDirectory"]).glob("*/intent.json")))
        finally:
            extra.unlink()

    def test_delivery_settings_refuse_changed_plan_overwrite_and_overlapping_outputs(self):
        f, source, path, settings = self.dispatch_fixture()
        self.prepare_dispatch(source, path)
        saved = path.read_bytes()
        self.prepare_dispatch(source, path, success=False)
        self.assertEqual(path.read_bytes(), saved)
        self.prepare_dispatch(source, self.console.parent / "must-not-create.json", success=False)
        self.assertFalse((self.console.parent / "must-not-create.json").exists())
        plan = f.output / "delivery-plan.json"
        plan.write_bytes(plan.read_bytes() + b" ")
        self.prepare_dispatch(source, f.root / "changed-plan.json", success=False)
        self.assertFalse((f.root / "changed-plan.json").exists())

    def test_workflow_preflight_consumes_generated_settings_and_refuses_changed_pins(self):
        f, source, path, settings = self.dispatch_fixture()
        self.prepare_dispatch(source, path)
        original = path.read_bytes()
        template = Path(__file__).resolve().parents[3] / "docs/submission/submission-delivery.yml.example"
        lines = template.read_text().split("        run: |\n", 1)[1].splitlines()
        script = "\n".join(line[10:] for line in lines)
        # Execute only the actual template's preflight. No credentials or process-launch code is included.
        script = script.split("$credentials = $env:PRIVATE_DELIVERY_CREDENTIALS", 1)[0]
        self.assertNotIn("Process]::Start", script)
        preflight = self.hostile / "preflight.ps1"
        preflight.write_text(script + "\nWrite-Output 'PreflightPassed'\n")
        env = dict(self.env, DELIVERY_SETTINGS=str(path), DELIVERY_SETTINGS_SHA256=sha(original))

        def run():
            return subprocess.run([os.environ["SUBMISSION_TEST_PWSH"], "-NoProfile", "-NonInteractive", "-File", str(preflight)],
                                  cwd=self.hostile, env=env, capture_output=True, timeout=60)

        result = run()
        self.assertEqual(result.returncode, 0, result.stderr.decode(errors="replace"))
        self.assertIn(b"PreflightPassed", result.stdout)
        path.write_bytes(original + b" ")
        self.assertNotEqual(run().returncode, 0)
        path.write_bytes(original)
        extra = self.console.parent / "unreviewed-workflow-test.txt"
        with extra.open("x") as file:
            file.write("Changed after independent review")
        try:
            result = run()
            self.assertNotEqual(result.returncode, 0)
            self.assertNotIn(b"PreflightPassed", result.stdout)
        finally:
            extra.unlink()


if __name__ == "__main__":
    unittest.main()
