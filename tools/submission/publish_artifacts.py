# Copyright (c) 2026 Neil Colvin. MIT licensed.
"""Publish verified staged bytes without renaming directories held by Windows scanners."""
import hashlib
from pathlib import Path
import re


def publish(staging, output, completion_digest):
    staging, output = Path(staging), Path(output)
    if not re.fullmatch(r"[0-9a-f]{64}", completion_digest):
        raise ValueError("Invalid completion digest")
    if not staging.is_dir() or output.exists() or not output.parent.is_dir():
        raise ValueError("Require staged artifacts and a fresh output")
    if staging == output or staging in output.parents or output in staging.parents:
        raise ValueError("Staging and output must be separate")
    for root in (staging, output.parent):
        for path in (root, *root.parents):
            if path.is_symlink() or getattr(path.lstat(), "st_file_attributes", 0) & 0x400:
                raise ValueError("Artifact publication cannot traverse links")
    files=[]; directories=[]
    for path in sorted(staging.rglob("*")):
        if path.is_symlink() or getattr(path.lstat(), "st_file_attributes", 0) & 0x400:
            raise ValueError("Staged artifacts cannot contain links")
        if path.is_dir(): directories.append(path.relative_to(staging))
        elif path.is_file(): files.append(path)
        else: raise ValueError("Staged artifact is not a regular file")
    if (staging/'COMPLETE').exists(): raise ValueError("Completion is published last")
    output.mkdir()
    for relative in directories: (output/relative).mkdir(parents=True,exist_ok=True)
    for source in files:
        destination=output/source.relative_to(staging)
        digest=hashlib.sha256()
        with source.open('rb') as incoming, destination.open('xb') as outgoing:
            while block:=incoming.read(1024*1024):
                digest.update(block);outgoing.write(block)
        with source.open('rb') as stream: after=hashlib.file_digest(stream,'sha256').hexdigest()
        with destination.open('rb') as stream: copied=hashlib.file_digest(stream,'sha256').hexdigest()
        if digest.hexdigest()!=after or copied!=after: raise ValueError("Artifacts changed while publishing")
    # Any interrupted copy stays incomplete. Never reuse/overwrite it or replay producer work.
    with (output/'COMPLETE').open('x',encoding='ascii') as marker: marker.write(completion_digest+'\n')
