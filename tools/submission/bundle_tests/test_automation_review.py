# Copyright (c) 2026 Neil Colvin. MIT licensed.
"""Real bundled-console review via the public C# automation adapter; synthetic inputs only."""
import json
import os
from pathlib import Path
import shutil
import subprocess
import unittest
from datetime import datetime, timezone, timedelta
from PIL import Image, ImageDraw

from pypdf import PdfReader
import test_self_test_form as fixtures
import test_audit_android as android_fixtures
from build_help import sha


class AutomationReviewIntegration(unittest.TestCase):
    def fixture(self):
        f = fixtures.SelfTestFormTests()
        f.setUp()
        self.addCleanup(f.doCleanups)
        root = f.root
        shutil.copyfile(f.template, root / "template.pdf")
        shutil.copyfile(f.package, root / "candidate.pkg")
        nunit = root / "nunit"
        nunit.mkdir()
        shutil.copyfile(f.evidence / "synthetic.txt", nunit / "synthetic.txt")
        for observation in f.observations["observations"]:
            observation["files"][0]["relativePath"] = "nunit/synthetic.txt"
        f.write_json(nunit / "observations.json", f.observations)
        f.write_json(root / "inventory.json", f.inventory)
        f.mapping["inventorySha256"] = sha((root / "inventory.json").read_bytes())
        f.write_json(root / "mapping.json", f.mapping)
        f.write_json(root / "release.json", {"PackageName": f.package.name})
        f.write_json(root / "windows-tests.json", {"Files": [
            {"RelativePath": p.relative_to(root).as_posix(), "Sha256": sha(p.read_bytes())}
            for p in nunit.iterdir()]})
        return f

    def test_public_adapter_prepares_real_pdf_and_recovers_without_regeneration(self):
        f = self.fixture()
        root = f.root
        command = [os.environ["SUBMISSION_TEST_DOTNET"], os.environ["SUBMISSION_TEST_PROBE"],
                   "--automation-review", str(root), str(Path(os.environ["SUBMISSION_TEST_BUNDLE"]).parent)]
        result = subprocess.run(command, capture_output=True, text=True, timeout=120)
        if result.returncode:
            diagnostics = root / "review-process" / "stderr.txt"
            self.fail(result.stderr + (diagnostics.read_text() if diagnostics.exists() else ""))
        report = json.loads(result.stdout)
        self.assertTrue(report["SyntheticOnly"])
        self.assertTrue(report["RecoveryPreserved"])
        self.assertTrue(report["RehearsalStopped"])
        form = PdfReader(root / "review" / "self-test.review.pdf")
        self.assertEqual("/Yes", form.get_fields()["First"]["/V"])
        self.assertEqual("/Yes", form.get_fields()["Second"]["/V"])
        self.assertFalse((root / "signed-review").exists())
        self.assertFalse((root / "delivery").exists())

    def test_coordinator_android_handoff_reaches_real_audit_and_pdf(self):
        f = self.fixture()
        android = android_fixtures.AndroidEvidenceTests()
        android.setUp()
        self.addCleanup(android.doCleanups)
        android.context.update(PackageSha256=sha((f.root / "candidate.pkg").read_bytes()),
                               ReleaseSourceCommit="a" * 40, DriverGuid=f.driver_id,
                               DriverVersion="1.0.000.0000")
        android.write("context.json", android.context)
        android.write("completion.json", {"SchemaVersion": 1, "RunId": android.run,
                      "PackageSha256": android.context["PackageSha256"], "RestorationConfirmed": True})
        android.pin["PackageSha256"] = android.context["PackageSha256"]
        android.write("producer-pin.json", android.pin)
        android.coverage.update(PackageSha256=android.context["PackageSha256"], ReleaseSourceCommit="a" * 40)
        android.write("coverage.json", android.coverage)
        android.capture.update(android.context)
        android.write("check/observation.json", android.capture)
        shutil.copytree(android.root, f.root / "nunit/AndroidUI")
        f.write_json(f.root / "windows-tests.json", {"InputSha256": "e" * 64, "Stage": "Local", "Files": [
            {"RelativePath": p.relative_to(f.root).as_posix(), "Sha256": sha(p.read_bytes())}
            for p in (f.root / "nunit").rglob("*") if p.is_file()]})
        command = [os.environ["SUBMISSION_TEST_DOTNET"], os.environ["SUBMISSION_TEST_PROBE"],
                   "--automation-review", str(f.root), str(Path(os.environ["SUBMISSION_TEST_BUNDLE"]).parent)]
        result = subprocess.run(command, capture_output=True, text=True, timeout=120)
        diagnostics = f.root / "review-process/stderr.txt"
        self.assertEqual(result.returncode, 0, result.stderr + (diagnostics.read_text() if diagnostics.exists() else ""))
        audit = json.loads((f.root / "review/android-audit.json").read_text())
        self.assertEqual(audit["runs"][0]["runId"], android.run)
        self.assertFalse(audit["producerAuthenticated"])
        self.assertTrue((f.root / "review/self-test.review.pdf").is_file())
        self.assertFalse((f.root / "signed-review").exists())

    def test_review_sign_delivery_retention_chain_waits_for_exact_authority_and_does_not_resend(self):
        f = self.fixture()
        root = f.root
        authority = Path(str(root) + "-authority")
        authority.mkdir()
        self.addCleanup(shutil.rmtree, authority)
        console = os.environ["SUBMISSION_TEST_BUNDLE"]

        def run(phase, expected=0):
            command = [os.environ["SUBMISSION_TEST_DOTNET"], os.environ["SUBMISSION_TEST_PROBE"],
                       "--automation-protected", str(root), str(Path(console).parent), phase]
            result = subprocess.run(command, capture_output=True, text=True, timeout=180)
            if result.returncode != expected:
                logs = "\n".join(p.read_text() for p in root.glob("*-process/stderr.txt"))
                self.fail(result.stderr + logs)
            return result

        def approve(name, value):
            path = authority / (name + ".json")
            f.write_json(path, value)
            (authority / (name + ".sha256")).write_text(sha(path.read_bytes()))

        run("review")
        run("sign", 4)
        self.assertFalse((root / "signed-review").exists())
        image = authority / "synthetic-signature.png"
        picture = Image.new("RGB", (220, 60), "white")
        ImageDraw.Draw(picture).line([(10, 45), (80, 10), (160, 45), (210, 15)], fill="black", width=3)
        picture.save(image)
        image_hash = sha(image.read_bytes())
        store = authority / "store"
        for arguments in (["create", "--store", str(store)],
                          ["signature", "--store", str(store), "--name", "synthetic", "--file", str(image)]):
            result = subprocess.run([console, "credentials", *arguments], capture_output=True, text=True, timeout=60)
            self.assertEqual(0, result.returncode, result.stderr)
        image.unlink()
        f.write_json(authority / "bindings.json", {"StoreDirectory": str(store), "Signature": "synthetic"})
        review = json.loads((root / "review/review-receipt.json").read_text())
        now = datetime.now(timezone.utc)
        approval = {key: review[key] for key in ("formSha256", "formReportSha256", "inventorySha256", "candidateSha256")}
        approval.update(schemaVersion=1, packageSha256=f.identity["packageSha256"], signatureImageSha256=image_hash,
                        signer="SYNTHETIC TEST ONLY", signingDate=now.date().isoformat(), expiresUtc=(now + timedelta(hours=1)).isoformat(),
                        signatureField="Signature", dateField="Date", visualReviewCompleted=True, signatureAuthorized=True)
        approve("sign", approval)
        run("sign")
        signed_path = root / "signed-review/signed-review-receipt.json"
        signed_hash = sha(signed_path.read_bytes())
        run("sign")
        self.assertEqual(signed_hash, sha(signed_path.read_bytes()))
        run("deliver", 4)
        self.assertFalse((root / "synthetic-provider-calls.txt").exists())
        signed = json.loads(signed_path.read_text())
        approval = {key: signed[key] for key in ("candidateSha256", "packageSha256", "signedFormSha256")}
        approval.update(schemaVersion=1, signedReviewSha256=signed_hash, sender="fixture@example.org", recipient="drivers@crestron.com",
                        subject="Driver Submission Package", expiresUtc=(now + timedelta(hours=1)).isoformat(),
                        signedVisualReviewCompleted=True, deliveryAuthorized=True)
        approve("deliver", approval)
        run("deliver")
        self.assertEqual("upload\nemail\n", (root / "synthetic-provider-calls.txt").read_text())
        retained = (root / "retained.json").read_bytes()
        run("deliver")
        self.assertEqual("upload\nemail\n", (root / "synthetic-provider-calls.txt").read_text())
        self.assertEqual(retained, (root / "retained.json").read_bytes())
        self.assertFalse(json.loads(retained)["CrestronAcceptanceEstablished"])
