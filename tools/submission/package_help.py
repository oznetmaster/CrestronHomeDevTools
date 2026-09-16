# Copyright (c) 2026 Neil Colvin.
# Licensed under the MIT License. See LICENSE in the repository root.
"""Build final help before compilation, stage it, and verify the packaged bytes."""

import argparse
import io
import json
from pathlib import Path
import re
import subprocess
import sys
from uuid import UUID
from urllib.parse import urlsplit
from zipfile import BadZipFile, ZipFile

from lxml import etree as ET

from build_help import build, keys, sha, strict_object, text
from render_help import render


def read_json(path):
    data = Path(path).read_bytes()
    if len(data) > 4 * 1024 * 1024:
        raise ValueError("Help build JSON exceeds 4 MiB")
    return data, json.loads(data, object_pairs_hook=strict_object)


def write_json(path, value):
    with Path(path).open("x", encoding="utf-8", newline="\n") as output:
        output.write(json.dumps(value, indent=2) + "\n")


def version(value):
    if not isinstance(value, str) or not re.fullmatch(r"\d{1,5}(?:\.\d{1,5}){3}", value):
        raise ValueError("Help and manifest require four-component driver versions")
    parts = tuple(int(part) for part in value.split("."))
    if max(parts) > 65534:
        raise ValueError("Driver version components exceed 65534")
    return parts


def identity(manifest, content, assembly, developer_token, support_email, support_website=""):
    if not isinstance(assembly, str) or not re.fullmatch(r"[A-Za-z0-9_-]{1,180}", assembly):
        raise ValueError("Use a plain driver assembly basename without a path or extension")
    if not re.fullmatch(r"[A-Za-z0-9-]+", developer_token) or developer_token not in assembly.split("_"):
        raise ValueError("New submission assembly name must include the developer token")
    if not support_email and not support_website:
        raise ValueError("Provide an approved public support email or website")
    if support_email and not re.fullmatch(r"[^\s<>@]+@[^\s<>@]+\.[^\s<>@]+", support_email):
        raise ValueError("Provide the approved public support email")
    if support_website:
        url = urlsplit(support_website)
        if (url.scheme not in ("http", "https") or not url.hostname or url.username is not None or
                url.password is not None or any(c.isspace() for c in support_website)):
            raise ValueError("Provide an absolute HTTP or HTTPS support website without embedded credentials")
    manifest_bytes, source = read_json(manifest)
    content_bytes, help_content = read_json(content)
    general = source["GeneralInformation"]
    driver_id = str(UUID(general["Guid"]))
    if version(general["DriverVersion"]) != version(help_content["version"]):
        raise ValueError("Help version differs from the driver manifest; update the public content")
    developer = general["Developer"]
    if support_email and text(developer.get("Email")).casefold() != support_email.casefold():
        raise ValueError("Manifest must contain the approved public support email")
    if support_website and developer.get("Website") != support_website:
        raise ValueError("Manifest must contain the approved public support website")
    text(developer["Company"])
    if not isinstance(general.get("DependencyGroup"), str):
        raise ValueError("Manifest requires a DependencyGroup string")
    result = {"assemblyName": assembly, "driverId": driver_id, "driverVersion": general["DriverVersion"],
            "developerToken": developer_token, "supportEmail": support_email,
            "manifestSha256": sha(manifest_bytes), "contentSha256": sha(content_bytes)}
    if support_website:
        result["supportWebsite"] = support_website
    return result


def prepare(manifest, content, assembly, developer_token, support_email, template,
            template_digest, soffice, output_directory, support_website=""):
    expected = identity(manifest, content, assembly, developer_token, support_email, support_website)
    output = Path(output_directory).resolve()
    # Each MSBuild invocation owns a new directory. Never accept an earlier build's PDF.
    output.mkdir(parents=True, exist_ok=False)
    docx = output / (assembly + ".docx")
    built = build(template, template_digest, content, docx)
    if built["contentSha256"] != expected["contentSha256"]:
        raise ValueError("Help content changed during generation")
    rendered = render(docx, built["docxSha256"], soffice, output)
    if identity(manifest, content, assembly, developer_token, support_email, support_website) != expected:
        raise ValueError("Manifest or help content changed during rendering")
    receipt = {"schemaVersion": 1, "identity": expected, "build": built, "render": rendered}
    write_json(output / "help-receipt.json", receipt)
    return receipt


