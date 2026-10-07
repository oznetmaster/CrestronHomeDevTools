# Copyright (c) 2026 Neil Colvin. MIT licensed.
"""Retain portable review inputs; preserve original test evidence without executing tests."""
import argparse
import json
from pathlib import Path
import re
import sys
import tempfile

from lxml.etree import XMLSyntaxError
from audit_android import Evidence, digest, require
from build_help import keys, sha, strict_object
from package_help import read_json, write_json
from review_android import audit_runs
from self_test_form import pinned_json

MANIFEST = "review-inputs.json"
FIELDS = ("candidate", "inventory", "mapping", "policy", "observations", "package", "template")
MAX_FILES = 16384
MAX_BYTES = 1024 * 1024 * 1024


def relative(value):
    require(isinstance(value, str) and len(value) <= 1024 and ":" not in value and "\\" not in value and
            all(p not in ("", ".", "..") and not p.endswith((" ", ".")) and
                not re.search(r'[<>"|?*\x00-\x1f]', p) and
                p.split(".")[0].upper() not in {"CON", "PRN", "AUX", "NUL", *[f"COM{i}" for i in range(10)], *[f"LPT{i}" for i in range(10)]}
                for p in value.split("/")), "Unsafe portable input path")
    return value


def read_file(path):
    path = Path(path)
    require(path.is_absolute(), "Input paths must be absolute")
    Evidence.check_path(path)
    require(path.is_file() and path.stat().st_size <= 64 * 1024 * 1024, "Missing or oversized input")
    data = path.read_bytes()
    require(len(data) <= 64 * 1024 * 1024, "Oversized input")
    return data


def freeze(settings_path, output, candidate_digest, inventory_digest, mapping_digest, source_commit, *,
           android_pins=None, android_pins_sha256=None, review_mode="complete", declarations=None, declarations_sha256=None):
    _, settings = read_json(settings_path)
    keys(settings, ("schemaVersion", *FIELDS, "evidence", "output", "title", "author"), ("androidEvidence", "dotnet", "validator"))
    require(type(settings["schemaVersion"]) is int and settings["schemaVersion"] == 1, "Unsupported settings")
    require(re.fullmatch(r"[0-9a-f]{40}", source_commit or ""), "Supply the release commit")
    require(review_mode in ("complete", "declared-gaps"), "Unsupported review mode")
    require((review_mode == "declared-gaps") == (declarations is not None and declarations_sha256 is not None), "Gap declarations require explicit mode")
    if review_mode == "complete":
        require(declarations is None and declarations_sha256 is None, "Unexpected declarations")
    _, candidate = pinned_json(settings["candidate"], candidate_digest)
    require(candidate["identity"]["sourceCommit"] == source_commit, "Candidate does not identify the selected release commit")
    android = audit_runs(settings, android_pins, android_pins_sha256, candidate_digest)
    expected = {"candidate": digest(candidate_digest), "inventory": digest(inventory_digest), "mapping": digest(mapping_digest),
                "policy": digest(candidate["identity"]["policySha256"]), "template": digest(candidate["identity"]["templateSha256"]),
                "package": digest(candidate["identity"]["packageSha256"])}
    output = Path(output)
    require(output.is_absolute() and not output.exists() and output.parent.is_dir(), "Use a new private output directory")
    Evidence.check_path(output)
    with tempfile.TemporaryDirectory(prefix=".review-inputs-", dir=output.parent) as temporary:
        staging = Path(temporary) / "completed"
        staging.mkdir()
        files, folded, total = {}, set(), 0

        def copy(source, name, pin=None):
            nonlocal total
            relative(name)
            data = read_file(source)
            value = sha(data)
            if pin is not None:
                require(value == digest(pin), "Input changed during snapshot")
            if name in files:
                require(files[name] == value, "Conflicting input bytes")
                return name
            require(name.casefold() not in folded and len(files) < MAX_FILES, "Duplicate or excessive input files")
            total += len(data)
            require(total <= MAX_BYTES, "Review inputs exceed size limit")
            destination = staging / name
            destination.parent.mkdir(parents=True, exist_ok=True)
            with destination.open("xb") as stream:
                stream.write(data)
            files[name] = value
            folded.add(name.casefold())
            return name

        portable = {"schemaVersion": 1, "title": settings["title"], "author": settings["author"], "evidence": "evidence"}
        for field in FIELDS:
            name = "documents/" + (Path(settings[field]).name if field == "package" else field + (".pdf" if field == "template" else ".json"))
            portable[field] = copy(settings[field], name, expected.get(field))
        observations = json.loads((staging / portable["observations"]).read_bytes(), object_pairs_hook=strict_object)
        require(observations["schemaVersion"] == 1, "Unsupported observation document")
        evidence = Path(settings["evidence"])
        require(evidence.is_absolute(), "Evidence must use an absolute private directory")
        for observation in observations["observations"]:
            for item in observation["files"]:
                name = relative(item["relativePath"])
                copy(evidence / name, "evidence/" + name, item["sha256"])
        (staging / "evidence").mkdir(exist_ok=True)
        options = {"reviewMode": review_mode, "sourceCommit": source_commit, "candidateSha256": digest(candidate_digest),
                   "inventorySha256": digest(inventory_digest), "mappingSha256": digest(mapping_digest)}
        if declarations is not None:
            options["declarations"] = copy(declarations, "documents/declarations.json", declarations_sha256)
            options["declarationsSha256"] = digest(declarations_sha256)
        if android is not None:
            options["androidPins"] = copy(android_pins, "documents/android-pins.json", android_pins_sha256)
            options["androidPinsSha256"] = digest(android_pins_sha256)
            roots = {item["runId"]: Path(item["path"]) for item in settings["androidEvidence"]}
            portable["androidEvidence"] = []
            for run in android["runs"]:
                prefix = "android/" + run["runId"]
                for item in run["files"]:
                    name = relative(item["relativePath"])
                    copy(roots[run["runId"]] / name, prefix + "/" + name, item["sha256"])
                portable["androidEvidence"].append({"runId": run["runId"], "path": prefix})
        document = {"schemaVersion": 1, "settings": portable, "options": options,
                    "files": [{"relativePath": name, "sha256": value} for name, value in sorted(files.items())]}
        write_json(staging / MANIFEST, document)
        pin = sha((staging / MANIFEST).read_bytes())
        # Validate the retained snapshot, including raw Android files, without consulting original paths.
        _, frozen_settings, frozen_options = load(staging, pin)
        audit_runs(frozen_settings, frozen_options.get("androidPins"), frozen_options.get("androidPinsSha256"), candidate_digest)
        output.mkdir()
        # Publish COMPLETE last. A partial publication is retained and is never reused as complete.
        for item in staging.iterdir():
            item.rename(output / item.name)
        with (output / "COMPLETE").open("x", encoding="ascii") as marker:
            marker.write(pin + "\n")
        return {"schemaVersion": 1, "inputsSha256": pin, "fileCount": len(files), "testsExecuted": False,
                "submissionReady": False, "deliveryAttempted": False}


