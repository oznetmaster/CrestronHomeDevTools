# Copyright (c) 2026 Neil Colvin.
# Licensed under the MIT License. See LICENSE in the repository root.
"""Acceptance checks against the actual self-contained Windows download; no delivery."""

import json
from datetime import datetime, timedelta, timezone
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest
from unittest.mock import patch
from xml.etree import ElementTree as ET

import test_prepare_review as review
import test_prepare_signed_review as signed
import test_prepare_delivery as delivery
import test_revalidate_delivery as revalidation
import test_prepare_review_request as request
import test_msbuild_help as build
import test_sign_self_test_form as signing
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

    def test_encrypted_signature_signs_only_the_approved_form_without_plaintext_file(self):
        f = signing.SigningTests()
        f.setUp()
        self.addCleanup(f.doCleanups)
        now = datetime.now(timezone.utc)
        f.approval.update(signingDate=now.date().isoformat(), expiresUtc=(now + timedelta(hours=1)).isoformat())
        f.fixture.write_json(f.authorization, f.approval)
        store = f.fixture.root / "private-store"

        def credentials(*arguments):
            result = subprocess.run([str(self.console), "credentials", *map(str, arguments)],
                                    env=self.env, cwd=self.hostile, capture_output=True, timeout=30)
            self.assertEqual(result.returncode, 0, result.stderr.decode(errors="replace"))
            return result

        credentials("create", "--store", store)
        credentials("signature", "--store", store, "--name", "synthetic", "--file", f.image)
        credentials("verify", "--store", store, "--name", "synthetic")
        f.image.unlink()
        bindings = f.fixture.root / "bindings.json"
        bindings.write_text(json.dumps({"StoreDirectory": str(store), "Signature": "synthetic"}))
        before = set(f.fixture.root.rglob("*"))
        common = ("--form", f.fixture.output, "--form-report", f.report_path, "--inventory", f.inventory,
                  "--authorization", f.authorization, "--output", f.output, "--report", f.fixture.root / "signed-report.json")
        self.invoke("sign-self-test-form", *common, "--authorization-sha256", "0" * 64,
                    "--credentials", bindings, success=False)
        self.assertFalse(f.output.exists())
        output, _ = self.invoke("sign-self-test-form", *common, "--authorization-sha256", sha(f.authorization.read_bytes()),
                                "--credentials", bindings)
        result = json.loads(output)
        self.assertTrue(result["signatureApplied"])
        self.assertFalse(result["submissionReady"])
        added = set(f.fixture.root.rglob("*")) - before
        self.assertEqual(added, {f.output, f.fixture.root / "signed-report.json"})

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

    def test_declared_gap_review_to_unsigned_request_uses_only_bundled_tools(self):
        f = self.fixture(request.ReviewRequestTests)
        review_fixture = f.base.base
        review_output = f.root / "bundled-review"
        review_fixture.settings["output"] = str(review_output)
        f.base.f.write_json(review_fixture.settings_path, review_fixture.settings)
        self.portable_settings(review_fixture)
        output, _ = self.invoke("prepare-review", "--settings", review_fixture.settings_path,
            "--candidate-sha256", review_fixture.pins[0], "--inventory-sha256", review_fixture.pins[1],
            "--mapping-sha256", review_fixture.pins[2], "--source-commit", "a" * 40, "--artifact-kind", "driver",
            "--review-mode", "declared-gaps", "--declarations", f.base.declarations, "--declarations-sha256", f.base.digest)
        review_receipt = json.loads(output)
        f.review_pin = sha((review_output / "review-receipt.json").read_bytes())
        f.settings["reviewDirectory"] = str(review_output)
        f.disposition.update(reviewReceiptSha256=f.review_pin, declarationsSha256=review_receipt["declarationsSha256"])
        f.save()
        self.portable_settings(f)
        original_receipt = (review_output / "review-receipt.json").read_bytes()
        changed = {**review_receipt, "schemaVersion": 2}
        (review_output / "review-receipt.json").write_text(json.dumps(changed), encoding="utf-8")
        invalid_pin = sha((review_output / "review-receipt.json").read_bytes())
        (review_output / "COMPLETE").write_text(invalid_pin, encoding="ascii")
        f.disposition["reviewReceiptSha256"] = invalid_pin
        f.save()
        self.portable_settings(f)
        self.invoke("prepare-review-request", "--settings", f.settings_path,
                    "--review-sha256", invalid_pin, "--disposition-sha256", f.disposition_pin, success=False)
        self.assertFalse(f.output.exists())
        (review_output / "review-receipt.json").write_bytes(original_receipt)
        (review_output / "COMPLETE").write_text(f.review_pin, encoding="ascii")
        f.disposition["reviewReceiptSha256"] = f.review_pin
        f.save()
        self.portable_settings(f)
        output, _ = self.invoke("prepare-review-request", "--settings", f.settings_path,
                               "--review-sha256", f.review_pin, "--disposition-sha256", f.disposition_pin)
        receipt = json.loads(output)
        self.assertEqual(receipt["state"], "UnsignedRequestWithDeclaredGapsPrepared")
        self.assertEqual(receipt["attachmentKind"], "UnsignedSelfTest")
        self.assertFalse(receipt["deliveryAttempted"])
        self.assertFalse(receipt["signatureApplied"])
        self.assertTrue((f.output / "delivery" / receipt["attachmentFileName"]).is_file())

    def test_synthetic_signing_uses_bundled_validator(self):
        f = self.fixture(signed.SignedReviewStageTests)
        output, _ = self.invoke("prepare-signed-review", "--settings", f.settings_path,
                               "--review-sha256", f.review_pin, "--authorization-sha256", f.approval_pin)
        receipt = json.loads(output)
        self.assertEqual(receipt["state"], "SignedReviewPrepared")
        self.assertFalse(receipt["deliveryAttempted"])

    def test_signed_review_can_use_encrypted_signature_without_image_setting(self):
        f = self.fixture(signed.SignedReviewStageTests)
        data = json.loads(f.settings_path.read_text())
        image = Path(data.pop("signatureImage"))
        store = image.parent / "encrypted-signature"
        for command in (["create", "--store", str(store)],
                        ["signature", "--store", str(store), "--name", "synthetic", "--file", str(image)]):
            result = subprocess.run([str(self.console), "credentials", *command], env=self.env,
                                    cwd=self.hostile, capture_output=True, timeout=30)
            self.assertEqual(result.returncode, 0, result.stderr.decode(errors="replace"))
        image.unlink()
        f.settings_path.write_text(json.dumps(data))
        bindings = image.parent / "signature-bindings.json"
        bindings.write_text(json.dumps({"StoreDirectory": str(store), "Signature": "synthetic"}))
        output, _ = self.invoke("prepare-signed-review", "--settings", f.settings_path,
                               "--review-sha256", f.review_pin, "--authorization-sha256", f.approval_pin,
                               "--credentials", bindings)
        receipt = json.loads(output)
        self.assertEqual(receipt["state"], "SignedReviewPrepared")
        self.assertTrue(receipt["signatureApplied"])
        self.assertFalse(receipt["deliveryAttempted"])

    def test_reviewed_interpretation_signing_uses_bundled_tools(self):
        f = signed.SignedReviewStageTests()
        f.setUp(interpreted=True)
        self.addCleanup(f.doCleanups)
        self.portable_settings(f)
        output, _ = self.invoke("prepare-signed-review", "--settings", f.settings_path,
                               "--review-sha256", f.review_pin, "--authorization-sha256", f.approval_pin)
        receipt = json.loads(output)
        self.assertEqual(receipt["verificationStatus"], "GapsDeclared")
        self.assertTrue(receipt["signatureApplied"])
        self.assertFalse(receipt["deliveryAttempted"])
        self.assertFalse(receipt["deliveryAuthorized"])
        form = signed.PdfReader(f.output / "delivery" / "Driver-Self-Test.signed.pdf")
        self.assertIn("Checked - reviewed interpretation", "\n".join(page.extract_text() for page in form.pages))

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

    def dispatch_fixture(self, prepared=None):
        f = prepared if prepared is not None else self.fixture(delivery.DeliveryStageTests)
        if prepared is None:
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

    def test_actual_preparation_templates_chain_into_synthetic_delivery(self):
        # Match the runner's relative installation layout, without modifying the download.
        installation = self.hostile / "artifacts" / "submission-console"
        shutil.copytree(self.console.parent, installation)
        template_root = Path(__file__).resolve().parents[3] / "docs/submission"
        completed = []

        def stage(name, fixture, values, receipt_name, rejected_values=None):
            self.portable_settings(fixture)
            template = template_root / ("submission-" + name + ".yml.example")
            pieces = template.read_text().split("        run: |\n")
            self.assertEqual(len(pieces), 2, "Review the extraction if the template adds another multiline step")
            lines = pieces[1].splitlines()
            self.assertTrue(all(not line.strip() or line.startswith("          ") for line in lines))
            script = self.hostile / (name + ".ps1")
            script.write_text("\n".join(line[10:] for line in lines) + "\n")
            env = dict(self.env, **values, GITHUB_STEP_SUMMARY=str(self.hostile / "job-summary.md"))

            def invoke(overrides):
                return subprocess.run([os.environ["SUBMISSION_TEST_PWSH"], "-NoProfile", "-NonInteractive",
                                       "-File", str(script)], cwd=self.hostile, env=dict(env, **overrides),
                                      capture_output=True, timeout=180)

            if rejected_values is not None:
                rejected = invoke(rejected_values)
                self.assertNotEqual(rejected.returncode, 0)
                self.assertFalse(fixture.output.exists(), "Rejected stage must not prepare an output")
            result = invoke({})
            self.assertEqual(result.returncode, 0, result.stderr.decode(errors="replace"))
            receipt_path = fixture.output / receipt_name
            self.assertEqual((fixture.output / "COMPLETE").read_text().strip(), sha(receipt_path.read_bytes()))
            receipt = json.loads(receipt_path.read_bytes())
            self.assertFalse(receipt["deliveryAttempted"])
            completed.append(name)
            return receipt

        def review_stage(fixture, *, signing_copy=False, **options):
            self.assertTrue(signing_copy)
            return stage("review", fixture, {
                "CRESTRON_SUBMISSION_REVIEW_SETTINGS": str(fixture.settings_path),
                "CRESTRON_SUBMISSION_ANDROID_PINS": str(options["android_pins"]),
                "REVIEW_KIND": "driver", "REVIEW_SOURCE": "a" * 40,
                "REVIEW_CANDIDATE": fixture.pins[0], "REVIEW_INVENTORY": fixture.pins[1],
                "REVIEW_MAPPING": fixture.pins[2], "REVIEW_SIGNING_COPY": "true", "REVIEW_MODE": "complete",
                "REVIEW_ANDROID_PINS": options["android_pins_sha256"]}, "review-receipt.json",
                {"REVIEW_KIND": "library"})

        def signing_stage(fixture):
            return stage("signing", fixture, {
                "CRESTRON_SUBMISSION_SIGNING_SETTINGS": str(fixture.settings_path),
                "SIGNING_REVIEW": fixture.review_pin, "SIGNING_AUTHORIZATION": fixture.approval_pin},
                "signed-review-receipt.json", {"SIGNING_AUTHORIZATION": "0" * 64})

        fixture = delivery.DeliveryStageTests()
        self.addCleanup(fixture.doCleanups)
        # Replace only test-fixture setup helpers with the actual PowerShell template steps.
        # Evidence, validator, packaged console and artifact handoffs remain real; inputs are synthetic.
        with patch.object(review.ReviewStageTests, "run_stage", review_stage), \
             patch.object(signed.SignedReviewStageTests, "run_stage", signing_stage):
            fixture.setUp(android=True)
        receipt = stage("delivery-preparation", fixture, {
            "CRESTRON_SUBMISSION_DELIVERY_SETTINGS": str(fixture.settings_path),
            "DELIVERY_REVIEW": fixture.pin, "DELIVERY_AUTHORIZATION": fixture.authorization_pin},
            "delivery-review-receipt.json", {"DELIVERY_REVIEW": "0" * 64})
        self.assertEqual(completed, ["review", "signing", "delivery-preparation"])
        self.assertEqual(receipt["state"], "DeliveryPlanPrepared")
        for name in ("android-pins.json", "android-audit.json"):
            original = (fixture.signed.review.output / name).read_bytes()
            self.assertEqual(original, (fixture.output / name).read_bytes())
            self.assertFalse((fixture.output / "delivery" / name).exists())
        self.assertEqual(len(list((fixture.output / "delivery").iterdir())), 2)
        _, source, path, _ = self.dispatch_fixture(prepared=fixture)
        self.prepare_dispatch(source, path)
        code, result = self.dispatch(path, sha(path.read_bytes()))
        self.assertEqual(code, 0, result)
        self.assertEqual((result["Uploads"], result["Sends"], result["SyntheticTransport"]), (1, 1, True))
        code, replay = self.dispatch(path, sha(path.read_bytes()))
        self.assertEqual(code, 0, replay)
        self.assertEqual((replay["Uploads"], replay["Sends"]), (0, 0))

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
        preflight.write_text(script + "\nWrite-Output \"PreflightPassed $deliveryCommand\"\n")
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

        # The same actual workflow preflight selects the new command only from independently pinned gap settings.
        original_settings = json.loads(original)
        review_settings = {"schemaVersion": 1, "plan": {"reviewMode": "DeclaredGaps"},
                           "tooling": {"consoleDirectory": original_settings["bundledRevalidation"]["consoleDirectory"],
                                       "consoleFiles": original_settings["bundledRevalidation"]["consoleFiles"]}}
        path.write_text(json.dumps(review_settings))
        env["DELIVERY_SETTINGS_SHA256"] = sha(path.read_bytes())
        result = run()
        self.assertEqual(result.returncode, 0, result.stderr.decode(errors="replace"))
        self.assertIn(b"PreflightPassed submission-review-request-deliver", result.stdout)
        review_settings["plan"]["reviewMode"] = "Complete"
        path.write_text(json.dumps(review_settings))
        env["DELIVERY_SETTINGS_SHA256"] = sha(path.read_bytes())
        self.assertNotEqual(run().returncode, 0)


if __name__ == "__main__":
    unittest.main()
