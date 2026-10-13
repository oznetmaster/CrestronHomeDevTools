# Reliable submission execution

This guide is part of the normal workflow. It addresses failures found during a completed production submission; it does not add Crestron acceptance requirements. The new helper commands and APIs described here require a build containing them. A previously installed console does not gain them when documentation changes.

## Resolve coverage before asking for a signature

Read the exact official wording and build one table containing every official row and each applicable mode. Keep the official wording unchanged. Record the original producer, candidate, time, tested scope, result, raw evidence references and any separate interpretation. Do this before preparing an approval copy, not after a user notices unchecked boxes.

Review all retained relevant evidence first: completed tests, original historical captures, cleanup receipts, native log archives, actual human observations and explicitly reviewed source. A current error log may not contain yesterday's removal event. Check the original dated archives and timezone; keep unrelated errors and coverage limits visible. Use actual delivered comparator originals when checking consistency with another submission, never a later rehearsal. A precedent can expose inconsistent interpretation but cannot prove that this driver was tested.

Alternatives remain alternatives: room **or** end-user UI does not require both; a driver with no room view does not acquire one as a submission prerequisite. Removal evidence can relate to the actual removed test instance under its original identity; do not demand deletion of the retained production instance without an official requirement. A generic processor power test is not a driver-specific power test. A processor's network disconnection is not proof that its power was removed; PoE unplugging may do both. Keep separately performed interruptions separate; never describe them as simultaneous.

Selected-mode absence is not global N/A when optional modes expose the control. A human report of an actual historical interaction is evidence with human provenance; source review alone is not a new live observation. A button tap without an attributable effect is not measured backend completion. Check release-build observability before planning log assertions: debug-only logs may not exist in the deployed package.

Use the supported historical, documentary, human-observation and archived-interruption bindings where applicable. Keep old failures/Partial outcomes and candidate identities. Reassess change impact when the candidate changes; do not discard a completed endurance run automatically, relabel it as a current-candidate run, or impose an unrequested repeat. Distinguish a software/workflow gap from a disclosed submission-evidence gap. The developer chooses any qualified submission route.

Before presenting the form, reconcile table totals, scope-level assessment, canonical fields, widget appearances and outgoing qualifications. A corrected interpretation, verified evidence, regenerated form, applied signature and confirmed delivery are distinct states. Report the state actually reached. In particular, do not say an item is fixed while the reviewed form still leaves it unchecked.

## Freeze inputs once and compare changes

Use `submission freeze-review-inputs` for the typed review/evidence handoff. Use `submission snapshot-inputs --manifest FILE --manifest-sha256 PIN --output NEW_SHORT_DIRECTORY` for separately pinned source/tool files. Its strict schema is:

```json
{"schemaVersion":1,"files":[{"source":"C:/Private/source.cs","relativePath":"fixture/source.cs","sha256":"INDEPENDENT_64_HEX_PIN"}]}
```

The snapshot copies bytes, including CRLF and binary data, without path or version substitution. It refuses stale pins; locate the original preimage instead of rehashing changed historical files. `COMPLETE` is written last. Publication byte-copies and verifies staged files into a fresh directory instead of renaming open directories on Windows. Interrupted output remains incomplete and cannot be reused. Keep private sources private. Keep the import/build root short; do not nest each new revision inside the previous run's long directory. Do not recursively interpret arbitrary strings such as producer-relative `assembly/file.dll` as evidence-root references.

Use structured JSON serializers and argument arrays. Never construct scripts by chained string replacement of Windows paths, quoted JSON, regexes or hashes. A raw Python string cannot end in a single backslash; PowerShell `$args` and `$HOME` are reserved conventions. Write one deliberate UTF-8 encoding/newline form, then hash and embed the bytes actually written. Copy pinned files in binary mode. Do not retell a digest from memory or parse a filename as JSON.

Validate the complete input contract offline before reserving hardware: required/unknown keys, list-versus-object shape, unique scope bindings, identity and policy hashes, paths relative to their declared root, fixture assembly name, source-control identity, dependency assembly uniqueness and release instrumentation. Use one current binding per scope; preserve older bindings as explicitly historical evidence, not duplicate current bindings. Discovery/build must not change the source identity: retain `.gitignore` and keep build output separate.

Pin one immutable manifest and verify it at stage boundaries. Review a coherent change delta rather than repeatedly constructing recursively growing inventories of prior inventories. Cached validation is not authority to skip the public stage's fresh integrity checks.

## Preserve useful failures

Review/form and signing validation retain failed child stdout, stderr and actual exit in a unique private `validator-failure-*` directory beside the requested output. An exit-1 JSON report is often the diagnosis, not an absent result. Inspect its blocking issues before changing input. Schema failures identify the JSON category and position without printing input content. Do not replace these artifacts with a manually transcribed success/failure label.

Keep launcher status, child exit, public result, test outcome, restoration, cleanup, reservation release and provider outcome distinct. Empty collections must be written as `[]`, not piped through PowerShell where zero objects produce no file. Capture native exit immediately after the native command; `$LASTEXITCODE` is not the result of a PowerShell function. Suppress incidental task return objects (`$null = ...GetResult()`) and use a dedicated JSON receipt file. A local parser failure does not authorize replay of a successful remote operation. Preserve primary failure separately from retention or cleanup failure. `WorkflowEvidence.ProcessAsync` bounds child termination and output-pipe draining, retains partial output and writes a `.process.json` receipt; inherited compiler-server pipes cannot prolong cleanup indefinitely. An incomplete capture is a failure, never an invented zero exit.

For apparent PDF clipping, inspect the original at native resolution, crop the suspect region, compare another renderer and check glyph positions. A preview omission is not proof of a PDF defect. Verify the help PDF actually embedded in the package, including clickable URI annotations, before claiming a support link is missing or rebuilding a candidate.