def load(root, expected_sha256, *, completed=False):
    root = Path(root)
    require(root.is_absolute(), "Frozen input directory must be absolute")
    data = read_file(root / MANIFEST)
    require(sha(data) == digest(expected_sha256), "Frozen input manifest changed")
    if completed:
        require(read_file(root / "COMPLETE").decode("ascii").strip() == digest(expected_sha256), "Incomplete snapshot")
    value = json.loads(data, object_pairs_hook=strict_object)
    keys(value, ("schemaVersion", "settings", "options", "files"))
    require(type(value["schemaVersion"]) is int and value["schemaVersion"] == 1, "Unsupported frozen inputs")
    require(isinstance(value["files"], list) and 0 < len(value["files"]) <= MAX_FILES, "Invalid frozen inventory")
    files, folded, total = {}, set(), 0
    for item in value["files"]:
        keys(item, ("relativePath", "sha256"))
        name = relative(item["relativePath"])
        require(name.casefold() not in folded and name not in (MANIFEST, "COMPLETE"), "Duplicate frozen path")
        folded.add(name.casefold())
        content = read_file(root / name)
        total += len(content)
        require(total <= MAX_BYTES and sha(content) == digest(item["sha256"]), "Frozen evidence changed")
        files[name] = item["sha256"]
    actual, pending, visited = set(), [root], 0
    while pending:
        for path in pending.pop().iterdir():
            visited += 1
            require(visited <= MAX_FILES * 3, "Excessive snapshot entries")
            Evidence.check_path(path)
            if path.is_dir():
                pending.append(path)
            else:
                require(path.is_file(), "Snapshot contains a non-regular entry")
                actual.add(path.relative_to(root).as_posix())
    require(actual == set(files) | {MANIFEST} | ({"COMPLETE"} if completed else set()), "Unexpected or missing snapshot files")
    settings, options = dict(value["settings"]), dict(value["options"])
    keys(settings, ("schemaVersion", *FIELDS, "evidence", "title", "author"), ("androidEvidence",))
    keys(options, ("reviewMode", "sourceCommit", "candidateSha256", "inventorySha256", "mappingSha256"),
         ("androidPins", "androidPinsSha256", "declarations", "declarationsSha256"))
    require(settings["evidence"] == "evidence", "Unexpected evidence root")

    def file_path(name):
        require(relative(name) in files, "Input reference is absent from pinned inventory")
        return str(root / name)

    for field in FIELDS:
        settings[field] = file_path(settings[field])
    settings["evidence"] = str(root / "evidence")
    for field in ("androidPins", "declarations"):
        if field in options:
            options[field] = file_path(options[field])
    if "androidEvidence" in settings:
        settings["androidEvidence"] = [{"runId": item["runId"], "path": str(root / relative(item["path"]))}
                                       for item in settings["androidEvidence"]]
    return value, settings, options


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ("settings", "output", "candidate-sha256", "inventory-sha256", "mapping-sha256", "source-commit"):
        parser.add_argument("--" + name, required=True)
    for name in ("android-pins", "android-pins-sha256", "declarations", "declarations-sha256"):
        parser.add_argument("--" + name)
    parser.add_argument("--review-mode", choices=("complete", "declared-gaps"), default="complete")
    args = parser.parse_args()
    try:
        print(json.dumps(freeze(args.settings, args.output, args.candidate_sha256, args.inventory_sha256, args.mapping_sha256,
                               args.source_commit, android_pins=args.android_pins, android_pins_sha256=args.android_pins_sha256,
                               review_mode=args.review_mode, declarations=args.declarations, declarations_sha256=args.declarations_sha256), indent=2))
        return 0
    except (ValueError, OSError, KeyError, TypeError, XMLSyntaxError):
        print("Cannot retain review inputs. Inspect private evidence; no tests or delivery were started.", file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
