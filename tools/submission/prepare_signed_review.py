# Copyright (c) 2026 Neil Colvin.
# Licensed under the MIT License. See LICENSE in the repository root.
"""Revalidate a private review and prepare authorized signed artifacts; never deliver."""

import argparse
from datetime import datetime, timezone
import hashlib
import json
from pathlib import Path
import re
import subprocess
import sys
import tempfile
import zipfile

from pypdf.errors import PdfReadError

from build_help import keys, sha, strict_object
from package_help import read_json, write_json
from render_help import run_process
from self_test_form import pinned_json
import sign_self_test_form as signing
from review_android import retained_files


def copy_pinned(source, destination, digest, limit):
    if not isinstance(digest, str) or not re.fullmatch(r"[0-9a-f]{64}", digest):
        raise ValueError("Supply independently retained lowercase SHA-256 pins")
    total, hashed = 0, hashlib.sha256()
    with Path(source).open("rb") as incoming, Path(destination).open("xb") as outgoing:
        while block := incoming.read(1024 * 1024):
            total += len(block)
            if total > limit:
                raise ValueError("Review input exceeds its size limit")
            hashed.update(block)
            outgoing.write(block)
    if hashed.hexdigest() != digest:
        raise ValueError("Retained review input changed")


def prepare(settings_path, review_digest, authorization_digest):
    _, settings = read_json(settings_path)
    keys(settings, ("schemaVersion", "reviewDirectory", "authorization", "signatureImage", "dotnet", "validator", "output"))
    if type(settings["schemaVersion"]) is not int or settings["schemaVersion"] != 1:
        raise ValueError("Unsupported signing-stage settings version")
    for name in ("reviewDirectory", "authorization", "signatureImage", "dotnet", "validator", "output"):
        if not isinstance(settings[name], str) or not Path(settings[name]).is_absolute():
            raise ValueError("Signing-stage settings require absolute private paths")
    review = Path(settings["reviewDirectory"])
    receipt_bytes, receipt = pinned_json(review / "review-receipt.json", review_digest)
    if not isinstance(receipt["bundleSha256"], str) or not re.fullmatch(r"[0-9a-fA-F]{64}", receipt["bundleSha256"]):
        raise ValueError("Review bundle digest is invalid")
    if (type(receipt["schemaVersion"]) is not int or receipt["schemaVersion"] != 1 or
            receipt["state"] != "UnsignedReviewPrepared" or receipt.get("signingCopy") is not True or
            receipt["submissionReady"] is not False or receipt["deliveryAttempted"] is not False or
            (review / "COMPLETE").read_text(encoding="ascii").strip() != review_digest):
        raise ValueError("A completed unsigned signing-copy review is required")
    if not re.fullmatch(r"[0-9a-f]{40}", receipt["sourceCommit"]):
        raise ValueError("Review has no full release source commit")
    _, approval = pinned_json(settings["authorization"], authorization_digest)
    if (approval["formSha256"] != receipt["formSha256"] or
            approval["formReportSha256"] != receipt["formReportSha256"] or
            approval["inventorySha256"] != receipt["inventorySha256"] or
            approval["candidateSha256"] != receipt["candidateSha256"]):
        raise ValueError("Signing authorization does not identify this exact review")
    output = Path(settings["output"])
    if output.exists() or not output.parent.is_dir():
        raise ValueError("Use a new signed-review output under an existing private parent")
    android_files = retained_files(review, receipt)
    with tempfile.TemporaryDirectory(prefix=".submission-signing-", dir=output.parent) as temporary:
        staging = Path(temporary)
        # Freeze pinned bytes before validation/signing. Raw evidence and signing
        # inputs never enter the delivery folder or a public CI artifact.
        for name, digest, limit in (
                ("self-test.review.pdf", receipt["formSha256"], 64 * 1024 * 1024),
                ("form-report.json", receipt["formReportSha256"], 8 * 1024 * 1024),
                ("inventory.json", receipt["inventorySha256"], 8 * 1024 * 1024),
                ("mapping.json", receipt["mappingSha256"], 8 * 1024 * 1024),
                ("evidence.zip", receipt["bundleSha256"].lower(), 513 * 1024 * 1024)):
            copy_pinned(review / name, staging / name, digest, limit)
        _, form_report = read_json(staging / "form-report.json")
        checked = run_process([settings["dotnet"], settings["validator"], "submission-bundle-check",
                               "--bundle", str(staging / "evidence.zip"),
                               "--bundle-sha256", receipt["bundleSha256"].lower(),
                               "--candidate-sha256", receipt["candidateSha256"], "--scratch", str(staging)], 180)
        if checked.returncode != 0:
            raise ValueError("Evidence no longer passes validation; no signature was applied")
        revalidated_utc = datetime.now(timezone.utc).isoformat()
        bundle = json.loads(checked.stdout, object_pairs_hook=strict_object)
        validation = bundle["Validation"]
        identity = form_report["identity"]
        if (bundle["ValidationChecksPassed"] is not True or
                bundle["BundleSha256"].lower() != receipt["bundleSha256"].lower() or
                validation["CandidateSha256"] != receipt["candidateSha256"] or
                validation["ObservationsSha256"] != identity["observationsSha256"] or
                validation["Package"]["Sha256"] != approval["packageSha256"] or
                identity["packageSha256"] != approval["packageSha256"]):
            raise ValueError("Current evidence validation does not match the reviewed form and authorization")
        completed = staging / "completed"
        delivery = completed / "delivery"
        delivery.mkdir(parents=True)
        with zipfile.ZipFile(staging / "evidence.zip") as archive:
            candidate = json.loads(archive.read("candidate.json"), object_pairs_hook=strict_object)
            if candidate["identity"]["sourceCommit"] != receipt["sourceCommit"]:
                raise ValueError("Retained candidate source differs from the review")
            if not re.fullmatch(r"\d+\.\d+\.\d+\.0+", candidate["packageRequirements"]["driverVersion"]):
                raise ValueError("Signing requires an actual-driver Release candidate with revision zero")
            packages = [entry for entry in archive.infolist() if entry.filename.startswith("package/")]
            if len(packages) != 1:
                raise ValueError("A single validated production package is required")
            entry = packages[0]
            filename = entry.filename.removeprefix("package/")
            if (filename != Path(filename).name or any(c in filename for c in "/\\:") or
                    not filename.endswith(".pkg") or entry.file_size > 64 * 1024 * 1024):
                raise ValueError("Invalid delivery package filename or size")
            package = archive.read(entry)
            if sha(package) != approval["packageSha256"]:
                raise ValueError("Delivery package differs from the authorized bytes")
            (delivery / filename).write_bytes(package)
        signed_form = delivery / "Driver-Self-Test.signed.pdf"
        signed = signing.sign(staging / "self-test.review.pdf", staging / "form-report.json", staging / "inventory.json",
                              settings["authorization"], authorization_digest, settings["signatureImage"], signed_form)
        write_json(completed / "signing-report.json", signed)
        write_json(completed / "validation-report.json", bundle)
        (completed / "review-receipt.json").write_bytes(receipt_bytes)
        for name, data in android_files.items():
            (completed / name).write_bytes(data)
        result = {"schemaVersion": 1, "state": "SignedReviewPrepared", "sourceCommit": receipt["sourceCommit"],
                  "reviewReceiptSha256": review_digest, "authorizationSha256": authorization_digest,
                  "candidateSha256": receipt["candidateSha256"], "bundleSha256": receipt["bundleSha256"].lower(),
                  "packageFileName": filename, "packageSha256": approval["packageSha256"],
                  "signedFormFileName": signed_form.name, "signedFormSha256": signed["signedFormSha256"],
                  "signingReportSha256": sha((completed / "signing-report.json").read_bytes()),
                  "validationReportSha256": sha((completed / "validation-report.json").read_bytes()),
                  "evidenceRevalidatedUtc": revalidated_utc,
                  "signatureApplied": True, "visualReviewRequired": True, "deliveryAuthorized": False,
                  "submissionReady": False, "deliveryAttempted": False}
        write_json(completed / "signed-review-receipt.json", result)
        output.mkdir()
        for item in completed.iterdir():
            item.rename(output / item.name)
        with (output / "COMPLETE").open("x", encoding="ascii") as marker:
            marker.write(sha((output / "signed-review-receipt.json").read_bytes()) + "\n")
        return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ("settings", "review-sha256", "authorization-sha256"):
        parser.add_argument("--" + name, required=True)
    args = parser.parse_args()
    try:
        print(json.dumps(prepare(args.settings, args.review_sha256, args.authorization_sha256), indent=2))
        return 0
    except (ValueError, OSError, KeyError, TypeError, PdfReadError, zipfile.BadZipFile, subprocess.SubprocessError):
        print("Signed review preparation failed. Inspect private inputs; no delivery was attempted.", file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
