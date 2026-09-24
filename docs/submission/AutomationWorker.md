# Windows submission automation worker

This source-preview worker connects release discovery, the public Crestron NUnit and endurance APIs, document preparation, authorized signing and delivery, and final retention. The adapters are implemented; the full route still needs a fresh real end-to-end rehearsal. No additional NuGet release is required to test this branch.

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
| `Review` | `SubmissionAutomationReviewPlan`: pinned policy, official template, inventory, mapping, complete bundled console, title/author and retained observation paths. Optional declarations and Android pins follow the public review contract. |
| `Protected` | Separate signing/delivery credential bindings and exact approval channels. Each channel has an approval document path and an independently recorded digest-file path outside the evidence run. Delivery includes the approved sender, SMTP endpoint and reviewed uploader form/terms digests. |

Freeze settings and their digest after accepting the plan. Do not recalculate the expected digest to bypass a changed configuration on an existing run. The current setup form does not yet generate all these bindings; that integration is still required. The tooling manifest is retained by intake, but complete service-account tool-inventory enforcement is also unfinished.

The worker reads a processor login directly from the encrypted store into memory. It does not write a plaintext credential file or pass passwords in process arguments or environment variables. A worker that builds repository code must not have unrelated signing or email credentials. A store created under an interactive user's identity does not automatically become readable by a Windows service; provision the intended service identity through the public private-store tools during setup.

## GitHub rehearsal option

The source-preview [release workflow template](submission-automation.yml.example) supplies a **rehearsal / submit** choice in GitHub Actions **Run workflow**, defaulting to rehearsal. This is a workflow option, not an extra field in GitHub's standard Publish release page. Copy it into a trusted **private orchestration repository**, not a public driver repository. It advances an already registered release. The optional Windows release watcher below detects new published releases without requiring submission files in the public driver repository.

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

The template invokes the controller once and ends when an operation is waiting. Install the background worker below to resume waits automatically. A successful GitHub job that reports **Waiting** is not a completed rehearsal. The mode gate and registry selection have offline tests; the copied GitHub template has not yet been run against a live registered release.

## Background continuation and release discovery

Provision the trusted executable, protected registry and service-writable status directory first. Use a dedicated evidence-worker identity that can build the chosen sources and access only its test credentials. Provision signing/delivery under a separate protected identity with its own credential store and approval directory. `--role` routes work; it is **not** a substitute for Windows permissions or isolation between these accounts. The protected worker must not execute driver source code.

Run the installer from administrator PowerShell 7.6 or later:

```powershell
./tools/InstallSubmissionAutomationWorker.ps1 -Executable C:/CI/Tools/CrestronHomeDevTools.Automation.exe -Registry C:/CI/Private/automation-registry.json -StatusDirectory C:/CI/Private/worker-status -Name Evidence -Role evidence
```

It creates an ordinary Windows startup task, starts it immediately, prevents overlapping task instances and allows bounded process restart. No interactive desktop or AI heartbeat is needed. The default identity is LocalService; the optional account can be NetworkService. Check the actual account's access before enabling real runs. A user-scope DPAPI store is not transferable to a service by copying its files.

The protected role additionally requires `-ProtectedWorker PRIVATE_JSON -ProtectedWorkerSha256 INDEPENDENT_DIGEST`. Its `SubmissionAutomationProtectedWorker` model contains schema version 1, exact `AllowedPrivateRoots`, opted-in `Repositories`, a complete pinned `Console` and the protected `Plan` described above. Store this installed configuration, tools, credentials and approvals outside build-writable run roots and deny the build account write access. The installer embeds its reviewed digest in the protected startup command. This independent configuration overrides the run's executable/credential/approval choices; merely writing a different run registry cannot choose a different signing executable. Approval paths may contain `${runKey}`, expanded to the deterministic 64-character run key, to keep concurrent releases separate. In C#, use `SubmissionAutomationStages.CreateProtected` with the independently pinned installed configuration. Role selection alone cannot create a protected adapter.

