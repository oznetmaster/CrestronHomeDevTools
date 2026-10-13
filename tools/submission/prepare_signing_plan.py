# Copyright (c) 2026 Neil Colvin. MIT licensed.
"""Prepare exact signing arguments from a reviewed receipt and existing authorization. Never sign."""
import argparse
import json
from pathlib import Path
import sys
from build_help import sha
from package_help import write_json
from self_test_form import pinned_json


def prepare(review_directory, review_digest, authorization, authorization_digest, output, signed_output, credentials):
    review, output, signed_output = map(Path, (review_directory, output, signed_output))
    for path in (review, output, signed_output, Path(authorization), Path(credentials)):
        if not path.is_absolute():
            raise ValueError("Use absolute private paths")
    if output.exists() or signed_output.exists() or not output.parent.is_dir() or not signed_output.parent.is_dir():
        raise ValueError("Use fresh preparation and signed-output directories")
    if output == signed_output or output in signed_output.parents or signed_output in output.parents:
        raise ValueError("Preparation and signed output must be separate directories")
    _, receipt = pinned_json(review / "review-receipt.json", review_digest)
    _, approval = pinned_json(authorization, authorization_digest)
    if ((review / "COMPLETE").read_text(encoding="ascii").strip() != review_digest or
            receipt.get("signingCopy") is not True or receipt.get("signatureApplied", False) or
            receipt.get("state") not in ("UnsignedReviewPrepared", "UnsignedReviewWithDeclaredGapsPrepared")):
        raise ValueError("Require the completed unsigned signing-copy receipt")
    if approval.get("visualReviewCompleted") is not True or approval.get("signatureAuthorized") is not True:
        raise ValueError("Existing exact signing authorization is required; preparation cannot grant it")
    for key in ("formSha256", "formReportSha256", "inventorySha256", "candidateSha256"):
        if approval[key] != receipt[key]:
            raise ValueError("Authorization identifies a different review")
    for key in ("reviewMode", "verificationStatus", "declarationsSha256"):
        if approval.get(key) != receipt.get(key):
            raise ValueError("Authorization does not identify this declared-gap review")
    for filename, key in (("self-test.review.pdf", "formSha256"), ("form-report.json", "formReportSha256"),
                          ("inventory.json", "inventorySha256"), ("mapping.json", "mappingSha256"),
                          ("evidence.zip", "bundleSha256")):
        if sha((review / filename).read_bytes()) != receipt[key].lower():
            raise ValueError("Reviewed input bytes changed")
    _, report = pinned_json(review / "form-report.json", receipt["formReportSha256"])
    if report["identity"]["packageSha256"] != approval["packageSha256"]:
        raise ValueError("Authorization package differs from reviewed form")
    # Never read the named credential file or the image while preparing arguments.
    settings = {"schemaVersion": 1, "reviewDirectory": str(review), "authorization": str(authorization),
                "output": str(signed_output)}
    output.mkdir()
    settings_path = output / "signing-settings.json"
    write_json(settings_path, settings)
    arguments = ["prepare-signed-review", "--settings", str(settings_path), "--review-sha256", review_digest,
                 "--authorization-sha256", authorization_digest, "--credentials", str(credentials)]
    result = {"schemaVersion": 1, "arguments": arguments, "settingsSha256": sha(settings_path.read_bytes()),
              "reviewSha256": review_digest, "authorizationSha256": authorization_digest,
              "signatureRead": False, "signatureApplied": False, "deliveryAttempted": False}
    write_json(output / "signing-plan.json", result)
    (output / "COMPLETE").write_text(sha((output / "signing-plan.json").read_bytes()) + "\n", encoding="ascii")
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ("review-directory", "review-sha256", "authorization", "authorization-sha256", "output", "signed-output", "credentials"):
        parser.add_argument("--" + name, required=True)
    args = parser.parse_args()
    try:
        print(json.dumps(prepare(args.review_directory, args.review_sha256, args.authorization,
              args.authorization_sha256, args.output, args.signed_output, args.credentials), indent=2))
        return 0
    except (ValueError, OSError, KeyError, TypeError):
        print("Signing plan refused; check exact review, authorization and fresh private paths. No signature was accessed.", file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
