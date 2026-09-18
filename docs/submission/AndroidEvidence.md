# Audit Android evidence before submission review

Command examples use the [bundled submission console](ConsoleTools.md); see that guide for source/release availability and setup.

The source tool `tools/submission/audit_android.py` checks retained output from the NUnit Android workflow against independent release and producer pins. It is an offline step between the hardware run and requirement-by-requirement evidence preparation. It does not connect to the processor or emulator, operate devices, fill the form or send a submission.

Use `submission audit-android` from the complete DevTools 1.9.0 or later console archive; its runtime and validator are included. The complete-producer check first appeared in the v1.8.0 source tools. Its producer-manifest contract requires CrestronHomeNUnit workflow 1.11.1 or later; older workflows do not create these files. Selected-case inventories require the corresponding 1.12.0 or later workflow and independently retained selection pins.

## Inputs from trusted CI

The release/coordinator jobs must retain these independently of downloaded test results:

- The candidate declaration SHA-256. The declaration identifies the actual driver's production package, full release commit, reviewed policy and official template.
- The workflow run ID assigned before execution.
- The approved fixture assembly filename and SHA-256, and the discovery dump SHA-256 captured before execution. These identify the intended producer and test inventory, including duplicate-name multiplicity.
- The SHA-256 of `producer-manifest.json`, captured before execution. The manifest lists every file under the retained `assembly/` directory, including dependency assemblies, runtime settings, nested resources and discovery output.

Do not calculate replacement pins from whatever files a worker returns. A hash supplied alongside a substituted file establishes no independent trust. The workflow writes `producer-manifest.json` and `producer-pin.json` after discovery and before test execution. It holds the original hashes in coordinator memory and rejects changed producer files, manifest, discovery or receipt after execution. Authenticated retention of those pre-execution pins outside the worker's control remains the trusted CI coordinator's responsibility. A receipt downloaded with worker results is not, by itself, independent authentication. This audit does not attest which executable the worker actually ran or authenticate its machine.

Use the private `AndroidUI` results directory from a completed workflow. It contains `context.json`, `completion.json`, `coverage.json`, `discovery.dump`, `producer-manifest.json`, `producer-pin.json`, one `TestResult*.trx`, the `assembly` folder and capture folders. Keep this directory private: its context, screenshots and hierarchy files can identify the household. Private fixture settings and credentials are not arguments to this command. Fixtures must write their results outside `assembly/`; the retained producer directory must not change during execution.

```text
CrestronHomeDevTools.Console.exe submission audit-android --candidate PRIVATE_CANDIDATE_JSON --candidate-sha256 TRUSTED_CANDIDATE_SHA256 --evidence PRIVATE_ANDROIDUI_DIRECTORY --run-id TRUSTED_RUN_ID --assembly Example.AndroidTests.dll --assembly-sha256 TRUSTED_ASSEMBLY_SHA256 --discovery-sha256 TRUSTED_DISCOVERY_SHA256 --producer-manifest-sha256 TRUSTED_PRODUCER_MANIFEST_SHA256 --output NEW_PRIVATE_AUDIT_JSON
```

These uppercase values are placeholders. The output's parent must exist. Existing output files are never overwritten. Exit 0 means the audit passed and the report was written; nonzero means the job must stop. Treat a missing report as incomplete even if a previous attempt passed. Ordinary releases that deliberately omit unavailable hardware can still proceed under their existing policy, but the submission path must remain pending.

## What is checked

### Selected Android phases (source development after 1.8.0)

The matching NUnit source branch can declare exact required test names while retaining complete project discovery. Its selected runs use producer receipt schema 2, `selection.json` and `selection.runsettings`. This source auditor accepts them only with an independently retained selection SHA-256, passed internally as `--selection-sha256` or as `selectionSha256` on the run in the private review pins. Omitting that pin cannot downgrade a selected run to complete-project evidence. These changes are not in the published 1.8.0 tools.

