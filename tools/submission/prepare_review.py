# Copyright (c) 2026 Neil Colvin.
# Licensed under the MIT License. See LICENSE in the repository root.
"""Prepare private, unsigned submission review artifacts; never sign or deliver."""

import argparse
import json
from pathlib import Path
import re
import subprocess
import sys
import tempfile

from pypdf.errors import PdfReadError
from reportlab.platypus.doctemplate import LayoutError
from lxml.etree import XMLSyntaxError

from build_help import keys, sha, strict_object
from package_help import read_json, write_json
from render_help import run_process
import self_test_form as forms
from review_android import audit_runs
from validator_runtime import settings_validator


def prepare(settings_path, candidate_digest, inventory_digest, mapping_digest, source_commit, artifact_kind, *, signing_copy=False,
            android_pins=None, android_pins_sha256=None, review_mode="complete", declarations=None, declarations_sha256=None):
    # These pins come from the trusted release job, separately from worker settings.
    if artifact_kind != "driver":
        raise ValueError("Submission review is only available for an explicitly selected driver release")
    if type(signing_copy) is not bool:
        raise ValueError("Signing-copy selection must be a boolean")
    if review_mode not in ("complete", "declared-gaps"):
        raise ValueError("Review mode must be complete or declared-gaps")
    declared_gaps = review_mode == "declared-gaps"
    if declared_gaps:
        if not declarations or not re.fullmatch(r"[0-9a-f]{64}", declarations_sha256 or ""):
            raise ValueError("Declared-gap review requires pinned declarations")
        if not Path(declarations).is_absolute():
            raise ValueError("Gap declarations require an absolute private path")
        _, gap_document = forms.pinned_json(declarations, declarations_sha256)
    elif declarations is not None or declarations_sha256 is not None:
        raise ValueError("Gap declarations require explicit declared-gaps review mode")
    for digest in (candidate_digest, inventory_digest, mapping_digest):
        if not re.fullmatch(r"[0-9a-f]{64}", digest):
            raise ValueError("Supply independent lowercase SHA-256 release pins")
    if not re.fullmatch(r"[0-9a-f]{40}", source_commit):
        raise ValueError("Supply the full release source commit")
    _, settings = read_json(settings_path)
    keys(settings, ("schemaVersion", "candidate", "inventory", "mapping", "policy", "observations",
                    "package", "template", "evidence", "output", "title", "author"), ("androidEvidence", "dotnet", "validator"))
    if type(settings["schemaVersion"]) is not int or settings["schemaVersion"] != 1:
        raise ValueError("Unsupported review settings version")
    for key in ("candidate", "inventory", "mapping", "policy", "observations", "package", "template",
                "evidence", "output"):
        if not isinstance(settings[key], str) or not Path(settings[key]).is_absolute():
            raise ValueError("Review settings require absolute private paths")
    validator_args = settings_validator(settings)
    _, candidate = forms.pinned_json(settings["candidate"], candidate_digest)
    if candidate["identity"]["sourceCommit"] != source_commit:
        raise ValueError("Candidate does not identify the selected release commit")
    version = candidate["packageRequirements"]["driverVersion"]
    if not re.fullmatch(r"\d+\.\d+\.\d+\.0+", version):
        raise ValueError("Submission review requires a Release version with revision zero")
    _, inventory = forms.pinned_json(settings["inventory"], inventory_digest)
    source = Path(settings["template"]).read_bytes()
    if sha(source) != inventory["templateSha256"]:
        raise ValueError("Official form template digest mismatch")
    output = Path(settings["output"])
    if output.exists() or not output.parent.is_dir():
        raise ValueError("Use a new review output directory under an existing private parent")
    android = audit_runs(settings, android_pins, android_pins_sha256, candidate_digest)
    _, policy = forms.pinned_json(settings["policy"], candidate["identity"]["policySha256"])
    android_scopes = {rule["id"] for rule in policy["requirements"]
                      if isinstance(rule.get("execution"), dict) and rule["execution"].get("method") == "android"}
    if android is None and android_scopes:
        # Only wholly unperformed, explicitly declared scopes can lack Android run evidence.
        # Supplied observations still require the normal raw-run audit, regardless of pass/failure.
        _, observed = read_json(settings["observations"])
        observed_ids = {item["requirementId"] for item in observed["observations"]}
        declared_ids = {item["requirementId"] for item in gap_document["declarations"]} if declared_gaps else set()
        if not declared_gaps or not android_scopes <= declared_ids or android_scopes & observed_ids:
            raise ValueError("Android policy requirements need independently pinned Android evidence")
    # Only our own random staging directory is cleaned after failure. No success
    # output is exposed until both artifacts and their shared identities agree.
    with tempfile.TemporaryDirectory(prefix=".submission-review-", dir=output.parent) as temporary:
        staging = Path(temporary)
        completed = staging / "completed"
        completed.mkdir()
        rows, identity, validation_json = forms.validate_evidence(
            inventory, inventory_digest, settings["mapping"], mapping_digest,
            settings["candidate"], candidate_digest, settings["policy"], settings["observations"],
            settings["package"], settings["template"], settings["evidence"], settings.get("dotnet"), settings.get("validator"),
            **({"declarations_path": declarations, "declarations_digest": declarations_sha256} if declared_gaps else {}))
        identity["inventorySha256"] = inventory_digest
        form = completed / "self-test.review.pdf"
        report = forms.write_form(source, inventory, form, settings["title"], settings["author"], rows, identity, False,
                                  signing_copy=signing_copy, **({"declared_gaps": True} if declared_gaps else {}))
        report["validationReportJson"] = validation_json
        write_json(completed / "form-report.json", report)
        arguments = [*validator_args, "submission-review-bundle-create" if declared_gaps else "submission-bundle-create",
                     "--output", str(completed / "evidence.zip"), "--candidate-sha256", candidate_digest]
        for key in ("candidate", "package", "policy", "template", "observations", "evidence"):
            arguments.extend(("--" + key, settings[key]))
        if declared_gaps:
            arguments.extend(("--declarations", str(declarations), "--declarations-sha256", declarations_sha256, "--mode", "declared-gaps"))
        result = run_process(arguments, 180)
        if result.returncode != 0:
            raise ValueError("Evidence bundle validation failed; no completed review was published")
        bundle = json.loads(result.stdout, object_pairs_hook=strict_object)
        if declared_gaps:
            reviewed = bundle["review"]
            checked = reviewed["validation"]
            if (bundle["readyForReview"] is not True or reviewed["declarationsSha256"] != declarations_sha256 or
                    reviewed["assessment"]["verificationStatus"] != identity["verificationStatus"]):
                raise ValueError("Archived review does not match the form's declarations and assessment")
            valid = reviewed["readyForReview"]
            bound_candidate = checked["candidateSha256"]
            bound_observations = checked["observationsSha256"]
            bound_package = checked["package"]["sha256"]
            bundle_digest = bundle["bundleSha256"]
        else:
            checked = bundle["Validation"]
            valid = bundle["ValidationChecksPassed"]
            bound_candidate = checked["CandidateSha256"]
            bound_observations = checked["ObservationsSha256"]
            bound_package = checked["Package"]["Sha256"]
            bundle_digest = bundle["BundleSha256"]
        if (valid is not True or bound_candidate != candidate_digest or
                bound_observations != identity["observationsSha256"] or bound_package != identity["packageSha256"] or
                sha((completed / "evidence.zip").read_bytes()) != bundle_digest.lower() or
                sha(form.read_bytes()) != report["formSha256"]):
            raise ValueError("Form and retained evidence do not describe the same candidate snapshot")
        write_json(completed / "bundle-report.json", bundle)
        # Retain the independently pinned mapping/inventory bytes used by the form.
        for key, digest in (("mapping", mapping_digest), ("inventory", inventory_digest)):
            data, _ = forms.pinned_json(settings[key], digest)
            (completed / (key + ".json")).write_bytes(data)
        if declared_gaps:
            data, _ = forms.pinned_json(declarations, declarations_sha256)
            (completed / "declarations.json").write_bytes(data)
        # Revalidate after the form/bundle operations, including independent pins.
        # Do not accept a worker's precomputed audit summary in place of raw data.
        android_report_sha256 = None
        if android is not None:
            if audit_runs(settings, android_pins, android_pins_sha256, candidate_digest) != android:
                raise ValueError("Android evidence changed during review preparation")
            write_json(completed / "android-audit.json", android)
            data, _ = forms.pinned_json(android_pins, android_pins_sha256)
            (completed / "android-pins.json").write_bytes(data)
            android_report_sha256 = sha((completed / "android-audit.json").read_bytes())
        receipt = {"schemaVersion": 1, "state": "UnsignedReviewPrepared", "sourceCommit": source_commit,
                   "candidateSha256": candidate_digest, "inventorySha256": inventory_digest,
                   "mappingSha256": mapping_digest, "formSha256": report["formSha256"],
                   "formReportSha256": sha((completed / "form-report.json").read_bytes()),
                   "signingCopy": signing_copy,
                   "bundleSha256": bundle_digest, "visualReviewRequired": True,
                   "producerAuthenticationRequired": True, "signingAuthorizationRequired": True,
                   "submissionReady": False, "deliveryAttempted": False}
        if declared_gaps:
            receipt.update(state="UnsignedReviewWithDeclaredGapsPrepared", reviewMode="DeclaredGaps",
                           verificationStatus=identity["verificationStatus"], declarationsSha256=declarations_sha256)
        if android is not None:
            receipt["androidAuditSha256"] = android_report_sha256
            receipt["androidPinsSha256"] = android_pins_sha256
        write_json(completed / "review-receipt.json", receipt)
        # mkdir is exclusive on both Windows and POSIX. Never replace a prior run.
        output.mkdir()
        # An interrupted move leaves no completion marker and is not reusable.
        for item in completed.iterdir():
            item.rename(output / item.name)
        with (output / "COMPLETE").open("x", encoding="ascii") as marker:
            marker.write(sha((output / "review-receipt.json").read_bytes()) + "\n")
        return receipt


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ("settings", "candidate-sha256", "inventory-sha256", "mapping-sha256", "source-commit", "artifact-kind"):
        parser.add_argument("--" + name, required=True)
    parser.add_argument("--prepare-for-signing", action="store_true",
                        help="Prepare the evidence-backed unsigned signing copy; does not authorize or apply a signature")
    parser.add_argument("--android-pins", help="Independent pre-execution Android run pins; requires the matching digest")
    parser.add_argument("--android-pins-sha256", help="SHA-256 retained by the trusted coordinator")
    parser.add_argument("--review-mode", choices=("complete", "declared-gaps"), default="complete")
    parser.add_argument("--declarations", help="Candidate-bound private gap declarations; requires declared-gaps mode")
    parser.add_argument("--declarations-sha256", help="Independent SHA-256 of the exact reviewed explanations")
    args = parser.parse_args()
    try:
        report = prepare(args.settings, args.candidate_sha256, args.inventory_sha256, args.mapping_sha256,
                         args.source_commit, args.artifact_kind, signing_copy=args.prepare_for_signing,
                         android_pins=args.android_pins, android_pins_sha256=args.android_pins_sha256,
                         review_mode=args.review_mode, declarations=args.declarations, declarations_sha256=args.declarations_sha256)
        print(json.dumps(report, indent=2))
        return 0
    except (ValueError, OSError, KeyError, TypeError, PdfReadError, LayoutError, XMLSyntaxError, subprocess.SubprocessError):
        # Input errors can contain private paths or observation rationales.
        print("Submission review failed. Inspect private inputs; no delivery was attempted.", file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
