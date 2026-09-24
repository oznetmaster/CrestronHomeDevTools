# Submission setup app

The Windows **CrestronHomeDevTools.Setup** app captures reusable input data before a submission starts. It is a desktop form application, not a web service: no GitHub login, Python knowledge or public form endpoint is involved. Profiles, credentials and signatures stay in the local encrypted DevTools private store.

This is the initial source implementation, not yet included in a published release or independently validated through a complete submission. It covers standard reusable input collection; driver-specific questions and new requirements may still arise. Input readiness is not test completion, authorization or Crestron acceptance.

## Start and edit

Build `CrestronHomeDevTools.Setup/CrestronHomeDevTools.Setup.csproj` on Windows with the .NET 10 SDK. Run its `CrestronHomeDevTools.Setup.exe` with the .NET 10 Desktop Runtime installed, or use `tools/BuildSubmissionSetup.ps1` to produce a self-contained Windows build. The full Visual Studio IDE is not required.

The default storage location is `%LOCALAPPDATA%\CrestronHomeDevTools\PrivateStore`. An optional `--store ABSOLUTE_DIRECTORY` selects a local store. The existing private-store API creates new stores with restricted NTFS permissions and Windows encryption. No existing profiles are migrated automatically.

1. **Developer:** save your name/company, correspondence details, public support methods, sender/server, named credentials, signature reference and optional equipment inventory. An email address is not required as a public support method when a website or telephone is provided.
2. **Driver:** save repository identity, manufacturer/models, help content and known limitations, test equipment and real-use restrictions. Empty support overrides inherit shared defaults. These are factual inputs, not assertions that tests passed.
3. **Credentials & signature:** save named SMTP, uploader, processor or Windows logins and a signature image using the existing encrypted store. Load a credential using its name, purpose and endpoint to edit it. Passwords are masked and cleared from the editor after saving. Endpoint/trust fields must be obtained and verified through the normal setup procedure; do not guess them.
4. **Submission:** select saved developer/driver profile names and enter the release version, exact four-component manifest version, release notes, source reference, private workspace and test/worker/app targets for the specific attempt.
5. **Readiness & snapshots:** check all saved input fields and named credentials, fix omissions, then create a uniquely named encrypted snapshot. The check does not connect to hardware or email providers, prove machine suitability, or validate a built package.
6. **Prepare review drafts:** generate help content, release notes and operation defaults from the chosen snapshot. The app displays the paths. These draft files contain readable selected facts, inherit the store's restricted access and are kept inside it. Passwords and signature images stay encrypted. Each preparation creates a fresh directory so reviewed edits are never overwritten.
7. **Prepare rehearsal profile:** optionally fill the Submission tab's automation fields before creating the snapshot: the reviewed settings template, tooling manifest, package asset filename and earliest release publication in UTC. This button captures the selected files, pins their bytes and prepares a fresh release profile and empty registry for the public automation worker. It does not start that worker or operate equipment. Its report lists missing stage bindings; resolve those before claiming a full rehearsal.

Save incomplete drafts at any time. To edit later, select the profile name, click **Load / reload**, make changes and **Save**. A changed name saves a separate profile. Existing names require loading their current revision before overwriting; another editor's newer revision cannot silently be lost. Unsaved profile changes prompt before replacement/exit.

Snapshots cannot be edited or overwritten. Later profile changes affect a new snapshot, not a previous submission. Saved credential references remain names: credential rotation does not copy old passwords into snapshots. Exact signing and delivery approvals remain separate and bind the final artifacts.

## Public workflow access

Console commands expose safe summaries and prepared-file paths, never passwords or signature bytes:

```powershell
CrestronHomeDevTools.Console.exe submission-setup list --kind run
CrestronHomeDevTools.Console.exe submission-setup check --run example-release
CrestronHomeDevTools.Console.exe submission-setup snapshot --run example-release --name example-release-attempt1
CrestronHomeDevTools.Console.exe submission-setup prepare --snapshot example-release-attempt1
```

A C# workflow reads the frozen inputs directly and uses the same named credential bindings already supported by signing, delivery and processor tools:

