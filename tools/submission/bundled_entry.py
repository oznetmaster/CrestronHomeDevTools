# Copyright (c) 2026 Neil Colvin.
# Licensed under the MIT License. See LICENSE in the repository root.
"""Internal entry point for the verified, isolated DevTools runtime."""

import importlib
import json
from pathlib import Path
import sys


def run():
    commands = json.loads(Path(__file__).with_name("commands.json").read_text(encoding="utf-8"))
    command = sys.argv[1] if len(sys.argv) > 1 else ""
    if command == "runtime-check":
        import lxml.etree
        import PIL.Image
        import pypdf
        import reportlab
        import charset_normalizer
        print(json.dumps({"ready": True, "isolated": bool(sys.flags.isolated),
                          "dependencies": {"lxml": lxml.etree.LXML_VERSION,
                                           "pillow": PIL.Image.__version__, "pypdf": pypdf.__version__,
                                           "reportlab": reportlab.Version,
                                           "charset-normalizer": charset_normalizer.__version__}}))
        return 0
    if command not in commands:
        print("Unknown submission command. Run submission --help.", file=sys.stderr)
        return 2
    sys.argv = ["CrestronHomeDevTools.Console submission " + command, *sys.argv[2:]]
    return importlib.import_module(commands[command]).main()


def main():
    try:
        return run()
    except KeyboardInterrupt:
        print("Submission command interrupted. Inspect retained outputs before resuming.", file=sys.stderr)
        return 130
    except Exception:
        # Do not expose a traceback, input credentials or arbitrary exception text.
        print("Submission command could not finish. Check the input paths and retained reports. "
              "If the inputs are valid, report the command name and DevTools version to support.", file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
