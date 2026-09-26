# Windows submission automation worker

This source-preview worker connects release discovery, the public Crestron NUnit and endurance APIs, document preparation, authorized signing and delivery, and final retention. A configured real release-to-review rehearsal completed without intervention after startup, with shortened endurance explicitly disclosed. Protected signing/delivery/retention passed separate synthetic validation. A newly published release followed by real submission remains a distinct validation boundary; see [validation status](ValidationStatus.md). No additional NuGet release is required to test this branch.

Observers must check `worker-status.json` as well as the workflow checkpoint and
the actual scheduled-task/process state. An adapter exception produces worker
`AttentionRequired` even though its durable checkpoint remains `Running` to
preserve the original operation for recovery. A live watcher process alone does
not mean its submission is progressing. Inspect the reported stage and retained
input/process diagnostics; do not reset the run or mark it passed.

Create release checkouts under the account that will execute the worker. During
preflight, verify Git source identity and encrypted credential access under that
same account. Checkouts created by an administrator can be rejected by Git when
the service subsequently reads them; do not bypass this with a global trust rule.

## Build and run

Use Windows, .NET 10, PowerShell 7.6 or later, Git and the documented driver-build prerequisites. The build machine needs the .NET Framework 4.7.2 targeting assemblies and Crestron packaging tools. Visual Studio's full editor is optional. Configure tool paths using the public [processor-test workflow](https://github.com/oznetmaster/CrestronHomeNUnit/blob/main/docs/ProcessorTestWorkflow.md). Paths and SDK overrides are build configuration, not credentials.

Use a short private work root and enable Windows long-path support before starting
the build worker. Intake adds a 64-character run directory, then the repository,
project and build-output paths. With long paths disabled, MSBuild can report
`MSB3030` for a DLL that the compiler has actually written once its absolute path
reaches 260 characters. Inspect `LongPathsEnabled` under
`HKLM:\SYSTEM\CurrentControlSet\Control\FileSystem`; an administrator can set
that DWORD to `1`. Start a fresh worker process after changing it, and verify the
real project under its service account. Individual vendor tools can still have
shorter path limits, so this setting does not replace the build rehearsal. In
the hardware rehearsal, a longer directory also caused the .NET Framework test
host to report no matching tests with unchanged filters; the same source and
filters passed all 86 Windows tests from a short directory. Keep the complete
build and test paths below 260 characters even with the Windows setting enabled.
Do not remove a filter or lower the minimum test count to get past this symptom.

If a run has already failed, retain its result. Correct the machine prerequisite
and register a reviewed new attempt with a new private root and updated tooling
manifest; do not erase the failed checkpoint or turn a diagnostic rerun into its
passing result.

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
| `InstalledAppTests` | Optional public `InstalledDriverTestPlan` for a separate app phase against an already-installed exact candidate. Leave `NUnit.AndroidTests` empty when using this route. The release package/commit, processor and trust pins must match this attempt. |
| `InstalledAppFixtureSettings` | Optional JSON object of driver-specific factual inputs for the separate installed-app phase or combined `NUnit.AndroidTests` deployment route. Release placeholders are expanded and the frozen settings bind its bytes. The controller retains it as `app-fixture-settings.json` at the run root before invocation, verifies it after execution and during recovery, and includes it in the owning producer inventory (NUnit for combined deployment, installed-app for the separate route). Store credential references, never raw passwords or signing material. |
| `Endurance` | Optional public `SubmissionEnduranceWorkerPlan`, including the fully pinned read-only producer. Its candidate/source and processor must match this attempt. Missing settings stop at the corresponding stage. |
| `PostEnduranceTests` | Optional public `InstalledDriverTestPlan` for functional checks after endurance finishes and before review preparation. Use the same frozen candidate, processor and trust pins. Results are kept separately from the initial app tests. |
| `PostEnduranceFixtureSettings` | Optional factual JSON object for those checks; defaults to `InstalledAppFixtureSettings`. Use phase-specific observation IDs where the review includes both phases. |
| `EnduranceFromDeployment` | Optional boolean (default false). Bind the final probe settings to the verified actual-driver deployment before collection. Requires `NUnit.ActualDriver`, `NUnit.ReleaseCandidate` and a pinned probe settings template; see deployment below. |
| `EnduranceProbeSettingsTemplate` | Optional pinned JSON settings template for release discovery. Intake copies the declared producer publication into the new run, expands candidate/path placeholders, includes the generated settings in its file inventory and binds the resulting producer ID. Leave the source probe's `SettingsFile` null. Explicit prebuilt settings continue to use the existing probe contract. |
| `Review` | `SubmissionAutomationReviewPlan`: pinned policy, official template, inventory, mapping, complete bundled console, title/author and retained observation paths. Optional declarations and Android pins follow the public review contract. |
| `Protected` | Separate signing/delivery credential bindings and exact approval channels. Each channel has an approval document path and an independently recorded digest-file path outside the evidence run. Delivery includes the approved sender, SMTP endpoint and reviewed uploader form/terms digests. |

