# Copyright (c) 2026 Neil Colvin.
# Licensed under the MIT License. See LICENSE in the repository root.
"""Apply an authorized private signature image to an exact reviewed self-test form."""

import argparse
from datetime import date, datetime, timezone
import io
import json
from pathlib import Path
import sys

from PIL import Image
from pypdf import PdfReader, PdfWriter
from pypdf.errors import PdfReadError
from pypdf.generic import ArrayObject, DecodedStreamObject, DictionaryObject, FloatObject, NameObject, NumberObject, TextStringObject
from reportlab.lib.utils import ImageReader
from reportlab.pdfgen.canvas import Canvas

from build_help import keys, sha, strict_object
from package_help import write_json
from self_test_form import indexed, inspect_form, pinned_json


def appearance(writer, width, height, image=None, label=None):
    if not 8 <= width <= 1000 or not 8 <= height <= 300:
        raise ValueError("Unsupported signing field dimensions")
    buffer = io.BytesIO()
    canvas = Canvas(buffer, pagesize=(width, height))
    if image is not None:
        iw, ih = image.size
        ratio = min((width - 4) / iw, (height - 4) / ih)
        canvas.drawImage(ImageReader(image), (width - iw * ratio) / 2, (height - ih * ratio) / 2,
                         width=iw * ratio, height=ih * ratio, mask="auto")
    else:
        size = min(12, height - 4)
        while canvas.stringWidth(label, "Helvetica", size) > width - 4 and size >= 6:
            size -= .5
        if size < 6:
            raise ValueError("Date cannot fit legibly in its signing field")
        canvas.setFont("Helvetica", size)
        canvas.drawCentredString(width / 2, (height - size) / 2 + 2, label)
    canvas.showPage()
    canvas.save()
    page = PdfReader(buffer).pages[0]
    stream = DecodedStreamObject()
    stream.update({NameObject("/Type"): NameObject("/XObject"), NameObject("/Subtype"): NameObject("/Form"),
                   NameObject("/FormType"): NumberObject(1),
                   NameObject("/BBox"): ArrayObject([FloatObject(v) for v in (0, 0, width, height)]),
                   NameObject("/Resources"): page["/Resources"].clone(writer)})
    stream.set_data(page.get_contents().get_data())
    return writer._add_object(stream)