The selection must partition the complete discovery inventory without omissions, invented cases or splitting duplicate names. The actual TRX must execute every selected case successfully. Reports state discovered, executed and excluded counts. A selected-phase result still establishes no official requirement by itself; the reviewed coverage policy must account for all intended phases and exclusions. Old complete-project evidence retains its schema 1 contract.

The commands on this page currently describe source-tool operation. The supported developer workflow must manage this internal runtime and its pinned dependencies automatically through documented public commands. Developers are not expected to understand or maintain Python; that packaged entry point remains unfinished.

### Common checks

- A revision-zero Release candidate and its exact package hash, source commit, driver GUID/version, installed instance and run identity agree with the recorded Android context. A prior Debug run cannot be relabelled as Release evidence.
- Every retained producer file matches the independently pinned manifest. Added, removed or modified files, ambiguous paths and links are rejected. The coordinator receipt and coverage record must identify those same pins. The inventory is bounded to 4,096 files, 32 MiB per file and 512 MiB total, with a manifest no larger than 1 MiB.
- The separately pinned fixture and discovery bytes match. Discovery contains one runnable NUnit assembly with a complete nonempty inventory.
- The actual TRX executes every discovered case, preserving duplicate-name multiplicity, with unique execution IDs and successful outcomes. A passing total in `coverage.json` cannot conceal skipped, missing, duplicated or failed tests.
- The completion record confirms restoration for the same package and run.
- Each retained capture belongs to that same run and candidate, falls within the TRX run interval and has matching screenshot/hierarchy hashes. An interrupted capture folder without its completed observation is rejected.
- Referenced inputs stay within the selected directory without traversing symlinks or junctions. Their exact hashes are recorded in the audit report.

The report says `AndroidEvidenceAudited`, `submissionReady: false`, `producerAuthenticated: false` and `officialRequirementsSatisfied: []`. These are deliberate limits, not placeholders to change to true. The report is a point-in-time consistency check, not an immutable archive or proof that a physical action occurred. Preserve the files through the [private evidence bundle](EvidenceBundle.md) and authenticate the producer through trusted CI before using their observations.

## From test results to official requirements

A passing NUnit test is not automatically a completed Crestron form checkbox. The [coverage plan](CoveragePlanning.md) gives each required subcondition its own target and expected behavior. A reviewed producer binding must establish which assertions actually measured it, with any required response timing, restoration and physical feedback. The resulting observations still have to pass the normal [evidence validator](EvidenceCli.md), complete mapping and [unsigned review stage](ReviewStage.md).

For example, reading every displayed choice and cancelling an editor proves only the asserted list contents and cancellation behavior. It does not prove that selecting each choice performs its intended action, conditional controls work, edits persist correctly or the physical device responds. Producer bindings must name the actual assertions and their limits; complete Release-candidate validation remains in development.

## Validation so far

Offline regressions cover Release identity, independent pins, changed dependencies and runtime settings, missing or unlisted files, replacement manifests, nested resource assemblies, missing restoration, changed captures, incomplete runs, duplicate execution IDs, repeated test names, malformed JSON/XML and escaping paths. Parsing was also exercised on retained Debug workflow and fixture results with matching capture pairs. Debug runs are not accepted as submission evidence.

On 18 September 2026, a complete read-only suite against an unchanged installed Release candidate passed through the released NUnit 1.11.1 Android stage. The DevTools 1.8.0 source auditor accepted all discovered cases, the complete producer inventory and retained captures against separately recorded pre-execution pins. The coordinator verified original state, temporary-device cleanup and reservation release. This exercised a private integration coordinator invoking the released stage, not the complete public workflow or protected CI handoff. Same-account pin retention did not authenticate an independent worker. Complete requirement mapping and the real final review stage remain outstanding. Older results without a manifest pinned before execution must not be upgraded by inventing one afterward; retain their original scope or perform a new candidate run.
