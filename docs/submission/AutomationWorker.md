# Windows submission automation worker

This source-preview worker connects the release controller to public Crestron NUnit and endurance APIs. It is not yet the complete unattended submission route. No additional NuGet release is required to test this branch.

## Build and run

Use Windows, .NET 10, PowerShell 7.6 or later, Git and the documented driver-build prerequisites. The build machine needs the .NET Framework 4.7.2 targeting assemblies and Crestron packaging tools. Visual Studio's full editor is optional. Configure tool paths using the public [processor-test workflow](https://github.com/oznetmaster/CrestronHomeNUnit/blob/main/docs/ProcessorTestWorkflow.md). Paths and SDK overrides are build configuration, not credentials.

```powershell
dotnet publish CrestronHomeDevTools.Automation/CrestronHomeDevTools.Automation.csproj -c Release -o artifacts/automation
dotnet artifacts/automation/CrestronHomeDevTools.Automation.dll --settings C:/CI/Private/automation.json --settings-sha256 RECORDED_LOWERCASE_SHA256
```

Run [release intake](ReleaseAutomation.md#try-intake-from-the-source-build) first. The worker settings identify the resulting checkpoint; the worker does not infer a repository or choose a different package. Settings are ordinary private configuration using the `SubmissionAutomationSettings` C# model. Property names are case-insensitive; unknown properties are rejected.

| Setting | Value |
|---|---|
| `SchemaVersion` | `1` |
| `Mode` | `Rehearsal` (default) or `Submit`. Frozen with the settings; dispatch cannot override it. |
| `PrivateRoot` | The protected run root used by intake. |
| `Release` | The exact `checkpoint.release` object returned by intake, including its frozen digests. |
| `SourceRepository` | Absolute path to a clean checkout of that release's resolved commit. |
| `PackageRequirements` | `SubmissionPackageRequirements`: expected driver GUID, four-component version, portal kind, developer filename token and actual approved package contact metadata. |
| `CredentialBindings` | Absolute path to saved credential bindings or an encrypted setup snapshot. The named processor credential's host and HTTPS/SSH pins must match the NUnit plan. |
| `NUnit` | The public `WorkflowPlan` object. Declare source roots, Windows tests, processor test package/suites, test-room scope and cleanup. For actual-driver/app tests, include the exact release candidate and the approved Android fixture/profile. |
| `Endurance` | Optional public `SubmissionEnduranceWorkerPlan`, including the fully pinned read-only producer. Its candidate/source and processor must match this attempt. Missing settings stop at the corresponding stage. |

Freeze settings and their digest after accepting the plan. Do not recalculate the expected digest to bypass a changed configuration on an existing run. The current setup form does not yet generate all these bindings; that integration is still required. The tooling manifest is retained by intake, but complete service-account tool-inventory enforcement is also unfinished.

The worker reads a processor login directly from the encrypted store into memory. It does not write a plaintext credential file or pass passwords in process arguments or environment variables. A worker that builds repository code must not have unrelated signing or email credentials. A store created under an interactive user's identity does not automatically become readable by a Windows service; provision the intended service identity through the public private-store tools during setup.

## GitHub rehearsal option

The source-preview [release workflow template](submission-automation.yml.example) supplies a **rehearsal / submit** choice in GitHub Actions **Run workflow**, defaulting to rehearsal. This is a workflow option, not an extra field in GitHub's standard Publish release page. Copy it into a trusted **private orchestration repository**, not a public driver repository. It advances an already registered release; automatic release-event intake and registration remain pending.

Rehearsal uses the same real tests, endurance requirements and document preparation as submission. It is **not** a hardware dry run: equipment permissions and household-device restrictions still apply. The worker stops before signing, uploading or sending a submission email, including after a restart. A prepared rehearsal is reported as `RehearsalPrepared`, not `Submitted` or Crestron acceptance. Missing earlier bindings or failed tests remain explicit failures/attention states. It cannot currently be promoted in place by changing a dispatch selector or editing its frozen settings; promotion with verified evidence reuse is a remaining integration task.

Submit mode selects the delivery path but does not provide signature or delivery authority. Independently reviewed exact artifact authorizations remain necessary. Keep source-build access separate from protected signing and delivery identities.

Configure the same repository/branch/runner variables described in [workflow setup](WorkflowSetup.md), plus `CRESTRON_SUBMISSION_AUTOMATION_ENABLED=true` only after worker setup. The protected `crestron-submission-automation` environment supplies two path variables: `CRESTRON_SUBMISSION_AUTOMATION_EXE` (the trusted installed automation executable) and `CRESTRON_SUBMISSION_AUTOMATION_REGISTRY` (the protected local registry below). No signature or provider secrets belong in that environment. Dispatch provides only a profile name, release ID and mode; it cannot provide commands or arbitrary settings paths.

```json
{
  "SchemaVersion": 1,
  "Entries": [
    {
      "Profile": "my-driver",
      "ReleaseId": 123456,
      "Mode": "Rehearsal",
      "SettingsPath": "C:/CI/Private/releases/123456/rehearsal.json",
      "SettingsSha256": "REPLACE_WITH_RECORDED_LOWERCASE_SHA256"
    }
  ]
}
```

Only trusted setup can write this registry. Each selector must identify exactly one entry, and the pinned settings must match the selected release and mode. Duplicate dispatches reuse the same checkpoint. Separate attempts need separate protected roots; a different mode is not authorization to overwrite an existing attempt. Run the equivalent command locally with:

```powershell
CrestronHomeDevTools.Automation.exe --registry C:/CI/Private/automation-registry.json --profile my-driver --release-id 123456 --mode rehearsal
```

The template invokes the controller once and ends when endurance is waiting. It does not yet install the continuation service. A successful GitHub job that reports **Waiting** is not a completed rehearsal. The mode gate and registry selection have offline tests; the copied GitHub template has not yet been run against a live registered release.

## Execution and recovery

Candidate validation checks the package bytes against their **original published filename**, verifies the release metadata and requires the clean frozen source revision. The package report is retained even when validation fails. Package contact metadata and the Contact Information section of the help PDF are distinct checks; configure each against the correct artifact.

The Windows stage invokes the existing public NUnit workflow once. That workflow owns Windows tests, temporary processor-package installation, processor tests, any configured actual-driver/app tests, resource reservations and cleanup. The processor and app controller stages consume its verified results rather than deploying or running tests again. Receipts hash the retained raw evidence; continuation rejects changed evidence. Both temporary-instance cleanup and confirmed reservation release are required. A package that existed before the run is deliberately preserved.

The source must initially be clean. Subsequent verification uses NUnit's public source digest, which allows generated Debug revision/date fields while checking other source content. It does not permit changes to the release's major/minor/patch version or test implementation. The intent record preserves the original operation ID, workflow identity and source digest.

The app stage requires a configured actual release and Android plan, a passing `Android.Workflow` result, the public runner's deployed-driver gate, and successful cleanup. The public runner includes fixture selection, starting-state restoration and owned-child cleanup in that outcome. This verifies the **configured tests**; it does not establish that every official checklist item was covered. The later evidence policy/review remains responsible for coverage, physical tests, applicability and disclosed gaps. No app plan is an explicit missing-input state, not an automatic N/A or pass.

The endurance adapter starts one public monitor and returns while collection waits. Later invocations resume the same monitor. Passed or failed read-only runs release their reservation; failures remain failures. An interrupted probe or uncertain acquisition/release stops for inspection. A passing run exports revalidated observations and identifies their retained evidence directory. Subsequent stages revalidate those samples. Separated runs are never combined into uninterrupted evidence.

An interrupted NUnit intent without a complete terminal result becomes `OutcomeUnknown`; the worker will not launch a replacement run. Inspect its public NUnit result and lease journal. The controller's `RequestRecovery` API requires the digest of the state being reviewed and does not itself authorize replay or submission.

Exit codes: `0` complete submission or prepared rehearsal (distinguished by `Outcome`); `4` endurance waiting; `3` attention/missing binding; `2` input or execution exception. The retained checkpoint and original domain journals are authoritative. The six-hour invocation deadline and console cancellation retain operation state; neither resets an attempt. Automatic Windows startup/scheduled continuation is not wired into this preview yet—do not claim reboot-resilient unattended operation merely from checkpoint support.

## Validation boundary

On 24 September 2026, an isolated Windows worker fetched a published driver release and checked out the exact public driver and NUnit sources. Its controller passed package validation, **86 Windows tests and 86 processor tests**, removed the temporary test instance and confirmed reservation release. A repeated invocation left the checkpoint unchanged. The actual installed product driver was not replaced. The rehearsal stopped at the missing app binding as expected.

Earlier preparation attempts exposed a persisted enum-format mismatch and an incorrect expected contact URL; both stopped before processor operations and their evidence was retained. Automated regression coverage includes the receipt format, generated-source changes, cleanup/lease failures, altered raw evidence, interrupted operations, app restoration and endurance completion/failure recovery.

This is component integration evidence. It is not a new driver submission, physical-device test, real Android/endurance run through this controller, service-account restart test, GitHub release-event test or complete public end-to-end demonstration. Review preparation, signing, delivery and final retention adapters remain explicit missing bindings. Existing submissions must not be resent to demonstrate controller progress.

Copyright (c) 2026 Neil Colvin. MIT licensed.
