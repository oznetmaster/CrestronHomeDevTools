# Copyright (c) 2026 Neil Colvin.
# Licensed under the MIT License. See LICENSE in the repository root.
"""Generate an unsigned official self-test review form from validated evidence."""

import argparse
from decimal import Decimal
import io
import json
from pathlib import Path
import re
import subprocess
import sys
from xml.sax.saxutils import escape

from pypdf import PdfReader, PdfWriter
from pypdf.errors import PdfReadError
from pypdf.generic import DecodedStreamObject, NameObject
from reportlab.lib import colors
from reportlab.lib.styles import ParagraphStyle
from reportlab.platypus import SimpleDocTemplate, Paragraph, Spacer, Table, TableStyle
from reportlab.platypus.doctemplate import LayoutError

from build_help import keys, sha, strict_object, text
from package_help import read_json, write_json
from render_help import run_process


def pinned_json(path, expected):
    data, value = read_json(path)
    if sha(data) != expected:
        raise ValueError("Input differs from the trusted workflow's pinned digest")
    return data, value


def duration_seconds(value):
    match = re.fullmatch(r"(?:(\d+)\.)?(\d{2}):(\d{2}):(\d{2}(?:\.\d{1,7})?)", value)
    if not match:
        raise ValueError("Policy duration must use the constant .NET TimeSpan format")
    days, hours, minutes, seconds = match.groups()
    if int(hours) > 23 or int(minutes) > 59 or Decimal(seconds) >= 60:
        raise ValueError("Invalid policy duration")
    return Decimal(days or 0) * 86400 + int(hours) * 3600 + int(minutes) * 60 + Decimal(seconds)


def indexed(items, key):
    if not isinstance(items, list) or not items:
        raise ValueError("Require a nonempty requirement list")
    result = {}
    for item in items:
        identifier = text(item[key])
        if identifier in result:
            raise ValueError("Duplicate requirement or field identifier")
        result[identifier] = item
    return result


def inspect_form(reader, inventory, expected_values=None, page_offset=0):
    if reader.is_encrypted or reader.attachments or len(reader.pages) != inventory["templatePages"] + page_offset:
        raise ValueError("Official form page count or document structure changed")
    requirements = indexed(inventory["requirements"], "field")
    signing = indexed(inventory["signingFields"], "field")
    if requirements.keys() & signing.keys():
        raise ValueError("A test checkbox cannot also be a signing field")
    expected = {**requirements, **signing}
    fields = reader.get_fields() or {}
    if fields.keys() != expected.keys():
        raise ValueError("Official form field inventory changed")
    canonical = {}
    for reference in reader.trailer["/Root"]["/AcroForm"]["/Fields"]:
        field = reference.get_object()
        # The pinned official forms have flat fields. Do not guess at ambiguous trees.
        if field.get("/Kids") or field.get("/Parent") or field.get("/T") in canonical:
            raise ValueError("Ambiguous or unsupported canonical form field tree")
        canonical[field["/T"]] = reference
    seen = set()
    for page_number, page in enumerate(reader.pages, 1):
        for reference in page.get("/Annots", []):
            widget = reference.get_object()
            if widget.get("/Subtype") != "/Widget":
                continue
            name = widget.get("/T")
            if widget.get("/Parent") or name not in canonical or reference != canonical[name] or name in seen:
                raise ValueError("Widget does not uniquely match the canonical field")
            seen.add(name)
            if page_number != expected[name]["page"] + page_offset:
                raise ValueError("Form field moved to a different page")
            kind = "/Btn" if name in requirements else "/Tx"
            if fields[name].get("/FT") != kind or widget.get("/FT") != kind:
                raise ValueError("Official field type changed")
            value = fields[name].get("/V", "")
            if value != widget.get("/V", ""):
                raise ValueError("Widget value differs from the canonical field value")
            appearances = widget.get("/AP", {}).get("/N")
            if kind == "/Btn":
                states = requirements[name]["checkedAppearance"]
                if len(states) != 1 or set(appearances or {}) != {"/Off", states[0]}:
                    raise ValueError("Checkbox appearance states differ from the inventory")
                target = expected_values[name] if expected_values is not None else "/Off"
                if (value not in ("", "/Off") if expected_values is None else value != target):
                    raise ValueError("Checkbox has an unexpected value")
                if widget.get("/AS") != target or not appearances[target].get_object().get_data():
                    raise ValueError("Checkbox appearance differs from its logical value")
            elif value != "":
                raise ValueError("Signature and date fields must remain blank")
    if seen != expected.keys():
        raise ValueError("A canonical field has no page widget")


