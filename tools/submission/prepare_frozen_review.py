# Copyright (c) 2026 Neil Colvin. MIT licensed.
"""Prepare a review using only a pinned portable input snapshot and installed tools."""
import argparse
import json
from pathlib import Path
import subprocess
import sys
import tempfile

from lxml.etree import XMLSyntaxError
from pypdf.errors import PdfReadError
from reportlab.platypus.doctemplate import LayoutError
from audit_android import Evidence, require
from freeze_review_inputs import load
from package_help import write_json
from prepare_review import _prepare


def prepare_frozen(inputs, inputs_sha256, output, *, signing_copy=False, validator_settings=None):
    root, output = Path(inputs), Path(output)
    require(root.is_absolute() and output.is_absolute(), "Use absolute private paths")
    Evidence.check_path(root)
    Evidence.check_path(output)
    root, output = root.resolve(), output.resolve()
    require(not output.is_relative_to(root) and
            not output.exists() and output.parent.is_dir(), "Use a new private output outside the frozen inputs")
    Evidence.check_path(output)
    _, settings, options = load(root, inputs_sha256, completed=True)
    settings["output"] = str(output)
    if validator_settings:
        require(set(validator_settings) <= {"dotnet", "validator"}, "Unexpected runtime settings")
        settings.update(validator_settings)
    with tempfile.TemporaryDirectory(prefix=".frozen-review-", dir=output.parent) as temporary:
        path = Path(temporary) / "settings.json"
        write_json(path, settings)
        receipt = _prepare(path, options["candidateSha256"], options["inventorySha256"], options["mappingSha256"],
                          options["sourceCommit"], "driver", signing_copy=signing_copy,
                          android_pins=options.get("androidPins"), android_pins_sha256=options.get("androidPinsSha256"),
                          review_mode=options["reviewMode"], declarations=options.get("declarations"),
                          declarations_sha256=options.get("declarationsSha256"))
        try:
            load(root, inputs_sha256, completed=True)
        except (ValueError, OSError, KeyError, TypeError):
            # Preserve the generated artifacts but revoke this invocation's completion marker.
            (output / "COMPLETE").rename(output / "INVALIDATED-inputs-changed")
            raise
        return receipt


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ("inputs", "inputs-sha256", "output"):
        parser.add_argument("--" + name, required=True)
    parser.add_argument("--prepare-for-signing", action="store_true")
    args = parser.parse_args()
    try:
        print(json.dumps(prepare_frozen(args.inputs, args.inputs_sha256, args.output, signing_copy=args.prepare_for_signing), indent=2))
        return 0
    except (ValueError, OSError, KeyError, TypeError, PdfReadError, LayoutError, XMLSyntaxError, subprocess.SubprocessError):
        print("Frozen review preparation failed. Inspect retained inputs; no delivery was attempted.", file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
