# Copyright (c) 2026 Neil Colvin.
# Licensed under the MIT License. See LICENSE in the repository root.
"""Audit retained Android workflow evidence without claiming official coverage."""

import argparse
from collections import Counter
from datetime import datetime, timezone
import json
from pathlib import Path, PureWindowsPath
import re
import sys
from uuid import UUID

from lxml import etree as ET

from build_help import keys, sha, strict_object
from package_help import version, write_json


def require(condition, message):
    if not condition:
        raise ValueError(message)


def digest(value):
    require(isinstance(value, str) and re.fullmatch(r"[A-Fa-f0-9]{64}", value), "Invalid SHA-256 pin")
    return value.lower()


def timestamp(value):
    require(isinstance(value, str), "Missing evidence timestamp")
    result = datetime.fromisoformat(value.replace("Z", "+00:00"))
    require(result.utcoffset() is not None, "Evidence timestamps require a timezone")
    return result


class Evidence:
    def __init__(self, root):
        self.root = Path(root).absolute()
        self.files = {}
        self.check_path(self.root)
        require(self.root.is_dir(), "Missing Android evidence directory")

    @staticmethod
    def check_path(path):
        # Reject junctions as well as symlinks, including in parent directories.
        for part in (path, *path.parents):
            require(not part.is_symlink() and not part.is_junction(), "Evidence must not traverse links or junctions")

    def read(self, relative):
        require(isinstance(relative, str) and "\\" not in relative and ":" not in relative and
                all(part not in ("", ".", "..") for part in relative.split("/")), "Invalid evidence relative path")
        path = self.root / relative
        self.check_path(path)
        require(path.is_file() and path.stat().st_size <= 32 * 1024 * 1024, "Missing or oversized evidence file")
        data = path.read_bytes()
        require(len(data) <= 32 * 1024 * 1024, "Oversized evidence file")
        self.files[relative] = sha(data)
        return data

    def json(self, relative):
        return json.loads(self.read(relative), object_pairs_hook=strict_object)


def xml(data):
    root = ET.fromstring(data, ET.XMLParser(resolve_entities=False, no_network=True, load_dtd=False))
    require(not root.getroottree().docinfo.doctype, "DTD evidence is not supported")
    return root


def discovery(data, assembly):
    root = xml(data)
    runs = root.findall(".//test-run")
    require(len(runs) == 1, "Require one NUnit discovery run")
    run = runs[0]
    require(all(node.get("runstate", "Runnable") == "Runnable" for node in run.iter()), "Discovery contains non-runnable cases")
    suites = run.findall(".//test-suite[@type='Assembly']")
    require(len(suites) == 1 and suites[0].get("name") == assembly, "Discovery assembly differs from the pinned producer")
    cases = run.findall(".//test-case")
    require(cases and int(run.get("testcasecount", "-1")) == len(cases) and
            len({case.get("id") for case in cases}) == len(cases) and
            all(case.get("id") and case.get("fullname") and case.get("runstate") == "Runnable" for case in cases),
            "Incomplete or duplicate NUnit discovery records")
    return Counter(case.get("fullname") for case in cases)


def results(data, expected, assembly):
    root = xml(data)
    namespace = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"
    require(root.tag == "{" + namespace + "}TestRun", "Unexpected TRX root")
    def children(path):
        return root.findall(path, {"t": namespace})
    summaries = children("t:ResultSummary")
    require(len(summaries) == 1 and summaries[0].get("outcome") == "Completed", "TRX run did not complete")
    definitions = children("t:TestDefinitions/t:UnitTest")
    require(definitions and all(item.get("id") for item in definitions) and
            len({item.get("id") for item in definitions}) == len(definitions), "Invalid TRX definitions")
    definitions = {item.get("id"): item for item in definitions}
    actual, executions, starts, ends = Counter(), set(), [], []
    for item in children("t:Results/t:UnitTestResult"):
        execution = item.get("executionId")
        require(execution and execution not in executions and item.get("outcome") == "Passed", "Duplicated or unsuccessful TRX execution")
        executions.add(execution)
        definition = definitions.get(item.get("testId"))
        require(definition is not None, "TRX result lacks a definition")
        method = definition.find("{" + namespace + "}TestMethod")
        require(method is not None and method.get("adapterTypeName") == "executor://nunit3testexecutor/" and
                PureWindowsPath(method.get("codeBase", "")).name == assembly and method.get("className") and definition.get("name"),
                "TRX producer or test identity differs")
        actual[method.get("className") + "." + definition.get("name")] += 1
        starts.append(timestamp(item.get("startTime")))
        ends.append(timestamp(item.get("endTime")))
        require(starts[-1] <= ends[-1], "Reversed test interval")
    require(actual == expected and actual, "TRX does not execute every discovered case exactly once")
    counters = children("t:ResultSummary/t:Counters")
    require(len(counters) == 1 and int(counters[0].get("total", "-1")) == sum(actual.values()) and
            int(counters[0].get("passed", "-1")) == sum(actual.values()) and int(counters[0].get("failed", "-1")) == 0,
            "TRX counters disagree with actual results")
    times = children("t:Times")
    require(len(times) == 1, "Missing TRX run interval")
    start, end = timestamp(times[0].get("start")), timestamp(times[0].get("finish"))
    require(start <= min(starts) <= max(ends) <= end <= datetime.now(timezone.utc), "Invalid TRX run interval")
    return start, end


