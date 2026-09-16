# Reviewed dependency notices in a submission package

The source tool `tools/submission/dependency_notices.py` binds license and notice text to the DLLs actually passed to a driver's merge command. It generates a root `THIRD-PARTY-NOTICES.txt` and checks the same bytes after packaging. It does not discover licensing obligations automatically or replace review of the upstream licenses.

Review the actual driver license and every merged dependency's license and required notices. Match the DLLs byte-for-byte to the restored package assets. Use notice files from those packages where appropriate; a repository-wide notice may cover unrelated components and is not an automatic substitute.

## Driver inputs

Keep these public source files in the driver repository:

- A `dependency-notices.json` inventory with `schemaVersion: 1`, `driverNoticeIds`, `documents` and `components`.
- The reviewed license/NOTICE texts referenced by the inventory. Preserve their bytes with Git attributes; do not add your own copyright header to third-party license text.

Each document has a unique `id`, a relative `path` within the inventory directory, its exact `sha256`, and a public `source` reference. Each component has its DLL `assembly` name and `sha256`, `packageId`, `packageVersion`, declared `license`, original `copyright` metadata, and nonempty `noticeIds`. Driver license references are listed separately. The inventory rejects missing references, duplicates and unreferenced documents.

Package IDs, versions and licensing metadata are reviewed declarations. The tool verifies the actual DLL hashes; it does not query NuGet or decide whether a legal declaration is correct. For a dependency update, review the corresponding package, upstream commit, license and shipped notices before updating its pins. Recalculating hashes alone is insufficient.

Set `SubmissionDependencyNotices` to the inventory path in the driver's opt-in submission property group. The shared `CrestronSubmissionHelp.targets` stages the notices after copying ordinary assets and verifies them after archive-path normalization. Each consuming project must explicitly configure it; an unset property does not establish that their notice obligations have been met.

The build must retain `$(TargetDir)merge_inputs.txt`, containing the exact absolute DLL paths passed to its merge command, including `$(TargetPath)` once. The driver DLL is excluded from third-party binary matching because its own license is listed separately. All other inputs must exist and match the inventory exactly. This catches a merge script silently ignoring a missing dependency. Explicit build paths may use a workspace junction; they are resolved to compare the same physical driver path. Relative notice paths cannot escape their reviewed source directory or traverse links.

## Standalone use

```text
python tools/submission/dependency_notices.py stage --manifest DRIVER/submission/dependency-notices.json --merge-inputs BUILD/merge_inputs.txt --driver-assembly BUILD/Driver.dll --include-directory BUILD/patched/IncludeInPkg --receipt PRIVATE/notices-receipt.json
python tools/submission/dependency_notices.py verify --manifest DRIVER/submission/dependency-notices.json --merge-inputs BUILD/merge_inputs.txt --driver-assembly BUILD/Driver.dll --receipt PRIVATE/notices-receipt.json --package BUILD/Driver.pkg --report PRIVATE/packaged-notices.json
```

Use absolute build paths and new receipt/report files under an existing private parent. Existing staged notices are not overwritten. Stage/verify exit nonzero on failure. Keep the receipt and verification report with the candidate build artifacts; they record hashes and assembly names, not machine paths. Only the generated notice text belongs in the package.

Verification rereads the inventory, source notices and dependency DLLs and compares them with the staging receipt. It requires one correctly named root notice entry whose bytes match exactly, and records the package hash. This is the notice check only; the normal package preflight, help verification and candidate testing remain required.

## Verified and remaining

Offline tests exercise changed/missing/additional DLLs, notice drift, duplicate inputs, stale receipts, source paths, workspace aliases, output preservation and missing/altered packaged text. A real MSBuild test verifies staging/packaging order and refuses a compiler-produced package with changed notices, using clearly synthetic driver/package tools.

Staging was also exercised against an actual development merge inventory. Final inclusion by ManifestUtil still requires verification against the exact Release package; successful staging alone does not establish it.
