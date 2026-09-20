# Copyright (c) 2026 Neil Colvin.
# Licensed under the MIT License. See LICENSE in the repository root.
"""Bind reviewed notices to the actual merge inputs and verify packaged bytes."""

import argparse
import io
import json
from pathlib import Path
import re
import sys
from zipfile import ZipFile, BadZipFile

from build_help import keys, sha, text, strict_object
from package_help import read_json, write_json

FILENAME = "THIRD-PARTY-NOTICES.txt"


def checked_file(path):
    path = Path(path).absolute()
    for part in (path, *path.parents):
        if part.is_symlink() or part.is_junction():
            raise ValueError("Notice inputs must not traverse links or junctions")
    if not path.is_file() or path.stat().st_size > 16 * 1024 * 1024:
        raise ValueError("Missing or oversized notice input")
    return path.read_bytes()


def file_name(value):
    if not isinstance(value, str) or value != value.strip() or not re.fullmatch(r"[A-Za-z0-9_. -]+\.dll", value):
        raise ValueError("Use a plain assembly filename")
    return value


def digest(value):
    if not isinstance(value, str) or not re.fullmatch(r"[a-f0-9]{64}", value):
        raise ValueError("Use a reviewed lowercase SHA-256")
    return value


def prepare(manifest, merge_inputs, driver_assembly):
    # Explicit build roots may be workspace aliases (for example C:/Projects
    # pointing to D:/Projects). Resolve them before checking child notice paths.
    manifest = Path(manifest).resolve(strict=True)
    data = checked_file(manifest)
    # Never pin one read while interpreting different bytes.
    plan = json.loads(data, object_pairs_hook=strict_object)
    keys(plan, ("schemaVersion", "driverNoticeIds", "documents", "components"))
    if type(plan["schemaVersion"]) is not int or plan["schemaVersion"] != 1:
        raise ValueError("Unsupported dependency-notice inventory")
    base = Path(manifest).absolute().parent
    documents = {}
    if not isinstance(plan["documents"], list) or not plan["documents"]:
        raise ValueError("Supply reviewed license and notice documents")
    for item in plan["documents"]:
        keys(item, ("id", "path", "sha256", "source"))
        identifier, relative = text(item["id"]), text(item["path"])
        if identifier in documents or "\\" in relative or ":" in relative or any(p in ("", ".", "..") for p in relative.split("/")):
            raise ValueError("Duplicate or unsafe notice document")
        raw = checked_file(base / relative)
        if sha(raw) != digest(item["sha256"]):
            raise ValueError("A reviewed notice changed")
        documents[identifier] = (text(item["source"]), raw.decode("utf-8-sig"))
    used = set()
    def notice_ids(value):
        if not isinstance(value, list) or not value or any(not isinstance(i, str) or i not in documents for i in value) or len(set(value)) != len(value):
            raise ValueError("Every component needs distinct reviewed notice references")
        used.update(value)
        return ", ".join(value)
    lines = ["Driver and third-party notices", "", "Driver license documents: " + notice_ids(plan["driverNoticeIds"]), "",
             "Merged dependency inventory (third-party terms remain applicable):", ""]
    expected = {}
    if not isinstance(plan["components"], list) or not plan["components"]:
        raise ValueError("Supply the merged dependency inventory")
    for item in plan["components"]:
        keys(item, ("assembly", "sha256", "packageId", "packageVersion", "license", "copyright", "noticeIds"))
        assembly = file_name(item["assembly"])
        if assembly.casefold() in expected:
            raise ValueError("Duplicate dependency assembly")
        expected[assembly.casefold()] = digest(item["sha256"])
        lines.extend([f"{text(item['packageId'])} {text(item['packageVersion'])} ({assembly})",
                      "Declared license: " + text(item["license"]), text(item["copyright"]),
                      "Documents: " + notice_ids(item["noticeIds"]), ""])
    if used != set(documents):
        raise ValueError("Notice inventory has unreferenced documents")
    actual, driver_found = {}, False
    driver_assembly = Path(driver_assembly).resolve(strict=True)
    # This must be the same input list passed to the project's merge command.
    for value in checked_file(Path(merge_inputs).resolve(strict=True)).decode("utf-8-sig").splitlines():
        if not value.strip():
            continue
        path = Path(value)
        if not path.is_absolute():
            raise ValueError("Merge inputs require absolute build-local paths")
        path = path.resolve(strict=True)
        raw = checked_file(path)
        if path.absolute() == driver_assembly:
            if driver_found:
                raise ValueError("Driver appears twice in merge inputs")
            driver_found = True
            continue
        name = file_name(path.name).casefold()
        if name in actual:
            raise ValueError("Duplicate merge dependency")
        actual[name] = sha(raw)
    if not driver_found or actual != expected:
        raise ValueError("Merged dependencies differ from the reviewed notices; review package or assembly changes")
    for identifier, (source, content) in documents.items():
        lines.extend(["=" * 72, identifier, "Source: " + source, "", content, ""])
    rendered = ("\n".join(lines)).encode("utf-8")
    return rendered, {"schemaVersion": 1, "inventorySha256": sha(data), "noticesSha256": sha(rendered),
                      "fileName": FILENAME, "mergedDependencies": actual}


def stage(manifest, merge_inputs, driver_assembly, include_directory, receipt):
    rendered, report = prepare(manifest, merge_inputs, driver_assembly)
    output = Path(include_directory)
    if not output.is_dir() or Path(receipt).exists():
        raise ValueError("Require an existing private stage and a new receipt")
    with (output / FILENAME).open("xb") as stream:
        stream.write(rendered)
    write_json(receipt, report)
    return report


def verify(manifest, merge_inputs, driver_assembly, receipt, package):
    rendered, expected = prepare(manifest, merge_inputs, driver_assembly)
    _, recorded = read_json(receipt)
    if recorded != expected:
        raise ValueError("Staged dependency notices no longer match this build")
    data = checked_file(Path(package).resolve(strict=True))
    with ZipFile(io.BytesIO(data)) as archive:
        entries = [e for e in archive.infolist() if e.filename.casefold() == FILENAME.casefold()]
        if len(entries) != 1 or entries[0].orig_filename != FILENAME or entries[0].flag_bits & 1 or entries[0].file_size != len(rendered) or archive.read(entries[0]) != rendered:
            raise ValueError("Package must contain the exact reviewed notices at its root")
    return {**expected, "packageSha256": sha(data), "packagedNoticesVerified": True}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("command", choices=("stage", "verify"))
    for name in ("manifest", "merge-inputs", "driver-assembly", "receipt"):
        parser.add_argument("--" + name, required=True)
    parser.add_argument("--include-directory")
    parser.add_argument("--package")
    parser.add_argument("--report")
    args = parser.parse_args()
    try:
        common = (args.manifest, args.merge_inputs, args.driver_assembly)
        if args.command == "stage":
            if not args.include_directory or args.package or args.report:
                raise ValueError("Stage requires only an include directory")
            stage(*common, args.include_directory, args.receipt)
        else:
            if not args.package or not args.report or args.include_directory:
                raise ValueError("Verify requires package and a new report")
            write_json(args.report, verify(*common, args.receipt, args.package))
        print("Dependency notices checked successfully.")
        return 0
    except (ValueError, KeyError, TypeError, OSError, BadZipFile):
        print("Dependency notice validation failed; review inventory, inputs and packaged bytes.", file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
