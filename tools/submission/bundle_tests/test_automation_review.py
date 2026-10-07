# Copyright (c) 2026 Neil Colvin. MIT licensed.
"""Real bundled-console document preparation and protected delivery; synthetic completed inputs only."""
import json
import os
from pathlib import Path
import shutil
import subprocess
import unittest
from uuid import uuid4
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
        if retained := os.environ.get("SUBMISSION_TEST_RETAIN_DIRECTORY"):
            destination = Path(retained) / (self._testMethodName + "-" + uuid4().hex)
            self.addCleanup(shutil.copytree, root, destination)
        shutil.copyfile(f.template, root / "template.pdf")
        shutil.copyfile(f.package, root / "candidate.pkg")
        nunit = root / "nunit"
        nunit.mkdir()
        shutil.copyfile(f.evidence / "synthetic.txt", nunit / "synthetic.txt")
        for observation in f.observations["observations"]:
            observation["files"][0]["relativePath"] = "nunit/synthetic.txt"
        # Endurance is its own producer result, never a duplicate ordinary observation.
        duration = next(o for o in f.observations["observations"] if o["requirementId"] == "second.duration")
        f.observations["observations"].remove(duration)
        endurance = root / "endurance/observations"
        endurance.mkdir(parents=True)
        shutil.copyfile(f.evidence / "synthetic.txt", endurance / "synthetic.txt")
        duration["files"][0]["relativePath"] = "synthetic.txt"
        f.write_json(root / "endurance-evidence.json", {"EvidenceDirectory": "endurance/observations", "Observation": duration})
        f.write_json(nunit / "observations.json", f.observations)
        f.write_json(root / "inventory.json", f.inventory)
        f.mapping["inventorySha256"] = sha((root / "inventory.json").read_bytes())
        f.write_json(root / "mapping.json", f.mapping)
        f.write_json(root / "release.json", {"PackageName": f.package.name})
        f.write_json(root / "windows-tests.json", {"Files": [
            {"RelativePath": p.relative_to(root).as_posix(), "Sha256": sha(p.read_bytes())}
            for p in nunit.iterdir()]})
        return f

    def test_generic_review_prepares_real_pdf_and_recovers_without_regeneration(self):
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
        self.assertTrue(report["ProtectedHandoff"])
        form = PdfReader(root / "review" / "self-test.review.pdf")
        self.assertEqual("/Yes", form.get_fields()["First"]["/V"])
        self.assertEqual("/Yes", form.get_fields()["Second"]["/V"])
        self.assertFalse((root / "signed-review").exists())
        self.assertFalse((root / "delivery").exists())

    def test_review_snapshot_is_sufficient_after_original_producers_are_unavailable(self):
        f = self.fixture()
        command = [os.environ["SUBMISSION_TEST_DOTNET"], os.environ["SUBMISSION_TEST_PROBE"],
                   "--automation-protected", str(f.root), str(Path(os.environ["SUBMISSION_TEST_BUNDLE"]).parent), "portable"]
        result = subprocess.run(command, capture_output=True, text=True, timeout=120)
        logs = "\n".join(p.read_text() for p in f.root.glob("*-process/stderr.txt"))
        self.assertEqual(0, result.returncode, result.stderr + logs)
        self.assertFalse((f.root / "nunit").exists())
        self.assertFalse((f.root / "review-inputs").exists())
        self.assertTrue((f.root / "review-snapshot/COMPLETE").exists())
        self.assertTrue((f.root / "review-snapshot.json").exists())
        self.assertTrue((f.root / "review/self-test.review.pdf").is_file())
        self.assertTrue(json.loads(result.stdout)["RecoveryPreserved"])

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
        self.delivery_chain(False)

    def test_rehearsal_sign_mail_retention_uses_exact_attachments_and_does_not_resend(self):
        self.delivery_chain(True)

    def test_qualified_rehearsal_preserves_short_endurance_gap_through_signed_mail(self):
        self.delivery_chain(True, qualified=True)

    def assert_phase_two_rejects(self, f):
        originals = {p: p.read_bytes() for p in (f.root / "nunit").rglob("*") if p.is_file()}
        originals[f.root / "endurance-evidence.json"] = (f.root / "endurance-evidence.json").read_bytes()
        command = [os.environ["SUBMISSION_TEST_DOTNET"], os.environ["SUBMISSION_TEST_PROBE"],
                   "--automation-protected", str(f.root), str(Path(os.environ["SUBMISSION_TEST_BUNDLE"]).parent), "rehearsal-review"]
        result = subprocess.run(command, capture_output=True, text=True, timeout=120)
        self.assertNotEqual(0, result.returncode)
        self.assertIn("test-requirements-failed", result.stderr)
        assessment = json.loads((f.root / "test-assessment.json").read_text())
        self.assertFalse(assessment["Eligible"])
        for path in ("tests-finalized.json", "review-inputs", "review-snapshot", "review", "signed-review", "delivery", "synthetic-provider-calls.txt"):
            self.assertFalse((f.root / path).exists(), path)
        for path, original in originals.items():
            self.assertEqual(original, path.read_bytes())

    def short_endurance_fixture(self, actual_minutes, selected_minutes=None):
        f = self.fixture()
        envelope = json.loads((f.root / "endurance-evidence.json").read_text())
        start = datetime.fromisoformat(envelope["Observation"]["startedUtc"].replace("Z", "+00:00"))
        envelope["Observation"]["finishedUtc"] = (start + timedelta(minutes=actual_minutes)).isoformat()
        f.write_json(f.root / "endurance-evidence.json", envelope)
        f.write_json(f.root / "declarations.json", {"schemaVersion": 1, "identity": f.identity, "mode": "DeclaredGaps",
            "declarations": [{"requirementId": "second.duration", "reason": "One-hour rehearsal; not 24-hour production evidence."}]})
        if selected_minutes is not None:
            f.write_json(f.root / "synthetic-endurance-minutes.json", selected_minutes)
        return f

    def test_declaration_without_explicit_endurance_plan_stops_before_documents(self):
        self.assert_phase_two_rejects(self.short_endurance_fixture(60))

    def test_shorter_than_selected_endurance_stops_before_documents(self):
        self.assert_phase_two_rejects(self.short_endurance_fixture(30, 60))

    def test_failed_requirement_stops_before_documents_even_with_declaration(self):
        f = self.fixture()
        path = f.root / "nunit/observations.json"
        observations = json.loads(path.read_text())
        observation = observations["observations"][0]
        observation["outcome"] = "Failed"
        f.write_json(path, observations)
        f.write_json(f.root / "windows-tests.json", {"Files": [
            {"RelativePath": p.relative_to(f.root).as_posix(), "Sha256": sha(p.read_bytes())}
            for p in (f.root / "nunit").iterdir()]})
        f.write_json(f.root / "declarations.json", {"schemaVersion": 1, "identity": f.identity, "mode": "DeclaredGaps",
            "declarations": [{"requirementId": observation["requirementId"], "reason": "Disclosing a failure cannot make it pass."}]})
        self.assert_phase_two_rejects(f)

    def delivery_chain(self, rehearsal, qualified=False):
        f = self.fixture()
        root = f.root
        if qualified:
            f.write_json(root / "synthetic-endurance-minutes.json", 60)
            envelope = json.loads((root / "endurance-evidence.json").read_text())
            envelope["Observation"]["finishedUtc"] = "2026-01-01T01:00:00Z"
            f.write_json(root / "endurance-evidence.json", envelope)
            f.write_json(root / "declarations.json", {"schemaVersion": 1, "identity": f.identity, "mode": "DeclaredGaps",
                "declarations": [{"requirementId": "second.duration", "reason": "One-hour rehearsal; the required 24-hour endurance is not completed."}]})
        authority = Path(str(root) + "-authority")
        authority.mkdir()
        self.addCleanup(shutil.rmtree, authority)
        console = os.environ["SUBMISSION_TEST_BUNDLE"]

        def run(phase, expected=0):
            command = [os.environ["SUBMISSION_TEST_DOTNET"], os.environ["SUBMISSION_TEST_PROBE"],
                       "--automation-protected", str(root), str(Path(console).parent), ("rehearsal-" if rehearsal else "") + phase]
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
        if qualified:
            approval.update({key: review[key] for key in ("reviewMode", "verificationStatus", "declarationsSha256")})
            self.assertEqual("/Off", PdfReader(root / "review/self-test.review.pdf").get_fields()["Second"]["/V"])
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
        if rehearsal:
            approval.update(environment="Rehearsal", sendRehearsalEmail=True, recipient="rehearsal@example.org",
                            subject="[REHEARSAL] Driver Submission Package")
            if not qualified:
                request = json.loads((root / "delivery-request.json").read_text())
                approval["rehearsalPackageDownloadUrl"] = request["rehearsalPackageDownloadUrl"]
        if qualified:
            preview = json.loads((root / "delivery-request.json").read_text())
            approval = {"schemaVersion": 1, "packetSha256": preview["packetSha256"],
                        "correspondenceSha256": preview["correspondenceSha256"],
                        "expiresUtc": (now + timedelta(hours=1)).isoformat(), "visualReviewCompleted": True,
                        "producerAuthenticationConfirmed": True, "deliveryAuthorized": True}
        approve("deliver", approval)
        run("deliver")
        self.assertEqual("email\n" if rehearsal else "upload\nemail\n", (root / "synthetic-provider-calls.txt").read_text())
        retained = (root / "retained.json").read_bytes()
        run("deliver")
        self.assertEqual("email\n" if rehearsal else "upload\nemail\n", (root / "synthetic-provider-calls.txt").read_text())
        self.assertEqual(retained, (root / "retained.json").read_bytes())
        self.assertFalse(json.loads(retained)["CrestronAcceptanceEstablished"])

        self.assertEqual("RehearsalCompleted" if rehearsal else "Submitted", json.loads(retained)["State"])
        if rehearsal:
            from email import policy
            from email.parser import BytesParser
            messages = list((root / "mail-receipts").glob("*/message.eml"))
            self.assertEqual(1, len(messages))
            message = BytesParser(policy=policy.default).parsebytes(messages[0].read_bytes())
            self.assertEqual("rehearsal@example.org", str(message["To"]))
            self.assertEqual("[REHEARSAL] Driver Submission Package", str(message["Subject"]))
            attachments = {part.get_filename(): part.get_payload(decode=True) for part in message.iter_attachments()}
            self.assertEqual(1, len(attachments))
            from urllib.parse import quote
            expected_url = "https://github.com/synthetic/never-submitted/releases/download/synthetic/" + quote(signed["packageFileName"], safe="")
            self.assertIn(expected_url, message.get_body(preferencelist=("plain",)).get_content())
            self.assertEqual([expected_url], (root / "synthetic-download-checks.txt").read_text().splitlines())
            self.assertEqual(signed["packageSha256"], sha((root / "rehearsal-upload/package.pkg").read_bytes()))
            self.assertEqual(signed["signedFormSha256"], sha(attachments[signed["signedFormFileName"]]))

        if qualified:
            self.assertEqual("DeclaredGaps", signed["reviewMode"])
            self.assertEqual("GapsDeclared", signed["verificationStatus"])
            self.assertEqual(sha((root / "declarations.json").read_bytes()), signed["declarationsSha256"])
            self.assertIn("One-hour rehearsal", message.get_body(preferencelist=("plain",)).get_content())
            self.assertIn("REHEARSAL ONLY", message.get_body(preferencelist=("plain",)).get_content())
            signed_form = PdfReader(root / "signed-review/delivery" / signed["signedFormFileName"])
            self.assertEqual("/Off", signed_form.get_fields()["Second"]["/V"])