def checked_help(receipt_path, manifest, content, assembly):
    receipt_bytes, receipt = read_json(receipt_path)
    keys(receipt, ("schemaVersion", "identity", "build", "render"))
    if type(receipt["schemaVersion"]) is not int or receipt["schemaVersion"] != 1:
        raise ValueError("Unsupported help receipt version")
    expected = receipt["identity"]
    current = identity(manifest, content, assembly, expected["developerToken"], expected["supportEmail"], expected.get("supportWebsite", ""))
    if current != expected:
        raise ValueError("Help receipt belongs to different build inputs")
    built, rendered = receipt["build"], receipt["render"]
    if (built["draft"] is not False or built["pendingItems"] != 0 or built["missingUiPages"] != [] or
            built["contentSha256"] != expected["contentSha256"] or rendered["sourceTextVerified"] is not True):
        raise ValueError("Help receipt does not describe completed final help")
    root = Path(receipt_path).resolve().parent
    docx = (root / (assembly + ".docx")).read_bytes()
    pdf = (root / (assembly + ".pdf")).read_bytes()
    if sha(docx) != built["docxSha256"] or sha(docx) != rendered["docxSha256"] or sha(pdf) != rendered["pdfSha256"]:
        raise ValueError("Help document differs from the generated and rendered bytes")
    return receipt, sha(receipt_bytes), pdf


def stage(receipt, manifest, content, assembly, include_directory):
    _, receipt_digest, pdf = checked_help(receipt, manifest, content, assembly)
    directory = Path(include_directory)
    directory.mkdir(parents=True, exist_ok=True)
    # The caller first copies ordinary assets. A pre-existing help file is a conflict.
    if any(p.name.casefold() == (assembly + ".pdf").casefold() for p in directory.iterdir()):
        raise ValueError("IncludeInPkg already contains candidate help; remove the competing source")
    with (directory / (assembly + ".pdf")).open("xb") as target:
        target.write(pdf)
    return {"helpReceiptSha256": receipt_digest, "pdfSha256": sha(pdf)}


def verify(receipt, manifest, content, assembly, package):
    prepared, receipt_digest, pdf = checked_help(receipt, manifest, content, assembly)
    package = Path(package)
    if package.name != assembly + ".pkg":
        raise ValueError("Package filename must exactly match the help and DLL basename")
    # Hash and inspect the same snapshot. Subsequent candidate checks pin this package hash.
    data = package.read_bytes()
    with ZipFile(io.BytesIO(data)) as archive:
        names = [entry.orig_filename for entry in archive.infolist()]
        if len(names) > 4096 or len(set(n.casefold() for n in names)) != len(names):
            raise ValueError("Package contains excessive or duplicate entries")
        if any("\\" in n or ":" in n or n.startswith("/") or any(p in (".", "..") for p in n.split("/")) for n in names):
            raise ValueError("Package contains unsafe archive paths")
        for suffix in (".dll", ".dat", ".pdf"):
            if assembly + suffix not in names:
                raise ValueError("Package requires matching root DLL, DAT and PDF names")
        entry = archive.getinfo(assembly + ".pdf")
        if entry.file_size != len(pdf) or archive.read(entry) != pdf:
            raise ValueError("Packaged help differs from the rendered PDF")
        entry = archive.getinfo(assembly + ".dat")
        if entry.file_size > 4 * 1024 * 1024:
            raise ValueError("Generated driver metadata exceeds 4 MiB")
        metadata = json.loads(archive.read(entry), object_pairs_hook=strict_object)
        expected = prepared["identity"]
        if (str(UUID(metadata["driverId"])) != expected["driverId"] or
                version(metadata["driverVersion"]) != version(expected["driverVersion"]) or
                metadata["assemblyFileName"] != assembly + ".dll" or
                (expected["supportEmail"] and text(metadata["developerContact"].get("email")).casefold() != expected["supportEmail"].casefold()) or
                (expected.get("supportWebsite") and metadata["developerContact"].get("website") != expected["supportWebsite"])):
            raise ValueError("Packaged driver identity or public support metadata differs from the help build")
    return {"schemaVersion": 1, "packageFileName": package.name, "packageSha256": sha(data),
            "helpReceiptSha256": receipt_digest, "pdfSha256": sha(pdf), "packagedHelpVerified": True,
            "submissionValidationStillRequired": True}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest="command", required=True)
    for name in ("prepare", "stage", "verify"):
        sub = commands.add_parser(name)
        for field in ("manifest", "content", "assembly"):
            sub.add_argument("--" + field, required=True)
        if name == "prepare":
            fields = ("developer-token", "template", "template-sha256", "soffice", "output-directory")
            sub.add_argument("--support-email", default="")
            sub.add_argument("--support-website", default="")
        else:
            fields = ("receipt", "include-directory") if name == "stage" else ("receipt", "package", "report")
        for field in fields:
            sub.add_argument("--" + field, required=True)
    args = vars(parser.parse_args())
    command = args.pop("command")
    report_path = args.pop("report", None)
    if "template_sha256" in args:
        args["template_digest"] = args.pop("template_sha256")
    try:
        report = {"prepare": prepare, "stage": stage, "verify": verify}[command](**args)
        if report_path:
            write_json(report_path, report)
        print(json.dumps(report, indent=2))
        return 0
    except (ValueError, OSError, KeyError, TypeError, BadZipFile, ET.XMLSyntaxError, subprocess.SubprocessError) as error:
        print(f"Submission help packaging failed: {error}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