The worker advances only Ready/Running/Waiting operations assigned to its role. It never automatically resets Failed, NeedsInput or OutcomeUnknown. Signing and delivery waits resume when their exact approval document and digest become available. The existing domain journal reconciles a confirmed upload before sending email; uncertain provider outcomes remain stopped. Polling keeps one current status plus a history that rotates at 1 MiB, with one previous file. Unchanged polling performs no history/status write. Each advancement has a six-hour limit; endurance returns promptly while collection continues separately.

Add `-ReleaseProfiles C:/CI/Private/release-profiles.json` to the **evidence** installer to detect opted-in releases every 15 minutes. This is a private Windows poll of published GitHub releases, not a webhook or a change to GitHub's release editor. The profile specifies an explicit UTC start time; installing the watcher does not opt in earlier releases. Default mode is Rehearsal. This source-preview command uses unauthenticated public-repository access; named GitHub authentication is still pending. API rate limits or access failures are attention conditions, not successful intake.

```json
{
  "SchemaVersion": 1,
  "Profiles": [{
    "Name": "my-driver",
    "Repository": "YOUR_ORGANISATION/YOUR_DRIVER",
    "NotBeforeUtc": "2026-10-01T00:00:00Z",
    "PrivateRoot": "C:/CI/Private/driver-runs",
    "PackageNameTemplate": "YourManufacturer_YourType_YourModel_IP.pkg",
    "SettingsTemplate": {"Path": "C:/CI/Private/driver-settings-template.json", "Sha256": "REPLACE_WITH_RECORDED_LOWERCASE_SHA256"},
    "ToolingManifest": {"Path": "C:/CI/Private/tooling.json", "Sha256": "REPLACE_WITH_RECORDED_LOWERCASE_SHA256"},
    "Mode": "Rehearsal",
    "AllowPrerelease": false
  }]
}
```

The settings template uses the settings model above. Its `Release`, `PrivateRoot`, `SchemaVersion` and `Mode` are filled from verified intake and the private profile. String values can use `${run}`, `${source}`, `${package}`, `${version}`, `${version4}`, `${commit}`, `${packageSha256}` and `${releaseId}`. Set `SourceRepository` to `${source}` and use it in the NUnit source/project paths. Other required source/tool roots must be provisioned in the saved plan. Unknown placeholders fail. Package names may use `${version}`. Automatic version expansion currently supports numeric three- or four-component tags, optionally prefixed with `v`; other tagging conventions use explicit intake.

Discovery waits for the exact package asset and digest, freezes the full private profile, downloads verified bytes, checks out the resolved public commit without hooks or submodules, expands and pins the per-release settings, then atomically registers the attempt. The ordinary worker advances it. Duplicate detection preserves an existing registration and checkpoint; it never overwrites completed evidence or resends an old submission. Workers sharing a registry must also share its storage and run locks; independent copies are not a distributed queue.

To validate discovery without starting tests, invoke it once against an unwatched registry:

```powershell
CrestronHomeDevTools.Automation.exe --intake-releases C:/CI/Private/release-profiles.json --registry C:/CI/Private/automation-registry.json
```

This only performs discovery, intake, source checkout and registration. The installed watcher will execute registered tests, so use an isolated unwatched registry when checking setup alone. A source-preview profile must be reviewed against the actual driver tests and device restrictions; filling placeholders does not manufacture coverage.

## Execution and recovery

Candidate validation checks the package bytes against their **original published filename**, verifies the release metadata and requires the clean frozen source revision. The package report is retained even when validation fails. Package contact metadata and the Contact Information section of the help PDF are distinct checks; configure each against the correct artifact.

The Windows stage invokes the existing public NUnit workflow once. That workflow owns Windows tests, temporary processor-package installation, processor tests, any configured actual-driver/app tests, resource reservations and cleanup. The processor and app controller stages consume its verified results rather than deploying or running tests again. Receipts hash the retained raw evidence; continuation rejects changed evidence. Both temporary-instance cleanup and confirmed reservation release are required. A package that existed before the run is deliberately preserved.

The source must initially be clean. Subsequent verification uses NUnit's public source digest, which allows generated Debug revision/date fields while checking other source content. It does not permit changes to the release's major/minor/patch version or test implementation. The intent record preserves the original operation ID, workflow identity and source digest.

