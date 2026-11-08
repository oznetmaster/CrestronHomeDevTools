# Copyright (c) 2026 Neil Colvin. MIT licensed.
import json
from pathlib import Path
import subprocess
import tempfile
import unittest
from validator_diagnostics import require_success


class ValidatorDiagnosticsTests(unittest.TestCase):
    def test_semantic_failure_retains_full_report_and_empty_stderr(self):
        with tempfile.TemporaryDirectory() as temporary:
            report = b'{"readyForReview":false,"blockingIssues":["GapUndeclared"],"private":"secret"}'
            result = subprocess.CompletedProcess([], 1, report, b"")
            with self.assertRaises(ValueError) as caught:
                require_success(result, "Assessment", Path(temporary).resolve())
            self.assertNotIn("secret", str(caught.exception))
            folder, = Path(temporary).iterdir()
            self.assertEqual((folder / "stdout.original").read_bytes(), report)
            self.assertEqual((folder / "stderr.original").read_bytes(), b"")
            receipt = json.loads((folder / "result.json").read_text())
            self.assertEqual(receipt["exitCode"], 1)
            self.assertFalse(receipt["validationPassed"])
            self.assertFalse(receipt["testsRepeated"])

    def test_parser_failure_is_retained_without_overwriting_previous_attempt(self):
        with tempfile.TemporaryDirectory() as temporary:
            result = subprocess.CompletedProcess([], 2, b"", b"private parser detail")
            for _ in range(2):
                with self.assertRaisesRegex(ValueError, "validator exit 2"):
                    require_success(result, "Assessment", Path(temporary).resolve())
            self.assertEqual(len(list(Path(temporary).iterdir())), 2)

    def test_success_creates_no_failure_artifact(self):
        with tempfile.TemporaryDirectory() as temporary:
            require_success(subprocess.CompletedProcess([], 0, b"{}", b""), "Assessment", temporary)
            self.assertEqual(list(Path(temporary).iterdir()), [])