def decisions(inventory, inventory_digest, mapping, policy, observations):
    keys(mapping, ("schemaVersion", "inventorySha256", "policySha256", "requirements"))
    if type(mapping["schemaVersion"]) is not int or mapping["schemaVersion"] != 1 or mapping["inventorySha256"] != inventory_digest:
        raise ValueError("Form mapping does not match the pinned inventory")
    declared = indexed(inventory["requirements"], "id")
    mapped = indexed(mapping["requirements"], "id")
    rules = indexed(policy["requirements"], "id")
    results = indexed(observations["observations"], "requirementId")
    if declared.keys() != mapped.keys() or rules.keys() != results.keys():
        raise ValueError("Form mapping or observations do not cover every required item")
    covered, rows = set(), []
    for identifier, requirement in declared.items():
        item = mapped[identifier]
        keys(item, ("id", "observationIds"))
        ids = item["observationIds"]
        if not isinstance(ids, list) or not ids or len(set(ids)) != len(ids) or any(i not in rules or i in covered for i in ids):
            raise ValueError("Every checkbox needs distinct, known observation IDs")
        covered.update(ids)
        if max(duration_seconds(rules[i]["minimumDuration"]) for i in ids) < requirement["minimumObservationSeconds"]:
            raise ValueError("Mapped policy does not enforce the official observation duration")
        statuses = [results[i]["outcome"] for i in ids]
        if any(s not in ("Passed", "NotApplicable") for s in statuses):
            raise ValueError("Incomplete or failing evidence cannot populate a completed review form")
        excluded = [i for i in ids if results[i]["outcome"] == "NotApplicable"]
        for i in excluded:
            if rules[i]["allowNotApplicable"] is not True:
                raise ValueError("Policy does not permit non-applicability")
            text(results[i]["rationale"])
        rows.append({"id": identifier, "label": requirement["label"], "field": requirement["field"],
                     "state": "NotApplicable" if excluded else "Passed", "observationIds": ids,
                     "rationale": "\n".join(i + ": " + results[i]["rationale"] for i in excluded)})
    if covered != rules.keys():
        raise ValueError("Form mapping leaves policy requirements unused")
    return rows


def validate_evidence(inventory, inventory_digest, mapping_path, mapping_digest, candidate_path,
                      candidate_digest, policy_path, observations_path, package_path, template_path,
                      evidence_directory, dotnet, validator):
    candidate_bytes, candidate = pinned_json(candidate_path, candidate_digest)
    mapping_bytes, mapping = pinned_json(mapping_path, mapping_digest)
    policy_bytes, policy = pinned_json(policy_path, candidate["identity"]["policySha256"])
    observation_bytes, observations = read_json(observations_path)
    if mapping["policySha256"] != sha(policy_bytes) or candidate["identity"]["templateSha256"] != inventory["templateSha256"]:
        raise ValueError("Policy or official form identity differs from the candidate")
    if not Path(dotnet).is_file() or not Path(validator).is_file():
        raise ValueError("Provide explicit .NET and DevTools console DLL paths")
    arguments = [str(dotnet), str(validator), "submission-evidence-check", "--candidate", str(candidate_path),
                 "--candidate-sha256", candidate_digest, "--package", str(package_path), "--policy", str(policy_path),
                 "--template", str(template_path), "--observations", str(observations_path), "--evidence", str(evidence_directory)]
    result = run_process(arguments, 120)
    if result.returncode != 0:
        raise ValueError("DevTools evidence validation failed; no completed form was generated")
    report = json.loads(result.stdout, object_pairs_hook=strict_object)
    if (report["ValidationChecksPassed"] is not True or report["CandidateSha256"] != sha(candidate_bytes) or
            report["ObservationsSha256"] != sha(observation_bytes) or
            report["Package"]["Sha256"] != candidate["identity"]["packageSha256"]):
        raise ValueError("Validation report does not match these candidate and observation bytes")
    rows = decisions(inventory, inventory_digest, mapping, policy, observations)
    identity = {"candidateSha256": sha(candidate_bytes), "mappingSha256": sha(mapping_bytes),
                "policySha256": sha(policy_bytes), "observationsSha256": sha(observation_bytes),
                "packageSha256": report["Package"]["Sha256"], "validationReportSha256": sha(result.stdout)}
    return rows, identity, result.stdout.decode("utf-8")


