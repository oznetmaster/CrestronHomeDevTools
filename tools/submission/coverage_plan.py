# Copyright (c) 2026 Neil Colvin.
# Licensed under the MIT License. See LICENSE in the repository root.
"""Expand a source-bound coverage blueprint into draft policy, form mapping and producer tasks."""

import argparse
import json
from pathlib import Path
import re
import sys

from lxml import etree as ET

from build_help import keys, sha, text
from package_help import read_json, write_json


def pinned(path, digest):
    data, value = read_json(path)
    if not re.fullmatch(r"[a-f0-9]{64}", digest) or sha(data) != digest:
        raise ValueError("Coverage input differs from its reviewed digest")
    return value


def ui_targets(data, surface):
    root = ET.fromstring(data, ET.XMLParser(resolve_entities=False, no_network=True, load_dtd=False))
    if root.getroottree().docinfo.doctype or root.tag != "uidefinition":
        raise ValueError("Use a plain supported UI definition without a DTD")
    result = {surface + "/tile"}
    if len(root.findall("tile")) != 1:
        raise ValueError("Expected one tile per UI definition")
    for layout in root.findall("layouts/layout"):
        prefix = surface + "/" + text(layout.get("id"))
        names = [surface + "/layout/" + text(layout.get("id"))]
        group = 0
        for node in layout.findall("controls//*"):
            identifier = node.get("id")
            if identifier:
                names.append(prefix + "/" + identifier)
            elif node.tag == "buttongroup":
                group += 1
                names.append(prefix + "/buttongroup-" + str(group))
            elif node.tag != "controlgroup":
                raise ValueError("Unidentified UI element needs an explicit inventory rule")
        if len(names) != len(set(names)) or result.intersection(names):
            raise ValueError("Duplicate UI target")
        result.update(names)
    return result