The app stage requires a configured actual release and Android plan, a passing `Android.Workflow` result, the public runner's deployed-driver gate, and successful cleanup. The public runner includes fixture selection, starting-state restoration and owned-child cleanup in that outcome. This verifies the **configured tests**; it does not establish that every official checklist item was covered. The later evidence policy/review remains responsible for coverage, physical tests, applicability and disclosed gaps. No app plan is an explicit missing-input state, not an automatic N/A or pass.

The endurance adapter starts one public monitor and returns while collection waits. Later invocations resume the same monitor. Passed or failed read-only runs release their reservation; failures remain failures. An interrupted probe or uncertain acquisition/release stops for inspection. A passing run exports revalidated observations and identifies their retained evidence directory. Subsequent stages revalidate those samples. Separated runs are never combined into uninterrupted evidence.

An interrupted NUnit intent without a complete terminal result becomes `OutcomeUnknown`; the worker will not launch a replacement run. Inspect its public NUnit result and lease journal. The controller's `RequestRecovery` API requires the digest of the state being reviewed and does not itself authorize replay or submission.

Review preparation consumes observations retained by the verified NUnit producer and the identity-matched endurance export. It calls the pinned bundled public console; no separate Python installation is required. Test counts never imply checklist coverage. Optional declared-gap submissions use the existing declarations and review contracts. Incomplete document output after interruption remains an explicit uncertain operation, rather than silently regenerating a different signing copy.

The protected worker verifies retained documents before signing and delivery. Missing exact authority creates a request file and returns Waiting. It never creates its own approval. Delivery uses the public upload/verify/email coordinator, revalidates before each provider operation and retains receipts. Final retention inventories the selected run and records provider-confirmed submission, **not** Crestron acceptance. Custom correspondence/gap summaries currently apply only to the declared-gap route; the complete route uses its standard approved correspondence. The final inventory does not delete source workspaces or unresolved evidence.

Exit codes for a single advancement: `0` complete submission or prepared rehearsal (distinguished by `Outcome`); `4` waiting, including role/approval/endurance handoffs; `3` attention/missing binding; `2` input or execution exception. Intake returns `0` for registration/no eligible releases/package waiting, `3` for an attention condition; inspect its structured result. The retained checkpoint and original domain journals are authoritative. Cancellation retains operation state and never resets an attempt.

## Validation boundary

On 24 September 2026, an isolated Windows worker fetched a published driver release and checked out the exact public driver and NUnit sources. Its controller passed package validation, **86 Windows tests and 86 processor tests**, removed the temporary test instance and confirmed reservation release. A repeated invocation left the checkpoint unchanged. The actual installed product driver was not replaced. The rehearsal stopped at the missing app binding as expected.

Earlier preparation attempts exposed a persisted enum-format mismatch and an incorrect expected contact URL; both stopped before processor operations and their evidence was retained. Automated regression coverage includes the receipt format, generated-source changes, cleanup/lease failures, altered raw evidence, interrupted operations, app restoration and endurance completion/failure recovery.

Further validation on 24 September ran the real bundled document tools through review, encrypted synthetic-signature use, delivery preparation, public journal/revalidation and final retention. Fake providers confirmed upload-before-email ordering and no duplicate send after resume; no real submission was sent. All 21 bundled-console integration tests passed. Release-discovery tests cover the publication cutoff, delayed package assets, pinned template changes, interrupted checkout and duplicate registration.

On a dedicated Windows worker, the public installer ran the empty-registry watcher under LocalService. After an actual computer restart, Windows started it about five seconds after boot without a desktop session; unchanged polling and restart kept history to one entry. This proves process startup, not service access to real device/signing credentials or recovery of a live hardware stage.

The public private-store provisioning API subsequently copied only the selected test-processor credential to a local service store. A task running as LocalService successfully decrypted it and resolved its purpose/endpoint binding without displaying values or opening a processor connection. This validates the evidence identity's credential access; signing/mail credentials were not provisioned to that account.

These are component and integration checks. A fresh real release through configured Android, endurance, document review and authorized provider delivery still needs validation. Service-account credential/ACL provisioning, setup-form generation of full automation bindings, authenticated GitHub access and the consolidated approval experience remain integration work. Existing submissions must not be resent to demonstrate controller progress.

Copyright (c) 2026 Neil Colvin. MIT licensed.