```csharp
var store = DevToolsPrivateStore.Open();
var snapshot = store.LoadSetupProfile<SubmissionSetupSnapshot>("example-release-attempt1").Value;
var developer = snapshot.Developer.Value;
var driver = snapshot.Driver.Value;
var attempt = snapshot.Run.Value;
var supportUrl = snapshot.SupportWebsite; // per-driver override or developer default
var bindings = store.GetSubmissionSetupBindings("example-release-attempt1");
var defaults = store.GetSubmissionSetupOperationDefaults("example-release-attempt1");
var prepared = store.PrepareSubmissionSetupInputs("example-release-attempt1");
// Do not log these private objects, publish them, or infer test results from them.
```

Existing processor, signing, delivery and endurance commands that accept `--credentials` also accept the absolute encrypted `snapshot-NAME.setup` path returned by preparation. No separate bindings JSON or credential environment variable is needed. Purpose, host, port and sender checks still apply. Existing JSON bindings remain supported. A developer, driver or editable run profile cannot be passed as a credentials snapshot.

The help draft uses the documented `submission build-help` content format. Pass `HelpContentPath` as its `--content` input in draft mode. It includes public support methods and the repository URL; it excludes the private correspondence address, postal address, equipment restrictions, passwords and signature. Release-note drafts include the same support methods and repository link. Review public text that you enter into help fields; the app cannot automatically identify confidential prose.

The draft retains pending items for model/support verification, actual test environment and UI screenshots, plus missing requirements, licensing or release notes. Do not remove these until resolved. Final help generation rejects unresolved pending items. A saved equipment description does not demonstrate a completed test.

`operation-defaults.json` provides the form title/author, sender/SMTP endpoint, processor address, Android target and encrypted credentials path. The public `GetSubmissionSetupOperationDefaults` method returns the same typed values without a file. Use these when composing the existing stage settings; do not publish this private defaults file. Package hashes, evidence mapping, generated-document identities and provider receipts must still come from the actual workflow. The app does not invent those values, approvals or checklist results. The standalone submission starting document should receive the store/run or snapshot name alongside the driver repository URL.

User-scoped stores cannot simply be copied to another computer or runner account. Use the existing explicit credential provisioning mechanisms for selected secrets; provision factual profiles separately under the intended account using the public save/load APIs. Do not switch to shared plaintext files or broaden access automatically. Inventory selection and hardware leases remain the responsibility of the executing workflow.

## Prepare a controller rehearsal

The automation worker exposes the same preparation as the form:

```powershell
CrestronHomeDevTools.Automation.exe --prepare-rehearsal --store C:\Private\SubmissionSetup --snapshot example-release-attempt1
```

Or call `SubmissionAutomationSetup.PrepareRehearsal(store, snapshotName)` from the
public automation assembly. Exit 0 means stage bindings are present; exit 3
returns the prepared paths and lists omissions. Neither result proves that the
equipment, credentials, tests or evidence have been validated.

The prepared profile derives its repository, private run workspace, review
title and author from the frozen facts. Release discovery supplies the actual
version in the title. The executable template must already select the same
processor and `${source}` checkout; preparation refuses a mismatch instead of
silently retargeting equipment. Test fixtures, device IDs, trust pins, endurance
criteria, evidence mappings and package support-metadata requirements remain
explicit reviewed template inputs. They cannot be inferred from contact details.

Preparation always selects **Rehearsal** and omits protected signing/delivery
settings. It preserves the template's selected evidence-worker credential
binding; it never substitutes the setup snapshot containing all credentials.
The saved snapshot freezes factual revisions and file selections; preparation
captures the current selected file bytes and records their hashes in
`setup-provenance.json`. Later edits do not modify an already prepared profile.

Outputs stay in a fresh restricted child of the setup store. Do not grant an
evidence service access to the entire store to read these files. When running
under another account or machine, provision only the prepared configuration and
its selected test credentials through the worker setup procedure, with paths
reviewed for that destination. The form does not install or provision a worker.
Follow [Rehearsal](Rehearsal.md) and [AutomationWorker](AutomationWorker.md) for
intake, execution and recording interventions.

## Validation

Focused tests cover encryption, editable draft round trips, stale-write refusal, tamper/name mismatch detection, readiness without secret leakage, endpoint mismatch, support overrides and immutable snapshot retention. Tests also exercise the existing processor, delivery and signing input consumers with an encrypted snapshot, including endpoint rejection and signature-buffer cleanup, without network operations. Prepared public drafts are checked for private-data exclusion. A generated synthetic draft has also been passed through the existing help builder: draft generation succeeds and final generation rejects its unresolved review items. The Developer and Driver forms have been opened and visually inspected on Windows. A full submission starting from this app remains to be demonstrated.