def companion(title, author, rows, identity, draft):
    buffer = io.BytesIO()
    body = ParagraphStyle("body", fontName="Helvetica", fontSize=9, leading=12, spaceAfter=8)
    small = ParagraphStyle("small", parent=body, fontSize=8, leading=10, spaceAfter=0)
    heading = ParagraphStyle("heading", parent=body, fontName="Helvetica-Bold", fontSize=17, leading=21, spaceAfter=10)
    def paragraph(value, style=body):
        return Paragraph(escape(str(value)).replace("\n", "<br/>"), style)
    story = [paragraph(title, heading), paragraph("UNSIGNED REVIEW - NOT FOR SUBMISSION", heading),
             paragraph("Prepared for " + author + ". Signature and date fields are blank. Crestron's original interactive form follows this companion matrix."),
             paragraph("No requirements have been attested. Every checkbox remains blank." if draft else
                       "Checkboxes are checked only when every mapped observation passed validation for the identified candidate. This does not authenticate the evidence producer or establish Crestron approval."),
             paragraph("Non-applicable items remain unchecked and are explained in the matrix. Confirm their representation with Crestron before signing. Review every page, mapping and applicable subcondition before authorizing a signature.")]
    for key, value in identity.items():
        story.append(paragraph(key + ": " + value, small))
    story.append(Spacer(1, 12))
    data = [[paragraph("Official item", small), paragraph("Result / evidence mapping", small)]]
    for row in rows:
        status = {"Passed": "Passed - checkbox checked", "NotApplicable": "Includes non-applicability - unchecked",
                  "NotTested": "Not evaluated - unchecked"}[row["state"]]
        detail = status + ("\n" + ", ".join(row["observationIds"]) if row["observationIds"] else "")
        if row["rationale"]:
            detail += "\n" + row["rationale"]
        data.append([paragraph(row["id"] + "\n" + row["label"], small), paragraph(detail, small)])
    table = Table(data, colWidths=[215, 289], repeatRows=1, hAlign="LEFT")
    table.setStyle(TableStyle([("BACKGROUND", (0, 0), (-1, 0), colors.HexColor("#e8eef4")),
                               ("VALIGN", (0, 0), (-1, -1), "TOP"), ("GRID", (0, 0), (-1, -1), .3, colors.HexColor("#b9c5d1")),
                               ("LEFTPADDING", (0, 0), (-1, -1), 7), ("RIGHTPADDING", (0, 0), (-1, -1), 7),
                               ("TOPPADDING", (0, 0), (-1, -1), 5), ("BOTTOMPADDING", (0, 0), (-1, -1), 5)]))
    story.append(table)
    SimpleDocTemplate(buffer, pagesize=(612, 792), leftMargin=54, rightMargin=54, topMargin=40,
                      bottomMargin=40, title=title, author=author).build(story)
    return PdfReader(buffer)


def vector_check_appearances(writer, inventory):
    # The official /Yes stream uses an unembedded ZapfDingbats glyph. Some headless
    # renderers display no tick even when /V and /AS are correct. Retain the exact
    # off appearance (background/border) and draw the tick with PDF path operators.
    fields = indexed(inventory["requirements"], "field")
    for page in writer.pages:
        for reference in page.get("/Annots", []):
            widget = reference.get_object()
            if widget.get("/Subtype") != "/Widget" or widget.get("/T") not in fields:
                continue
            states = widget["/AP"]["/N"]
            off = states["/Off"].get_object()
            x0, y0, x1, y1 = map(float, off["/BBox"])
            width, height = x1 - x0, y1 - y0
            if not 1 <= width <= 100 or not 1 <= height <= 100:
                raise ValueError("Unsupported checkbox appearance dimensions")
            tick = (f"\nq 0 G 1 J 1 j {min(width, height) * .09:.4f} w "
                    f"{x0 + width * .22:.4f} {y0 + height * .48:.4f} m "
                    f"{x0 + width * .43:.4f} {y0 + height * .25:.4f} l "
                    f"{x0 + width * .8:.4f} {y0 + height * .76:.4f} l S Q\n").encode("ascii")
            appearance = DecodedStreamObject()
            for key in ("/Type", "/Subtype", "/FormType", "/BBox", "/Matrix", "/Resources"):
                if key in off:
                    appearance[NameObject(key)] = off[key]
            appearance.set_data(off.get_data() + tick)
            states[NameObject(fields[widget["/T"]]["checkedAppearance"][0])] = writer._add_object(appearance)


