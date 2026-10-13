# Copyright (c) 2026 Neil Colvin. MIT licensed.
"""Preserve a failed validator's original output in the caller's private directory."""
import hashlib
import json
from pathlib import Path
import tempfile


def require_success(result, purpose, private_parent=None):
    if result.returncode == 0:
        return
    # Do not print arbitrary validator output: it can contain private evidence.
    # Keep semantic exit-1 reports as well as exit-2 parser/IO diagnostics.
    detail = f"{purpose} failed (validator exit {result.returncode})"
    if private_parent is not None:
        parent = Path(private_parent)
        if not parent.is_absolute() or not parent.is_dir():
            raise ValueError(detail + "; diagnostic parent must be an existing private directory")
        directory = Path(tempfile.mkdtemp(prefix="validator-failure-", dir=parent))
        files = []
        for name, data in (("stdout.original", result.stdout), ("stderr.original", result.stderr)):
            data = data if isinstance(data, bytes) else (data or "").encode("utf-8")
            with (directory / name).open("xb") as stream:
                stream.write(data)
            files.append({"relativePath": name, "length": len(data),
                          "sha256": hashlib.sha256(data).hexdigest()})
        with (directory / "result.json").open("x", encoding="utf-8", newline="\n") as stream:
            json.dump({"schemaVersion": 1, "exitCode": result.returncode,
                       "files": files, "validationPassed": False,
                       "testsRepeated": False, "deliveryAttempted": False}, stream, indent=2)
        detail += "; original output retained in private " + directory.name
    raise ValueError(detail + "; inspect the original report before changing inputs")
