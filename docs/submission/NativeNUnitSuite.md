# Native NUnit submission suite

A dedicated NUnit 5 test project can inherit `CrestronHomeDevTools.SubmissionTests.SubmissionFixture`. Use this same project in Visual Studio, VS Code and CI. Reference `CrestronHomeDevTools.SubmissionTests` 1.25.0 from the configured NuGet feed. Version 1.25.0 and its SDK 2.3.0 dependency are publicly published; release validation also checks the exact packages together in a staged feed. Its `CrestronHomeDevTools.Automation` dependency supplies the same test-session controller used by CI.

```csharp
[TestFixture]
public sealed class DriverSubmissionTests : SubmissionFixture
{
    protected override string SettingsEnvironment => "MY_DRIVER_SUBMISSION_SETTINGS";
}
```

Set that environment variable to the absolute private automation settings path, and its `_SHA256` companion to the independently recorded settings digest. These are the same pinned settings consumed by the automation worker. Discovery reads neither settings nor credentials and does not access equipment. Keep this project out of the configured LocalTests selection to avoid recursive orchestration.

The inherited tests expose candidate validation, the existing combined local/processor workflow, its processor evidence check, app/recovery tests, endurance and final post-endurance/removal checks. They use the existing adapters and original NUnit/TRX/Workflow.json, not a replacement pass report. Stage test results and attached receipts provide IDE navigation; submission review still independently audits the original detailed results. This integration does not establish that every Crestron requirement is covered by a driver's configured fixtures.

Endurance has native NUnit `[Explicit]`. Ordinary Run All executes initial checks, skips endurance, and reports final checks as inconclusive if endurance has not completed. Select Endurance deliberately, then final checks. CI must make the same explicit selection. The four initial tests use NUnit 5 dependency attributes; selecting a dependent initial test includes its prerequisite tests, whose completed evidence is verified and reused. Endurance and final checks have no NUnit dependency chain that could pull an unselected endurance case into Run All; select them after their durable prerequisites pass. Failure, operator attention and unknown outcomes do not become passes; waiting operations remain active until completion or cancellation. Cancelled waits retain the existing operation for recovery on the next deliberate invocation. Completed stages verify and reuse original evidence.

The existing configuration currently represents one endurance stage. The two required endurance test identities and their coverage still need to be resolved before claiming the complete submission suite has migrated; this fixture does not invent a second test or treat one test as both.

For CI, `tools/Invoke-SubmissionSuite.ps1 -Project <dedicated.csproj> -Fixture <Namespace.DriverSubmissionTests> -ResultsDirectory <fresh-directory>` selects the four initial tests. Add `-IncludeEndurance` to deliberately select all six stage tests, including the explicit endurance case and final checks, in three NUnit invocations (initial, explicit endurance, final) with separate original TRX files. The helper checks that every selected test was reported once and passed; a mistyped filter, skipped test or zero-test success is rejected. It does not execute phase three or grant submission authority.

All native fixture operations stop before PrepareReview. Phase three is separately invoked against completed test evidence and uses the shared portable review input, signing and delivery implementation. Do not concurrently run the automation worker and the IDE against the same run; the controller's exclusive lock protects stage writes, but does not make simultaneous owners useful.

Validation uses a separate software-only probe with the real NUnit adapter and durable controller. Its synthetic receipts prove selection and reuse behavior, not hardware or certification results.

NUnit 5 deprecates Order, and DependsOnTest automatically includes dependencies even when filtered out. This fixture uses native dependencies only among initial checks. The CI helper retains the adapter’s Strict explicit mode and selects endurance by itself. References: https://docs.nunit.org/articles/nunit/writing-tests/attributes/dependsontest.html and https://docs.nunit.org/articles/vs-test-adapter/Tips-And-Tricks.html#explicitmode.

## Boundary between tests and submission

The final test stage retains the completed test receipts and evidence in `tests-finalized.json`. It does not open the review template or mapping, prepare submission documents, sign, or deliver. The controller verifies this boundary before entering phase three.

Phase three packages the accepted phase-two observations and snapshots its review inputs in `review-snapshot/`, then runs the generic document preparation against that verified snapshot. Recovery never launches missing tests or silently overwrites a partial publication. A complete snapshot can be reused after the original producer folders become unavailable. Detailed NUnit, TRX, Workflow.json, and original failures remain retained.


