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


if __name__ == "__main__":
    unittest.main()
