# Copyright (c) 2026 Neil Colvin.
# Licensed under the MIT License. See LICENSE in the repository root.
"""Fill a pinned official help template without rebuilding untouched OOXML parts."""

import argparse
import copy
import hashlib
import io
import json
from pathlib import Path
import re
import sys
from zipfile import ZipFile

from lxml import etree as ET
from PIL import Image

NS = {"w": "http://schemas.openxmlformats.org/wordprocessingml/2006/main",
      "r": "http://schemas.openxmlformats.org/officeDocument/2006/relationships",
      "wp": "http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing",
      "a": "http://schemas.openxmlformats.org/drawingml/2006/main",
      "pic": "http://schemas.openxmlformats.org/drawingml/2006/picture"}
REL = "http://schemas.openxmlformats.org/package/2006/relationships"
CT = "http://schemas.openxmlformats.org/package/2006/content-types"
SECTIONS = [
    ("driver", 4, "Driver"), ("notes", 6, "Notes and Recommendations"),
    ("requirements", 8, "System Requirements and Dependencies"),
    ("installation", 12, "Installation/Upgrade Instructions"),
    ("experience", 17, "End-User Experience"), ("limitations", 19, "Limitations/Known Issues"),
    ("features", 21, "Supported Features"), ("environment", 38, "Test Environment"),
    ("models", 43, "Supported Models"), ("contact", 45, "Contact Information"),
    ("history", 47, "Version History"), ("license", 52, "Licensing and Copyright Information")]
TEXT_PATTERNS = {"paragraph": 5, "bullet": 10, "heading2": 48, "heading3": 49}


def sha(data):
    return hashlib.sha256(data).hexdigest()


def xml(data):
    parser = ET.XMLParser(resolve_entities=False, load_dtd=False, no_network=True)
    root = ET.fromstring(data, parser)
    if root.getroottree().docinfo.doctype:
        raise ValueError("DTD declarations are not supported in help templates")
    return root


def encoded(root):
    return ET.tostring(root, xml_declaration=True, encoding="UTF-8", standalone=True)


def strict_object(pairs):
    result = {}
    for key, value in pairs:
        if key in result:
            raise ValueError("Duplicate help JSON property")
        result[key] = value
    return result


def text(value):
    if not isinstance(value, str) or not value.strip():
        raise ValueError("Help text must be a nonempty string")
    if any(ord(c) < 32 and c not in "\n\t" for c in value):
        raise ValueError("Help text contains a control character")
    return value


def keys(value, required, optional=()):
    if not isinstance(value, dict) or not set(required) <= value.keys() or value.keys() - set(required) - set(optional):
        raise ValueError("Missing or unknown help content fields")


def paragraph(pattern, value):
    result = copy.deepcopy(pattern)
    for name in ("paraId", "textId"):
        result.attrib.pop("{http://schemas.microsoft.com/office/word/2010/wordml}" + name, None)
    original_run = result.find("w:r", NS)
    run_props = original_run.find("w:rPr", NS) if original_run is not None else None
    for child in list(result):
        if child.tag != f"{{{NS['w']}}}pPr":
            result.remove(child)
    run = ET.SubElement(result, f"{{{NS['w']}}}r")
    if run_props is not None:
        run.append(copy.deepcopy(run_props))
    lines = text(value).split("\n")
    for index, line in enumerate(lines):
        if index:
            ET.SubElement(run, f"{{{NS['w']}}}br")
        node = ET.SubElement(run, f"{{{NS['w']}}}t")
        node.set("{http://www.w3.org/XML/1998/namespace}space", "preserve")
        node.text = line
    return result


def image_bytes(root, relative, digest):
    if not isinstance(relative, str) or "\\" in relative or ":" in relative or any(p in ("", ".", "..") for p in relative.split("/")):
        raise ValueError("Images require safe relative forward-slash paths")
    path = root / relative
    if not path.resolve().is_relative_to(root.resolve()):
        raise ValueError("Image escaped the help content directory")
    if path.stat().st_size > 16 * 1024 * 1024:
        raise ValueError("Help image exceeds 16 MiB")
    data = path.read_bytes()
    if sha(data) != digest:
        raise ValueError("Help image digest differs from the approved content")
    with Image.open(io.BytesIO(data)) as image:
        if image.format != "PNG" or getattr(image, "n_frames", 1) != 1:
            raise ValueError("Help screenshots must be single-frame PNG images")
        width, height = image.size
        image.verify()
    scale = min(5.5 * 914400 / width, 5.5 * 914400 / height)
    return data, int(width * scale), int(height * scale)