## Launch and recover without replay

Compare Windows identity by the approved SID. Account-name casing is display metadata. Resolve named credentials on the actual worker host; local store locations are not remote bindings. Use `SubmissionDeveloperMetadata.From(profile)` for a whitelist projection; never serialize credential-bearing profile objects for inspection. Do not provision a different service account to bypass a missing credential binding.

After a successful update, retain the immediate native/readiness observation before assertions. Use `DriverReadinessWait.WaitAsync` for bounded read-only convergence, retaining each observation, then verify exact configuration and installed payload. An update result and later Ready result are separate facts. Do not repeat the update or write configuration because the first startup snapshot says NotConfigured/Offline. Unexpected identity, authentication failure and unclassified exceptions stop the wait; no controls are replayed. The wait does not change official recovery timing.

Create fresh scoped processor clients after long human waits and processor network interruptions; do not reuse an expired connection in cleanup. Preserve the original failure even when independent restoration succeeds. For Android recovery, save the last failing hierarchy and use the actual current page for bounded cleanup; a wrong pending guard must not prevent an independent supported restoration route. Use returned `.controls` capture paths from the public inspector, not guessed filenames.

A transport timeout must include preflight, active work, evidence collection, postchecks, independent cleanup and a margin. Use `WorkflowTransportBudget` from the NUnit workflow package to calculate and validate this envelope. Never extend the actual test criterion just to fit the wrapper. A prepared human readiness request with no expiry must not be enclosed by a finite SSH/job wall-clock timeout; use the explicit indefinite transport mode while preserving active-work cancellation and cleanup deadlines. Ordinary commands remain finite. A detached remote child is not proved alive by successful launch; retain actual PID, start time, owner SID and final child receipt. Do not kill unrelated processes.

Before a corrected physical attempt, diagnose the original, validate the correction offline, review exact target/topology and require fresh Ready plus separate precise human-only action prompts. Never answer those prompts automatically. Keep test instrumentation separate from continuous production-driver polling; permissions for one do not imply restrictions or permission for the other.

## Monitoring and authorization

A monitor performs one bounded read of actual process/task existence and fresh original results. It does not run submission stages. A failed worker is not progressing because an old status file says Running. If the same operator is idle after a completed short stage, wake that operator to inspect and continue authorized work. Do not create another operator. Hourly cadence applies to autonomous endurance; active short stages need prompt completion review (five minutes for the established monitor). Stay quiet while healthy/unchanged or while an already-presented human prompt remains pending.

A live process awaiting its presented human prompt still needs completion monitoring. Pause only for verified delivery or a genuinely unresolved human decision with no live process able to progress. Provider/model capacity failure is an unavailable operator, not successful monitoring; report it, retain identity and resume the same operator when available. No takeover or blind test restart.

After a stop, the reviewer records original evidence, cause certainty, concrete correction, maintained source/docs, validation, owner, commit/publication/live status and an explicit resume instruction. Once the validated correction is within existing human scope, resume it without a redundant generic approval. Technical review does not impersonate human permission; changed targets, scope, physical actions and signing/sending still use their applicable exact approval.

Use an existing approved account and host for unattended observation. Place later collectors/observers on the designated worker so routine checks do not launch visible desktop windows. Present actual Ready/action UI visibly in the interactive session, while noninteractive helpers use no-window launch. Do not redesign execution hosts mid-run. Host OS reboot/recovery rehearsal is optional infrastructure work, never an added driver-submission gate.

## Finish once

After exact human signing approval, use `submission prepare-signing-plan` with the pinned unsigned review receipt, exact existing authorization, a fresh preparation directory, a separate fresh signed-output directory and the named credential binding. It derives `signing-plan.json` argument values and `signing-settings.json` from the checked receipt; it cannot grant approval and never reads a signature. Invoke the returned argument array through the public console, using its protected `--credentials` mechanism. Review receipt/authorization pins are checked before loading a named signature, and the signing stage still revalidates the complete form/evidence and expiry.

Review signed-page appearance and unchanged checklist content. Check the current uploader form/terms through the existing supported route and approve the exact correspondence and endpoints. Preserve one durable delivery journal. An uncertain send is reconciled before retry; never change the journal to escape ambiguity. Confirm verified upload and SMTP acceptance separately. Then freeze originals and stop the completed attempt's monitor. Delivery is not inbox confirmation, acceptance, publication or certification.

## Before using this on another submission

Build and run the affected offline suites with a normal writable, non-redirected NUnit working directory. Exercise synthetic review, signing and simulated delivery including failure retention, changed-byte refusal, expired authorization, uncertain-provider reconciliation and duplicate-send prevention. The runtime build includes every local module imported by the public command graph, checked by the module-inventory regression. Run `tools/TestSubmissionConsole.ps1` against the complete built console, automation and setup directories. Verify the selected console/NUnit packages actually contain the fixes and their hashes; source changes alone do not update a frozen worker. Carry forward the developer's resource boundaries, not the previous driver's candidate or signature authority. A new submission starts only after this readiness review and the developer's next instruction.

### Validating coordinated source fixes before publication

The normal build uses its pinned public NUnit package. When a reviewed repair spans both repositories, pass `-NUnitWorkflowProject ABSOLUTE/CrestronHomeNUnit.Workflow.csproj` to `tools/BuildSubmissionConsole.ps1` and to `tools/TestSubmissionConsole.ps1`. The build explicitly passes source references through console, automation and setup; the acceptance probe uses the same references. Use a fresh output directory and record both source revisions and resulting artifact hashes. This local build does not publish, install or replace a released package. Do not infer its contents from its unchanged development version number.