def write_form(source_bytes, inventory, output, title, author, rows, identity, draft):
    output = Path(output)
    if not output.name.endswith(".review.pdf") or output.exists():
        raise ValueError("Use a new .review.pdf output; signing is a separate authorized stage")
    text(title)
    text(author)
    declared = indexed(inventory["requirements"], "id")
    resolved = indexed(rows, "id")
    allowed = {"NotTested"} if draft else {"Passed", "NotApplicable"}
    if declared.keys() != resolved.keys() or any(row["state"] not in allowed for row in rows):
        raise ValueError("Form decisions are incomplete or incompatible with the selected mode")
    source = PdfReader(io.BytesIO(source_bytes), strict=True)
    inspect_form(source, inventory)
    values = {r["field"]: NameObject(r["checkedAppearance"][0] if resolved[r["id"]]["state"] == "Passed" else "/Off")
              for r in inventory["requirements"]}
    cover = companion(title, author, rows, identity, draft)
    writer = PdfWriter()
    writer.clone_document_from_reader(source)
    vector_check_appearances(writer, inventory)
    writer.update_page_form_field_values(None, values, auto_regenerate=False)
    for index, page in enumerate(cover.pages):
        writer.insert_page(page, index)
    writer.add_metadata({"/Title": title + " - unsigned review", "/Author": author,
                         "/Subject": "Unsigned self-test review; not a submission or certification"})
    data = io.BytesIO()
    writer.write(data)
    result = PdfReader(io.BytesIO(data.getvalue()), strict=True)
    inspect_form(result, inventory, values, len(cover.pages))
    for index, page in enumerate(source.pages):
        actual = result.pages[index + len(cover.pages)]
        if (page.get_contents().get_data() != actual.get_contents().get_data() or
                list(page.mediabox) != list(actual.mediabox) or page.extract_text() != actual.extract_text()):
            raise ValueError("The official form's printed content changed")
    output.parent.mkdir(parents=True, exist_ok=True)
    with output.open("xb") as destination:
        destination.write(data.getvalue())
    return {"schemaVersion": 1, "formSha256": sha(data.getvalue()), "templateSha256": sha(source_bytes),
            "identity": identity, "pages": len(result.pages), "companionPages": len(cover.pages),
            "checkedRequirements": [row["id"] for row in rows if row["state"] == "Passed"],
            "notApplicableRequirements": [row["id"] for row in rows if row["state"] == "NotApplicable"],
            "signatureBlank": True, "dateBlank": True, "visualReviewRequired": True,
            "signingAuthorizationRequired": True, "submissionReady": False}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("mode", choices=("draft", "from-evidence"))
    for field in ("template", "inventory", "inventory-sha256", "output", "title", "author", "report"):
        parser.add_argument("--" + field, required=True)
    for field in ("mapping", "mapping-sha256", "candidate", "candidate-sha256", "policy", "observations", "package", "evidence", "dotnet", "validator"):
        parser.add_argument("--" + field)
    args = parser.parse_args()
    try:
        _, inventory = pinned_json(args.inventory, args.inventory_sha256)
        if type(inventory["schemaVersion"]) is not int or inventory["schemaVersion"] != 1:
            raise ValueError("Unsupported form inventory version")
        source = Path(args.template).read_bytes()
        if sha(source) != inventory["templateSha256"]:
            raise ValueError("Official form template digest mismatch")
        if Path(args.report).exists():
            raise ValueError("Use a new report path")
        indexed(inventory["requirements"], "id")
        rows = [{"id": r["id"], "label": r["label"], "field": r["field"], "state": "NotTested", "observationIds": [], "rationale": ""}
                for r in inventory["requirements"]]
        identity = {"inventorySha256": args.inventory_sha256}
        validation_json = None
        evidence_options = (args.mapping, args.mapping_sha256, args.candidate, args.candidate_sha256, args.policy,
                            args.observations, args.package, args.evidence, args.dotnet, args.validator)
        if args.mode == "draft" and any(evidence_options):
            raise ValueError("Use from-evidence mode to evaluate candidate evidence; draft mode never attests tests")
        if args.mode == "from-evidence":
            if not all(evidence_options):
                raise ValueError("Evidence mode requires all pinned candidate, policy, mapping and validator inputs")
            rows, checked, validation_json = validate_evidence(inventory, args.inventory_sha256, args.mapping, args.mapping_sha256,
                args.candidate, args.candidate_sha256, args.policy, args.observations, args.package, args.template,
                args.evidence, args.dotnet, args.validator)
            identity.update(checked)
        report = write_form(source, inventory, args.output, args.title, args.author, rows, identity, args.mode == "draft")
        report["validationReportJson"] = validation_json
        write_json(args.report, report)
        print(json.dumps(report, indent=2))
        return 0
    except (ValueError, OSError, KeyError, TypeError, PdfReadError, LayoutError, subprocess.SubprocessError) as error:
        print(f"Self-test form generation failed: {error}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
