# Retain and verify a private submission evidence bundle

The `submission-bundle-create` and `submission-bundle-check` commands, introduced in 1.5.0, retain a validated snapshot for later submission stages. Both run offline without processor credentials, an emulator or an email account.

The bundle is private working evidence. It may contain unredacted screenshots, device names or other private information from the referenced files. Do not attach it to a public GitHub release or send it to Crestron as the final submission package. The final delivery package and its disclosure review remain separate work.

## Build and retain a bundle

First produce the [candidate declaration, approved policy and observations](EvidenceCli.md). The trusted release job must retain the candidate declaration's SHA-256 independently of the evidence worker. The source commit and package hash must identify the exact tested Release artifact; a successful Debug workflow is insufficient. The NUnit source supports a [prebuilt Release handoff](https://github.com/oznetmaster/CrestronHomeNUnit/blob/main/docs/ReleaseCandidateTesting.md), whose Release hardware validation is still pending.

Use an existing private output directory with access restricted to the intended build/signing accounts. Keep it outside the source checkout. The commands create randomly named temporary children there and remove them after a completed or handled failed operation. They do not configure filesystem permissions for you.

```powershell
.\CrestronHomeDevTools.Console.exe submission-bundle-create `
  --output C:/CI/Private/Submission/candidate-evidence.zip `
  --candidate C:/CI/Private/Submission/candidate.json `
  --candidate-sha256 TRUSTED_CANDIDATE_SHA256 `
  --package C:/CI/Private/Release/NeilColvin_Platform_Example_IP.pkg `
  --policy C:/CI/Private/Submission/policy.json `
  --template C:/CI/Private/Submission/Extension-Test-Plan.pdf `
  --observations C:/CI/Private/Submission/observations.json `
  --evidence C:/CI/Private/Submission/evidence
if ($LASTEXITCODE -ne 0) { throw 'Submission bundle creation failed.' }
```

The example paths and digest placeholder must be replaced with private configuration and a trusted pin. Use the complete released Windows console archive described in [Console tools](ConsoleTools.md); building from source is optional. Run these PowerShell examples from the extracted console directory. The command does not build, deploy or test the driver; it validates the supplied evidence and package structure.

Creation copies the four input documents, the package under its original filename, and only files referenced by observations. It never recursively copies the evidence directory. It rejects escaping paths and links within that tree. Sharing one evidence file across observations stores one copy, but each observation still undergoes policy, identity and digest validation.

The archive layout is:

```text
candidate.json
policy.json
template.pdf
observations.json
package/<original-package-filename>.pkg
evidence/<referenced-relative-paths>
```

The original template's bytes are retained under the standard archive name `template.pdf`; the candidate's template digest remains authoritative. No source paths, generated pass report or credentials are automatically added. Referenced files themselves can contain private data.

Creation validates the archive's own extracted copies with `SubmissionValidation.CheckFiles`, then moves the completed ZIP to the requested output without overwriting an existing file. Validation failure or cancellation does not publish a completed output. A process crash can leave a randomly named `.submission-*` staging directory, which must not be treated as a successful bundle. Inspect and remove that abandoned private directory only after confirming its process has stopped.

On success, stdout is a JSON `SubmissionBundleReport`: `BundleSha256`, `FileCount`, the full `Validation` report and `ValidationChecksPassed`. Retain the archive digest in the trusted job's protected records, separately from the archive. The digest is a content identity, not a signature or build attestation. Use private artifact storage with retention/access controls for the ZIP.

## Check before consuming evidence

At each later boundary, use both the candidate pin from trusted release CI and the bundle pin recorded by the trusted bundling job:

```powershell
.\CrestronHomeDevTools.Console.exe submission-bundle-check `
  --bundle C:/CI/Private/Submission/candidate-evidence.zip `
  --bundle-sha256 TRUSTED_BUNDLE_SHA256 `
  --candidate-sha256 TRUSTED_CANDIDATE_SHA256 `
  --scratch C:/CI/Private/Submission/Scratch
if ($LASTEXITCODE -ne 0) { throw 'Submission bundle verification failed.' }
```

The scratch parent must already exist and be private. Do not calculate the expected bundle digest from the downloaded file at this step: that would remove the independent check for replacement. Repacking an otherwise identical ZIP changes its digest and requires an explicitly established new bundle identity.

Checking holds the archive open for reading, compares its bytes to the expected digest, extracts bounded regular files into a fresh private directory, and repeats package/evidence validation. Unknown top-level files, unreferenced evidence, missing files, duplicate or case-colliding names, path traversal, links and Windows device names are rejected. Limits are 4,096 files, 16 MiB per JSON declaration, 64 MiB per other file and 512 MiB total expanded content. The compressed archive is also bounded. This is an evidence snapshot format, not a general ZIP extractor.

Exit 0 means all structural checks passed. Exit 1 means validation or file-operation failure; 2 means invalid arguments or JSON; 130 means cancellation. A false report or absent report never passes a CI gate. Creation refuses to produce the destination when archived validation fails; use `submission-evidence-check` first for detailed diagnostics of original input defects.

## Remaining trust and submission work

The check detects changes relative to independently retained digests. Filesystem owners can still replace a stored ZIP; it is not physically immutable storage. The workflow must protect its pins and storage and revalidate the exact snapshot consumed by signing/delivery, without extracting or modifying a separate unchecked copy afterward.

These two bundle commands do not authenticate a producer, approve a requirement policy, map every official form subcondition, verify a real Release installation, render/sign the completed form or send a submission. A fabricated observation with internally consistent digests can pass structural validation. Trusted execution, approved policy completeness and real measured evidence remain mandatory. A passing bundle must not be presented as Crestron certification or used to waive missing UI/device tests. See the [full submission plan](../CrestronSubmission.md).
