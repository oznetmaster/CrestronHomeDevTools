# Audit Android evidence before submission review

The source tool `tools/submission/audit_android.py` checks retained output from the NUnit Android workflow against independent release and producer pins. It is an offline step between the hardware run and requirement-by-requirement evidence preparation. It does not connect to the processor or emulator, operate devices, fill the form or send a submission.

Use the matching DevTools source checkout and the Python dependencies in `tools/submission/requirements.txt`. This script is not included in the 1.5.0 NuGet package or console ZIP.

## Inputs from trusted CI

The release/coordinator jobs must retain these independently of downloaded test results:

- The candidate declaration SHA-256. The declaration identifies the actual driver's production package, full release commit, reviewed policy and official template.
- The workflow run ID assigned before execution.
- The approved fixture assembly filename and SHA-256, and the discovery dump SHA-256 captured before execution. These identify the intended producer and test inventory, including duplicate-name multiplicity.

Do not calculate replacement pins from whatever files a worker returns. A hash supplied alongside a substituted file establishes no independent trust. The existing workflow retains an assembly and discovery output, but authenticated retention of their pre-execution pins is still a responsibility of the trusted CI coordinator. This audit does not attest which executable the worker actually ran or authenticate its machine.

Use the private `AndroidUI` results directory from a completed workflow. It contains `context.json`, `completion.json`, `coverage.json`, `discovery.dump`, one `TestResult*.trx`, the `assembly` folder and capture folders. Keep this directory private: its context, screenshots and hierarchy files can identify the household. Private fixture settings and credentials are not arguments to this command.

```text
python tools/submission/audit_android.py --candidate PRIVATE_CANDIDATE_JSON --candidate-sha256 TRUSTED_CANDIDATE_SHA256 --evidence PRIVATE_ANDROIDUI_DIRECTORY --run-id TRUSTED_RUN_ID --assembly Example.AndroidTests.dll --assembly-sha256 TRUSTED_ASSEMBLY_SHA256 --discovery-sha256 TRUSTED_DISCOVERY_SHA256 --output NEW_PRIVATE_AUDIT_JSON
```

These uppercase values are placeholders. The output's parent must exist. Existing output files are never overwritten. Exit 0 means the audit passed and the report was written; nonzero means the job must stop. Treat a missing report as incomplete even if a previous attempt passed. Ordinary releases that deliberately omit unavailable hardware can still proceed under their existing policy, but the submission path must remain pending.

## What is checked

- A revision-zero Release candidate and its exact package hash, source commit, driver GUID/version, installed instance and run identity agree with the recorded Android context. A prior Debug run cannot be relabelled as Release evidence.
- The retained fixture and discovery bytes match the independent pins. Discovery contains one runnable NUnit assembly with a complete nonempty inventory.
- The actual TRX executes every discovered case, preserving duplicate-name multiplicity, with unique execution IDs and successful outcomes. A passing total in `coverage.json` cannot conceal skipped, missing, duplicated or failed tests.
- The completion record confirms restoration for the same package and run.
- Each retained capture belongs to that same run and candidate, falls within the TRX run interval and has matching screenshot/hierarchy hashes. An interrupted capture folder without its completed observation is rejected.
- Referenced inputs stay within the selected directory without traversing symlinks or junctions. Their exact hashes are recorded in the audit report.

The report says `AndroidEvidenceAudited`, `submissionReady: false`, `producerAuthenticated: false` and `officialRequirementsSatisfied: []`. These are deliberate limits, not placeholders to change to true. The report is a point-in-time consistency check, not an immutable archive or proof that a physical action occurred. Preserve the files through the [private evidence bundle](EvidenceBundle.md) and authenticate the producer through trusted CI before using their observations.

## From test results to official requirements

A passing NUnit test is not automatically a completed Crestron form checkbox. The [coverage plan](CoveragePlanning.md) gives each required subcondition its own target and expected behavior. A reviewed producer binding must establish which assertions actually measured it, with any required response timing, restoration and physical feedback. The resulting observations still have to pass the normal [evidence validator](EvidenceCli.md), complete mapping and [unsigned review stage](ReviewStage.md).

For example, the current Wiser room fixture reads all displayed schedule/day/time choices and cancels back to Home while checking retained state. That is useful evidence of choice contents and cancellation. It does not prove that every choice performs its intended action, all ten conditional editor slots work, saved schedules have the correct scope, or physical heating responds. Those remaining checks cannot receive a passing submission observation from this test. Producer bindings and complete Release-candidate validation remain in development.

## Validation so far

Offline regressions cover Release identity, independent pins, missing restoration, changed captures, incomplete runs, duplicate execution IDs, repeated test names, malformed JSON/XML and escaping paths. The parser also successfully read the real Wiser development workflow's two test results and fourteen capture pairs, and the later actual NUnit fixture run's three test results and twenty-seven capture pairs. Both runs were Debug; neither is accepted as submission evidence. The latter used a separate development coordinator, so only the retained result/capture parsing was checked, not the standard workflow directory layout. A real Release run through this new audit and the final review stage remains required.
