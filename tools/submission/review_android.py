# Copyright (c) 2026 Neil Colvin.
# Licensed under the MIT License. See LICENSE in the repository root.
"""Re-audit every independently selected Android run before preparing a review."""

from pathlib import Path
import re

from audit_android import audit, digest, require
from build_help import keys, sha
from self_test_form import pinned_json


def audit_runs(settings, pins_path, pins_sha256, candidate_sha256):
    locations = settings.get("androidEvidence")
    if locations is None and pins_path is None and pins_sha256 is None:
        return None
    require(isinstance(pins_path, (str, Path)) and Path(pins_path).is_absolute(),
            "Android review requires an independent absolute pins path")
    digest(pins_sha256)
    _, pins = pinned_json(pins_path, pins_sha256)
    keys(pins, ("schemaVersion", "candidateSha256", "runs"))
    require(type(pins["schemaVersion"]) is int and pins["schemaVersion"] == 1 and
            pins["candidateSha256"] == candidate_sha256, "Android pins identify another candidate")
    require(isinstance(pins["runs"], list) and 0 < len(pins["runs"]) <= 128,
            "Android review requires a bounded nonempty run inventory")
    require(isinstance(locations, list) and len(locations) == len(pins["runs"]),
            "Android evidence must include every independently pinned run")
    roots = {}
    for location in locations:
        keys(location, ("runId", "path"))
        require(isinstance(location["runId"], str) and location["runId"] not in roots and
                isinstance(location["path"], str) and Path(location["path"]).is_absolute(),
                "Android evidence needs distinct run IDs and absolute private paths")
        roots[location["runId"]] = location["path"]
    runs = {}
    for run in pins["runs"]:
        keys(run, ("runId", "assembly", "assemblySha256", "discoverySha256", "producerManifestSha256"), ("selectionSha256",))
        if "selectionSha256" in run:
            digest(run["selectionSha256"])
        run_id = run["runId"]
        require(isinstance(run_id, str) and re.fullmatch(r"[a-f0-9]{32}", run_id) and run_id not in runs,
                "Android pins require distinct workflow run IDs")
        runs[run_id] = run
    require(roots.keys() == runs.keys(), "Android evidence run inventory differs from independent pins")
    reports = []
    for run_id, run in sorted(runs.items()):
        reports.append(audit(settings["candidate"], candidate_sha256, roots[run_id], run_id,
                             run["assembly"], run["assemblySha256"], run["discoverySha256"],
                             run["producerManifestSha256"], run.get("selectionSha256")))
    return {"schemaVersion": 1, "pinsSha256": pins_sha256, "runs": reports,
            "producerAuthenticated": False, "officialRequirementsSatisfied": [], "submissionReady": False}


def retained_files(root, receipt):
    """Carry the exact review-time audit forward privately, never as attachments."""
    fields = ("androidAuditSha256", "androidPinsSha256")
    if not any(field in receipt for field in fields):
        return {}
    require(all(field in receipt for field in fields), "Incomplete Android audit receipt")
    result = {}
    for name, field, limit in (("android-audit.json", fields[0], 64 * 1024 * 1024),
                               ("android-pins.json", fields[1], 4 * 1024 * 1024)):
        path = Path(root) / name
        require(path.stat().st_size <= limit, "Retained Android audit exceeds its size limit")
        data = path.read_bytes()
        require(len(data) <= limit and sha(data) == digest(receipt[field]), "Retained Android audit changed")
        result[name] = data
    return result
