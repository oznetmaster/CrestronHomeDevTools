# Copyright (c) 2026 Neil Colvin.
# Licensed under the MIT License. See LICENSE in the repository root.
"""Normalize freshly built package paths before candidate hashing or testing."""

import argparse
import copy
import hashlib
import io
import json
import os
from pathlib import Path
import stat
import sys
import tempfile
from zipfile import BadZipFile, ZipFile


MAX_BYTES = 256 * 1024 * 1024


def digest(data):
    return hashlib.sha256(data).hexdigest()


def normalize(package):
    package = Path(package)
    if package.is_symlink() or package.suffix.lower() != '.pkg':
        raise ValueError('Expected a regular build-output .pkg file')
    with package.open('rb') as source:
        original = source.read(MAX_BYTES + 1)
    if len(original) > MAX_BYTES:
        raise ValueError('Package exceeds the normalization size limit')
    changes = []
    entries = []
    with ZipFile(io.BytesIO(original)) as archive:
        infos = archive.infolist()
        if not infos or len(infos) > 4096 or sum(i.file_size for i in infos) > MAX_BYTES:
            raise ValueError('Package has invalid entry count or expanded size')
        names = {}
        for info in infos:
            # ZipInfo normalizes native separators when parsed on Windows;
            # orig_filename retains the actual central-directory spelling.
            name = info.orig_filename.replace('\\', '/')
            parts = name.rstrip('/').split('/')
            if (not name or name.startswith('/') or ':' in name or '\x00' in info.orig_filename or
                    any(p in ('', '.', '..') for p in parts) or
                    stat.S_ISLNK(info.external_attr >> 16) or info.flag_bits & 1):
                raise ValueError('Package contains unsafe or unsupported archive paths')
            key = name.rstrip('/').casefold()
            if key in names:
                raise ValueError('Package paths collide after normalization')
            names[key] = name.endswith('/')
            if name != info.orig_filename:
                changes.append({'from': info.orig_filename, 'to': name})
            fixed = copy.copy(info)
            fixed.filename = name
            fixed.orig_filename = name
            entries.append((fixed, archive.read(info)))
        for name in names:
            parts = name.split('/')
            if any('/'.join(parts[:n]) in names and not names['/'.join(parts[:n])]
                   for n in range(1, len(parts))):
                raise ValueError('Package file conflicts with a directory path')
        comment = archive.comment

    result = original
    if changes:
        output = io.BytesIO()
        with ZipFile(output, 'w') as rewritten:
            rewritten.comment = comment
            for info, payload in entries:
                rewritten.writestr(info, payload)
        result = output.getvalue()
        with ZipFile(io.BytesIO(result)) as check:
            if check.namelist() != [i.filename for i, _ in entries]:
                raise ValueError('Normalized archive inventory differs')
            for info, payload in entries:
                if check.read(info.filename) != payload:
                    raise ValueError('Normalized archive changed payload bytes')
        # Publish only a completely written, independently reread archive. A failed
        # replacement leaves the original package in place; no in-place ZIP edits.
        temporary = None
        try:
            with tempfile.NamedTemporaryFile(dir=package.parent, prefix=package.name + '.', suffix='.tmp', delete=False) as target:
                temporary = Path(target.name)
                target.write(result)
                target.flush()
                os.fsync(target.fileno())
            if temporary.read_bytes() != result or package.read_bytes() != original:
                raise ValueError('Package changed during normalization')
            os.replace(temporary, package)
        finally:
            if temporary is not None and temporary.exists():
                pending_error = sys.exc_info()[0] is not None
                try:
                    temporary.unlink()
                except OSError:
                    if not pending_error:
                        raise
    return {'schemaVersion': 1, 'packageFileName': package.name,
            'inputSha256': digest(original), 'packageSha256': digest(result),
            'renamedEntries': changes, 'payloadBytesPreserved': True}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--package', required=True)
    parser.add_argument('--report', required=True)
    args = parser.parse_args()
    try:
        # Refuse a stale receipt before changing any build output.
        with Path(args.report).open('x', encoding='utf-8') as report:
            result = normalize(args.package)
            json.dump(result, report, indent=2)
        print('Submission package paths checked; payload bytes preserved.')
        return 0
    except (OSError, ValueError, BadZipFile, RuntimeError) as error:
        print(f'Package path normalization failed: {error}', file=sys.stderr)
        return 1


if __name__ == '__main__':
    sys.exit(main())
