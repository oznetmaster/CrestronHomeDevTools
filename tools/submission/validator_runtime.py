# Copyright (c) 2026 Neil Colvin.
# Licensed under the MIT License. See LICENSE in the repository root.
"""Resolve the packaged validator without changing independently pinned input files."""

import json
import os
from pathlib import Path


def validator_command(dotnet=None, validator=None):
    bundled = os.environ.get("CRESTRON_DEVTOOLS_BUNDLED_VALIDATOR")
    if bundled:
        if dotnet is not None or validator is not None:
            raise ValueError("Packaged commands use their bundled validator; omit dotnet and validator paths")
        args = json.loads(bundled)
        if not isinstance(args, list) or len(args) not in (1, 2) or any(
                not isinstance(path, str) or not Path(path).is_absolute() or not Path(path).is_file() for path in args):
            raise ValueError("The packaged validator is unavailable; extract a complete DevTools console download")
        return args
    if not dotnet or not validator or any(not Path(p).is_absolute() or not Path(p).is_file() for p in (dotnet, validator)):
        raise ValueError("Use the packaged DevTools submission command, or supply absolute maintainer validator paths")
    return [str(dotnet), str(validator)]


def settings_validator(settings):
    return validator_command(settings.get("dotnet"), settings.get("validator"))