def captures(evidence, context, start, end):
    count = 0
    # Assembly output may contain unrelated assets. Only immediate check folders
    # are Android capture folders; an interrupted capture must not disappear.
    for folder in sorted(evidence.root.iterdir()):
        evidence.check_path(folder)
        if not folder.is_dir() or folder.name == "assembly":
            continue
        if not any((folder / name).exists() for name in ("observation.json", "hierarchy.xml", "screen.png")):
            continue
        prefix = folder.name + "/"
        record = evidence.json(prefix + "observation.json")
        require(type(record.get("SchemaVersion")) is int and record["SchemaVersion"] == 1 and
                record.get("CheckId") == folder.name and record.get("Outcome") == "Passed", "Unfinished or invalid Android capture")
        for key in ("RunId", "PackageSha256", "SourceSha256", "ReleaseSourceCommit", "InstalledDriverId", "DriverGuid", "DriverVersion"):
            require(record.get(key) == context[key], "Android capture belongs to a different candidate or run")
        require(start <= timestamp(record.get("StartedUtc")) <= timestamp(record.get("FinishedUtc")) <= end,
                "Android capture is outside the test interval")
        require(sha(evidence.read(prefix + "hierarchy.xml")) == digest(record.get("HierarchySha256")) and
                sha(evidence.read(prefix + "screen.png")) == digest(record.get("ScreenshotSha256")), "Android capture content changed")
        count += 1
    require(count > 0, "No retained Android captures")
    return count


def producer_inventory(evidence, manifest_sha256):
    """Require an independently pinned, complete inventory, including dependencies and runtime settings."""
    data = evidence.read("producer-manifest.json")
    require(len(data) <= 1024 * 1024 and sha(data) == digest(manifest_sha256),
            "Producer manifest differs from its independent pin")
    manifest = json.loads(data, object_pairs_hook=strict_object)
    keys(manifest, ("schemaVersion", "files"))
    require(type(manifest["schemaVersion"]) is int and manifest["schemaVersion"] == 1 and
            isinstance(manifest["files"], list) and 0 < len(manifest["files"]) <= 4096,
            "Invalid producer inventory")
    pins, folded = {}, set()
    for entry in manifest["files"]:
        keys(entry, ("relativePath", "sha256"))
        path = entry["relativePath"]
        require(isinstance(path, str) and len(path) <= 1024 and "\\" not in path and ":" not in path and
                all(part not in ("", ".", "..") and not part.endswith((" ", ".")) for part in path.split("/")),
                "Invalid producer relative path")
        require(path.casefold() not in folded, "Duplicate or ambiguous producer path")
        folded.add(path.casefold())
        pins[path] = digest(entry["sha256"])
    root = evidence.root / "assembly"
    Evidence.check_path(root)
    require(root.is_dir(), "Missing retained producer directory")
    actual, pending, visited = set(), [root], 0
    while pending:
        folder = pending.pop()
        for path in folder.iterdir():
            visited += 1
            require(visited <= 8192, "Producer directory exceeds its bounded inventory")
            Evidence.check_path(path)
            if path.is_dir():
                pending.append(path)
            else:
                require(path.is_file(), "Producer contains a non-regular file")
                actual.add(path.relative_to(root).as_posix())
    require(actual == set(pins), "Producer files differ from the complete pinned inventory")
    total = 0
    for relative, expected in sorted(pins.items()):
        data = evidence.read("assembly/" + relative)
        total += len(data)
        require(total <= 512 * 1024 * 1024, "Retained producer exceeds its bounded size")
        require(sha(data) == expected, "Retained producer dependency or settings changed")
    return len(pins)