def compile_plan(plan_path, plan_sha256, inventory_path, inventory_sha256, source_root):
    plan = pinned(plan_path, plan_sha256)
    inventory = pinned(inventory_path, inventory_sha256)
    keys(plan, ("schemaVersion", "status", "inventorySha256", "sourceHashMode", "driver", "sources", "requirements"))
    if type(plan["schemaVersion"]) is not int or plan["schemaVersion"] != 1 or plan["status"] != "draft" or plan["inventorySha256"] != inventory_sha256 or plan["sourceHashMode"] != "line-endings-lf":
        raise ValueError("Require a draft blueprint bound to this official inventory")
    text(plan["driver"])
    root = Path(source_root).resolve(strict=True)
    targets, sources, paths = set(), [], set()
    if not isinstance(plan["sources"], list) or not plan["sources"]:
        raise ValueError("Declare the reviewed UI and behavior source files")
    for source in plan["sources"]:
        keys(source, ("path", "sha256", "surface"))
        relative = text(source["path"])
        if "\\" in relative or ":" in relative or any(part in ("", ".", "..") for part in relative.split("/")):
            raise ValueError("Source paths must stay relative to the driver checkout")
        path = (root / relative).resolve(strict=True)
        if not path.is_relative_to(root) or relative.casefold() in paths:
            raise ValueError("Duplicate or escaping source path")
        paths.add(relative.casefold())
        data = path.read_bytes()
        if sha(data.replace(b"\r\n", b"\n")) != source["sha256"]:
            raise ValueError("Driver source changed; review its coverage before generating a new plan")
        if source["surface"] is not None:
            surface = text(source["surface"])
            if not re.fullmatch(r"[a-z][a-z0-9-]*", surface):
                raise ValueError("Invalid UI surface name")
            found = ui_targets(data, surface)
            if targets.intersection(found):
                raise ValueError("UI surfaces must be unique")
            targets.update(found)
        sources.append(source)
    if not targets:
        raise ValueError("At least one UI source is required")
    if type(inventory["schemaVersion"]) is not int or inventory["schemaVersion"] != 1 or not inventory["requirements"]:
        raise ValueError("Unsupported or empty official inventory")
    expected = {item["id"]: item for item in inventory["requirements"]}
    if len(expected) != len(inventory["requirements"]) or any(type(item["minimumObservationSeconds"]) is not int or item["minimumObservationSeconds"] < 0 for item in expected.values()):
        raise ValueError("Official inventory IDs/durations are ambiguous")
    rows = plan["requirements"]
    if not isinstance(rows, list) or len(rows) != len(expected) or {row["id"] for row in rows} != expected.keys():
        raise ValueError("Blueprint must cover every official item exactly once")
    rules, mapping, tasks, seen, covered = [], [], [], set(), set()
    for row in rows:
        keys(row, ("id", "checks"))
        if not isinstance(row["checks"], list) or not row["checks"]:
            raise ValueError("Every official item requires explicit scoped checks")
        observation_ids, floor = [], 0
        for check in row["checks"]:
            keys(check, ("id", "targets", "method", "expectation", "minimumSeconds", "responseLimitSeconds", "restore"))
            if not re.fullmatch(r"[a-z][a-z0-9-]*", check["id"]):
                raise ValueError("Checks need stable lowercase identifiers")
            if check["method"] not in ("android", "configuration", "combined", "absence", "outage", "endurance"):
                raise ValueError("Unknown observation method")
            text(check["expectation"])
            minimum, limit = check["minimumSeconds"], check["responseLimitSeconds"]
            if type(minimum) is not int or not 0 <= minimum <= 604800 or (limit is not None and (type(limit) is not int or not 0 < limit <= 86400)) or type(check["restore"]) is not bool:
                raise ValueError("Invalid observation timing/restoration contract")
            scopes = check["targets"]
            if not isinstance(scopes, list) or not scopes or len(scopes) != len(set(scopes)):
                raise ValueError("Checks require distinct explicit targets")
            floor = max(floor, minimum)
            for scope in scopes:
                text(scope)
                if scope not in targets and not re.fullmatch(r"\$[a-z][a-z0-9.-]*", scope):
                    raise ValueError("Check references an unknown UI or context target")
                if scope in targets:
                    covered.add(scope)
                identifier = row["id"] + "." + check["id"] + "." + scope.replace("$", "context.").replace("/", ".")
                if identifier in seen:
                    raise ValueError("Duplicate expanded observation ID")
                seen.add(identifier)
                observation_ids.append(identifier)
                days, seconds = divmod(minimum, 86400)
                hours, seconds = divmod(seconds, 3600)
                minutes, seconds = divmod(seconds, 60)
                duration = (str(days) + "." if days else "") + f"{hours:02}:{minutes:02}:{seconds:02}"
                rules.append({"id": identifier, "minimumDuration": duration, "allowNotApplicable": check["method"] == "absence",
                              "execution": {"target": scope, "method": check["method"],
                                            "requiredOutcome": "NotApplicable" if check["method"] == "absence" else "Passed",
                                            "responseLimitSeconds": limit, "restore": check["restore"],
                                            "maximumSampleGapSeconds": None}})
                tasks.append({"observationId": identifier, "officialItem": row["id"], "target": scope,
                              "method": check["method"], "expectation": check["expectation"], "minimumSeconds": minimum,
                              "responseLimitSeconds": limit, "restore": check["restore"], "producer": None,
                              "requiredOutcome": "NotApplicable" if check["method"] == "absence" else "Passed"})
        if floor < expected[row["id"]]["minimumObservationSeconds"]:
            raise ValueError("Blueprint does not enforce the official minimum duration")
        mapping.append({"id": row["id"], "observationIds": observation_ids})
    if covered != targets:
        raise ValueError("Blueprint leaves UI targets without any coverage: " + ", ".join(sorted(targets - covered)))
    policy = {"schemaVersion": 1, "requirements": rules}
    policy_bytes = (json.dumps(policy, indent=2) + "\n").encode("utf-8")
    form_mapping = {"schemaVersion": 1, "inventorySha256": inventory_sha256, "policySha256": sha(policy_bytes), "requirements": mapping}
    contract = {"schemaVersion": 1, "status": "draft", "driver": plan["driver"], "planSha256": plan_sha256,
                "inventorySha256": inventory_sha256, "policySha256": sha(policy_bytes), "sourceHashMode": plan["sourceHashMode"], "sourceFiles": sources,
                "uiTargets": sorted(targets), "producerBindingRequired": True, "policyApprovalRequired": True,
                "submissionReady": False, "tasks": tasks}
    return policy, form_mapping, contract


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for field in ("plan", "plan-sha256", "inventory", "inventory-sha256", "source-root", "output"):
        parser.add_argument("--" + field, required=True)
    args = parser.parse_args()
    try:
        values = compile_plan(args.plan, args.plan_sha256, args.inventory, args.inventory_sha256, args.source_root)
        output = Path(args.output)
        output.mkdir(parents=False, exist_ok=False)
        for name, value in zip(("policy.json", "form-mapping.json", "execution-contract.json"), values):
            write_json(output / name, value)
        print(json.dumps({"status": "draft", "tasks": len(values[2]["tasks"]), "submissionReady": False}))
        return 0
    except (ValueError, KeyError, TypeError, OSError, ET.XMLSyntaxError) as error:
        print("Coverage generation failed: " + str(error), file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