def image_paragraph(pattern, rel_id, image_id, width, height, description):
    result = paragraph(pattern, description)
    props = result.find("w:pPr", NS)
    if props is None:
        props = ET.Element(f"{{{NS['w']}}}pPr")
        result.insert(0, props)
    if props.find("w:keepNext", NS) is None:
        ET.SubElement(props, f"{{{NS['w']}}}keepNext")
    run = result.find("w:r", NS)
    for child in list(run):
        run.remove(child)
    drawing = ET.SubElement(run, f"{{{NS['w']}}}drawing")
    inline = ET.SubElement(drawing, f"{{{NS['wp']}}}inline", distT="0", distB="0", distL="0", distR="0")
    ET.SubElement(inline, f"{{{NS['wp']}}}extent", cx=str(width), cy=str(height))
    ET.SubElement(inline, f"{{{NS['wp']}}}docPr", id=str(image_id), name=f"Help figure {image_id}", descr=description)
    graphic = ET.SubElement(inline, f"{{{NS['a']}}}graphic")
    gd = ET.SubElement(graphic, f"{{{NS['a']}}}graphicData", uri=NS["pic"])
    pic = ET.SubElement(gd, f"{{{NS['pic']}}}pic")
    nv = ET.SubElement(pic, f"{{{NS['pic']}}}nvPicPr")
    ET.SubElement(nv, f"{{{NS['pic']}}}cNvPr", id="0", name=f"Help figure {image_id}")
    ET.SubElement(nv, f"{{{NS['pic']}}}cNvPicPr")
    fill = ET.SubElement(pic, f"{{{NS['pic']}}}blipFill")
    ET.SubElement(fill, f"{{{NS['a']}}}blip", {f"{{{NS['r']}}}embed": rel_id})
    ET.SubElement(ET.SubElement(fill, f"{{{NS['a']}}}stretch"), f"{{{NS['a']}}}fillRect")
    sp = ET.SubElement(pic, f"{{{NS['pic']}}}spPr")
    transform = ET.SubElement(sp, f"{{{NS['a']}}}xfrm")
    ET.SubElement(transform, f"{{{NS['a']}}}off", x="0", y="0")
    ET.SubElement(transform, f"{{{NS['a']}}}ext", cx=str(width), cy=str(height))
    ET.SubElement(ET.SubElement(sp, f"{{{NS['a']}}}prstGeom", prst="rect"), f"{{{NS['a']}}}avLst")
    return result


