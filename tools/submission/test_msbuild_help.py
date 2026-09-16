# Copyright (c) 2026 Neil Colvin.
# Licensed under the MIT License. See LICENSE in the repository root.
"""Run real MSBuild ordering with synthetic source, renderer and package compiler."""

import json
import os
from pathlib import Path
import shlex
import shutil
import subprocess
import sys
import unittest
from xml.etree import ElementTree as ET

import test_package_help


class MsBuildHelpTests(unittest.TestCase):
    def setUp(self):
        self.fixture = test_package_help.PackageHelpTests()
        self.fixture.setUp()
        self.addCleanup(self.fixture.doCleanups)
        self.root = self.fixture.root
        self.tools = Path(__file__).resolve().parent
        self.dotnet = os.environ.get("SUBMISSION_TEST_DOTNET") or shutil.which("dotnet")
        if not self.dotnet and os.name == "nt":
            self.dotnet = str(Path(os.environ.get("ProgramFiles", "C:/Program Files")) / "dotnet/dotnet.exe")
        if not self.dotnet or not Path(self.dotnet).is_file():
            self.fail("Install the .NET SDK or set SUBMISSION_TEST_DOTNET for MSBuild integration tests")
        fake = self.root / "fixture_tool.py"
        fake.write_text('''# Synthetic test tools, never used for driver builds.
import json, sys
from pathlib import Path
from zipfile import ZipFile, ZipInfo
sys.path.insert(0, TOOLS)
import build_help
from test_render_help import pdf
if '--version' in sys.argv:
    print('LibreOffice synthetic integration renderer')
elif sys.argv[1] == 'pack':
    root, name = Path(sys.argv[2]), sys.argv[3]
    if (root.parent / 'pack-fail').exists():
        sys.exit(7)
    metadata = {'driverId': 'ff6818a8-af92-49ea-aeaf-1b7f02c30b2f', 'driverVersion': '1.2.003.0000',
                'assemblyFileName': name + '.dll', 'developerContact': {'email': 'support@example.org'}}
    with ZipFile(root / (name + '.pkg'), 'w') as package:
        package.writestr(name + '.dll', b'synthetic; never deployed')
        package.writestr(name + '.dat', json.dumps(metadata))
        raw_path = 'Translations' + chr(92) + 'en-US.json'
        entry = ZipInfo(raw_path)
        entry.filename = entry.orig_filename = raw_path
        package.writestr(entry, b'{}')
        if (root.parent / 'collision').exists():
            package.writestr('Translations/en-US.json', b'different')
        if (root.parent / 'corrupt-help').exists():
            package.writestr(name + '.pdf', b'wrong help')
        else:
            package.write(root / 'patched/IncludeInPkg' / (name + '.pdf'), name + '.pdf')
else:
    source = Path(sys.argv[-1])
    with ZipFile(source) as archive:
        document = build_help.xml(archive.read('word/document.xml'))
    pdf(source.with_suffix('.pdf'), ' '.join(document.xpath('//w:t/text()', namespaces=build_help.NS)))
'''.replace("TOOLS", repr(str(self.tools))), encoding="utf-8")
        renderer = self.root / ("renderer.cmd" if os.name == "nt" else "renderer")
        if os.name == "nt":
            renderer.write_text(f'@"{sys.executable}" "{fake}" %*\n', encoding="utf-8")
        else:
            renderer.write_text(f'#!/bin/sh\nexec {shlex.quote(sys.executable)} {shlex.quote(str(fake))} "$@"\n', encoding="utf-8")
            renderer.chmod(0o700)
        project = ET.Element("Project")
        props = ET.SubElement(project, "PropertyGroup")
        values = {"CrestronSubmission": "true", "Configuration": "Release", "BaseIntermediateOutputPath": "obj/",
                  "TargetDir": str(self.root / "bin") + "/", "AssemblyName": self.fixture.assembly,
                  "DriverManifestFile": str(self.fixture.manifest), "SubmissionPython": sys.executable,
                  "SubmissionSoffice": str(renderer), "SubmissionHelpTemplate": str(self.fixture.fixture.template),
                  "SubmissionHelpTemplateSha256": self.fixture.fixture.digest,
                  "SubmissionHelpContent": str(self.fixture.fixture.content_path), "SubmissionDeveloperToken": "Example",
                  "SubmissionSupportEmail": "support@example.org"}
        for key, value in values.items():
            ET.SubElement(props, key).text = value
        ET.SubElement(project, "Import", Project=str(self.tools / "CrestronSubmissionHelp.targets"))
        ET.SubElement(project, "Target", Name="BumpDriverVersion", DependsOnTargets="ValidateSubmissionHelpSettings")
        compile_target = ET.SubElement(project, "Target", Name="CoreCompile")
        ET.SubElement(compile_target, "WriteLinesToFile", File=str(self.root / "compiled.txt"), Lines="compiled", Overwrite="true")
        ET.SubElement(project, "Target", Name="Build", DependsOnTargets="CoreCompile")
        pack = ET.SubElement(project, "Target", Name="PackageDriver", AfterTargets="Build",
                             Condition="'$(BuildForTests)' != 'true' And '$(DesignTimeBuild)' != 'true'")
        ET.SubElement(pack, "RemoveDir", Directories="$(TargetDir)patched/IncludeInPkg")
        ET.SubElement(pack, "MakeDir", Directories="$(TargetDir)patched/IncludeInPkg")
        ET.SubElement(pack, "WriteLinesToFile", File="$(TargetDir)patched/IncludeInPkg/UiDefinition.xml", Lines="ordinary asset")
        ET.SubElement(pack, "CallTarget", Targets="StageSubmissionHelp", Condition="'$(CrestronSubmission)' == 'true'")
        ET.SubElement(pack, "Exec", Command=f'"{sys.executable}" "{fake}" pack "$(TargetDir)." "$(AssemblyName)"',
                      Condition="'$(CrestronSubmission)' == 'true'")
        ET.SubElement(pack, "CallTarget", Targets="VerifySubmissionHelp", Condition="'$(CrestronSubmission)' == 'true'")
        self.project = self.root / "build.proj"
        ET.ElementTree(project).write(self.project, encoding="utf-8", xml_declaration=True)

    def run_build(self, *properties):
        env = os.environ.copy()
        env.update(DOTNET_CLI_TELEMETRY_OPTOUT="1", DOTNET_NOLOGO="1", MSBUILDDISABLENODEREUSE="1")
        return subprocess.run([self.dotnet, "msbuild", str(self.project), "-t:Build", "-nologo", "-v:minimal", "-nr:false", *properties],
                              cwd=self.root, env=env, capture_output=True, text=True, timeout=60,
                              creationflags=subprocess.CREATE_NO_WINDOW if os.name == "nt" else 0)

    def test_real_msbuild_prepares_then_stages_after_assets_and_verifies_packaged_bytes(self):
        for attempt in range(2):
            result = self.run_build()
            self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
            reports = list((self.root / "obj/submission-help").glob("*/packaged-help.json"))
            self.assertEqual(len(reports), attempt + 1)
            self.assertTrue(all(json.loads(path.read_text())["packagedHelpVerified"] for path in reports))
            for report in reports:
                paths = json.loads(report.with_name('package-paths.json').read_text())
                self.assertTrue(paths['payloadBytesPreserved'])
                self.assertEqual(len(paths['renamedEntries']), 1)
                self.assertEqual(paths['packageSha256'], json.loads(report.read_text())['packageSha256'])
        self.assertTrue((self.root / "compiled.txt").exists())
        self.assertTrue((self.root / "bin/patched/IncludeInPkg/UiDefinition.xml").exists())

    def test_pending_facts_stop_msbuild_before_compilation_or_packaging(self):
        self.fixture.fixture.content["pending"] = ["Unverified models"]
        self.fixture.save()
        result = self.run_build()
        self.assertNotEqual(result.returncode, 0)
        self.assertIn("incomplete", result.stdout + result.stderr)
        self.assertFalse((self.root / "compiled.txt").exists())
        self.assertFalse((self.root / "bin").exists())

    def test_ordinary_test_and_designtime_builds_do_not_require_renderer(self):
        for properties in (("-p:CrestronSubmission=false",), ("-p:BuildForTests=true",), ("-p:DesignTimeBuild=true",)):
            with self.subTest(properties=properties):
                result = self.run_build(*properties, "-p:SubmissionSoffice=missing", "-p:SubmissionPython=missing")
                self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertFalse((self.root / "obj/submission-help").exists())

    def test_candidate_cannot_ignore_packaging_errors(self):
        result = self.run_build("-p:PackageDriverIgnoreExitCode=true")
        self.assertNotEqual(result.returncode, 0)
        self.assertIn("cannot ignore", result.stdout + result.stderr)
        self.assertFalse((self.root / "compiled.txt").exists())

    def test_failed_packager_cannot_reuse_an_old_package_or_emit_success(self):
        (self.root / "pack-fail").touch()
        (self.root / "bin").mkdir()
        old_package = self.root / "bin" / (self.fixture.assembly + ".pkg")
        old_package.write_bytes(b"old package must not be reused")
        result = self.run_build()
        self.assertNotEqual(result.returncode, 0)
        self.assertIn("code 7", result.stdout + result.stderr)
        self.assertFalse(old_package.exists())
        self.assertFalse(list((self.root / "obj/submission-help").glob("*/packaged-help.json")))

    def test_successful_packager_with_wrong_help_fails_the_build(self):
        (self.root / "corrupt-help").touch()
        result = self.run_build()
        self.assertNotEqual(result.returncode, 0)
        self.assertIn("Packaged help differs", result.stdout + result.stderr)
        self.assertFalse(list((self.root / "obj/submission-help").glob("*/packaged-help.json")))

    def test_debug_submission_does_not_reach_compilation(self):
        result = self.run_build("-p:Configuration=Debug")
        self.assertNotEqual(result.returncode, 0)
        self.assertIn("Release configuration", result.stdout + result.stderr)
        self.assertFalse((self.root / "compiled.txt").exists())

    def test_normalization_collision_stops_before_successful_help_receipt(self):
        (self.root / 'collision').touch()
        result = self.run_build()
        self.assertNotEqual(result.returncode, 0)
        self.assertIn('collide', result.stdout + result.stderr)
        self.assertFalse(list((self.root / 'obj/submission-help').glob('*/packaged-help.json')))


if __name__ == "__main__":
    unittest.main()
