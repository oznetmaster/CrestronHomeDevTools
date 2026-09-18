# Copyright (c) 2026 Neil Colvin.
# Licensed under the MIT License. See LICENSE in the repository root.
"""Maintainer-only acceptance harness, launched by TestSubmissionConsole.ps1."""

from pathlib import Path
import sys
import unittest


def main():
    # Explicit test source is used only by the maintainer harness, never the public CLI.
    source = Path(__file__).resolve().parent
    sys.path.insert(0, str(source))
    suite = unittest.defaultTestLoader.discover(str(source / "bundle_tests"), pattern="test_*.py")
    expected = suite.countTestCases()
    if not expected:
        return 1
    result = unittest.TextTestRunner(verbosity=2).run(suite)
    return 0 if result.wasSuccessful() and result.testsRun == expected and not result.skipped else 1


if __name__ == "__main__":
    sys.exit(main())
