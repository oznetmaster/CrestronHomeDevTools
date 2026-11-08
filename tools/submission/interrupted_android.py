# Copyright (c) 2026 Neil Colvin. MIT licensed.
"""Preserve an interrupted run's pinned bytes, without certifying its results."""
from datetime import datetime, timezone
import re

from audit_android import digest, require, timestamp
from build_help import keys, sha

MAX_FILES = 8192
MAX_BYTES = 512 * 1024 * 1024


def relative(value):
    require(isinstance(value, str) and len(value) <= 1024 and ":" not in value and "\\" not in value and
            all(p not in ("", ".", "..") and not p.endswith((" ", ".")) and
                not re.search(r'[<>"|?*\x00-\x1f]', p) and
                p.split(".")[0].upper() not in {"CON", "PRN", "AUX", "NUL", *[f"COM{i}" for i in range(10)], *[f"LPT{i}" for i in range(10)]}
                for p in value.split("/")), "Invalid interrupted evidence relative path")
    return value


def file_inventory(evidence):
    actual, pending, visited = set(), [evidence.root], 0
    while pending:
        for path in pending.pop().iterdir():
            visited += 1
            require(visited <= MAX_FILES * 3, "Interrupted evidence exceeds bounded inventory")
            evidence.check_path(path)
            if path.is_dir():
                pending.append(path)
            else:
                require(path.is_file(), "Interrupted evidence contains a non-regular file")
                actual.add(relative(path.relative_to(evidence.root).as_posix()))
                require(len(actual) <= MAX_FILES, "Too many interrupted evidence files")
    return actual


def capture_records(evidence, context):
    count = 0
    now = datetime.now(timezone.utc)
    for folder in sorted(evidence.root.iterdir()):
        if not folder.is_dir() or folder.name == "assembly":
            continue
        if not any((folder / name).exists() for name in ("observation.json", "hierarchy.xml", "screen.png")):
            continue
        prefix = folder.name + "/"
        record = evidence.json(prefix + "observation.json")
        require(type(record.get("SchemaVersion")) is int and record["SchemaVersion"] == 1 and
                record.get("CheckId") == folder.name and record.get("Outcome") in ("Passed", "Failed"),
                "Invalid retained Android capture")
        for key in ("RunId", "PackageSha256", "SourceSha256", "ReleaseSourceCommit", "InstalledDriverId", "DriverGuid", "DriverVersion"):
            require(record.get(key) == context[key], "Android capture belongs to a different candidate or run")
        require(timestamp(record.get("StartedUtc")) <= timestamp(record.get("FinishedUtc")) <= now,
                "Invalid retained capture interval")
        require(sha(evidence.read(prefix + "hierarchy.xml")) == digest(record.get("HierarchySha256")) and
                sha(evidence.read(prefix + "screen.png")) == digest(record.get("ScreenshotSha256")),
                "Android capture content changed")
        count += 1
    return count


def preserve(evidence, files, context, candidate_sha256, identity, assembly, assembly_sha256,
             producer_manifest_sha256, producer_files, selection_sha256, discovered_count, excluded_count):
    """Only runs interrupted before terminal records are supported by this route.

    The coordinator reviews and pins every original file. Unknown supplemental
    records are retained as opaque bytes, never promoted into verified results.
    Candidate, producer, discovery and selection were validated by audit().
    """
    require(isinstance(files, list) and 0 < len(files) <= MAX_FILES, "Missing interrupted evidence inventory")
    pins, folded = {}, set()
    for item in files:
        keys(item, ("relativePath", "sha256"))
        name = relative(item["relativePath"])
        require(name.casefold() not in folded, "Duplicate or ambiguous interrupted evidence path")
        pins[name] = digest(item["sha256"])
        folded.add(name.casefold())
    actual = file_inventory(evidence)
    require(actual == set(pins), "Interrupted files differ from independently pinned complete inventory")
    require(not any(name.casefold() in ("completion.json", "coverage.json") or
                    ("/" not in name and name.casefold().endswith(".trx")) for name in actual),
            "Interrupted preservation requires absent terminal records; cannot waive failed or corrupt terminal evidence")
    total = 0
    for name, expected in sorted(pins.items()):
        data = evidence.read(name)
        total += len(data)
        require(total <= MAX_BYTES and sha(data) == expected, "Interrupted evidence changed or exceeds size limit")
    count = capture_records(evidence, context)
    require(file_inventory(evidence) == actual, "Interrupted inventory changed during audit")
    return {"schemaVersion": 1, "status": "InterruptedAndroidEvidencePreserved", "submissionReady": False,
            "producerInventoryPinned": True, "producerFiles": producer_files,
            "producerManifestSha256": digest(producer_manifest_sha256),
            "officialRequirementsSatisfied": [], "producerAuthenticated": False,
            "candidateSha256": digest(candidate_sha256), "identity": identity, "runId": context["RunId"],
            "producerAssembly": assembly, "producerAssemblySha256": digest(assembly_sha256),
            "selectionSha256": digest(selection_sha256) if selection_sha256 is not None else None,
            "discoveredTests": discovered_count, "excludedTests": excluded_count,
            "executedTests": None, "retainedCaptureRecords": count,
            "restorationConfirmed": None, "completePassingRun": False,
            "missingTerminalRecords": ["completion.json", "coverage.json", "TRX"],
            "supplementalContentVerified": False,
            "files": [{"relativePath": name, "sha256": value} for name, value in sorted(pins.items())]}