def selected_inventory(evidence, independent_sha256, context, discovered):
    data = evidence.read("selection.json")
    require(sha(data) == digest(independent_sha256), "Android selection differs from its independent pin")
    value = json.loads(data, object_pairs_hook=strict_object)
    keys(value, ("SchemaVersion", "RunId", "PackageSha256", "DiscoveredTests", "ExpectedTests", "ExcludedTests", "SettingsSha256"))
    require(type(value["SchemaVersion"]) is int and value["SchemaVersion"] == 1 and
            value["RunId"] == context["RunId"] and value["PackageSha256"] == context["PackageSha256"],
            "Selection belongs to another run or candidate")
    for field in ("DiscoveredTests", "ExpectedTests", "ExcludedTests"):
        require(isinstance(value[field], list) and len(value[field]) <= 100000 and
                all(isinstance(name, str) and name.strip() for name in value[field]), "Invalid selection inventory")
    expected, excluded = Counter(value["ExpectedTests"]), Counter(value["ExcludedTests"])
    require(Counter(value["DiscoveredTests"]) == discovered and expected and
            not (expected.keys() & excluded.keys()) and expected + excluded == discovered,
            "Selection must partition complete discovered names and preserve every duplicate case")
    require(sha(evidence.read("selection.runsettings")) == digest(value["SettingsSha256"]), "Selected execution settings changed")
    return expected, sum(excluded.values())


