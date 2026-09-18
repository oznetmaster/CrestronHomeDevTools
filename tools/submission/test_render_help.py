# Copyright (c) 2026 Neil Colvin.
# Licensed under the MIT License. See LICENSE in the repository root.

import hashlib
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch
from zipfile import ZipFile

from pypdf import PdfWriter
from pypdf.generic import DictionaryObject, NameObject, DecodedStreamObject

import render_help


def pdf(path, text="Example help text", attachment=False):
    writer = PdfWriter()
    page = writer.add_blank_page(width=612, height=792)
    font = DictionaryObject({NameObject("/Type"): NameObject("/Font"), NameObject("/Subtype"): NameObject("/Type1"),
                             NameObject("/BaseFont"): NameObject("/Helvetica")})
    page[NameObject("/Resources")] = DictionaryObject({NameObject("/Font"): DictionaryObject({NameObject("/F1"): font})})
    content = DecodedStreamObject()
    content.set_data(("BT /F1 12 Tf 20 760 Td (" + text + ") Tj ET").encode("ascii"))
    page[NameObject("/Contents")] = content
    if attachment:
        writer.add_attachment("private.txt", b"must not be distributed")
    writer.write(path)


class HelpRendererTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)
        self.docx = self.root / "Example.docx"
        with ZipFile(self.docx, "w") as z:
            z.writestr("word/document.xml", '<w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:body><w:p><w:r><w:t>Example help text</w:t></w:r></w:p></w:body></w:document>')
        self.digest = hashlib.sha256(self.docx.read_bytes()).hexdigest()
        self.output = self.root / "output"
        self.convert = True
        self.calls = []

    def tearDown(self):
        self.temp.cleanup()

    def fake_process(self, arguments, timeout):
        self.calls.append(arguments)
        if "--version" in arguments:
            return subprocess.CompletedProcess(arguments, 0, b"LibreOffice fixture version", b"")
        if self.convert:
            pdf(Path(arguments[-1]).with_suffix(".pdf"))
        return subprocess.CompletedProcess(arguments, 0, b"", b"")

    def render(self):
        with patch.object(render_help, "run_process", side_effect=self.fake_process):
            return render_help.render(self.docx, self.digest, sys.executable, self.output)

    def test_render_checks_text_and_cleans_private_profile(self):
        report = self.render()
        self.assertEqual(report["pageCount"], 1)
        self.assertTrue(report["visualReviewRequired"])
        self.assertTrue(report["sourceTextVerified"])
        self.assertEqual(report["pdfSha256"], hashlib.sha256((self.output / "Example.pdf").read_bytes()).hexdigest())
        self.assertEqual([p.name for p in self.output.iterdir()], ["Example.pdf"])
        profiles = []
        for call in self.calls:
            self.assertIn("--headless", call)
            self.assertIn("--norestore", call)
            profiles.append(next(arg for arg in call if arg.startswith("-env:UserInstallation=")))
        self.assertEqual(profiles[0], profiles[1])

    def test_deep_build_output_is_not_passed_to_external_renderer(self):
        # Real Windows help conversion failed under the deep MSBuild receipt path,
        # while the same document converted successfully from a short work path.
        self.output = self.root / ("a" * 60) / ("b" * 60) / ("c" * 60)
        original_process = self.fake_process

        def bounded_process(arguments, timeout):
            if "--convert-to" in arguments:
                if len(arguments[-1]) >= 240:
                    raise OSError("External renderer cannot load this deep input path")
            return original_process(arguments, timeout)

        with patch.object(render_help, "run_process", side_effect=bounded_process):
            report = render_help.render(self.docx, self.digest, sys.executable, self.output)
        self.assertTrue(report["sourceTextVerified"])
        self.assertTrue((self.output / "Example.pdf").is_file())
        conversion = next(call for call in self.calls if "--convert-to" in call)
        self.assertFalse(Path(conversion[-1]).parent.exists(), "Private scratch must be cleaned")

    def test_success_exit_without_new_pdf_is_failure(self):
        self.convert = False
        with self.assertRaisesRegex(ValueError, "did not produce"):
            self.render()
        self.assertFalse((self.output / "Example.pdf").exists())

    def test_changed_docx_is_rejected_before_renderer(self):
        self.digest = "0" * 64
        with self.assertRaisesRegex(ValueError, "pinned"):
            self.render()
        self.assertFalse(self.calls)

    def test_missing_text_is_not_a_successful_render(self):
        output = self.root / "missing.pdf"
        pdf(output, "Some unrelated document")
        with self.assertRaisesRegex(ValueError, "source document text"):
            render_help.verify_pdf(self.docx.read_bytes(), output)

    def test_embedded_attachment_is_rejected(self):
        output = self.root / "attachment.pdf"
        pdf(output, attachment=True)
        with self.assertRaisesRegex(ValueError, "attachments"):
            render_help.verify_pdf(self.docx.read_bytes(), output)

    def test_existing_pdf_is_preserved(self):
        self.output.mkdir()
        target = self.output / "Example.pdf"
        target.write_bytes(b"existing work")
        with self.assertRaisesRegex(ValueError, "already exists"):
            self.render()
        self.assertEqual(target.read_bytes(), b"existing work")
        self.assertFalse(self.calls)

    def test_renderer_timeout_does_not_leave_passing_output(self):
        with patch.object(render_help, "run_process", side_effect=subprocess.TimeoutExpired("fixture", 1)):
            with self.assertRaises(subprocess.TimeoutExpired):
                render_help.render(self.docx, self.digest, sys.executable, self.output)
        self.assertEqual(list(self.output.iterdir()), [])

    def test_process_timeout_stops_its_own_child(self):
        with self.assertRaises(subprocess.TimeoutExpired):
            render_help.run_process([sys.executable, "-c", "import time; time.sleep(30)"], 0.2)


if __name__ == "__main__":
    unittest.main()
