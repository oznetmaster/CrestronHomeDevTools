# Copyright (c) 2026 Neil Colvin.
# Licensed under the MIT License. See LICENSE in the repository root.
"""Revalidate a completed delivery handoff offline immediately before an external step."""

import argparse
from datetime import datetime, timezone
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import zipfile

from build_help import keys, sha
from package_help import read_json, write_json
from prepare_signed_review import copy_pinned
import prepare_delivery
from self_test_form import pinned_json
from review_android import retained_files


def revalidate(settings_path, delivery_review_digest, signed_review_digest, authorization_digest):
    _, settings = read_json(settings_path)
    keys(settings, ("schemaVersion", "preparedDirectory", "preparationSettings", "output"))
    if type(settings["schemaVersion"]) is not int or settings["schemaVersion"] != 1:
        raise ValueError("Unsupported revalidation settings")
    for key in ("preparedDirectory", "preparationSettings", "output"):
        if not isinstance(settings[key], str) or not Path(settings[key]).is_absolute():
            raise ValueError("Use absolute private revalidation paths")
    prepared = Path(settings["preparedDirectory"])
    output = Path(settings["output"])
    if output.exists() or not output.parent.is_dir():
        raise ValueError("Use a new output directory under an existing private parent")
    original_bytes, receipt = pinned_json(prepared / "delivery-review-receipt.json", delivery_review_digest)
    if (type(receipt["schemaVersion"]) is not int or receipt["schemaVersion"] != 1 or
            receipt["state"] != "DeliveryPlanPrepared" or receipt["deliveryAuthorized"] is not True or
            receipt["deliveryAttempted"] is not False or receipt["submissionReady"] is not False or
            (prepared / "COMPLETE").read_text(encoding="ascii").strip() != delivery_review_digest or
            receipt["signedReviewSha256"] != signed_review_digest or receipt["authorizationSha256"] != authorization_digest):
        raise ValueError("Completed delivery handoff differs from the approved pins")
    plan_bytes, plan = pinned_json(prepared / "delivery-plan.json", receipt["planFileSha256"])
    keys(plan, ("candidateSha256", "packageSha256", "signedFormSha256", "packageFileName", "signedFormFileName",
                "reviewSha256", "authorizationSha256", "sender", "recipient"))
    if plan["reviewSha256"] != signed_review_digest or plan["authorizationSha256"] != authorization_digest:
        raise ValueError("Delivery plan approval pins differ")
    _, signed = pinned_json(prepared / "signed-review-receipt.json", signed_review_digest)
    _, approval = pinned_json(prepared / "delivery-authorization.json", authorization_digest)
    pinned_json(prepared / "validation-report.json", receipt["validationReportSha256"])
    prepare_delivery.require_authorization(approval, signed, signed_review_digest, datetime.now(timezone.utc))
    _, original_settings = read_json(settings["preparationSettings"])
    keys(original_settings, ("schemaVersion", "signedReviewDirectory", "reviewDirectory", "authorization", "output"), ("dotnet", "validator"))
    _, unsigned = pinned_json(Path(original_settings["reviewDirectory"]) / "review-receipt.json", signed["reviewReceiptSha256"])
    prepared_android = retained_files(prepared, unsigned)
    # Reject output layouts that could alter any retained review tree or private settings input.
    resolved = output.resolve()
    for source in (prepared, Path(original_settings["signedReviewDirectory"]), Path(original_settings["reviewDirectory"]),
                   Path(settings["preparationSettings"]), Path(original_settings["authorization"]), Path(settings_path)):
        source = source.resolve()
        if resolved == source or resolved in source.parents or source in resolved.parents:
            raise ValueError("Keep revalidation output separate from all original inputs")
    with tempfile.TemporaryDirectory(prefix=".delivery-revalidation-", dir=output.parent) as temporary:
        staging = Path(temporary)
        # Each call runs the full existing signed chain and real evidence validator again.
        fresh_settings = dict(original_settings, output=str(staging / "fresh"))
        write_json(staging / "private-settings.json", fresh_settings)
        fresh = prepare_delivery.prepare(staging / "private-settings.json", signed_review_digest, authorization_digest)
        fresh_root = staging / "fresh"
        if retained_files(fresh_root, unsigned) != prepared_android:
            raise ValueError("Prepared Android audit differs from the fresh review")
        fresh_plan_bytes, fresh_plan = pinned_json(fresh_root / "delivery-plan.json", fresh["planFileSha256"])
        if fresh_plan_bytes != plan_bytes or fresh_plan != plan or fresh["sourceCommit"] != receipt["sourceCommit"]:
            raise ValueError("Fresh review no longer produces the exact authorized delivery plan")
        # Validate the actual previously prepared send files too, not just upstream copies.
        for name, digest in ((plan["packageFileName"], plan["packageSha256"]), (plan["signedFormFileName"], plan["signedFormSha256"])):
            if not isinstance(name, str) or name != Path(name).name or any(c in name for c in '/\\:'):
                raise ValueError("Delivery filename is not a basename")
            copy_pinned(prepared / "delivery" / name, staging / ("checked-" + name), digest, 64 * 1024 * 1024)
        # Recheck original completion and approval after potentially lengthy validation.
        if ((prepared / "COMPLETE").read_text(encoding="ascii").strip() != delivery_review_digest or
                pinned_json(prepared / "delivery-review-receipt.json", delivery_review_digest)[0] != original_bytes):
            raise ValueError("Original handoff changed during validation")
        pinned_json(original_settings["authorization"], authorization_digest)
        prepare_delivery.require_authorization(approval, signed, signed_review_digest, datetime.now(timezone.utc))
        result = {"schemaVersion": 1, "state": "DeliveryRevalidated", "deliveryReviewSha256": delivery_review_digest,
                  "signedReviewSha256": signed_review_digest, "authorizationSha256": authorization_digest,
                  "planFileSha256": sha(plan_bytes), "plan": plan, "expiresUtc": approval["expiresUtc"],
                  "revalidatedUtc": datetime.now(timezone.utc).isoformat(), "deliveryAttempted": False,
                  "submissionReady": False, "validationReportSha256": fresh["validationReportSha256"]}
        write_json(fresh_root / "revalidation-receipt.json", result)
        # Retain the fresh chain and exact copies privately; the marker is published last.
        output.mkdir()
        for item in fresh_root.iterdir():
            if item.name != "COMPLETE":
                item.rename(output / item.name)
        with (output / "COMPLETE").open("x", encoding="ascii") as marker:
            marker.write(sha((output / "revalidation-receipt.json").read_bytes()) + "\n")
        return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ("settings", "delivery-review-sha256", "signed-review-sha256", "authorization-sha256"):
        parser.add_argument("--" + name, required=True)
    args = parser.parse_args()
    try:
        print(json.dumps(revalidate(args.settings, args.delivery_review_sha256, args.signed_review_sha256,
                                    args.authorization_sha256), indent=2))
        return 0
    except (ValueError, OSError, KeyError, TypeError, zipfile.BadZipFile, subprocess.SubprocessError):
        print("Delivery revalidation failed. No upload or email was attempted; inspect private inputs.", file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
