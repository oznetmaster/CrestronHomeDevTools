# Copyright (c) 2026 Neil Colvin.
# Licensed under the MIT License. See LICENSE in the repository root.
"""Offline regression tests use synthetic OOXML, never redistribute SDK templates."""

import copy
import io
import json
from pathlib import Path
import tempfile
import unittest
from zipfile import ZipFile

from lxml import etree as ET
from PIL import Image

import build_help as help_builder


class HelpBuilderTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)
        self.template = self.root / "template.docx"
        self.content_path = self.root / "content.json"
        self.output = self.root / "Example.docx"
        doc = ET.Element("{" + help_builder.NS["w"] + "}document", nsmap=help_builder.NS)
        body = ET.SubElement(doc, "{" + help_builder.NS["w"] + "}body")
        headings = {index: heading for _, index, heading in help_builder.SECTIONS}
        for i in range(54):
            p = ET.SubElement(body, "{" + help_builder.NS["w"] + "}p")
            props = ET.SubElement(p, "{" + help_builder.NS["w"] + "}pPr")
            ET.SubElement(props, "{" + help_builder.NS["w"] + "}pStyle", {"{" + help_builder.NS["w"] + "}val": str(i)})
            r = ET.SubElement(p, "{" + help_builder.NS["w"] + "}r")
            ET.SubElement(r, "{" + help_builder.NS["w"] + "}t").text = headings.get(i, "template example")
        ET.SubElement(body, "{" + help_builder.NS["w"] + "}sectPr")
        self.parts = {
            "word/document.xml": help_builder.encoded(doc),
            "word/_rels/document.xml.rels": f'<Relationships xmlns="{help_builder.REL}"/>'.encode(),
            "[Content_Types].xml": f'<Types xmlns="{help_builder.CT}"/>'.encode(),
            "word/styles.xml": b"styles must remain byte-for-byte unchanged",
            "customXml/item1.xml": b"<opaque>preserve me</opaque>"}
        self.save_template()
        self.content = {"schemaVersion": 1, "title": "Example driver help", "author": "Example Developer", "version": "1.2.003.0000",
                        "pending": [], "uiPages": [],
                        "sections": {key: [{"kind": "paragraph", "text": "Content for " + key}] for key, _, _ in help_builder.SECTIONS}}

    def tearDown(self):
        self.temp.cleanup()

    def save_template(self):
        with ZipFile(self.template, "w") as z:
            for name, data in self.parts.items():
                z.writestr(name, data)
        self.digest = help_builder.sha(self.template.read_bytes())

    def build(self, draft=False):
        self.content_path.write_text(json.dumps(self.content), encoding="utf-8")
        return help_builder.build(self.template, self.digest, self.content_path, self.output, draft)

    def add_image(self):
        image = self.root / "figure.png"
        Image.new("RGB", (64, 128), "white").save(image)
        block = {"kind": "image", "pageId": "home", "path": "figure.png",
                 "sha256": help_builder.sha(image.read_bytes()), "caption": "Synthetic home page figure"}
        self.content["uiPages"] = ["home"]
        self.content["sections"]["experience"].append(block)
        return block

    def test_preserves_unedited_parts_and_template_bytes(self):
        original = self.template.read_bytes()
        report = self.build()
        self.assertFalse(report["draft"])
        self.assertTrue(report["renderValidationRequired"])
        self.assertEqual(original, self.template.read_bytes())
        with ZipFile(self.output) as z:
            for name, data in self.parts.items():
                if name != "word/document.xml":
                    self.assertEqual(z.read(name), data)
            doc = help_builder.xml(z.read("word/document.xml"))
            for key, _, heading in help_builder.SECTIONS:
                self.assertIn(heading, "".join(doc.itertext()))
                self.assertIn("Content for " + key, "".join(doc.itertext()))
            self.assertEqual(len(doc.findall("w:body/w:sectPr", help_builder.NS)), 1)

    def test_different_template_digest_is_rejected_without_output(self):
        self.digest = "0" * 64
        with self.assertRaisesRegex(ValueError, "digest mismatch"):
            self.build()
        self.assertFalse(self.output.exists())

    def test_missing_or_unknown_section_is_rejected(self):
        original = copy.deepcopy(self.content)
        for change in ("missing", "unknown"):
            with self.subTest(change=change):
                self.content = copy.deepcopy(original)
                if change == "missing":
                    del self.content["sections"]["contact"]
                else:
                    self.content["sections"]["extra"] = []
                with self.assertRaises(ValueError):
                    self.build()
                self.assertFalse(self.output.exists())

    def test_pending_facts_block_final_help(self):
        self.content["pending"] = ["Verify minimum firmware"]
        with self.assertRaisesRegex(ValueError, "incomplete"):
            self.build()
        self.assertFalse(self.output.exists())

    def test_review_draft_keeps_pending_items_and_cannot_use_final_filename(self):
        self.content["pending"] = ["Verify minimum firmware"]
        self.content["uiPages"] = ["thermostat"]
        with self.assertRaisesRegex(ValueError, "review.docx"):
            self.build(draft=True)
        self.output = self.root / "Example.review.docx"
        report = self.build(draft=True)
        self.assertEqual(report["missingUiPages"], ["thermostat"])
        with ZipFile(self.output) as z:
            result = "".join(help_builder.xml(z.read("word/document.xml")).itertext())
            for value in ("REVIEW DRAFT", "Verify minimum firmware", "UI screenshot required: thermostat"):
                self.assertIn(value, result)

    def test_declared_ui_page_needs_image(self):
        self.content["uiPages"] = ["home"]
        with self.assertRaisesRegex(ValueError, "incomplete"):
            self.build()

    def test_approved_image_embedded_unchanged_with_valid_relationship(self):
        block = self.add_image()
        self.build()
        with ZipFile(self.output) as z:
            rels = help_builder.xml(z.read("word/_rels/document.xml.rels"))
            doc = help_builder.xml(z.read("word/document.xml"))
            rid = doc.xpath("//a:blip/@r:embed", namespaces=help_builder.NS)[0]
            relationship = next(r for r in rels if r.get("Id") == rid)
            self.assertEqual(z.read("word/" + relationship.get("Target")), (self.root / block["path"]).read_bytes())
            self.assertIn(block["caption"], "".join(doc.itertext()))
            self.assertEqual(z.read("word/styles.xml"), self.parts["word/styles.xml"])

    def test_changed_or_escaping_image_is_rejected(self):
        block = self.add_image()
        for name, value in (("sha256", "0" * 64), ("path", "../figure.png"), ("path", str(self.root / "figure.png"))):
            with self.subTest(name=name, value=value):
                original = block[name]
                block[name] = value
                with self.assertRaises(ValueError):
                    self.build()
                block[name] = original
                self.assertFalse(self.output.exists())

    def test_duplicate_page_claim_is_rejected(self):
        block = self.add_image()
        self.content["sections"]["experience"].append(copy.deepcopy(block))
        with self.assertRaisesRegex(ValueError, "uniquely"):
            self.build()

    def test_unsafe_text_is_escaped_as_text(self):
        self.content["sections"]["driver"][0]["text"] = "Use <value> & check \"state\""
        self.build()
        with ZipFile(self.output) as z:
            self.assertIn('Use <value> & check "state"', "".join(help_builder.xml(z.read("word/document.xml")).itertext()))

    def test_existing_output_is_never_overwritten(self):
        self.output.write_bytes(b"keep existing work")
        with self.assertRaises(ValueError):
            self.build()
        self.assertEqual(self.output.read_bytes(), b"keep existing work")

    def test_changed_template_layout_is_rejected_even_when_hash_is_updated(self):
        self.parts["word/document.xml"] = self.parts["word/document.xml"].replace(b"Contact Information", b"Different section")
        self.save_template()
        with self.assertRaisesRegex(ValueError, "mapping changed"):
            self.build()

    def test_generated_document_does_not_claim_template_author_or_stale_counts(self):
        self.parts["docProps/core.xml"] = b'<cp:coreProperties xmlns:cp="http://schemas.openxmlformats.org/package/2006/metadata/core-properties" xmlns:dc="http://purl.org/dc/elements/1.1/" xmlns:dcterms="http://purl.org/dc/terms/"><dc:creator>Template author</dc:creator><dcterms:created>2001-01-01T00:00:00Z</dcterms:created></cp:coreProperties>'
        self.parts["docProps/app.xml"] = b'<Properties xmlns="http://schemas.openxmlformats.org/officeDocument/2006/extended-properties"><Company>Template company</Company><Pages>99</Pages></Properties>'
        self.save_template()
        self.build()
        with ZipFile(self.output) as z:
            core = help_builder.xml(z.read("docProps/core.xml"))
            self.assertIn("Example Developer", "".join(core.itertext()))
            self.assertIn("Example driver help", "".join(core.itertext()))
            self.assertNotIn("Template author", "".join(core.itertext()))
            self.assertNotIn("2001-01-01", "".join(core.itertext()))
            app = help_builder.xml(z.read("docProps/app.xml"))
            self.assertNotIn("99", "".join(app.itertext()))
            self.assertNotIn("Template company", "".join(app.itertext()))


if __name__ == "__main__":
    unittest.main()
