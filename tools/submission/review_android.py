# Copyright (c) 2026 Neil Colvin.
# Licensed under the MIT License. See LICENSE in the repository root.
"""Re-audit every independently selected Android run before preparing a review."""

from pathlib import Path
import re

from audit_android import audit, digest, require, Evidence, timestamp
from datetime import datetime, timezone
from package_help import read_json
from build_help import keys, sha
from self_test_form import pinned_json


def audit_runs(settings, pins_path, pins_sha256, candidate_sha256, *, review_mode="complete"):
    require(review_mode in ("complete", "declared-gaps"), "Unsupported Android review mode")
    locations = settings.get("androidEvidence")
    if locations is None and pins_path is None and pins_sha256 is None:
        return None
    require(isinstance(pins_path, (str, Path)) and Path(pins_path).is_absolute(),
            "Android review requires an independent absolute pins path")
    digest(pins_sha256)
    _, pins = pinned_json(pins_path, pins_sha256)
    keys(pins, ("schemaVersion", "candidateSha256", "runs"), ("observationBindings",))
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
        keys(run, ("runId", "assembly", "assemblySha256", "discoverySha256", "producerManifestSha256"), ("selectionSha256", "interruptedFiles", "historicalReview"))
        if "interruptedFiles" in run:
            require(review_mode == "declared-gaps" and isinstance(run["interruptedFiles"], list),
                    "Interrupted evidence requires explicit declared-gaps mode and a complete file inventory")
        if "selectionSha256" in run:
            digest(run["selectionSha256"])
        run_id = run["runId"]
        require(isinstance(run_id, str) and re.fullmatch(r"[a-f0-9]{32}", run_id) and run_id not in runs,
                "Android pins require distinct workflow run IDs")
        runs[run_id] = run
    require(roots.keys() == runs.keys(), "Android evidence run inventory differs from independent pins")
    reports, review_files = [], {}
    for run_id, run in sorted(runs.items()):
        historical = run.get("historicalReview")
        if historical is not None:
            require(review_mode == "declared-gaps" and "interruptedFiles" not in run,
                    "Historical evidence is only available for complete-run declared interpretations")
            review_files.update(validate_historical_review(historical, settings["evidence"]))
        report = audit(settings["candidate"], candidate_sha256, roots[run_id], run_id,
                             run["assembly"], run["assemblySha256"], run["discoverySha256"],
                             run["producerManifestSha256"], run.get("selectionSha256"),
                             interrupted_files=run.get("interruptedFiles"),
                             historical_identity=historical["identity"] if historical is not None else None)
        if historical is not None:
            report["sourceReview"] = historical["sourceReview"]
        reports.append(report)
    bindings = pins.get("observationBindings", [])
    require(isinstance(bindings, list) and len(bindings) <= 4096, "Invalid observation bindings")
    return {"schemaVersion": 1, "pinsSha256": pins_sha256, "runs": reports,
            "observationBindings": bindings,
            "reviewFiles": [{"relativePath": name, "sha256": value} for name, value in sorted(review_files.items())],
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


def pinned_evidence_files(entries, evidence):
    require(isinstance(entries, list) and 0 < len(entries) <= 4096, "Require bounded review evidence files")
    values = {}
    for item in entries:
        keys(item, ("relativePath", "sha256"))
        name = item["relativePath"]
        require(isinstance(name, str) and name not in values and name.casefold() not in {v.casefold() for v in values}, "Duplicate review evidence")
        value = digest(item["sha256"])
        require(sha(evidence.read(name)) == value, "Reviewed evidence changed")
        values[name] = value
    return values


def validate_historical_review(historical, evidence_root):
    keys(historical, ("identity", "sourceReview"))
    review = historical["sourceReview"]
    keys(review, ("reviewer", "reviewedUtc", "rationale", "sourceCommit", "package", "files"))
    require(all(isinstance(review[k], str) and review[k].strip() for k in ("reviewer", "rationale")), "Named source review required")
    require(timestamp(review["reviewedUtc"]) <= datetime.now(timezone.utc), "Source review is in the future")
    require(isinstance(review["sourceCommit"], str) and re.fullmatch(r"[a-f0-9]{40}|[a-f0-9]{64}", review["sourceCommit"]),
            "Supply the separately reviewed source commit")
    raw_commit = historical["identity"]["releaseSourceCommit"]
    require(raw_commit is None or raw_commit == review["sourceCommit"], "Source review contradicts original commit")
    evidence = Evidence(evidence_root)
    files = pinned_evidence_files(review["files"], evidence)
    package = review["package"]
    keys(package, ("relativePath", "sha256"))
    require(files.get(package["relativePath"]) == digest(package["sha256"]) == digest(historical["identity"]["packageSha256"]),
            "Source review must retain the exact historical package bytes")
    require(len(files) >= 2, "Source review requires independent attestation beyond package bytes")
    return files


def validate_observation_bindings(android, android_scopes, observations, declarations, evidence_root):
    """An interrupted run cannot support a claim; complete historical runs support named interpretations only."""
    reports = {run["runId"]: run for run in android["runs"]}
    restricted = any(run["status"] in ("InterruptedAndroidEvidencePreserved", "HistoricalAndroidEvidenceAudited")
                     for run in reports.values())
    entries = android.get("observationBindings", [])
    observed = {item["requirementId"]: item for item in observations["observations"]}
    declared = {item["requirementId"]: item for item in declarations.get("declarations", [])}
    bindings, used_historical = {}, set()
    evidence = Evidence(evidence_root)
    for entry in entries:
        keys(entry, ("requirementId", "runId", "files"))
        scope = entry["requirementId"]
        require(scope in android_scopes and scope not in bindings and scope in observed, "Unknown or duplicate Android scope binding")
        run = reports.get(entry["runId"])
        require(run is not None and run["status"] in ("AndroidEvidenceAudited", "HistoricalAndroidEvidenceAudited"),
                "Observation requires an independently audited complete producer")
        files = pinned_evidence_files(entry["files"], evidence)
        obs_files = {f["relativePath"]: digest(f["sha256"]) for f in observed[scope]["files"]}
        require(all(obs_files.get(k) == v for k, v in files.items()), "Binding evidence must belong to this observation")
        hashes = set(files.values())
        raw = {item["relativePath"]: digest(item["sha256"]) for item in run["files"]}
        core = {"context.json", "coverage.json", "completion.json"} | {n for n in raw if n.startswith("TestResult") and n.endswith(".trx")}
        require(len(core) == 4 and all(raw[n] in hashes for n in core), "Binding must retain original context, coverage, completion and TRX")
        folders = [n[:-len("observation.json")] for n in raw if n.endswith("/observation.json")]
        require(any(all(raw.get(folder + n) in hashes for n in ("observation.json", "hierarchy.xml", "screen.png")) for folder in folders),
                "Binding requires an audited capture triple")
        if run["status"] == "HistoricalAndroidEvidenceAudited":
            review = declared.get(scope, {}).get("interpretationReview")
            require(observed[scope]["outcome"] in ("Partial", "Inconclusive") and isinstance(review, dict),
                    "Historical runs require named interpretations without a new passing observation")
            review_refs = {f["relativePath"]: digest(f["sha256"]) for f in review.get("evidence", [])}
            source_refs = {f["relativePath"]: digest(f["sha256"]) for f in run["sourceReview"]["files"]}
            require(all(review_refs.get(k) == v for k, v in files.items()) and
                    all(files.get(k) == v for k, v in source_refs.items()),
                    "Interpretation must retain its exact producer and independent source-review evidence")
            used_historical.add(run["runId"])
        bindings[scope] = entry
    require(all(r["runId"] in used_historical for r in reports.values() if r["status"] == "HistoricalAndroidEvidenceAudited"),
            "Every historical run requires an explicit scoped interpretation")
    if restricted:
        for scope in android_scopes:
            observation = observed.get(scope, {})
            if scope in bindings:
                continue
            require(observation.get("outcome") not in ("Passed", "ReviewedPriorPass"),
                    "Interrupted Android preservation cannot support a Passed Android observation")
            if observation.get("outcome") == "NotApplicable":
                continue
            require(scope in declared and "interpretationReview" not in declared[scope],
                    "Interrupted Android scopes require explicit gaps without accepted interpretations")
    return bindings
