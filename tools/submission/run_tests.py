# Copyright (c) 2026 Neil Colvin.
# Licensed under the MIT License. See LICENSE in the repository root.
"""Run every discovered offline help test and reject empty or skipped runs."""

from pathlib import Path
import sys
import unittest


def main():
    suite = unittest.defaultTestLoader.discover(str(Path(__file__).parent), pattern="test_*.py")
    expected = suite.countTestCases()
    if expected == 0:
        print("No submission help tests were discovered.", file=sys.stderr)
        return 1
    result = unittest.TextTestRunner(verbosity=2).run(suite)
    return 0 if result.wasSuccessful() and result.testsRun == expected and not result.skipped else 1


if __name__ == "__main__":
    sys.exit(main())
