# Copyright (c) 2026 Neil Colvin.
# Licensed under the MIT License. See LICENSE in the repository root.
"""Offline template contracts. No runner, credentials, processor or provider access."""
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest

import yaml


ROOT = Path(__file__).resolve().parents[2]
TEMPLATES = ROOT / "docs" / "submission"
STAGES = {
    "review": "submission-review",
    "sign": "submission-signing",
    "delivery-plan": "submission-delivery-preparation",
    "deliver": "submission-delivery",
}


def load(name):
    # BaseLoader preserves GitHub's YAML 'on' key instead of YAML 1.1 boolean coercion.
    return yaml.load((TEMPLATES / (name + ".yml.example")).read_text(encoding="utf-8"), Loader=yaml.BaseLoader)


class SubmissionTemplateTests(unittest.TestCase):
    def test_disabled_manual_entrypoint_and_no_parent_lock(self):
        entry = load("submission")
        self.assertEqual(["workflow_dispatch"], list(entry["on"]))
        self.assertEqual("false", entry["on"]["workflow_dispatch"]["inputs"]["enabled"]["default"])
        self.assertNotIn("concurrency", entry)
        self.assertEqual(list(STAGES), entry["on"]["workflow_dispatch"]["inputs"]["stage"]["options"])

    def test_caller_callee_inputs_and_stage_selection_match(self):
        entry = load("submission")
        for stage, name in STAGES.items():
            with self.subTest(stage=stage):
                call = entry["jobs"][stage]
                self.assertEqual("validate-request", call["needs"])
                self.assertEqual("${{ inputs.stage == '" + stage + "' }}", call["if"])
                self.assertEqual("./.github/workflows/" + name + ".yml", call["uses"])
                contract = load(name)["on"]["workflow_call"]
                self.assertTrue(set(call["with"]).issubset(contract["inputs"]))
                for key, value in contract["inputs"].items():
                    if value.get("required") == "true":
                        self.assertIn(key, call["with"])
                self.assertTrue(set(call.get("secrets", {})).issubset(contract.get("secrets", {})))
                self.assertNotEqual("inherit", call.get("secrets"))

    def test_every_entry_checks_explicit_repository_branch_and_enablement(self):
        jobs = [load("submission")["jobs"]["validate-request"]]
        jobs.extend(next(iter(load(name)["jobs"].values())) for name in STAGES.values())
        for job in jobs:
            with self.subTest(job=job.get("environment", "dispatch")):
                guard = job["if"]
                for required in (
                    "inputs.enabled", "vars.CRESTRON_SUBMISSION_STAGES_ENABLED == 'true'",
                    "vars.CRESTRON_SUBMISSION_REPOSITORY != ''",
                    "github.repository == vars.CRESTRON_SUBMISSION_REPOSITORY",
                    "vars.CRESTRON_SUBMISSION_BRANCH != ''",
                    "github.ref == format('refs/heads/{0}', vars.CRESTRON_SUBMISSION_BRANCH)",
                    "github.event_name == 'workflow_dispatch'",
                ):
                    self.assertIn(required, guard)
                self.assertNotIn("CrestronHomeHardwareCI", guard)

    def test_stages_share_protected_lock_and_worker_but_not_parent(self):
        for name in STAGES.values():
            with self.subTest(name=name):
                stage = load(name)
                self.assertEqual({"group": "crestron-submission-protected-stages", "queue": "max", "cancel-in-progress": "false"}, stage["concurrency"])
                job = next(iter(stage["jobs"].values()))
                self.assertEqual(["self-hosted", "Windows", "${{ vars.CRESTRON_SUBMISSION_RUNNER_LABEL }}"], job["runs-on"])
                self.assertIn("vars.CRESTRON_SUBMISSION_RUNNER_LABEL != ''", job["if"])
                self.assertTrue(job["environment"].startswith("crestron-submission-"))

    def test_delivery_credentials_reach_only_final_protected_stage(self):
        for name in STAGES.values():
            with self.subTest(name=name):
                template = load(name)
                self.assertEqual({"contents": "read"}, template["permissions"])
                if name != "submission-delivery":
                    self.assertNotIn("secrets", template["on"]["workflow_call"])
                    self.assertNotIn("secrets.", (TEMPLATES / (name + ".yml.example")).read_text())
                for job in template["jobs"].values():
                    for step in job["steps"]:
                        self.assertNotIn("${{", step.get("run", ""), "Pass inputs through environment, not shell interpolation")
                        self.assertNotIn("upload-artifact", step.get("uses", ""), "Private evidence must remain private")

    def run_request(self, stage, overrides=None):
        step = load("submission")["jobs"]["validate-request"]["steps"][0]
        env = {key: value for key, value in os.environ.items() if not key.startswith("REQUEST_")}
        env.update({key: "" for key in step["env"]})
        env["REQUEST_STAGE"] = stage
        if stage == "review":
            env.update(REQUEST_SOURCE="a" * 40, REQUEST_CANDIDATE="b" * 64, REQUEST_INVENTORY="c" * 64, REQUEST_MAPPING="d" * 64)
        elif stage in ("sign", "delivery-plan"):
            env.update(REQUEST_REVIEW="e" * 64, REQUEST_AUTHORIZATION="f" * 64)
        env.update(overrides or {})
        shell = shutil.which("pwsh")
        self.assertIsNotNone(shell, "PowerShell is required to exercise the actual dispatch validation")
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "validate.ps1"
            path.write_text(step["run"], encoding="utf-8")
            env["GITHUB_STEP_SUMMARY"] = str(Path(directory) / "summary.txt")
            return subprocess.run([shell, "-NoProfile", "-NonInteractive", "-File", str(path)], env=env, capture_output=True, text=True, timeout=20)

    def test_each_valid_stage_reaches_syntax_confirmation(self):
        for stage in STAGES:
            with self.subTest(stage=stage):
                result = self.run_request(stage)
                self.assertEqual(0, result.returncode, result.stderr)
        self.assertEqual(0, self.run_request("review", {"REQUEST_ANDROID": "a" * 64}).returncode)

    def test_missing_required_pin_refused_before_private_stage(self):
        for stage, keys in {
            "review": ["REQUEST_SOURCE", "REQUEST_CANDIDATE", "REQUEST_INVENTORY", "REQUEST_MAPPING"],
            "sign": ["REQUEST_REVIEW", "REQUEST_AUTHORIZATION"],
            "delivery-plan": ["REQUEST_REVIEW", "REQUEST_AUTHORIZATION"],
        }.items():
            for key in keys:
                with self.subTest(stage=stage, missing=key):
                    self.assertNotEqual(0, self.run_request(stage, {key: ""}).returncode)

    def test_bad_pin_and_unknown_stage_refused(self):
        for pin in ["a" * 63, "a" * 65, "A" * 64, "z" * 64, "a" * 64 + "\n", "$(throw 'not executed')"]:
            with self.subTest(pin=pin):
                self.assertNotEqual(0, self.run_request("review", {"REQUEST_ANDROID": pin}).returncode)
        self.assertNotEqual(0, self.run_request("review", {"REQUEST_SOURCE": "a" * 64}).returncode)
        self.assertNotEqual(0, self.run_request("unknown").returncode)


if __name__ == "__main__":
    unittest.main(verbosity=2)