Phase two retains `test-assessment.json` and its full-policy observations before writing `tests-finalized.json`. It checks coverage and evidence outcomes before final removal (deferring only that removal observation), then checks the full set after cleanup. Missing, failed, inconclusive or unperformed requirements cannot be turned into a successful test suite by a gap declaration. A deliberately shortened rehearsal interval and a declared comparison without acceptance limits remain qualified evidence, not production passes.

Phase three reads the accepted assessment and packages the exact observations. It does not collect test results, create new applicability decisions, or infer checklist passes from test counts. Changes to the policy, selected observation sources, assessment or underlying evidence block the handoff. Older finalization receipts without a valid assessment do not establish this boundary; preserve them and revalidate through phase two. No test replay is implied by revalidation.

See [Correcting a failed phase-two run](FailedTestContinuation.md) for preserved evidence, affected-test dependencies and current continuation limits. A corrected observer does not authorize phase three or make an old result acceptable.

Completed endurance verification is read-only. Later stages require the exact recorded endurance receipt and validate its original samples against the frozen plan. They cannot recreate a missing receipt, collection directory, collector lock, deployment binding or producer files. Only the endurance stage publishes its result; missing or altered evidence stops the handoff without repairing the retained run.

## Package release verification

The release build stages the main library, Automation and SubmissionTests packages together. It then restores a separate consumer from an empty package cache, compiles a derived fixture and discovers all six inherited tests using the NUnit adapter. This check does not run tests, read private settings or access equipment. `tools/Test-SubmissionNuGetConsumer.ps1` retains restore and discovery logs and rejects missing packages, wrong versions, failed builds or missing/duplicate stage tests. It also verifies the SHA-256 of each restored staged package, including staged transitive dependencies, against the original archive and records those hashes in the consumer receipt. An empty cache alone is not treated as proof of which feed supplied a package. A local workflow project override cannot be used to publish these packages; the SDK dependency must already be available as a package.

The staged main library, Automation and SubmissionTests packages have passed build, documentation validation and fresh-cache consumer discovery with the staged SDK 2.3.0 package. All six inherited tests were discovered without running equipment tests. The SDK Android, Workflow, TestAdapter and Client software suites also passed. This verifies the package path; the complete release pipeline and public publication remain separate requirements.


Submission packages 1.25.0 require CrestronHomeNUnit.TestAdapter 2.3.0, including the public Android session opening API used for endpoint-verified placement and removal checks. Version 2.2.0 lacks that API. Verify that both versions are available from the configured package sources before starting a run; a successful local project-reference build is insufficient evidence of public package availability.

Routine run retention follows [current plus one previous generation](RoutineRunRetention.md). New-run startup rotates registered, explicitly closed routine generations; existing unregistered evidence and active runs remain protected.

## Invoking phase three independently

The source command `CrestronHomeDevTools.Automation --phase-three --settings PRIVATE_JSON --settings-sha256 PIN` uses the same retained run after the defined NUnit suite passes. Alternatively, supply the ordinary `--registry PRIVATE_JSON --profile NAME --release-id ID --mode rehearsal|submit` selectors after `--phase-three`. This new command requires a build containing the phase-three gate; it is not part of the already published 1.25.0 executable.

It refuses incomplete test stages before changing the checkpoint or invoking any stage adapter. It never starts missing tests. The adapters still validate the original detailed results and final assessment; receipt presence alone is not a test pass.

The default evidence role prepares review documents and returns waiting at the protected-role handoff. The protected worker uses the same `--phase-three` prefix followed by its existing independently pinned configuration and role options. Existing exact review/signing and delivery authorizations apply in both submission and rehearsal modes. Reinvoke the same command for a waiting operation; failed or unknown outcomes still require their existing inspected recovery path.

Do not attach `--phase-three` to the registry watcher or release-intake commands. The general automation command remains the whole-workflow entry point and can execute tests; use the explicit prefix when invoking phase three from IDE test results.

The shared `submission-automation.yml.example` dispatch exposes `scope: phase-three` for this handoff. Its existing `full-workflow` default remains available for orchestration from phase one. A waiting role handoff or operation is not reported as completion.