Freeze settings and their digest after accepting the plan. Do not recalculate the expected digest to bypass a changed configuration on an existing run. The [saved-setup bridge](SetupApp.md#prepare-a-controller-rehearsal) combines factual profiles with a reviewed executable template; it cannot invent driver-specific fixtures or coverage mappings. Review-console and endurance-producer file inventories are enforced by their stage adapters. The general tooling manifest is retained by intake; it is not an enforcement mechanism for every machine-wide SDK or executable. Verify those prerequisites under the actual worker account during setup.

Before a full rehearsal, list missing stage bindings together:

```powershell
CrestronHomeDevTools.Automation.exe --check-settings C:/CI/Private/automation.json --settings-sha256 RECORDED_LOWERCASE_SHA256
```

This read-only command verifies the saved settings digest and reports missing Windows,
processor, app, endurance and review bindings. Submit additionally requires protected
signing/delivery configuration; rehearsal does not. It neither opens a run nor starts
tests, reads secret values or contacts equipment/providers. Exit `0` means all stage
bindings are present; `3` lists omissions; `2` means the settings could not be read or
verified. This is a completeness check, not a substitute for each stage's file,
credential, equipment and evidence validation. It does not create missing evidence or
turn omitted tests into passes. Deliberately partial component rehearsals remain
possible, but do not describe them as full-route validation.

### Deployment followed by app tests

For a newly published candidate, configure `NUnit.ActualDriver` and
`NUnit.ReleaseCandidate` to the frozen `${package}`, `${packageSha256}` and
`${commit}`. Include the public runner's required live suites, deployed checks,
room and explicit replacement/configuration policy. Set `NUnit.AndroidTests` and
leave `InstalledAppTests` null to run the app fixture against the instance returned
by that deployment. The runner retains import/activation receipts and passes the
actual instance ID, candidate hash and source commit to the Android context.
Do not obtain that ID by selecting the first similarly named device.

The WeatherLink sample now accepts this route. Use `DeviceId: 0` in
`InstalledAppFixtureSettings` to bind it to the deployment context; an explicit
positive ID instead requires that exact instance. Its observation source is
`nunit/AndroidUI/weather-observations.json`. The controller creates and retains the
fixture settings before NUnit starts, detects changes during execution and includes
them in the Windows/processor producer receipt. Recovery does not regenerate changed
inputs or replay an uncertain deployment. This route has offline regression coverage;
its new WeatherLink integration still needs a fresh hardware deployment run.

For endurance after this deployment, set `EnduranceFromDeployment: true` and use
the two deployment placeholders in the pinned `EnduranceProbeSettingsTemplate`:

```json
{
  "DeviceId": "${deployedDeviceId}",
  "CatalogueId": "${deployedCatalogueId}"
}
```

These are the target fields within the producer's complete settings, not a complete
producer configuration. The device placeholder must occupy the entire JSON string;
it becomes an integer. Normal release placeholders still expand at intake.
Both deployment placeholders are required, and they are allowed only in this
probe template. The frozen `Endurance.Plan.InstallationIdentity` can be a unique
per-release string such as `release:${releaseId}`; it must match the producer's
installation-identity input. Do not use an unknown device ID in that string.

Intake pins a template publication in `endurance-producer-template`. Once Windows,
processor and app tests have completed, the controller verifies the retained
`actual-import.json`, `actual-activation.json` and `ReleaseCandidate.json` against
the completed NUnit inventory and frozen package/commit. It creates the final
`endurance-producer` publication, records `endurance-deployment-binding.json` and
uses the resulting producer ID when starting collection. The candidate, policy,
reservation, duration and sampling criteria are unchanged. Frozen release settings
are not rewritten. Recovery verifies the same binding and final files; it never
retargets an existing collection or silently replaces missing final files.

The flag defaults to false, preserving the existing installed-candidate route.
The new handoff has offline integration/regression coverage; fresh deployment
through endurance still needs a hardware rehearsal. An installed-candidate
rehearsal remains useful but does not prove deployment.

### Separate app phase

Use `InstalledAppTests` when the candidate is already installed and the Windows/processor
test phase should not redeploy it merely to run app fixtures. This invokes the public
[installed-driver test API](https://github.com/oznetmaster/CrestronHomeNUnit/blob/main/docs/InstalledDriverTests.md)
at the AppTests stage. It verifies installed identity and package files before and
after the fixtures, and requires passing tests, physical-state restoration,
temporary-child cleanup and released processor/Android reservations. The setup
selects the exact existing instance; it does not authorize a replacement or infer
identity from a similar tile name. A Home-readiness sample proves only its own
assertions and cannot supply missing driver-specific checklist coverage.

The worker retains the invocation identity, fixture-source digest and private Android
profile digest before execution. Recovery consumes an existing terminal result;
it never repeats an interrupted app operation. Preparing, missing or failed-to-record
results require inspection of the public phase and reservation journals. Completed
raw app evidence is inventoried and checked before later stages. Keep the profile,
fixtures and results private where they contain household information. An old
checkpoint's frozen settings cannot be edited to retrofit this new binding; create
a reviewed new attempt rather than relabelling an earlier pass.

Review preparation can consume observations from either the combined NUnit producer
or a completed separate app producer. Its source must be listed in that producer's
retained receipt; the app receipt, workflow identity and complete app inventory are
rechecked before composition. A JSON file placed in the run directory later is not
accepted as completed test evidence.

### Functional checks after endurance

Configure `PostEnduranceTests` when the test plan requires controls or response
checks after the endurance interval. This source-preview addition runs the public
installed-driver test runner at the start of `PrepareReview`, after a verified
completed `Endurance` receipt. It does not redeploy the candidate or repeat the
Windows and processor unit suites. The plan identifies the exact installed target;
it does not discover a target by its display name. The worker still enforces the
public runner's reservations, candidate verification, restoration and cleanup.

Keep measurements needed for a before/after response comparison in the fixture's
evidence. Merely rerunning a functional test does not establish unchanged response
time. A shortened rehearsal remains shortened even when its later checks pass.
Leaving this optional plan absent preserves existing workflows; it does not mark
any post-endurance checklist requirement complete.

The worker retains the second invocation under `post-endurance/`, with a binding to
the original endurance receipt and frozen input identity. Interrupted invocations
are not replayed automatically. Recovery inspects their existing result; uncertain
execution or restoration stops review preparation. Initial app evidence remains
unchanged, and altered post-endurance files also prevent later continuation.

List the new producer's observation file in `Review.ObservationSources`, for
example `post-endurance/installed-app/AndroidUI/after-observations.json`. The fixture
uses paths relative to its own phase run root (for example
`installed-app/AndroidUI/after/screen.png`); composition prefixes those references
with `post-endurance/` in a separate derived document and preserves the original.
This includes response, restoration and sample evidence references. Give before
and after assertions distinct policy/observation IDs when including both; the
composer does not replace an earlier observation with the later one. Passing
NUnit counts alone never populate checklist boxes.

The stage adapter and evidence composition have offline regression coverage.
A hardware run exercising this additional stage remains to be completed; existing
rehearsals did not include it. Build from source to validate this addition before
the next packaged release.

### Reviewed evidence from an earlier candidate

The optional `Review.PriorEvidence` object supplies a private source `Directory`
and a `Files` inventory of relative paths and SHA-256 digests. The frozen settings
pin that selection. Candidate validation copies those exact files into the run's
`prior-evidence/` directory and invokes the public
[prior-evidence review](PriorEvidence.md) before hardware tests start. The source
directory may then become unavailable: later stages verify the retained copies.
Missing or changed retained files stop the run; they are not silently recopied.

The reviewed target policy must explicitly permit each earlier assertion and
reference its original policy, observations, evidence directory and scoped
change-impact review beneath `prior-evidence/`. Relative paths in `Files` are
relative to the source directory; they do **not** include that destination prefix.
Preserve the original observation bytes and execution identity. Preparing the
target policy and change review is a reviewed input, not a decision the worker
makes merely because earlier tests passed. The inventory is limited to 4,096
files and 512 MiB; traversal, redirected paths and extra retained files fail.

At review preparation the controller imports the validated subset as
`ReviewedPriorPass`, retains the original records and unrelated failures, and
composes it with current producer observations and endurance. It cannot import
an earlier `ReviewedPriorPass` as another original pass. If current evidence also
addresses the same assertion, ordinary composition reports the conflict instead
of choosing the older pass. Omit prior permission for scopes intentionally being
tested again. This handoff does not supply N/A decisions, waive missing tests,
change an endurance duration or establish Crestron acceptance.

### Reviewed non-applicability

The optional `Review.Applicability` object retains candidate-specific N/A decisions
without presenting them as executed tests. Supply a private `Directory`, a pinned
`Files` inventory and `Observations` (the relative path of the standard observation
document within that inventory). Supporting references inside that document use
the destination `applicability/` prefix; inventory paths do not.

Every observation must be `NotApplicable`, identify the exact candidate and policy,
have a rationale, and reference retained source evidence. The reviewed policy must
permit N/A for that scope. The controller validates these inputs before hardware
testing and retains them using the same bounds and recovery rules as prior evidence.
It cannot import passes or failures through this input, infer N/A from an absent
test, or resolve a conflict with a test producer. A changed candidate requires a new
applicability review; the worker never rewrites identities to reuse old decisions.
Ordinary composition and the unsigned review bundle include these decisions and
their source files. This input records a reviewed decision; it does not itself
analyze source code or establish whether Crestron agrees with that interpretation.

### Source-based applicability before a release exists

`Review.SourceApplicability` is an optional pinned `SubmissionAutomationInput`
(`Path`, `Sha256`) for a private `SubmissionSourceApplicabilityPlan`. It lets a
reviewed source-only N/A decision apply to a new release without knowing its
future package hash during setup. This addition is available from source; use a
bundle containing it before configuring this field.

The plan contains `SchemaVersion: 1`, a nonempty `ReviewedBy`, `ReviewedUtc`,
`SourceFiles` (checkout-relative paths and exact byte SHA-256 values), and
`Decisions` (`RequirementId`, `Rationale`). Include every source file needed to
support each decision. Git checkout line endings affect these exact byte pins;
use stable attributes or verify the target Windows checkout when preparing them.

The candidate stage first verifies the release checkout, then checks the reviewed
source files and the policy. Each decision must reference a policy scope that
explicitly allows N/A, uses method `absence`, requires outcome `NotApplicable`
and has no response, restoration or sampling measurements. The worker retains
the source and the dated review, binds the decision to the actual new release,
and includes it in the unsigned review bundle. Changed source stops the workflow
for a new review. Recovery verifies retained bytes and does not relabel an old
decision or silently recreate missing evidence.

Use this only when applicability is determined by those source files: for
example, a control type absent from all driver-defined UI variants. Do not use
it for framework-generated controls, device capabilities, installation-dependent
behavior, unperformed tests or runtime observations. Those need their own
producer or a disclosed gap. This input cannot generate a passing result, infer
that the source inventory is complete, authorize a signature or determine
Crestron acceptance. Existing candidate-specific `Applicability` remains valid;
do not supply both routes for the same requirement.

### Retained qualifications

When a limitation is known during setup, `Review.PlannedGaps` may instead provide
an array of `SubmissionGapDeclaration` objects with a `RequirementId` and a
nonempty `Reason`. For example, a rehearsal can explicitly declare its planned
one-hour observation against the unchanged 24-hour policy requirement. The
frozen settings authorize only those exact scoped explanations; the worker binds
them to the release identity when preparing the review. No future package hash
is needed during setup. This addition is available from source alongside
`SourceApplicability`.

Use either `PlannedGaps` or the existing pinned candidate-specific `Declarations`,
never both. Planned gaps cannot contain interpretation reviews or add unknown
policy scopes. They generate no observations and cannot convert a failure or an
unperformed test into a pass. The normal assessor still rejects malformed or
conflicting evidence, undeclared gaps, and a stale declaration where the actual
requirement passed. Signing and delivery approvals remain separate.

For an explicitly reviewed partial, inconclusive, failed or untested item,
`Review.Qualifications` accepts the same `Directory`, `Files` and `Observations`
shape, using the `qualifications/` destination prefix. It requires a rationale,
retained supporting files and the exact current candidate/policy identity. It
accepts no passing or N/A outcome. A review of historical evidence must preserve
the original records, identify it as a current review, and disclose that no fresh
execution occurred; do not change an old test's identity or dates.

The records are retained before testing and composed without altering their
outcome. They still fail ordinary completeness checks: `Review.Declarations` or
the frozen `Review.PlannedGaps` must explicitly account for the exact gaps. The existing
interpretation-review rules continue to apply and cannot accept a failed or
unperformed test. This handoff neither grants signing authority nor suppresses
conflicting fresh results. It replaces manual mid-workflow file copying, not
the developer's review of those limitations.

The worker reads a processor login directly from the encrypted store into memory. It does not write a plaintext credential file or pass passwords in process arguments or environment variables. A worker that builds repository code must not have unrelated signing or email credentials. A store created under an interactive user's identity does not automatically become readable by a Windows service; provision the intended service identity through the public private-store tools during setup.

Build-tool setup must also work under that actual service account. A successful
interactive build is insufficient if its shell temporarily supplied SDK paths.
For projects using the NUnit processor-package targets, configure the applicable
`ProcessorTestSdkRoot`, `CrestronDriverSdkRoot`, `ManifestUtilExe`,
`CompactJsonPath` and `IlRepackToolDirectory` paths persistently on a dedicated
worker, and verify them in a fresh process under its service identity. These are
tool locations, not credential variables. Record their versions and file hashes
in the tooling manifest. On a shared machine, preserve existing settings and
resolve incompatible toolchains before installing the worker.

Keep build tools in a directory the evidence account can read and execute. Do
not grant it access to a private CI or signing directory merely to reach a tool
inside that directory. Prepare public source checkouts under the account that
will build them, with write access for build output; this also avoids Git
ownership errors. Never disable Git ownership checking globally. Release intake
creates the candidate checkout under the executing worker account itself.

## GitHub rehearsal option

The source-preview [release workflow template](submission-automation.yml.example) supplies a **rehearsal / submit** choice in GitHub Actions **Run workflow**, defaulting to rehearsal. This is a workflow option, not an extra field in GitHub's standard Publish release page. Copy it into a trusted **private orchestration repository**, not a public driver repository. It advances an already registered release. The optional Windows release watcher below detects new published releases without requiring submission files in the public driver repository.

Rehearsal uses the same real tests, endurance requirements and document preparation as submission. It is **not** a hardware dry run: equipment permissions and household-device restrictions still apply. The worker stops before signing, uploading or sending a submission email, including after a restart. A prepared rehearsal is reported as `RehearsalPrepared`, not `Submitted` or Crestron acceptance. Missing earlier bindings or failed tests remain explicit failures/attention states. It cannot currently be promoted in place by changing a dispatch selector or editing its frozen settings; promotion with verified evidence reuse is a remaining integration task.

Follow the [complete rehearsal procedure](Rehearsal.md) and retain every manual
intervention. A successfully assisted submission is not evidence that these
automatic handoffs have been validated.

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

The source-preview console archive build includes the self-contained worker in
`automation/` and installers in `scripts/automation/`. Extract the complete archive;
keep both directory trees. Building drivers still requires the documented .NET
SDK and Crestron tools, but starting this worker does not require compiling it.
The examples below use source-checkout installer paths; in the archive substitute
`scripts/automation/` for `tools/`.

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

The settings template uses the settings model above. Its `Release`, `PrivateRoot`, `SchemaVersion` and `Mode` are filled from verified intake and the private profile. String values can use `${run}`, `${source}`, `${package}`, `${version}`, `${version4}`, `${commit}`, `${packageSha256}`, `${releaseId}`, `${reservationId}` and `${runKey}`. Set `SourceRepository` to `${source}` and use it in the NUnit source/project paths. Other required source/tool roots must be provisioned in the saved plan. Unknown placeholders fail. Package names may use `${version}`. Automatic version expansion currently supports numeric three- or four-component tags, optionally prefixed with `v`; other tagging conventions use explicit intake.

`${runKey}` is the stable repository/release identifier also used by the installed
protected worker for per-release approval paths. It keeps each release's signing
and delivery channels separate. Expanding those references creates no approval
files and does not replace the protected worker's independently pinned bindings.

Set `Endurance.Plan.ReservationId` to `${reservationId}` in a release template.
Intake derives a stable GUID in N format from the frozen release identity,
including the profile and tooling digests. Retrying the same intake keeps the
same ID; another release or reviewed profile gets a different one. Explicit
settings require a unique 32-hexadecimal-character GUID, without hyphens.
Descriptive names are invalid. Intake, settings loading and candidate validation
reject an invalid ID before hardware tests; the read-only completeness check
also reports it. Never change this value in an existing frozen run.

An `EnduranceProbeSettingsTemplate` uses those same placeholders. Its file path
and SHA-256 are pinned in the settings template. The source `Endurance.Probe`
declares an already published executable directory and complete file inventory,
with `SettingsFile: null`. Intake verifies that inventory, copies it into
`${run}/endurance-producer`, writes `settings.generated.json`, and computes the
per-release inventory and producer ID before registering the run. It never runs
the producer during intake. Existing generated copies must match on recovery;
changed source or retained bytes stop registration instead of being overwritten.

For the [WeatherLink sample](../../samples/WeatherLinkEnduranceProducer/README.md),
set `packagePath` to `${package}`, identity package/source values to
`${packageSha256}` and `${commit}`, and `baselineFile` to `${run}/weather-lifetime.json`.
Equipment IDs, driver manifest version, credential binding, policy/form hashes
and applicability remain reviewed inputs. `${version4}` adds `.0` to a three-part
tag; use an explicit manifest version if the published package has another build
component. Explicit `--settings` execution consumes an already prepared probe;
this template expansion belongs to release discovery, not each scheduler tick.

Discovery waits for the exact package asset and digest, freezes the full private profile, downloads verified bytes, checks out the resolved public commit without hooks or submodules, expands and pins the per-release settings, then atomically registers the attempt. The ordinary worker advances it. Duplicate detection preserves an existing registration and checkpoint; it never overwrites completed evidence or resends an old submission. Workers sharing a registry must also share its storage and run locks; independent copies are not a distributed queue.

To validate discovery without starting tests, invoke it once against an unwatched registry:

```powershell
CrestronHomeDevTools.Automation.exe --intake-releases C:/CI/Private/release-profiles.json --registry C:/CI/Private/automation-registry.json
```

This only performs discovery, intake, source checkout and registration. The installed watcher will execute registered tests, so use an isolated unwatched registry when checking setup alone. A source-preview profile must be reviewed against the actual driver tests and device restrictions; filling placeholders does not manufacture coverage.

## Execution and recovery

For an existing Google Android emulator on a dedicated Windows test worker, see
[unattended Android setup](AndroidWorkerSetup.md). Its startup task is separate
from the evidence worker. Complete and verify the Home connection once; emulator
startup alone does not satisfy the app-test stage.

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

The later configured rehearsal completed deployment, Android, shortened endurance
and unsigned review without intervention after startup; see the current
[validation record](ValidationStatus.md) for its exact scope. Saved setup can
prepare Rehearsal and Submit profiles from reviewed executable templates, and the
complete archive includes its Windows form. Neither successful configuration nor
the rehearsal proves a new publication through real protected delivery. Configure
the actual protected service account and exact approvals before enabling Submit.
Named GitHub access and an independent operator run remain unvalidated integration
work. Existing submissions must not be resent to demonstrate controller progress.

Copyright (c) 2026 Neil Colvin. MIT licensed.

### Kasa and Tapo observation sample

The [Kasa/Tapo producer](../../samples/KasaTapoEnduranceProducer/README.md) uses
released public packages to observe a configured platform, explicitly selected
lasting managed children and independent physical outlets. Its source and
configuration contract are public. It does not provision devices or send controls;
read its documented limits before choosing it for a run. Temporary Android managed
children cannot be used after their automatic cleanup. Component hardware checks
have passed; the full Kasa release rehearsal is separate validation.
