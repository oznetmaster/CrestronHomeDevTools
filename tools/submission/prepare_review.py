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

from build_help import keys, sha, strict_object
from package_help import read_json, write_json
from render_help import run_process
import self_test_form as forms


def prepare(settings_path, candidate_digest, inventory_digest, mapping_digest, source_commit, artifact_kind, *, signing_copy=False):
    # These pins come from the trusted release job, separately from worker settings.
    if artifact_kind != "driver":
        raise ValueError("Submission review is only available for an explicitly selected driver release")
    if type(signing_copy) is not bool:
        raise ValueError("Signing-copy selection must be a boolean")
    for digest in (candidate_digest, inventory_digest, mapping_digest):
        if not re.fullmatch(r"[0-9a-f]{64}", digest):
            raise ValueError("Supply independent lowercase SHA-256 release pins")
    if not re.fullmatch(r"[0-9a-f]{40}", source_commit):
        raise ValueError("Supply the full release source commit")
    _, settings = read_json(settings_path)
    keys(settings, ("schemaVersion", "candidate", "inventory", "mapping", "policy", "observations",
                    "package", "template", "evidence", "dotnet", "validator", "output", "title", "author"))
    if type(settings["schemaVersion"]) is not int or settings["schemaVersion"] != 1:
        raise ValueError("Unsupported review settings version")
    for key in ("candidate", "inventory", "mapping", "policy", "observations", "package", "template",
                "evidence", "dotnet", "validator", "output"):
        if not isinstance(settings[key], str) or not Path(settings[key]).is_absolute():
            raise ValueError("Review settings require absolute private paths")
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
    # Only our own random staging directory is cleaned after failure. No success
    # output is exposed until both artifacts and their shared identities agree.
    with tempfile.TemporaryDirectory(prefix=".submission-review-", dir=output.parent) as temporary:
        staging = Path(temporary)
        completed = staging / "completed"
        completed.mkdir()
        rows, identity, validation_json = forms.validate_evidence(
            inventory, inventory_digest, settings["mapping"], mapping_digest,
            settings["candidate"], candidate_digest, settings["policy"], settings["observations"],
            settings["package"], settings["template"], settings["evidence"], settings["dotnet"], settings["validator"])
        identity["inventorySha256"] = inventory_digest
        form = completed / "self-test.review.pdf"
        report = forms.write_form(source, inventory, form, settings["title"], settings["author"], rows, identity, False,
                                  signing_copy=signing_copy)
        report["validationReportJson"] = validation_json
        write_json(completed / "form-report.json", report)
        arguments = [settings["dotnet"], settings["validator"], "submission-bundle-create",
                     "--output", str(completed / "evidence.zip"), "--candidate-sha256", candidate_digest]
        for key in ("candidate", "package", "policy", "template", "observations", "evidence"):
            arguments.extend(("--" + key, settings[key]))
        result = run_process(arguments, 180)
        if result.returncode != 0:
            raise ValueError("Evidence bundle validation failed; no completed review was published")
        bundle = json.loads(result.stdout, object_pairs_hook=strict_object)
        checked = bundle["Validation"]
        if (bundle["ValidationChecksPassed"] is not True or
                checked["CandidateSha256"] != candidate_digest or
                checked["ObservationsSha256"] != identity["observationsSha256"] or
                checked["Package"]["Sha256"] != identity["packageSha256"] or
                sha((completed / "evidence.zip").read_bytes()) != bundle["BundleSha256"].lower() or
                sha(form.read_bytes()) != report["formSha256"]):
            raise ValueError("Form and retained evidence do not describe the same candidate snapshot")
        write_json(completed / "bundle-report.json", bundle)
        # Retain the independently pinned mapping/inventory bytes used by the form.
        for key, digest in (("mapping", mapping_digest), ("inventory", inventory_digest)):
            data, _ = forms.pinned_json(settings[key], digest)
            (completed / (key + ".json")).write_bytes(data)
        receipt = {"schemaVersion": 1, "state": "UnsignedReviewPrepared", "sourceCommit": source_commit,
                   "candidateSha256": candidate_digest, "inventorySha256": inventory_digest,
                   "mappingSha256": mapping_digest, "formSha256": report["formSha256"],
                   "formReportSha256": sha((completed / "form-report.json").read_bytes()),
                   "signingCopy": signing_copy,
                   "bundleSha256": bundle["BundleSha256"], "visualReviewRequired": True,
                   "producerAuthenticationRequired": True, "signingAuthorizationRequired": True,
                   "submissionReady": False, "deliveryAttempted": False}
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
    args = parser.parse_args()
    try:
        report = prepare(args.settings, args.candidate_sha256, args.inventory_sha256, args.mapping_sha256,
                         args.source_commit, args.artifact_kind, signing_copy=args.prepare_for_signing)
        print(json.dumps(report, indent=2))
        return 0
    except (ValueError, OSError, KeyError, TypeError, PdfReadError, LayoutError, subprocess.SubprocessError):
        # Input errors can contain private paths or observation rationales.
        print("Submission review failed. Inspect private inputs; no delivery was attempted.", file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