def build(template, template_digest, content_path, output, draft=False):
    template, content_path, output = map(Path, (template, content_path, output))
    source = template.read_bytes()
    if sha(source) != template_digest:
        raise ValueError("Official help template digest mismatch")
    if output.suffix.lower() != ".docx" or output.resolve() == template.resolve() or output.exists():
        raise ValueError("Use a new DOCX output path separate from the template")
    if draft and not output.name.endswith(".review.docx"):
        raise ValueError("Review drafts require a .review.docx filename")
    content_bytes = content_path.read_bytes()
    if len(content_bytes) > 4 * 1024 * 1024:
        raise ValueError("Help content exceeds 4 MiB")
    content = json.loads(content_bytes, object_pairs_hook=strict_object)
    keys(content, ("schemaVersion", "title", "author", "version", "pending", "uiPages", "sections"))
    author = text(content["author"])
    if type(content["schemaVersion"]) is not int or content["schemaVersion"] != 1:
        raise ValueError("Unsupported help schema version")
    if not re.fullmatch(r"\d{1,5}\.\d{1,5}\.\d{1,5}\.\d{1,5}", text(content["version"])):
        raise ValueError("Help needs the candidate's four-component driver version")
    if any(int(part) > 65534 for part in content["version"].split(".")):
        raise ValueError("Driver version components must not exceed 65534")
    if not isinstance(content["pending"], list) or not isinstance(content["uiPages"], list):
        raise ValueError("Pending items and UI pages must be arrays")
    pending = [text(item) for item in content["pending"]]
    ui_pages = [text(item) for item in content["uiPages"]]
    if len(set(ui_pages)) != len(ui_pages):
        raise ValueError("UI page identifiers must be unique")
    keys(content["sections"], [key for key, _, _ in SECTIONS])
    with ZipFile(io.BytesIO(source)) as archive:
        infos = archive.infolist()
        if len({i.filename for i in infos}) != len(infos):
            raise ValueError("Duplicate template archive entries")
        parts = {info.filename: archive.read(info) for info in infos}
    doc = xml(parts["word/document.xml"])
    body = doc.find("w:body", NS)
    paragraphs = body.findall("w:p", NS)
    if len(paragraphs) != 54:
        raise ValueError("Official help template paragraph layout changed")
    for _, index, heading in SECTIONS:
        if "".join(paragraphs[index].itertext()).strip() != heading:
            raise ValueError("Official help template section mapping changed")
    replacements = [copy.deepcopy(paragraphs[0]), copy.deepcopy(paragraphs[1]),
                    paragraph(paragraphs[2], text(content["title"])),
                    paragraph(paragraphs[3], "Version " + content["version"])]
    if draft:
        replacements.append(paragraph(paragraphs[5], "REVIEW DRAFT - not for packaging or submission"))
    rels = xml(parts["word/_rels/document.xml.rels"])
    types = xml(parts["[Content_Types].xml"])
    used_ids = {item.get("Id") for item in rels}
    seen_pages = set()
    new_parts = {}
    image_id = max([int(v) for v in doc.xpath("//wp:docPr/@id", namespaces=NS)] + [0])
    for key, index, _ in SECTIONS:
        replacements.append(copy.deepcopy(paragraphs[index]))
        blocks = content["sections"][key]
        if not isinstance(blocks, list) or not blocks:
            raise ValueError("Every help section requires content")
        for block in blocks:
            if not isinstance(block, dict):
                raise ValueError("Invalid help block")
            kind = block.get("kind")
            if kind in TEXT_PATTERNS:
                keys(block, ("kind", "text"))
                replacements.append(paragraph(paragraphs[TEXT_PATTERNS[kind]], text(block["text"])))
            elif kind == "image":
                keys(block, ("kind", "pageId", "path", "sha256", "caption"))
                page_id = text(block["pageId"])
                if key != "experience" or page_id not in ui_pages or page_id in seen_pages:
                    raise ValueError("Images must uniquely cover declared UI pages in the experience section")
                data, width, height = image_bytes(content_path.parent, block["path"], block["sha256"])
                image_id += 1
                rel_id = f"rIdHelp{image_id}"
                name = f"word/media/submission-help-{image_id}.png"
                if rel_id in used_ids or name in parts:
                    raise ValueError("Generated image identifier collides with the template")
                used_ids.add(rel_id)
                seen_pages.add(page_id)
                new_parts[name] = data
                ET.SubElement(rels, f"{{{REL}}}Relationship", Id=rel_id, Type=NS["r"] + "/image", Target=name[5:])
                replacements.append(image_paragraph(paragraphs[5], rel_id, image_id, width, height, text(block["caption"])))
                replacements.append(paragraph(paragraphs[5], block["caption"]))
            else:
                raise ValueError("Unsupported help block kind")
    missing_pages = sorted(set(ui_pages) - seen_pages)
    if not draft and (pending or missing_pages):
        raise ValueError("Final help is incomplete: resolve pending facts and provide every declared UI screenshot")
    if draft and (pending or missing_pages):
        replacements.append(paragraph(paragraphs[48], "Review items"))
        for item in pending + ["UI screenshot required: " + page for page in missing_pages]:
            replacements.append(paragraph(paragraphs[10], item))
    for child in list(body):
        if child.tag != f"{{{NS['w']}}}sectPr":
            body.remove(child)
    for index, node in enumerate(replacements):
        body.insert(index, node)
    parts["word/document.xml"] = encoded(doc)
    if "docProps/core.xml" in parts:
        core = xml(parts["docProps/core.xml"])
        values = {"{http://purl.org/dc/elements/1.1/}title": content["title"],
                  "{http://purl.org/dc/elements/1.1/}creator": author,
                  "{http://purl.org/dc/elements/1.1/}subject": "Driver help review draft" if draft else "Driver help",
                  "{http://schemas.openxmlformats.org/package/2006/metadata/core-properties}lastModifiedBy": author,
                  "{http://schemas.openxmlformats.org/package/2006/metadata/core-properties}revision": "1"}
        for tag, value in values.items():
            node = core.find(tag)
            if node is None:
                node = ET.SubElement(core, tag)
            node.text = value
        for name in ("created", "modified"):
            for node in core.findall("{http://purl.org/dc/terms/}" + name):
                core.remove(node)
        parts["docProps/core.xml"] = encoded(core)
    if "docProps/app.xml" in parts:
        app = xml(parts["docProps/app.xml"])
        app_ns = "{http://schemas.openxmlformats.org/officeDocument/2006/extended-properties}"
        for name in ("Pages", "Words", "Characters", "Lines", "Paragraphs", "CharactersWithSpaces", "AppVersion", "Template", "TotalTime"):
            for node in app.findall(app_ns + name):
                app.remove(node)
        for name, value in (("Company", author), ("Application", "CrestronHomeDevTools")):
            node = app.find(app_ns + name)
            if node is None:
                node = ET.SubElement(app, app_ns + name)
            node.text = value
        parts["docProps/app.xml"] = encoded(app)
    if new_parts:
        if not any(item.get("Extension") == "png" for item in types):
            ET.SubElement(types, f"{{{CT}}}Default", Extension="png", ContentType="image/png")
        parts["word/_rels/document.xml.rels"] = encoded(rels)
        parts["[Content_Types].xml"] = encoded(types)
    output.parent.mkdir(parents=True, exist_ok=True)
    with output.open("xb") as destination:
        with ZipFile(destination, "w") as archive:
            for info in infos:
                archive.writestr(info, parts[info.filename])
            for name, data in new_parts.items():
                archive.writestr(name, data)
    return {"schemaVersion": 1, "draft": draft, "templateSha256": sha(source),
            "contentSha256": sha(content_bytes), "docxSha256": sha(output.read_bytes()),
            "pendingItems": len(pending), "missingUiPages": missing_pages,
            "renderValidationRequired": True}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ("template", "template-sha256", "content", "output"):
        parser.add_argument("--" + name, required=True)
    parser.add_argument("--draft", action="store_true")
    args = parser.parse_args()
    try:
        report = build(args.template, args.template_sha256, args.content, args.output, args.draft)
        print(json.dumps(report, indent=2))
        return 0
    except (ValueError, OSError, KeyError, ET.XMLSyntaxError) as error:
        print(f"Help build failed: {error}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