def sign(form_path, report_path, inventory_path, authorization_path, authorization_sha256, image_path, output, now=None):
    # The pin must originate from an authorized review/protected workflow, not the evidence worker.
    _, approval = pinned_json(authorization_path, authorization_sha256)
    keys(approval, ("schemaVersion", "formSha256", "formReportSha256", "inventorySha256", "candidateSha256",
                    "packageSha256", "signatureImageSha256", "signer", "signingDate", "expiresUtc",
                    "signatureField", "dateField", "visualReviewCompleted", "signatureAuthorized"))
    if type(approval["schemaVersion"]) is not int or approval["schemaVersion"] != 1 or approval["visualReviewCompleted"] is not True or approval["signatureAuthorized"] is not True:
        raise ValueError("Explicit reviewed-form signing authorization is required")
    now = now or datetime.now(timezone.utc)
    expires = datetime.fromisoformat(approval["expiresUtc"].replace("Z", "+00:00"))
    if expires.tzinfo is None or expires <= now or date.fromisoformat(approval["signingDate"]) != now.date():
        raise ValueError("Signing authorization is expired or is for a different date")
    signer = approval["signer"]
    if not isinstance(signer, str) or not signer.strip() or len(signer) > 120 or any(ord(c) < 32 for c in signer):
        raise ValueError("Provide the explicitly authorized signer name")
    form_bytes = Path(form_path).read_bytes()
    if sha(form_bytes) != approval["formSha256"]:
        raise ValueError("Reviewed form changed")
    _, report = pinned_json(report_path, approval["formReportSha256"])
    _, inventory = pinned_json(inventory_path, approval["inventorySha256"])
    identity = report["identity"]
    if (report.get("signingCopy") is not True or report["signatureBlank"] is not True or report["dateBlank"] is not True or
            report["formSha256"] != approval["formSha256"] or report["templateSha256"] != inventory["templateSha256"] or
            identity["inventorySha256"] != approval["inventorySha256"] or
            identity["candidateSha256"] != approval["candidateSha256"] or identity["packageSha256"] != approval["packageSha256"]):
        raise ValueError("Signing requires the exact evidence-backed signing copy and candidate")
    validation_bytes = report["validationReportJson"].encode("utf-8")
    validation = json.loads(validation_bytes, object_pairs_hook=strict_object)
    if (sha(validation_bytes) != identity["validationReportSha256"] or validation["ValidationChecksPassed"] is not True or
            validation["CandidateSha256"] != identity["candidateSha256"] or
            validation["ObservationsSha256"] != identity["observationsSha256"] or
            validation["Package"]["Sha256"] != identity["packageSha256"]):
        raise ValueError("Evidence validation report is incomplete or inconsistent")
    declared = indexed(inventory["requirements"], "id")
    passed, excluded = report["checkedRequirements"], report["notApplicableRequirements"]
    if (len(set(passed)) != len(passed) or len(set(excluded)) != len(excluded) or set(passed) & set(excluded) or
            set(passed) | set(excluded) != declared.keys()):
        raise ValueError("Form decisions are incomplete")
    values = {v["field"]: v["checkedAppearance"][0] if k in passed else "/Off" for k, v in declared.items()}
    fields = indexed(inventory["signingFields"], "field")
    signature_field, date_field = approval["signatureField"], approval["dateField"]
    if signature_field == date_field or set(fields) != {signature_field, date_field}:
        raise ValueError("Signature and date must map to the two reviewed official fields")
    reader = PdfReader(io.BytesIO(form_bytes), strict=True)
    inspect_form(reader, inventory, values, report["companionPages"])
    with Path(image_path).open("rb") as source_image:
        image_bytes = source_image.read(10 * 1024 * 1024 + 1)
    if len(image_bytes) > 10 * 1024 * 1024 or sha(image_bytes) != approval["signatureImageSha256"]:
        raise ValueError("Private signature image changed or is too large")
    with Image.open(io.BytesIO(image_bytes)) as supplied:
        if supplied.format not in ("PNG", "JPEG") or getattr(supplied, "n_frames", 1) != 1 or not all(1 <= n <= 4096 for n in supplied.size):
            raise ValueError("Use a single PNG or JPEG signature image up to 4096 pixels per side")
        supplied.load()
        # Reconstruct pixels so camera/EXIF/private image metadata is not embedded.
        image = Image.frombytes("RGBA", supplied.size, supplied.convert("RGBA").tobytes())
    writer = PdfWriter()
    writer.clone_document_from_reader(reader)
    signing_values = {signature_field: signer, date_field: approval["signingDate"]}
    for reference in writer.root_object["/AcroForm"]["/Fields"]:
        widget = reference.get_object()
        name = widget["/T"]
        widget[NameObject("/Ff")] = NumberObject(int(widget.get("/Ff", 0)) | 1)
        if name in signing_values:
            x0, y0, x1, y1 = map(float, widget["/Rect"])
            normal = appearance(writer, x1 - x0, y1 - y0, image=image if name == signature_field else None,
                                label=signing_values[name] if name == date_field else None)
            widget[NameObject("/V")] = TextStringObject(signing_values[name])
            widget[NameObject("/AP")] = DictionaryObject({NameObject("/N"): normal})
    writer.add_metadata({"/Title": "Completed driver self-test form", "/Author": signer,
                         "/Subject": "Authorized image signature; not a cryptographic signature or Crestron acceptance"})
    buffer = io.BytesIO()
    writer.write(buffer)
    signed = buffer.getvalue()
    result = PdfReader(io.BytesIO(signed), strict=True)
    inspect_form(result, inventory, values, report["companionPages"], signing_values)
    for before, after in zip(reader.pages, result.pages, strict=True):
        if before.get_contents().get_data() != after.get_contents().get_data() or list(before.mediabox) != list(after.mediabox):
            raise ValueError("Printed official or companion content changed while signing")
    output = Path(output)
    if not output.name.endswith(".signed.pdf"):
        raise ValueError("Use a new .signed.pdf output")
    with output.open("xb") as stream:
        stream.write(signed)
    return {"schemaVersion": 1, "signedFormSha256": sha(signed), "unsignedFormSha256": sha(form_bytes),
            "authorizationSha256": authorization_sha256, "candidateSha256": identity["candidateSha256"],
            "packageSha256": identity["packageSha256"], "signer": signer, "signingDate": approval["signingDate"],
            "signatureApplied": True, "cryptographicSignature": False, "visualReviewRequired": True, "submissionReady": False}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for field in ("form", "form-report", "inventory", "authorization", "authorization-sha256", "signature-image", "output", "report"):
        parser.add_argument("--" + field, required=True)
    args = parser.parse_args()
    try:
        if Path(args.report).exists():
            raise ValueError("Use a new report path")
        result = sign(args.form, args.form_report, args.inventory, args.authorization, args.authorization_sha256,
                      args.signature_image, args.output)
        write_json(args.report, result)
        print(json.dumps(result, indent=2))
        return 0
    except (ValueError, OSError, KeyError, TypeError, PdfReadError) as error:
        print(f"Signing failed: {error}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
