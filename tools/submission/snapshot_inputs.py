# Copyright (c) 2026 Neil Colvin. MIT licensed.
"""Copy explicitly pinned source/tool inputs byte-for-byte to a fresh immutable handoff."""
import argparse
import json
from pathlib import Path
import sys
from audit_android import Evidence, digest, require
from build_help import keys, sha
from package_help import read_json, write_json
from freeze_review_inputs import relative


def snapshot(manifest_path, manifest_digest, output):
    data, manifest = read_json(manifest_path)
    require(sha(data) == digest(manifest_digest), "Manifest differs from independent pin")
    keys(manifest, ("schemaVersion", "files"))
    require(type(manifest["schemaVersion"]) is int and manifest["schemaVersion"] == 1, "Unsupported input inventory")
    require(isinstance(manifest["files"], list) and 0 < len(manifest["files"]) <= 16384, "Require bounded explicit input files")
    output=Path(output)
    require(output.is_absolute() and not output.exists() and output.parent.is_dir(), "Use a new absolute private snapshot")
    Evidence.check_path(output)
    prepared=[]; names=set(); total=0
    for item in manifest["files"]:
        keys(item, ("source", "relativePath", "sha256"))
        name=relative(item["relativePath"]); source=Path(item["source"])
        require(name.casefold() not in names and name.casefold() not in ("complete", "inputs.json"), "Duplicate or reserved snapshot path")
        names.add(name.casefold())
        require(source.is_absolute() and source.is_file(), "Missing absolute source file")
        Evidence.check_path(source)
        require(len(str(output/name)) < 240, "Choose a shorter snapshot root for portable tool imports")
        require(source.stat().st_size <= 256*1024*1024, "Source exceeds file limit")
        content=source.read_bytes(); total+=len(content)
        require(total <= 1024*1024*1024, "Snapshot exceeds total limit")
        require(sha(content)==digest(item["sha256"]), "Source changed; preserve old pin and locate its exact retained preimage")
        prepared.append((name, content))
    # No script/template replacements, newline conversion, recursive reference guessing or repinning.
    output.mkdir()
    files=[]
    for name, content in prepared:
        destination=output/name; destination.parent.mkdir(parents=True,exist_ok=True)
        with destination.open("xb") as stream: stream.write(content)
        files.append({"relativePath":name,"sha256":sha(content),"length":len(content)})
    result={"schemaVersion":1,"sourceManifestSha256":manifest_digest,"files":files,
            "testsExecuted":False,"authorizationGranted":False}
    write_json(output/'inputs.json',result)
    (output/'COMPLETE').write_text(sha((output/'inputs.json').read_bytes())+'\n',encoding='ascii')
    return result


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    for field in ('manifest','manifest-sha256','output'): parser.add_argument('--'+field,required=True)
    args=parser.parse_args()
    try:
        print(json.dumps(snapshot(args.manifest,args.manifest_sha256,args.output),indent=2));return 0
    except (ValueError,OSError,KeyError,TypeError):
        print('Input snapshot refused; inspect the pinned inventory and choose a short fresh root. No tests ran.',file=sys.stderr);return 1


if __name__=='__main__': sys.exit(main())
