# Copyright (c) 2026 Neil Colvin.
# Licensed under the MIT License. See LICENSE in the repository root.
"""Prepare an unsigned outbound request with explicit omissions; never authorize or deliver it."""

import argparse
from datetime import datetime, timezone
import io
import json
from pathlib import Path, PurePosixPath
import re
import subprocess
import sys
import tempfile
import zipfile

from pypdf import PdfReader, PdfWriter
from pypdf.errors import PdfReadError
from pypdf.generic import NameObject
from reportlab.platypus.doctemplate import LayoutError

from build_help import keys, sha, strict_object, text
from package_help import read_json, write_json
from prepare_signed_review import copy_pinned
from render_help import run_process
from review_android import retained_files
import self_test_form as forms
from validator_runtime import settings_validator


def disposition_for_review(value, receipt, review_digest):
    keys(value, ("schemaVersion", "reviewReceiptSha256", "candidateSha256", "declarationsSha256", "reviewMode", "attachmentKind", "omissions"))
    if (type(value["schemaVersion"]) is not int or value["schemaVersion"] != 1 or
            value["reviewReceiptSha256"] != review_digest or value["candidateSha256"] != receipt["candidateSha256"] or
            value["declarationsSha256"] != receipt["declarationsSha256"] or value["reviewMode"] != "DeclaredGaps"):
        raise ValueError("Document disposition must identify the exact reviewed candidate and declarations")
    if value["attachmentKind"] not in ("UnsignedSelfTest", "DisclosureOnly"):
        raise ValueError("This stage prepares explicitly unsigned requests only")
    omissions = value["omissions"]
    expected = {"signature"} if value["attachmentKind"] == "UnsignedSelfTest" else {"officialSelfTestForm", "signature"}
    if not isinstance(omissions, list) or len(omissions) != len(expected):
        raise ValueError("Every omitted required document or signature needs its own explanation")
    identifiers = set()
    for omission in omissions:
        keys(omission, ("id", "reason"))
        reason = omission["reason"]
        if (omission["id"] not in expected or omission["id"] in identifiers or not isinstance(reason, str) or
                not reason.strip() or len(reason) > 8000 or any(ord(c) < 32 for c in reason)):
            raise ValueError("Supply unique document omissions and nonempty public explanations")
        identifiers.add(omission["id"])
    if identifiers != expected:
        raise ValueError("The document disposition conceals an omission")


def extract_validated_archive(path, destination):
    """Only called after .NET reassesses a frozen archive; still enforce bounded regular-file extraction."""
    names, total = set(), 0
    with zipfile.ZipFile(path) as archive:
        if len(archive.infolist()) > 10000:
            raise ValueError("Too many retained evidence files")
        for entry in archive.infolist():
            parts = PurePosixPath(entry.filename).parts
            if (entry.orig_filename != entry.filename or not parts or entry.filename != "/".join(parts) or any(part in (".", "..") for part in parts) or
                    any(c in entry.filename for c in "\\:\x00") or entry.filename.startswith("/") or
                    entry.filename.casefold() in names or entry.is_dir() or
                    (entry.external_attr >> 16) & 0xF000 not in (0, 0x8000) or entry.external_attr & 0x400):
                raise ValueError("Unsafe retained archive entry")
            names.add(entry.filename.casefold())
            if entry.file_size < 0 or entry.file_size > 64 * 1024 * 1024 or total + entry.file_size > 512 * 1024 * 1024:
                raise ValueError("Retained evidence exceeds extraction bounds")
            target = destination.joinpath(*parts)
            target.parent.mkdir(parents=True, exist_ok=True)
            size = 0
            with archive.open(entry) as source, target.open("xb") as output:
                while block := source.read(1024 * 1024):
                    size += len(block)
                    if size > entry.file_size:
                        raise ValueError("Retained archive entry length changed")
                    output.write(block)
            if size != entry.file_size:
                raise ValueError("Retained archive entry is incomplete")
            total += size
    (destination / "evidence").mkdir(exist_ok=True)