def audit(candidate_path, candidate_sha256, evidence_root, run_id, assembly, assembly_sha256, discovery_sha256,
          producer_manifest_sha256, selection_sha256=None):
    require(isinstance(run_id, str) and re.fullmatch(r"[a-fA-F0-9]{32}", run_id), "Supply the independently retained workflow run ID")
    require(isinstance(assembly, str) and re.fullmatch(r"[A-Za-z0-9_.-]+\.dll", assembly), "Supply a plain fixture assembly filename")
    data = Path(candidate_path).read_bytes()
    require(len(data) <= 4 * 1024 * 1024 and sha(data) == digest(candidate_sha256), "Candidate declaration differs from its trusted pin")
    candidate = json.loads(data, object_pairs_hook=strict_object)
    keys(candidate, ("schemaVersion", "identity", "packageRequirements"))
    require(type(candidate["schemaVersion"]) is int and candidate["schemaVersion"] == 1, "Unsupported candidate schema")
    identity, package = candidate["identity"], candidate["packageRequirements"]
    keys(identity, ("packageSha256", "sourceCommit", "policySha256", "templateSha256"))
    for field in ("packageSha256", "policySha256", "templateSha256"):
        digest(identity[field])
    require(isinstance(identity["sourceCommit"], str) and re.fullmatch(r"[a-fA-F0-9]{40}|[a-fA-F0-9]{64}", identity["sourceCommit"]), "Missing release source commit")
    require(version(package["driverVersion"])[3] == 0, "Debug packages cannot supply submission evidence")
    evidence = Evidence(evidence_root)
    context = evidence.json("context.json")
    require(type(context.get("SchemaVersion")) is int and context["SchemaVersion"] == 1 and context.get("RunId") == run_id and
            digest(context.get("PackageSha256")) == digest(identity["packageSha256"]) and
            context.get("ReleaseSourceCommit") == identity["sourceCommit"] and
            version(context["DriverVersion"]) == version(package["driverVersion"]) and
            UUID(context["DriverGuid"]) == UUID(package["driverId"]) and
            type(context.get("InstalledDriverId")) is int and context["InstalledDriverId"] > 0, "Android run does not identify this Release candidate")
    digest(context["SourceSha256"])
    completion = evidence.json("completion.json")
    require(type(completion.get("SchemaVersion")) is int and completion["SchemaVersion"] == 1 and
            completion.get("RunId") == run_id and completion.get("PackageSha256") == context["PackageSha256"] and
            completion.get("RestorationConfirmed") is True, "Android restoration is unconfirmed or belongs to another run")
    require(sha(evidence.read("assembly/" + assembly)) == digest(assembly_sha256), "Retained producer assembly changed")
    producer_files = producer_inventory(evidence, producer_manifest_sha256)
    pin = evidence.json("producer-pin.json")
    keys(pin, ("SchemaVersion", "RunId", "PackageSha256", "ProducerManifestSha256", "DiscoverySha256"),
         ("SelectionSha256",) if selection_sha256 is not None else ())
    require(type(pin["SchemaVersion"]) is int and pin["SchemaVersion"] == (2 if selection_sha256 is not None else 1) and
            pin["RunId"] == run_id and pin["PackageSha256"] == context["PackageSha256"] and
            digest(pin["ProducerManifestSha256"]) == digest(producer_manifest_sha256) and
            digest(pin["DiscoverySha256"]) == digest(discovery_sha256),
            "Coordinator producer receipt differs from the independent pins")
    discovered = evidence.read("discovery.dump")
    require(sha(discovered) == digest(discovery_sha256), "Discovery differs from the independently retained inventory")
    expected = discovery(discovered, assembly)
    discovered_count, excluded_count = sum(expected.values()), 0
    if selection_sha256 is not None:
        require(digest(pin.get("SelectionSha256")) == digest(selection_sha256), "Coordinator selection differs from the independent pin")
        expected, excluded_count = selected_inventory(evidence, selection_sha256, context, expected)
    coverage = evidence.json("coverage.json")
    require(coverage.get("SelectionSha256") is None if selection_sha256 is None else
            digest(coverage.get("SelectionSha256")) == digest(selection_sha256), "Coverage identifies another selection")
    outcome = coverage.get("Results", {})
    require(isinstance(outcome, dict) and all(type(outcome.get(key)) is int for key in ("Passed", "Failed", "Skipped")) and
            outcome.get("Complete") is True and outcome.get("MeetsGate") is True,
            "Workflow coverage requires typed successful results")
    require(coverage.get("RunId") == run_id and coverage.get("PackageSha256") == context["PackageSha256"] and
            coverage.get("ReleaseSourceCommit") == identity["sourceCommit"] and
            digest(coverage.get("DiscoverySha256")) == sha(discovered) and
            digest(coverage.get("ProducerManifestSha256")) == digest(producer_manifest_sha256) and
            Counter(coverage.get("ExpectedTests", [])) == expected and
            coverage.get("Results") == {"Passed": sum(expected.values()), "Failed": 0, "Skipped": 0, "Complete": True, "MeetsGate": True},
            "Workflow coverage is incomplete or inconsistent")
    paths = list(evidence.root.glob("TestResult*.trx"))
    require(len(paths) == 1, "Require exactly one workflow TRX result")
    start, end = results(evidence.read(paths[0].name), expected, assembly)
    count = captures(evidence, context, start, end)
    return {"schemaVersion": 1, "status": "AndroidEvidenceAudited", "submissionReady": False,
            "producerInventoryPinned": True, "producerFiles": producer_files,
            "producerManifestSha256": digest(producer_manifest_sha256),
            "officialRequirementsSatisfied": [], "producerAuthenticated": False,
            "candidateSha256": digest(candidate_sha256), "identity": identity, "runId": run_id,
            "producerAssembly": assembly, "producerAssemblySha256": digest(assembly_sha256),
            "selectionSha256": digest(selection_sha256) if selection_sha256 is not None else None,
            "discoveredTests": discovered_count, "excludedTests": excluded_count,
            "startedUtc": start.isoformat(), "finishedUtc": end.isoformat(),
            "executedTests": sum(expected.values()), "retainedCaptures": count,
            "files": [{"relativePath": path, "sha256": value} for path, value in sorted(evidence.files.items())]}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for option in ("candidate", "candidate-sha256", "evidence", "run-id", "assembly", "assembly-sha256", "discovery-sha256", "producer-manifest-sha256", "output"):
        parser.add_argument("--" + option, required=True)
    parser.add_argument("--selection-sha256")
    args = parser.parse_args()
    try:
        report = audit(args.candidate, args.candidate_sha256, args.evidence, args.run_id, args.assembly, args.assembly_sha256,
                       args.discovery_sha256, args.producer_manifest_sha256, args.selection_sha256)
        write_json(args.output, report)
        print("Android evidence audited; official coverage and submission approval remain separate.")
        return 0
    except (ValueError, KeyError, TypeError, OSError, ET.XMLSyntaxError):
        # Evidence paths, XML and JSON can contain household details. Keep the
        # public job output generic; detailed inspection belongs on the worker.
        print("Android evidence audit failed; no passing report was written.", file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
