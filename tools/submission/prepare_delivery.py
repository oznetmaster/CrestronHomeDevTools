# Copyright (c) 2026 Neil Colvin.
# Licensed under the MIT License. See LICENSE in the repository root.
"""Prepare an explicitly approved delivery plan from signed artifacts; never send."""

import argparse
from datetime import datetime, timezone
import json
from pathlib import Path
import re
import subprocess
import sys
import tempfile
import zipfile

from build_help import keys, sha, strict_object
from package_help import read_json, write_json
from prepare_signed_review import copy_pinned
from render_help import run_process
from self_test_form import pinned_json


def require_authorization(approval, receipt, receipt_digest, now):
    keys(approval, ("schemaVersion", "signedReviewSha256", "candidateSha256", "packageSha256",
                    "signedFormSha256", "sender", "recipient", "subject", "expiresUtc",
                    "signedVisualReviewCompleted", "deliveryAuthorized"))
    if (type(approval["schemaVersion"]) is not int or approval["schemaVersion"] != 1 or
            approval["signedVisualReviewCompleted"] is not True or approval["deliveryAuthorized"] is not True):
        raise ValueError("Separate final signed-form review and delivery authorization are required")
    if approval["signedReviewSha256"] != receipt_digest or any(
            approval[name] != receipt[name] for name in ("candidateSha256", "packageSha256", "signedFormSha256")):
        raise ValueError("Delivery authorization does not identify these exact signed artifacts")
    if not isinstance(approval["expiresUtc"], str):
        raise ValueError("Delivery authorization expiry must be a timezone-qualified timestamp")
    expires = datetime.fromisoformat(approval["expiresUtc"].replace("Z", "+00:00"))
    if expires.tzinfo is None or expires <= now:
        raise ValueError("Delivery authorization has expired or lacks a timezone")
    # Require a plain ASCII mailbox, not display names, lists or header content.
    sender = approval["sender"]
    if (not isinstance(sender, str) or len(sender) > 254 or not re.fullmatch(
            r"[A-Za-z0-9!#$%&'*+/=?^_`{|}~-]+(?:\.[A-Za-z0-9!#$%&'*+/=?^_`{|}~-]+)*@"
            r"[A-Za-z0-9](?:[A-Za-z0-9-]*[A-Za-z0-9])?(?:\.[A-Za-z0-9](?:[A-Za-z0-9-]*[A-Za-z0-9])?)+", sender)):
        raise ValueError("Provide the approved plain sender mailbox")
    if approval["recipient"] != "drivers@crestron.com" or approval["subject"] != "Driver Submission Package":
        raise ValueError("Delivery must use the documented Crestron recipient and subject")


