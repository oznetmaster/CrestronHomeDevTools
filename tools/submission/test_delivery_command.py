# Copyright (c) 2026 Neil Colvin.
# Licensed under the MIT License. See LICENSE in the repository root.
"""Real console parsing, pinned preparation and revalidation; no external delivery."""

import json
import subprocess
import unittest

from build_help import sha
import test_delivery_bridge as bridge_tests


def camel(value):
    if isinstance(value, dict):
        return {key[0].lower() + key[1:]: camel(item) for key, item in value.items()}
    if isinstance(value, list):
        return [camel(item) for item in value]
    return value


class DeliveryCommandIntegrationTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        bridge_tests.DeliveryProcessBridgeTests.setUpClass()
        cls.dotnet = bridge_tests.DeliveryProcessBridgeTests.dotnet
        cls.harness = bridge_tests.DeliveryProcessBridgeTests.harness

    def setUp(self):
        bridge_tests.DeliveryProcessBridgeTests.setUp(self)
        self.upload_receipts = self.fixture.root / "upload-receipts"
        self.mail_receipts = self.fixture.root / "mail-receipts"
        self.upload_receipts.mkdir()
        self.mail_receipts.mkdir()
        self.command_path = self.fixture.root / "protected-command.json"
        self.command_settings = {
            "schemaVersion": 1, "plan": self.plan, "journalDirectory": str(self.journal),
            "packagePath": str(self.fixture.output / "delivery" / self.plan["packageFileName"]),
            "signedFormPath": str(self.fixture.output / "delivery" / self.plan["signedFormFileName"]),
            "revalidation": camel(self.settings), "uploadReceiptDirectory": str(self.upload_receipts),
            "reviewedUploadFormSha256": "a" * 64, "acceptedUploadTermsSha256": "b" * 64,
            "uploadTimeoutSeconds": 60, "mailReceiptDirectory": str(self.mail_receipts),
            "smtpHost": "smtp.example.test", "smtpPort": 587, "mailTimeoutSeconds": 60}
        self.command_path.write_text(json.dumps(self.command_settings), encoding="utf-8")
        self.command_pin = sha(self.command_path.read_bytes())

    def command(self, revoke=False):
        flag = "--delivery-command-revoke-after-upload" if revoke else "--real-delivery-command"
        result = subprocess.run([self.dotnet, str(self.harness), flag,
            "--settings", str(self.command_path), "--settings-sha256", self.command_pin, "--execute-approved"],
            input=json.dumps({"uploadUserName": "synthetic-uploader", "uploadPassword": "synthetic-upload-secret",
                              "smtpUserName": "synthetic-mailbox", "smtpPassword": "synthetic-mail-secret"}).encode(),
            capture_output=True, timeout=150)
        combined = (result.stdout + result.stderr).decode(errors="replace")
        for private in ("synthetic-upload-secret", "synthetic-mail-secret", self.plan["sender"], str(self.fixture.root)):
            self.assertNotIn(private, combined)
        return result, json.loads(result.stdout)

    def test_command_runs_both_real_revalidations_and_completed_replay_sends_nothing(self):
        run, result = self.command()
        self.assertEqual(run.returncode, 0, run.stderr.decode())
        self.assertEqual(result, {"Command": {"SchemaVersion": 1, "State": "Submitted", "Submitted": True},
                                  "Uploads": 1, "Sends": 1, "SyntheticTransport": True})
        folders = [path for path in self.attempts.iterdir() if path.is_dir()]
        self.assertEqual({path.name.split("-")[0] for path in folders}, {"Upload", "Send"})
        self.assertEqual(len(folders), 2)
        for path in folders:
            self.assertTrue(json.loads((path / "finished.json").read_bytes())["Success"])
            receipt = json.loads((path / "result/revalidation-receipt.json").read_bytes())
            self.assertEqual(receipt["plan"], self.plan)
            self.assertFalse(receipt["deliveryAttempted"])
        again, receipt = self.command()
        self.assertEqual(again.returncode, 0, again.stderr.decode())
        self.assertEqual((receipt["Uploads"], receipt["Sends"]), (0, 0))
        self.assertEqual(len([path for path in self.attempts.iterdir() if path.is_dir()]), 2)

    def test_command_changed_settings_stops_before_journal_or_revalidation(self):
        with self.command_path.open("a") as file:
            file.write("\n")
        run, result = self.command()
        self.assertEqual(run.returncode, 2)
        self.assertEqual((result["Uploads"], result["Sends"]), (0, 0))
        self.assertFalse(any(self.journal.iterdir()))
        self.assertFalse(any(self.attempts.iterdir()))

    def test_command_changed_prepared_package_stops_before_upload(self):
        package = self.fixture.output / "delivery" / self.plan["packageFileName"]
        package.write_bytes(package.read_bytes() + b"altered")
        run, result = self.command()
        self.assertEqual(run.returncode, 2)
        self.assertEqual((result["Uploads"], result["Sends"]), (0, 0))

    def test_command_rechecks_authorization_after_upload_and_preserves_receipt(self):
        run, result = self.command(revoke=True)
        self.assertEqual(run.returncode, 2)
        self.assertEqual((result["Uploads"], result["Sends"]), (1, 0))
        receipt = json.loads(next(self.journal.glob("*.json")).read_bytes())
        self.assertEqual(receipt["state"], "Uploaded")
        self.assertIsNotNone(receipt["upload"])
        again, result = self.command()
        self.assertEqual(again.returncode, 2)
        self.assertEqual((result["Uploads"], result["Sends"]), (0, 0))
