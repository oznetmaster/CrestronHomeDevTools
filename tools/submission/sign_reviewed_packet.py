# Copyright (c) 2026 Neil Colvin.
# Licensed under the MIT License. See LICENSE in the repository root.
"""Sign an explicitly approved manual technical review; never upload or email.

This separate route preserves its manual review identity. It neither fabricates
machine producer records nor changes the protected automated-evidence signer.
"""

import argparse
from datetime import datetime, timezone
import io
import json
from pathlib import Path
import sys

from PIL import Image
from pypdf import PdfReader, PdfWriter
from pypdf.errors import PdfReadError
from pypdf.generic import DictionaryObject, NameObject, NumberObject, TextStringObject

import combined_approval as combined
from build_help import sha
from package_help import write_json
from self_test_form import inspect_form
from sign_self_test_form import appearance


def sign(packet_reference, authorization_reference, output, report_path, *, image_bytes, now=None):
    clock = (lambda: now) if now is not None else (lambda: datetime.now(timezone.utc))
    now = clock()
    scope = combined.check(packet_reference, authorization_reference, now=now)
    packet = combined.document(packet_reference)
    if scope["evidenceReviewMethod"] != "ManualTechnicalReview":
        raise ValueError("Use the existing revalidating signer for automated-evidence reviews")
    output, report_path = Path(output), Path(report_path)
    if (not output.is_absolute() or not report_path.is_absolute() or not output.name.endswith(".signed.pdf") or
            output.exists() or report_path.exists() or output == report_path or
            not output.parent.is_dir() or not report_path.parent.is_dir()):
        raise ValueError("Use new absolute signed-PDF and receipt paths under existing private directories")
    # Check ordinary ancestors before ever creating a signed file.
    for path in (output.parent, report_path.parent):
        for node in (path, *path.parents):
            info = node.lstat()
            if combined.stat.S_ISLNK(info.st_mode) or getattr(info, "st_file_attributes", 0) & getattr(combined.stat, "FILE_ATTRIBUTE_REPARSE_POINT", 0):
                raise ValueError("Signed output must not traverse a link or reparse point")
    signing = packet["signing"]
    if len(image_bytes) > 10 * 1024 * 1024 or sha(image_bytes) != signing["signatureImageSha256"]:
        raise ValueError("Stored signature differs from the explicitly approved identity")
    with Image.open(io.BytesIO(image_bytes)) as supplied:
        if supplied.format not in ("PNG", "JPEG") or getattr(supplied, "n_frames", 1) != 1 or not all(1 <= n <= 4096 for n in supplied.size):
            raise ValueError("Use one PNG or JPEG signature up to 4096 pixels per side")
        supplied.load()
        image = Image.frombytes("RGBA", supplied.size, supplied.convert("RGBA").tobytes())
    source = PdfReader(io.BytesIO(combined.pinned(packet["unsignedForm"])), strict=True)
    inventory = combined.document(packet["inventory"])
    review = combined.document(packet["evidenceReview"])
    rows = {r["id"]: r for r in review["rows"]}
    values = {r["field"]: r["checkedAppearance"][0] if rows[r["id"]]["disposition"] == "Checked" else "/Off" for r in inventory["requirements"]}
    signing_values = {signing["signatureField"]: signing["signer"], signing["dateField"]: signing["date"]}
    writer = PdfWriter(); writer.clone_document_from_reader(source)
    for reference in writer.root_object["/AcroForm"]["/Fields"]:
        widget = reference.get_object()
        name = widget["/T"]
        widget[NameObject("/Ff")] = NumberObject(int(widget.get("/Ff", 0)) | 1)
        if name in signing_values:
            x0, y0, x1, y1 = map(float, widget["/Rect"])
            normal = appearance(writer, x1 - x0, y1 - y0,
                                image=image if name == signing["signatureField"] else None,
                                label=signing_values[name] if name == signing["dateField"] else None)
            widget[NameObject("/V")] = TextStringObject(signing_values[name])
            widget[NameObject("/AP")] = DictionaryObject({NameObject("/N"): normal})
    writer.add_metadata({"/Author": signing["signer"], "/Subject": "Authorized image signature of a manual technical review; not automated producer validation or Crestron acceptance"})
    buffer = io.BytesIO(); writer.write(buffer)
    data = buffer.getvalue()
    result = PdfReader(io.BytesIO(data), strict=True)
    inspect_form(result, inventory, values, 0, signing_values, len(source.pages) - inventory["templatePages"])
    for before, after in zip(source.pages, result.pages, strict=True):
        if before.get_contents().get_data() != after.get_contents().get_data() or list(before.mediabox) != list(after.mediabox):
            raise ValueError("Signing changed the printed form or notes")
    # Recheck authority/pins immediately before exposing the signed artifact.
    combined.check(packet_reference, authorization_reference, now=clock())
    with output.open("xb") as stream:
        stream.write(data)
    receipt = {**scope, "state": "ManualReviewSignedUnderCombinedAuthority", "signedFormSha256": sha(data),
               "unsignedFormSha256": packet["unsignedForm"]["sha256"], "signer": signing["signer"],
               "signingDate": signing["date"], "signatureApplied": True, "cryptographicSignature": False,
               "sourceImageMetadataEmbedded": False, "visualReviewRequired": True,
               "deliveryAttempted": False, "signedAtUtc": now.isoformat()}
    write_json(report_path, receipt)
    return receipt


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ("packet", "packet-sha256", "authorization", "authorization-sha256", "output", "report"):
        parser.add_argument("--" + name, required=True)
    signature = parser.add_mutually_exclusive_group(required=True)
    signature.add_argument("--signature-image")
    signature.add_argument("--signature-stdin", action="store_true")
    args = parser.parse_args()
    try:
        packet = {"path": args.packet, "sha256": args.packet_sha256}
        authority = {"path": args.authorization, "sha256": args.authorization_sha256}
        # Do not read even a caller-provided private image before authority checks.
        combined.check(packet, authority)
        if args.signature_stdin:
            image = sys.stdin.buffer.read(10 * 1024 * 1024 + 1)
        else:
            with Path(args.signature_image).open("rb") as stream:
                image = stream.read(10 * 1024 * 1024 + 1)
        result = sign(packet, authority, args.output, args.report, image_bytes=image)
        print(json.dumps(result, indent=2))
        return 0
    except (ValueError, OSError, KeyError, TypeError, PdfReadError):
        print("Manual-review signing refused; check the exact combined approval and pinned inputs. No upload or email was performed; inspect private output before retrying.", file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