def prepare(settings_path, signed_review_digest, authorization_digest):
    _, settings = read_json(settings_path)
    keys(settings, ("schemaVersion", "signedReviewDirectory", "reviewDirectory", "authorization",
                    "dotnet", "validator", "output"))
    if type(settings["schemaVersion"]) is not int or settings["schemaVersion"] != 1:
        raise ValueError("Unsupported delivery-stage settings version")
    for name in ("signedReviewDirectory", "reviewDirectory", "authorization", "dotnet", "validator", "output"):
        if not isinstance(settings[name], str) or not Path(settings[name]).is_absolute():
            raise ValueError("Delivery-stage settings require absolute private paths")
    for pin in (signed_review_digest, authorization_digest):
        if not isinstance(pin, str) or not re.fullmatch(r"[0-9a-f]{64}", pin):
            raise ValueError("Supply independently approved lowercase SHA-256 pins")
    signed, review = Path(settings["signedReviewDirectory"]), Path(settings["reviewDirectory"])
    receipt_bytes, receipt = pinned_json(signed / "signed-review-receipt.json", signed_review_digest)
    if (type(receipt["schemaVersion"]) is not int or receipt["schemaVersion"] != 1 or
            receipt["state"] != "SignedReviewPrepared" or receipt["signatureApplied"] is not True or
            receipt["deliveryAuthorized"] is not False or receipt["deliveryAttempted"] is not False or
            receipt["submissionReady"] is not False or receipt["visualReviewRequired"] is not True or
            (signed / "COMPLETE").read_text(encoding="ascii").strip() != signed_review_digest):
        raise ValueError("A completed signed review is required")
    authorization_bytes, approval = pinned_json(settings["authorization"], authorization_digest)
    require_authorization(approval, receipt, signed_review_digest, datetime.now(timezone.utc))
    _, unsigned = pinned_json(review / "review-receipt.json", receipt["reviewReceiptSha256"])
    _, retained = pinned_json(signed / "review-receipt.json", receipt["reviewReceiptSha256"])
    if (unsigned != retained or unsigned["state"] != "UnsignedReviewPrepared" or unsigned["signingCopy"] is not True or
            (review / "COMPLETE").read_text(encoding="ascii").strip() != receipt["reviewReceiptSha256"] or
            any(unsigned[name] != receipt[name] for name in ("candidateSha256", "sourceCommit")) or
            unsigned["bundleSha256"].lower() != receipt["bundleSha256"]):
        raise ValueError("Original review differs from the signed review")
    _, report = pinned_json(signed / "signing-report.json", receipt["signingReportSha256"])
    pinned_json(signed / "validation-report.json", receipt["validationReportSha256"])
    if (report["signatureApplied"] is not True or report["unsignedFormSha256"] != unsigned["formSha256"] or
            any(report[name] != receipt[name] for name in
                ("candidateSha256", "packageSha256", "signedFormSha256", "authorizationSha256"))):
        raise ValueError("Signing report does not match the reviewed artifacts")
    _, form = pinned_json(review / "form-report.json", unsigned["formReportSha256"])
    output = Path(settings["output"])
    if output.exists() or not output.parent.is_dir():
        raise ValueError("Use a new delivery output under an existing private parent")
    for key, extension in (("packageFileName", ".pkg"), ("signedFormFileName", ".signed.pdf")):
        name = receipt[key]
        if (not isinstance(name, str) or name != Path(name).name or any(c in name for c in '/\\:') or
                any(ord(c) < 32 for c in name) or not name.endswith(extension)):
            raise ValueError("Invalid delivery filename")
    with tempfile.TemporaryDirectory(prefix=".submission-delivery-", dir=output.parent) as temporary:
        staging = Path(temporary)
        completed = staging / "completed"
        delivery = completed / "delivery"
        delivery.mkdir(parents=True)
        for filename, digest in ((receipt["packageFileName"], receipt["packageSha256"]),
                                 (receipt["signedFormFileName"], receipt["signedFormSha256"])):
            copy_pinned(signed / "delivery" / filename, delivery / filename, digest, 64 * 1024 * 1024)
        copy_pinned(review / "evidence.zip", staging / "evidence.zip", receipt["bundleSha256"], 513 * 1024 * 1024)
        checked = run_process([settings["dotnet"], settings["validator"], "submission-bundle-check",
                               "--bundle", str(staging / "evidence.zip"), "--bundle-sha256", receipt["bundleSha256"],
                               "--candidate-sha256", receipt["candidateSha256"], "--scratch", str(staging)], 180)
        if checked.returncode != 0:
            raise ValueError("Evidence no longer passes validation; no delivery was attempted")
        bundle = json.loads(checked.stdout, object_pairs_hook=strict_object)
        validation = bundle["Validation"]
        if (bundle["ValidationChecksPassed"] is not True or bundle["BundleSha256"].lower() != receipt["bundleSha256"] or
                validation["CandidateSha256"] != receipt["candidateSha256"] or
                validation["Package"]["Sha256"] != receipt["packageSha256"] or
                validation["ObservationsSha256"] != form["identity"]["observationsSha256"]):
            raise ValueError("Fresh validation differs from the approved signed review")
        with zipfile.ZipFile(staging / "evidence.zip") as archive:
            candidate = json.loads(archive.read("candidate.json"), object_pairs_hook=strict_object)
            if (candidate["identity"]["sourceCommit"] != receipt["sourceCommit"] or
                    not re.fullmatch(r"\d+\.\d+\.\d+\.0+", candidate["packageRequirements"]["driverVersion"])):
                raise ValueError("The reviewed production Release source must be unchanged")
        # Long validation must not let a now-expired approval produce a usable plan.
        require_authorization(approval, receipt, signed_review_digest, datetime.now(timezone.utc))
        plan = {key: receipt[key] for key in ("candidateSha256", "packageSha256", "signedFormSha256",
                                             "packageFileName", "signedFormFileName")}
        plan.update(reviewSha256=signed_review_digest, authorizationSha256=authorization_digest,
                    sender=approval["sender"], recipient=approval["recipient"])
        write_json(completed / "delivery-plan.json", plan)
        write_json(completed / "validation-report.json", bundle)
        (completed / "signed-review-receipt.json").write_bytes(receipt_bytes)
        (completed / "delivery-authorization.json").write_bytes(authorization_bytes)
        result = {"schemaVersion": 1, "state": "DeliveryPlanPrepared", "sourceCommit": receipt["sourceCommit"],
                  "signedReviewSha256": signed_review_digest, "authorizationSha256": authorization_digest,
                  "planFileSha256": sha((completed / "delivery-plan.json").read_bytes()),
                  "validationReportSha256": sha((completed / "validation-report.json").read_bytes()),
                  "evidenceRevalidatedUtc": datetime.now(timezone.utc).isoformat(), "expiresUtc": approval["expiresUtc"],
                  "subject": approval["subject"], "deliveryAuthorized": True, "deliveryAttempted": False,
                  "submissionReady": False}
        write_json(completed / "delivery-review-receipt.json", result)
        output.mkdir()
        for item in completed.iterdir():
            item.rename(output / item.name)
        with (output / "COMPLETE").open("x", encoding="ascii") as marker:
            marker.write(sha((output / "delivery-review-receipt.json").read_bytes()) + "\n")
        return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ("settings", "signed-review-sha256", "authorization-sha256"):
        parser.add_argument("--" + name, required=True)
    args = parser.parse_args()
    try:
        print(json.dumps(prepare(args.settings, args.signed_review_sha256, args.authorization_sha256), indent=2))
        return 0
    except (ValueError, OSError, KeyError, TypeError, zipfile.BadZipFile, subprocess.SubprocessError):
        print("Delivery preparation failed. Inspect private inputs; no upload or email was attempted.", file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
