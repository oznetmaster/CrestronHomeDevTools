# Copyright (c) 2026 Neil Colvin.
# Licensed under the MIT License. See LICENSE in the repository root.
"""Check one explicit signing-and-submission authority; never sign, upload or email.

This is a scope/integrity guard for a trusted coordinator, not evidence-producer
authentication or a replacement for a signer's or transport's own checks.
"""

import argparse
from datetime import datetime, timezone
import io
import json
from pathlib import Path
import re
import stat
import sys

from pypdf import PdfReader
from pypdf.errors import PdfReadError

from build_help import keys, sha, strict_object
from package_help import write_json
from self_test_form import inspect_form


def pinned(reference, limit=64 * 1024 * 1024):
    keys(reference, ("path", "sha256"))
    path = Path(reference["path"])
    if not path.is_absolute() or not re.fullmatch(r"[0-9a-f]{64}", reference["sha256"]):
        raise ValueError("Use absolute files with independently reviewed lowercase SHA-256 pins")
    for node in (path, *path.parents):
        attributes = node.lstat()
        if stat.S_ISLNK(attributes.st_mode) or getattr(attributes, "st_file_attributes", 0) & getattr(stat, "FILE_ATTRIBUTE_REPARSE_POINT", 0):
            raise ValueError("Approval inputs must not traverse links or reparse points")
    with path.open("rb") as stream:
        if stream.seek(0, 2) > limit:
            raise ValueError("Approval input exceeds its size bound")
        stream.seek(0)
        data = stream.read(limit + 1)
    if sha(data) != reference["sha256"]:
        raise ValueError("Approved input changed")
    return data


def document(reference):
    return json.loads(pinned(reference, 4 * 1024 * 1024), object_pairs_hook=strict_object)


def moment(value):
    result = datetime.fromisoformat(value.replace("Z", "+00:00"))
    if result.tzinfo is None:
        raise ValueError("Approval times require a time zone")
    return result


