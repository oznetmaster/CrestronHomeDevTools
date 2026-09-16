# Copyright (c) 2026 Neil Colvin.
# Licensed under the MIT License. See LICENSE in the repository root.
"""Exercise generation, build-input binding and final package tamper detection."""

import json
from pathlib import Path
import subprocess
import sys
import unittest
from unittest.mock import patch
from zipfile import ZipFile

import build_help
import package_help
import render_help
import test_build_help
import test_render_help


class PackageHelpTests(unittest.TestCase):
    def setUp(self):
        self.fixture = test_build_help.HelpBuilderTests()
        self.fixture.setUp()
        self.addCleanup(self.fixture.tearDown)
        self.root = self.fixture.root
        self.manifest = self.root / "driver.json"
        self.general = {"Guid": "ff6818a8-af92-49ea-aeaf-1b7f02c30b2f", "DriverVersion": "1.2.003.0000",
                        "Developer": {"Company": "Example Developer", "Email": "support@example.org"}, "DependencyGroup": ""}
        self.assembly = "Example_Platform_Device_IP"
        self.output = self.root / "prepared"
        self.receipt = self.output / "help-receipt.json"
        self.include = self.root / "IncludeInPkg"
        self.package = self.root / (self.assembly + ".pkg")
        self.save()

    def save(self):
        self.manifest.write_text(json.dumps({"GeneralInformation": self.general}), encoding="utf-8")
        self.fixture.content_path.write_text(json.dumps(self.fixture.content), encoding="utf-8")

    def fake_process(self, arguments, timeout):
        if "--version" in arguments:
            return subprocess.CompletedProcess(arguments, 0, b"LibreOffice test renderer", b"")
        docx = Path(arguments[-1])
        with ZipFile(docx) as archive:
            doc = build_help.xml(archive.read("word/document.xml"))
        content = " ".join(doc.xpath("//w:t/text()", namespaces=build_help.NS))
        test_render_help.pdf(docx.with_suffix(".pdf"), content)
        return subprocess.CompletedProcess(arguments, 0, b"", b"")

    def prepare(self):
        with patch.object(render_help, "run_process", side_effect=self.fake_process):
            return package_help.prepare(self.manifest, self.fixture.content_path, self.assembly,
                                        "Example", "support@example.org", self.fixture.template,
                                        self.fixture.digest, sys.executable, self.output)

    def stage(self):
        return package_help.stage(self.receipt, self.manifest, self.fixture.content_path, self.assembly, self.include)

    def make_package(self, pdf=None, driver_version=None, pdf_name=None):
        metadata = {"driverId": self.general["Guid"], "driverVersion": driver_version or self.general["DriverVersion"],
                    "assemblyFileName": self.assembly + ".dll", "developerContact": {"email": "support@example.org"}}
        with ZipFile(self.package, "w") as archive:
            archive.writestr(self.assembly + ".dll", b"synthetic DLL; never deployed")
            archive.writestr(self.assembly + ".dat", json.dumps(metadata))
            archive.writestr(pdf_name or self.assembly + ".pdf", pdf if pdf is not None else (self.include / (self.assembly + ".pdf")).read_bytes())

    def verify(self):
        return package_help.verify(self.receipt, self.manifest, self.fixture.content_path, self.assembly, self.package)

    def test_final_pdf_is_embedded_byte_for_byte_and_bound_to_the_package(self):
        prepared = self.prepare()
        self.include.mkdir()
        (self.include / "UiDefinition.xml").write_bytes(b"keep other assets")
        staged = self.stage()
        self.make_package()
        report = self.verify()
        self.assertTrue(report["packagedHelpVerified"])
        self.assertTrue(report["submissionValidationStillRequired"])
        self.assertEqual(prepared["render"]["pdfSha256"], staged["pdfSha256"])
        self.assertEqual(staged["pdfSha256"], report["pdfSha256"])
        self.assertEqual(build_help.sha(self.package.read_bytes()), report["packageSha256"])
        self.assertEqual(build_help.sha(self.receipt.read_bytes()), report["helpReceiptSha256"])
        self.assertEqual((self.include / "UiDefinition.xml").read_bytes(), b"keep other assets")
        self.assertFalse((self.include / "help-receipt.json").exists())

    def test_pending_help_never_produces_a_receipt(self):
        self.fixture.content["pending"] = ["Model support needs verification"]
        self.save()
        with self.assertRaisesRegex(ValueError, "incomplete"):
            self.prepare()
        self.assertFalse(self.receipt.exists())

    def test_missing_declared_screenshot_never_produces_a_receipt(self):
        self.fixture.content["uiPages"] = ["home"]
        self.save()
        with self.assertRaisesRegex(ValueError, "incomplete"):
            self.prepare()
        self.assertFalse(self.receipt.exists())

    def test_manifest_help_version_mismatch_stops_before_rendering(self):
        self.general["DriverVersion"] = "1.2.4.0"
        self.save()
        with self.assertRaisesRegex(ValueError, "version differs"):
            self.prepare()
        self.assertFalse(self.output.exists())

    def test_unapproved_support_address_is_rejected(self):
        self.general["Developer"]["Email"] = "private@example.org"
        self.save()
        with self.assertRaisesRegex(ValueError, "approved public"):
            self.prepare()

    def test_missing_developer_filename_token_is_rejected(self):
        self.assembly = "Platform_Device_IP"
        with self.assertRaisesRegex(ValueError, "developer token"):
            self.prepare()

    def test_changed_source_manifest_and_content_each_prevent_staging(self):
        self.prepare()
        for path in (self.manifest, self.fixture.content_path):
            with self.subTest(path=path.name):
                original = path.read_bytes()
                path.write_bytes(original + b"\n")
                with self.assertRaisesRegex(ValueError, "different build inputs"):
                    self.stage()
                path.write_bytes(original)
                self.assertFalse(self.include.exists())

    def test_changed_pdf_and_docx_each_prevent_staging(self):
        self.prepare()
        for suffix in (".pdf", ".docx"):
            with self.subTest(suffix=suffix):
                path = self.output / (self.assembly + suffix)
                original = path.read_bytes()
                path.write_bytes(original + b"modified")
                with self.assertRaisesRegex(ValueError, "generated and rendered"):
                    self.stage()
                path.write_bytes(original)

    def test_prior_build_directory_and_competing_help_are_not_reused(self):
        self.prepare()
        with self.assertRaises(FileExistsError):
            self.prepare()
        self.include.mkdir()
        competing = self.include / (self.assembly.lower() + ".pdf")
        competing.write_bytes(b"old")
        with self.assertRaisesRegex(ValueError, "already contains"):
            self.stage()
        self.assertEqual(competing.read_bytes(), b"old")

    def test_missing_replaced_or_case_changed_packaged_help_is_rejected(self):
        self.prepare()
        self.stage()
        for changes in ({"pdf": b"%PDF-old"}, {"pdf_name": self.assembly.lower() + ".pdf"}, {"pdf_name": "subdir/" + self.assembly + ".pdf"}):
            with self.subTest(changes=changes):
                self.make_package(**changes)
                with self.assertRaises(ValueError):
                    self.verify()

    def test_old_package_driver_version_is_rejected(self):
        self.prepare()
        self.stage()
        self.make_package(driver_version="1.2.2.0")
        with self.assertRaisesRegex(ValueError, "identity"):
            self.verify()

    def test_duplicate_case_colliding_package_entries_are_rejected(self):
        self.prepare()
        self.stage()
        self.make_package()
        with ZipFile(self.package, "a") as archive:
            archive.writestr(self.assembly.lower() + ".pdf", b"old")
        with self.assertRaisesRegex(ValueError, "duplicate"):
            self.verify()


if __name__ == "__main__":
    unittest.main()