def write_request(source, inventory, output, title, author, rows, identity, disposition, disposition_digest):
    text(title)
    text(author)
    if not str(output).endswith(".request.pdf") or Path(output).exists():
        raise ValueError("Use a new .request.pdf output")
    declared, resolved = forms.indexed(inventory["requirements"], "id"), forms.indexed(rows, "id")
    if declared.keys() != resolved.keys() or any(row["state"] not in ("Passed", "NotApplicable", "GapDeclared") for row in rows):
        raise ValueError("The complete official inventory must be represented")
    original = PdfReader(io.BytesIO(source), strict=True)
    if sha(source) != inventory["templateSha256"]:
        raise ValueError("The official template changed")
    forms.inspect_form(original, inventory)
    includes_form = disposition["attachmentKind"] == "UnsignedSelfTest"
    identity = {**identity, "dispositionSha256": disposition_digest}
    display_identity = {**identity, "evidenceVerificationStatus": identity["verificationStatus"], "verificationStatus": "GapsDeclared"}
    cover = forms.companion(title, author, rows, display_identity, False, declared_gaps=True, request=disposition)
    writer = PdfWriter()
    values = {r["field"]: NameObject(r["checkedAppearance"][0] if resolved[r["id"]]["state"] == "Passed" else "/Off")
              for r in inventory["requirements"]}
    if includes_form:
        writer.clone_document_from_reader(original)
        forms.vector_check_appearances(writer, inventory)
        writer.update_page_form_field_values(None, values, auto_regenerate=False)
        for index, page in enumerate(cover.pages):
            writer.insert_page(page, index)
    else:
        for page in cover.pages:
            writer.add_page(page)
    writer.add_metadata({"/Title": title + " - request with declared gaps", "/Author": author,
                         "/Subject": "Unsigned request with declared omissions; no acceptance or certification implied"})
    data = io.BytesIO()
    writer.write(data)
    rendered = PdfReader(io.BytesIO(data.getvalue()), strict=True)
    if includes_form:
        forms.inspect_form(rendered, inventory, values, len(cover.pages))
        for before, after in zip(original.pages, rendered.pages[len(cover.pages):], strict=True):
            if before.get_contents().get_data() != after.get_contents().get_data() or list(before.mediabox) != list(after.mediabox):
                raise ValueError("Printed official form changed")
    elif rendered.get_fields() or any(page.get("/Annots") for page in rendered.pages):
        raise ValueError("A disclosure-only report must not contain official form or signature widgets")
    with Path(output).open("xb") as target:
        target.write(data.getvalue())
    return {"schemaVersion": 1, "attachmentSha256": sha(data.getvalue()), "attachmentKind": disposition["attachmentKind"],
            "identity": identity, "pages": len(rendered.pages), "companionPages": len(cover.pages), "officialFormIncluded": includes_form,
            "verifiedRequirements": [r["id"] for r in rows if r["state"] == "Passed"],
            "checkedRequirements": [r["id"] for r in rows if r["state"] == "Passed"] if includes_form else [],
            "declaredGapRequirements": [r["id"] for r in rows if r["state"] == "GapDeclared"],
            "reviewMode": "DeclaredGaps", "verificationStatus": "GapsDeclared", "evidenceVerificationStatus": identity["verificationStatus"],
            "dispositionSha256": disposition_digest, "signatureApplied": False, "signingCopy": False,
            "visualReviewRequired": True, "deliveryAuthorized": False, "submissionReady": False}