def check(packet_reference, authorization_reference, *, signed_reference=None,
          visual_review_reference=None, now=None):
    """Recheck all original pins and, optionally, bind an inspected signed result.

    The independently trusted authorization must explicitly grant both actions.
    The returned scope preserves its deadline. A newly calculated signed hash is
    an observed output binding, never represented as a pre-existing human review.
    """
    now = now or datetime.now(timezone.utc)
    if now.tzinfo is None:
        raise ValueError("Current time requires a time zone")
    packet = document(packet_reference)
    keys(packet, ("schemaVersion", "unsignedForm", "inventory", "package", "coverLetter",
                  "evidenceReview", "signing", "delivery", "provider"))
    approval = document(authorization_reference)
    keys(approval, ("schemaVersion", "packetSha256", "approvalMode", "signingAuthorized",
                    "deliveryAuthorized", "visualReviewCompleted", "expiresUtc"))
    if (type(packet["schemaVersion"]) is not int or packet["schemaVersion"] != 1 or
            type(approval["schemaVersion"]) is not int or approval["schemaVersion"] != 1 or
            approval["packetSha256"] != packet_reference["sha256"] or
            approval["approvalMode"] != "CombinedSigningAndSubmission" or
            any(approval[name] is not True for name in ("signingAuthorized", "deliveryAuthorized", "visualReviewCompleted"))):
        raise ValueError("An exact, explicit combined approval is required; signing-only authority is insufficient")
    expires = moment(approval["expiresUtc"])
    if expires <= now:
        raise ValueError("Combined authority expired; it cannot be extended by deriving stage records")
    unsigned_bytes = pinned(packet["unsignedForm"])
    inventory = document(packet["inventory"])
    pinned(packet["package"], 256 * 1024 * 1024)
    letter = pinned(packet["coverLetter"], 256 * 1024).decode("utf-8")
    review = document(packet["evidenceReview"])
    # Retain this distinction. This guard authenticates neither kind of producer.
    keys(review, ("schemaVersion", "method", "reviewer", "packageSha256", "inventorySha256", "rows"))
    if (type(review["schemaVersion"]) is not int or review["schemaVersion"] != 1 or
            review["method"] not in ("ManualTechnicalReview", "AutomatedEvidenceReview") or
            not isinstance(review["reviewer"], str) or not review["reviewer"].strip() or
            review["packageSha256"] != packet["package"]["sha256"] or
            review["inventorySha256"] != packet["inventory"]["sha256"]):
        raise ValueError("The exact evidence-review method, reviewer and candidate must be retained")
    rows = review["rows"]
    required = {item["id"]: item for item in inventory["requirements"]}
    if (len(required) != len(inventory["requirements"]) or len(rows) != len(required) or
            {row["id"] for row in rows} != set(required)):
        raise ValueError("Account for every official item exactly once")
    values = {}
    counts = {"Checked": 0, "NotApplicable": 0, "Incomplete": 0}
    for row in rows:
        keys(row, ("id", "disposition", "rationale", "evidence"))
        if row["disposition"] not in counts or not isinstance(row["rationale"], str) or not row["rationale"].strip():
            raise ValueError("Every item needs its actual disposition and reviewed scope/rationale")
        if row["disposition"] == "Checked" and not row["evidence"]:
            raise ValueError("A checked item needs retained evidence; integrity alone is not a pass")
        for reference in row["evidence"]:
            pinned(reference)
        counts[row["disposition"]] += 1
        rule = required[row["id"]]
        values[rule["field"]] = rule["checkedAppearance"][0] if row["disposition"] == "Checked" else "/Off"
    signing = packet["signing"]
    keys(signing, ("signer", "date", "signatureField", "dateField", "signatureImageSha256"))
    if (not isinstance(signing["signer"], str) or not signing["signer"].strip() or
            len(signing["signer"]) > 120 or any(ord(c) < 32 for c in signing["signer"]) or
            not re.fullmatch(r"[0-9a-f]{64}", signing["signatureImageSha256"]) or
            set(f["field"] for f in inventory["signingFields"]) != {signing["signatureField"], signing["dateField"]} or
            signing["signatureField"] == signing["dateField"] or
            signing["date"] != now.astimezone(timezone.utc).date().isoformat()):
        raise ValueError("Approve the exact signer, signing date, signature identity and two official fields")
    delivery = packet["delivery"]
    keys(delivery, ("sender", "recipient", "subject", "urlToken"))
    for name in ("sender", "recipient"):
        if not isinstance(delivery[name], str) or not re.fullmatch(r"[^\s<>@,;]+@[^\s<>@,;]+\.[^\s<>@,;]+", delivery[name]):
            raise ValueError("Use one explicit sender and recipient")
    if (not isinstance(delivery["subject"], str) or not delivery["subject"].strip() or
            any(ord(c) < 32 for c in delivery["subject"]) or
            delivery["urlToken"] != "{{PACKAGE_DOWNLOAD_URL}}" or letter.count(delivery["urlToken"]) != 1 or
            "{{" in letter.replace(delivery["urlToken"], "") or
            not letter.replace("\r\n", "\n").startswith("To: " + delivery["recipient"] + "\nSubject: " + delivery["subject"] + "\n\n")):
        raise ValueError("Freeze the correspondence, with only one non-sendable verified-URL token")
    provider = packet["provider"]
    keys(provider, ("uploadHost", "formSha256", "termsSha256"))
    if provider["uploadHost"] != "uploader.crestron.com" or any(not re.fullmatch(r"[0-9a-f]{64}", provider[n]) for n in ("formSha256", "termsSha256")):
        raise ValueError("Bind the reviewed Crestron provider form and terms")
    unsigned = PdfReader(io.BytesIO(unsigned_bytes), strict=True)
    trailing = len(unsigned.pages) - inventory["templatePages"]
    inspect_form(unsigned, inventory, values, 0, trailing_pages=trailing)
    result = {"schemaVersion": 1, "state": "CombinedAuthorityChecked", "packetSha256": packet_reference["sha256"],
              "authorizationSha256": authorization_reference["sha256"], "expiresUtc": approval["expiresUtc"],
              "evidenceReviewMethod": review["method"], "producerAuthenticationPerformed": False,
              "testOutcomesReassessed": False, "counts": counts, "package": packet["package"],
              "coverLetter": packet["coverLetter"], "delivery": delivery, "provider": provider,
              "newHumanApprovalCreated": False, "signatureApplied": False, "deliveryAttempted": False}
    if signed_reference is None:
        if visual_review_reference is not None:
            raise ValueError("A signed visual review requires the signed document")
        return result
    if visual_review_reference is None:
        raise ValueError("Inspect the actual signed output before binding delivery")
    signed = PdfReader(io.BytesIO(pinned(signed_reference)), strict=True)
    visual = document(visual_review_reference)
    keys(visual, ("schemaVersion", "signedFormSha256", "reviewer", "reviewedUtc", "signatureAndDateLegible", "unchangedOutsideSignatureDate"))
    if (type(visual["schemaVersion"]) is not int or visual["schemaVersion"] != 1 or
            visual["signedFormSha256"] != signed_reference["sha256"] or not visual["reviewer"].strip() or
            visual["signatureAndDateLegible"] is not True or visual["unchangedOutsideSignatureDate"] is not True or
            moment(visual["reviewedUtc"]) > now or len(signed.pages) != len(unsigned.pages)):
        raise ValueError("A current, exact signed-output inspection is required")
    signing_values = {signing["signatureField"]: signing["signer"], signing["dateField"]: signing["date"]}
    inspect_form(signed, inventory, values, 0, signing_values, trailing)
    for before, after in zip(unsigned.pages, signed.pages, strict=True):
        if (before.get_contents().get_data() != after.get_contents().get_data() or
                list(before.mediabox) != list(after.mediabox) or before.extract_text() != after.extract_text()):
            raise ValueError("Printed form/notes changed; combined approval no longer covers this output")
    # All non-signing logical fields must remain unchanged, including checkbox states.
    old_fields, new_fields = unsigned.get_fields(), signed.get_fields()
    if set(old_fields) != set(new_fields) or any(old_fields[n].get("/V") != new_fields[n].get("/V") for n in old_fields if n not in signing_values):
        raise ValueError("Signed output changed an unapproved field")
    result.update(state="SignedOutputBoundToCombinedAuthority", signedForm=signed_reference,
                  signedVisualReview=visual_review_reference, signatureApplied=True,
                  deliveryAuthorizedUnderOriginalApproval=True)
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ("packet", "packet-sha256", "authorization", "authorization-sha256", "output"):
        parser.add_argument("--" + name, required=True)
    for name in ("signed-form", "signed-form-sha256", "signed-visual-review", "signed-visual-review-sha256"):
        parser.add_argument("--" + name)
    args = parser.parse_args()
    try:
        supplied = [args.signed_form, args.signed_form_sha256, args.signed_visual_review, args.signed_visual_review_sha256]
        if any(supplied) and not all(supplied):
            raise ValueError("Supply every signed-output and inspection pin together")
        result = check({"path": args.packet, "sha256": args.packet_sha256},
                       {"path": args.authorization, "sha256": args.authorization_sha256},
                       signed_reference={"path": args.signed_form, "sha256": args.signed_form_sha256} if all(supplied) else None,
                       visual_review_reference={"path": args.signed_visual_review, "sha256": args.signed_visual_review_sha256} if all(supplied) else None)
        write_json(args.output, result)
        print(json.dumps(result, indent=2))
        return 0
    except (ValueError, OSError, KeyError, TypeError, PdfReadError):
        print("Combined approval refused; retain the original packet and inspect the exact scope, files, deadline and signed output. No signature, upload or email was performed.", file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
