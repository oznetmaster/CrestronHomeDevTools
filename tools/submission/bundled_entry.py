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



def _runtime_diagnostics():
    if len(sys.argv) < 2 or sys.argv[1] != "runtime-check":
        return None
    if len(sys.argv) == 2:
        return None
    if len(sys.argv) != 4 or sys.argv[2] != "--private-diagnostics-directory":
        raise ValueError("Invalid runtime-check diagnostic arguments")
    parent = Path(sys.argv[3])
    if not parent.is_absolute() or not parent.is_dir() or parent.is_symlink():
        raise ValueError("Diagnostic directory must be existing, absolute and ordinary")
    _require_ordinary_ancestors(parent)
    return parent


def _require_ordinary_ancestors(parent):
    import stat
    # Inspect the original lexical path, not resolve(), which would hide a junction.
    for node in (parent, *parent.parents):
        info = node.lstat()
        if stat.S_ISLNK(info.st_mode) or (getattr(info, "st_file_attributes", 0)
                & getattr(stat, "FILE_ATTRIBUTE_REPARSE_POINT", 0)):
            raise ValueError("Diagnostic path cannot traverse a link or reparse point")


def _retain_runtime_failure(parent, error):
    # Fixed runtime-check has no private credential or signature arguments.
    import hashlib
    import tempfile
    import traceback
    _require_ordinary_ancestors(parent)
    directory = Path(tempfile.mkdtemp(prefix="runtime-failure-", dir=parent))
    _require_ordinary_ancestors(directory)
    data = "".join(traceback.format_exception(type(error), error, error.__traceback__)).encode("utf-8")
    with (directory / "exception.original.txt").open("xb") as stream:
        stream.write(data)
    receipt = {"schemaVersion": 1, "command": "runtime-check",
               "exceptionType": type(error).__name__, "validationPassed": False,
               "trace": {"relativePath": "exception.original.txt", "length": len(data),
                         "sha256": hashlib.sha256(data).hexdigest()}}
    with (directory / "result.json").open("x", encoding="utf-8") as stream:
        json.dump(receipt, stream, indent=2)
    return directory.name

def main():
    diagnostics = None
    try:
        diagnostics = _runtime_diagnostics()
        return run()
    except KeyboardInterrupt:
        print("Submission command interrupted. Inspect retained outputs before resuming.", file=sys.stderr)
        return 130
    except Exception as error:
        if diagnostics is not None:
            try:
                name = _retain_runtime_failure(diagnostics, error)
                print("Private runtime failure retained in " + name, file=sys.stderr)
            except Exception:
                print("Private runtime failure retention failed; original command still failed.", file=sys.stderr)
        # Do not expose a traceback, input credentials or arbitrary exception text.
        print("Submission command could not finish. Check the input paths and retained reports. "
              "If the inputs are valid, report the command name and DevTools version to support.", file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