def prepare(settings_path, review_digest, disposition_digest):
    _, settings = read_json(settings_path)
    keys(settings, ("schemaVersion", "reviewDirectory", "disposition", "output", "title", "author"), ("dotnet", "validator"))
    if type(settings["schemaVersion"]) is not int or settings["schemaVersion"] != 1:
        raise ValueError("Unsupported request preparation settings")
    for key in ("reviewDirectory", "disposition", "output"):
        if not isinstance(settings[key], str) or not Path(settings[key]).is_absolute():
            raise ValueError("Use absolute private input and output paths")
    validator = settings_validator(settings)
    review, output = Path(settings["reviewDirectory"]), Path(settings["output"])
    receipt_bytes, receipt = forms.pinned_json(review / "review-receipt.json", review_digest)
    if (type(receipt.get("schemaVersion")) is not int or receipt["schemaVersion"] != 1 or
            receipt.get("state") != "UnsignedReviewWithDeclaredGapsPrepared" or receipt.get("reviewMode") != "DeclaredGaps" or
            receipt.get("signingCopy") is not False or receipt.get("submissionReady") is not False or
            receipt.get("deliveryAttempted") is not False or not re.fullmatch(r"[a-f0-9]{40}", receipt["sourceCommit"]) or
            (review / "COMPLETE").read_text(encoding="ascii").strip() != review_digest):
        raise ValueError("An intact completed declared-gap review is required")
    disposition_bytes, disposition = forms.pinned_json(settings["disposition"], disposition_digest)
    disposition_for_review(disposition, receipt, review_digest)
    if output.exists() or not output.parent.is_dir():
        raise ValueError("Use a new request output under an existing private directory")
    android = retained_files(review, receipt)
    with tempfile.TemporaryDirectory(prefix=".submission-request-", dir=output.parent) as temporary:
        staging = Path(temporary)
        for name, pin, limit in (("evidence.zip", receipt["bundleSha256"].lower(), 513 * 1024 * 1024),
                                 ("inventory.json", receipt["inventorySha256"], 8 * 1024 * 1024),
                                 ("mapping.json", receipt["mappingSha256"], 8 * 1024 * 1024),
                                 ("self-test.review.pdf", receipt["formSha256"], 64 * 1024 * 1024),
                                 ("form-report.json", receipt["formReportSha256"], 8 * 1024 * 1024),
                                 ("declarations.json", receipt["declarationsSha256"], 16 * 1024 * 1024)):
            copy_pinned(review / name, staging / name, pin, limit)
        checked = run_process([*validator, "submission-review-bundle-check", "--bundle", str(staging / "evidence.zip"),
                               "--bundle-sha256", receipt["bundleSha256"].lower(), "--candidate-sha256", receipt["candidateSha256"],
                               "--mode", "declared-gaps", "--declarations-sha256", receipt["declarationsSha256"], "--scratch", str(staging)], 180)
        if checked.returncode != 0:
            raise ValueError("Retained evidence no longer permits a consistent review request")
        bundle = json.loads(checked.stdout, object_pairs_hook=strict_object)
        assessment, validation = bundle["review"]["assessment"], bundle["review"]["validation"]
        _, original_form = read_json(staging / "form-report.json")
        if (bundle["readyForReview"] is not True or bundle["bundleSha256"].lower() != receipt["bundleSha256"].lower() or
                bundle["review"]["declarationsSha256"] != receipt["declarationsSha256"] or
                assessment["verificationStatus"] != receipt["verificationStatus"] or
                validation["candidateSha256"] != receipt["candidateSha256"] or
                validation["observationsSha256"] != original_form["identity"]["observationsSha256"] or
                validation["package"]["sha256"] != original_form["identity"]["packageSha256"]):
            raise ValueError("Current assessment differs from the exact reviewed evidence")
        extracted = staging / "extracted"
        extracted.mkdir()
        extract_validated_archive(staging / "evidence.zip", extracted)
        _, candidate = forms.pinned_json(extracted / "candidate.json", receipt["candidateSha256"])
        if candidate["identity"]["sourceCommit"] != receipt["sourceCommit"] or not re.fullmatch(r"\d+\.\d+\.\d+\.0+", candidate["packageRequirements"]["driverVersion"]):
            raise ValueError("The outbound request needs the exact actual-driver Release candidate")
        _, inventory = forms.pinned_json(staging / "inventory.json", receipt["inventorySha256"])
        packages = list((extracted / "package").iterdir())
        if len(packages) != 1:
            raise ValueError("The request requires one validated production driver package")
        rows, identity, current_validation = forms.validate_evidence(inventory, receipt["inventorySha256"],
            staging / "mapping.json", receipt["mappingSha256"], extracted / "candidate.json", receipt["candidateSha256"],
            extracted / "policy.json", extracted / "observations.json", packages[0], extracted / "template.pdf", extracted / "evidence",
            settings.get("dotnet"), settings.get("validator"), declarations_path=staging / "declarations.json", declarations_digest=receipt["declarationsSha256"])
        if (identity["observationsSha256"] != validation["observationsSha256"] or identity["verificationStatus"] != receipt["verificationStatus"] or
                [row["id"] for row in rows if row["state"] == "Passed"] != original_form["checkedRequirements"]):
            raise ValueError("The outbound matrix differs from the previously reviewed results")
        identity["inventorySha256"] = receipt["inventorySha256"]
        completed = staging / "completed"
        delivery = completed / "delivery"
        delivery.mkdir(parents=True)
        attachment = delivery / "Driver-Review.request.pdf"
        report = write_request((extracted / "template.pdf").read_bytes(), inventory, attachment, settings["title"], settings["author"],
                               rows, identity, disposition, disposition_digest)
        report["validationReportJson"] = current_validation
        copy_pinned(packages[0], delivery / packages[0].name, identity["packageSha256"], 64 * 1024 * 1024)
        write_json(completed / "request-form-report.json", report)
        write_json(completed / "bundle-report.json", bundle)
        (completed / "review-receipt.json").write_bytes(receipt_bytes)
        (completed / "disposition.json").write_bytes(disposition_bytes)
        copy_pinned(staging / "evidence.zip", completed / "evidence.zip", receipt["bundleSha256"].lower(), 513 * 1024 * 1024)
        for name in ("inventory.json", "mapping.json", "declarations.json"):
            (completed / name).write_bytes((staging / name).read_bytes())
        for name, data in android.items():
            (completed / name).write_bytes(data)
        # Independent pins and Android provenance must still match at handoff; none enters delivery/.
        forms.pinned_json(review / "review-receipt.json", review_digest)
        forms.pinned_json(settings["disposition"], disposition_digest)
        if retained_files(review, receipt) != android:
            raise ValueError("Android provenance changed during request preparation")
        result = {"schemaVersion": 1, "state": "UnsignedRequestWithDeclaredGapsPrepared", "reviewMode": "DeclaredGaps",
                  "verificationStatus": "GapsDeclared", "evidenceVerificationStatus": identity["verificationStatus"],
                  "sourceCommit": receipt["sourceCommit"], "candidateSha256": receipt["candidateSha256"], "reviewReceiptSha256": review_digest,
                  "declarationsSha256": receipt["declarationsSha256"], "dispositionSha256": disposition_digest, "bundleSha256": receipt["bundleSha256"].lower(),
                  "packageFileName": packages[0].name, "packageSha256": identity["packageSha256"],
                  "attachmentFileName": attachment.name, "attachmentSha256": report["attachmentSha256"], "attachmentKind": disposition["attachmentKind"],
                  "requestFormReportSha256": sha((completed / "request-form-report.json").read_bytes()),
                  "bundleReportSha256": sha((completed / "bundle-report.json").read_bytes()), "evidenceRevalidatedUtc": datetime.now(timezone.utc).isoformat(),
                  "signatureApplied": False, "visualReviewRequired": True, "producerAuthenticationRequired": True,
                  "deliveryAuthorized": False, "submissionReady": False, "deliveryAttempted": False}
        write_json(completed / "request-receipt.json", result)
        output.mkdir()
        for item in completed.iterdir():
            item.rename(output / item.name)
        with (output / "COMPLETE").open("x", encoding="ascii") as marker:
            marker.write(sha((output / "request-receipt.json").read_bytes()) + "\n")
        return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for field in ("settings", "review-sha256", "disposition-sha256"):
        parser.add_argument("--" + field, required=True)
    args = parser.parse_args()
    try:
        print(json.dumps(prepare(args.settings, args.review_sha256, args.disposition_sha256), indent=2))
        return 0
    except (ValueError, OSError, KeyError, TypeError, PdfReadError, LayoutError, zipfile.BadZipFile, subprocess.SubprocessError):
        print("Review request preparation failed. Inspect private inputs; nothing was signed or delivered.", file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
