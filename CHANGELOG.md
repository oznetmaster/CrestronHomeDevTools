# Changelog

## Unreleased

Added source APIs for final actual-driver removal validation and read-only processor error-log comparison. Removal checks the exact device tree and unrelated inventory, accepts a caller-supplied app observer, and retains a one-shot journal. The log reader has been checked against real CP4-R output; actual removal with Android and unattended review integration remain unverified. See [the API contract and validation limits](docs/DriverRemovalValidation.md).

- Automation plans can run separately configured functional checks after endurance and before preparing review. The worker preserves both phases, binds the later checks to the completed interval, and stops on failed or uncertain restoration. This has offline regression coverage; hardware validation of the new stage is pending.
- Those post-endurance checks can obtain the installed device and catalogue IDs from the workflow's verified deployment receipts, avoiding manual ID updates and assumptions about catalogue version formatting. Expected identity and room constraints remain enforced, and the resolved plan is retained with the evidence.
- Saved automation review plans can include source-based N/A decisions before a release's package hash exists. The worker checks the pinned source files against the verified release checkout, retains the dated review and binds the decisions to the actual candidate. Changed source requires a new review. Runtime-dependent decisions and passing test results cannot use this route.
- Planned, scoped limitations such as shortened rehearsal endurance can also be saved before release. They are bound to the discovered candidate for declared-gaps review, retain the original policy and test outcomes, and do not authorize signing or delivery.

## 1.19.0 - 2026-09-25

This release adds a preview of the persistent release-to-submission workflow. An opted-in private Windows worker discovers published GitHub driver releases, verifies the package and source, runs configured Windows/processor/Android tests, collects endurance evidence, prepares the review packet and hands off to separately authorized signing and delivery. It resumes recorded waits without an AI heartbeat. Ordinary driver releases and library/client releases remain independent of submission.

The complete Windows console download includes the automation worker and a self-contained setup app at `setup/CrestronHomeDevTools.Setup.exe`. The form saves editable developer, driver, equipment and submission information in the encrypted private store. It can prepare Rehearsal or Submit profiles from frozen snapshots. Rehearsal stops at unsigned review; Submit still requires an independently configured protected worker and exact signing and delivery approvals. Saving a signature or preparing a profile does not authorize its use.

The workflow preserves original test results, failed attempts, immutable candidate identities and disclosed qualifications. Temporary processor test instances are cleaned up by the NUnit workflow. Per-release deployment identifiers connect automatically to Android tests and endurance collection. Review preparation verifies retained Android producer evidence before composing the checklist. Repeated release discovery does not restart a failed attempt or resend a completed submission.

An explicit `endurance-stop` command can end an idle incomplete collection and release its reservation while preserving its original plan and samples. It records the incomplete result as `operator-stopped`, not passed. Pending operations and uncertain ownership still require inspection.

**Verified scope:** one configured hardware rehearsal completed release intake, fresh deployment, Windows/processor/live/Android tests, cleanup, a disclosed one-hour endurance interval, evidence export and unsigned PDF preparation without intervention after startup. Separate bundled acceptance tests exercised document signing and upload-before-email sequencing using synthetic authority and test providers. Earlier actual uploads and SMTP deliveries used the public APIs with operator assistance. A newly published release through real protected delivery, and an independent operator starting solely from the single-document guide, have not yet been demonstrated. Submission delivery never establishes Crestron acceptance or certification.

Start with [the operator guide](docs/submission/START-DRIVER-SUBMISSION.md), [setup app](docs/submission/SetupApp.md), [worker installation](docs/submission/AutomationWorker.md) and [validation boundaries](docs/submission/ValidationStatus.md). Configure actual C# fixtures, equipment restrictions, credentials and official forms before enabling a profile. Public-repository discovery supports anonymous GitHub access; named GitHub authentication remains a separate setup integration. No Python or Linux knowledge is required to use the complete Windows bundle.

Copyright (c) 2026 Neil Colvin. MIT licensed. Crestron and Crestron Home are trademarks of Crestron Electronics, Inc. This project is independent and is not affiliated with, endorsed by or sponsored by Crestron Electronics, Inc.

## Unreleased

The complete console archive now includes the self-contained Windows setup app under `setup/`. Release validation requires that executable and checks its version matches the console, so profile collection does not require building the form from source or installing a separate Desktop Runtime.

Saved setup can prepare a Submit-mode controller profile through the Windows form, `--prepare-submission` command or public API. It requires a submission-purpose snapshot, preserves explicitly configured protected-stage references and reports missing bindings. Release intake supports the same `${runKey}` approval-path placeholder as the protected worker. Preparation saves configuration only; it neither exports secrets nor authorizes or starts signing, upload or email.

Integration checks now exercise the production worker's combined release-discovery and dispatch cycle, including publication before asset upload, automatic registration, durable continuation and refusal to restart failed or review-ready runs on duplicate discovery. GitHub and domain operations in these checks are simulated; see the validation guide for the separate completed hardware rehearsal.

Automatic review preparation now connects completed Android coordinator receipts to the raw-evidence audit for both combined and separate app-test routes. It preserves pre-execution pins, verifies retained files and release identity, and keeps explicit independent bindings when supplied. Missing or changed evidence stops preparation instead of generating replacement proof.

The WeatherLink Android fixture now writes the camel-case evidence schema required by the public composer and references captures under the actual combined-deployment or separate-app output directory. Integration regressions cover both routes and passing/failing observations without rewriting their results. Background-worker exception status identifies the affected stage; observers must inspect worker attention status even when the durable operation checkpoint remains Running for recovery.

An explicit `endurance-stop` command and public monitor API can end an idle collection and release its reservation while retaining its original plan and samples. An incomplete interval is recorded as `operator-stopped`, never converted to a pass. Pending probes and uncertain ownership still require inspection.

An optional deployment-to-endurance binding now derives the probe's device and catalogue IDs from retained, verified NUnit release-deployment receipts. Intake preserves a pinned template; the final producer is prepared before collection and reused on recovery without rewriting frozen settings or changing the test criteria. Existing installed-candidate runs keep their current behavior. This route passed the configured release-to-review hardware rehearsal documented in the validation guide.

The combined release-deployment route now supplies retained fixture inputs before NUnit starts. The WeatherLink app sample can use the newly deployed instance ID from the public Android context, without a manual settings edit. Existing-instance tests still require an exact ID. Changes or loss of fixture inputs prevent recovery from reporting a pass. This route passed the configured release-to-review hardware rehearsal documented in the validation guide.

Saved setup now has an explicit rehearsal purpose in the Windows form, console and public APIs. Rehearsals can prepare unsigned testing/review inputs without mail, uploader or signature credentials; their snapshots expose only testing credential bindings. Full submission readiness remains the default, and existing public method signatures are preserved.

Release templates can use `${reservationId}` for a stable, per-release endurance reservation GUID. Invalid descriptive reservation names now fail before hardware tests instead of after Windows, processor and app checks. Windows-worker instructions also cover long-path support and the shorter paths needed by the Framework test host.

Explicitly reviewed nonpassing records can travel through the controller with their original limitations and supporting files. They remain nonpassing and need separately pinned gap declarations; the handoff cannot import passing results. Prior-evidence validation now permits unused reuse permissions elsewhere in an original policy, while still rejecting carried-forward observations and changed selected scopes.

The controller can retain candidate-specific N/A decisions and their source evidence through an explicit applicability input. It verifies policy permission, rationale and candidate identity before hardware tests, and includes the unchanged decisions in review composition. This input accepts no test passes and cannot hide conflicting producer results.

The automatic review workflow can retain a pinned inventory of original evidence and scoped change-impact decisions during candidate validation, then import it through the public prior-evidence API. Earlier passes remain `ReviewedPriorPass`; original records and unrelated failures are preserved. Missing or changed retained files stop continuation, and conflicting fresh results cannot be replaced by an older pass. This closes a manual handoff for updated candidates; full hardware rehearsal remains pending.

Separate installed-app tests can receive a frozen `InstalledAppFixtureSettings` object, retained before invocation and verified on completion and recovery. The public C# WeatherLink Android fixture consumes those inputs, checks visible values against their named UI rows, exercises forecast navigation and both close transitions, and retains standard scoped observations. It preserves uncertain input outcomes without repeating taps. Fresh generalized hardware validation is still pending.

A public C# WeatherLink endurance-producer sample replaces embedded equipment/version constants with pinned settings and named encrypted processor credentials. It acquires and retains its lifetime baseline automatically outside the immutable program bundle, refuses changed/interrupted baseline reuse, and includes standalone synthetic checks. Release discovery can render a pinned producer-settings template into a new per-release publication, verify its complete inventory and bind its producer ID without manual copying. The generalized hardware rehearsal remains pending.

The saved-setup app and public automation API can prepare a fresh rehearsal release profile and registry from a frozen snapshot and reviewed executable template. Preparation captures template/tooling hashes, reuses saved review identity, rejects processor mismatches, and lists incomplete stage bindings. It neither starts tests nor exports credentials; rehearsal profiles omit protected signing/delivery settings.

Review preparation now includes retained observations from the separate installed-app stage, with its completed receipt, workflow identity and file inventory verified. Previously that handoff accepted only the earlier combined NUnit inventory. A read-only `--check-settings` command lists missing stage bindings before a full rehearsal; it performs no tests or provider operations.

The automation worker can run a separate app-test phase through the public installed-driver NUnit API, without redeploying the existing candidate or repeating Windows/processor tests. The phase pins the release and processor, preserves raw results, requires restoration and cleanup, and stops for inspection after an uncertain interruption. Actual LocalService Android capture passed on the headless Windows worker; app restart recovery exposed a nonresponse dialog, so full unattended app reliability remains under validation.

Android startup now starts ADB before opening emulator logs. Previously, ADB could inherit a log handle and prevent log rotation on the next emulator launch. Startup retains failure diagnostics, and the installer accepts explicit CPU and memory allocations for the assessed worker.
An optional configured app activity opens after Android boot. Startup failure stops the launcher's own emulator process tree, preventing an orphan QEMU process behind a failed Windows task. A bounded local log-rename retry handles closing process handles without replaying app or device commands.

The source-preview console archive now includes the self-contained automation worker and Windows startup installers. An existing Google Android emulator can start headlessly at boot under its owning account without a saved Windows password. Initial Home agreement/connection setup remains explicit; a running emulator does not count as passing app tests. See [unattended Android setup](docs/submission/AndroidWorkerSetup.md).

Successful console builds remove their temporary runtime staging directory after smoke checks, retaining failed staging for diagnosis. Signed-review failures now report the error category and code location without disclosing exception text, private paths or signature contents.

The source-preview GitHub automation template offers Rehearsal (default) and Submit for a registered release. The worker enforces the frozen mode on execution and recovery: rehearsal stops before signing or delivery; Submit still requires exact artifact authorizations. Dispatch selects a protected local registration instead of accepting commands or arbitrary private paths. An optional private Windows watcher discovers published public-repository releases, freezes their package/commit and settings, and registers them automatically. An explicit publication cutoff excludes older releases; delayed package uploads wait and duplicate events preserve the same attempt.

The persistent worker now binds public NUnit, endurance, review preparation, encrypted-signature use, upload/verified-link/email delivery and final retention. It preserves domain receipts, waits for exact signing/delivery authority and does not replay uncertain provider operations. Evidence and protected work use separate roles; actual Windows permissions and credential stores must enforce that separation. The Windows startup installer resumes waits without AI reminders, keeps bounded history and passed an actual empty-registry reboot test. Live GitHub intake and a real Windows/processor rehearsal passed; the document chain passed with synthetic authority and fake providers. Full real end-to-end validation and setup integration remain pending. See [release automation](docs/submission/ReleaseAutomation.md), the [worker](docs/submission/AutomationWorker.md) and the [single starting document](docs/submission/START-DRIVER-SUBMISSION.md).

Workflow checkpoints, endurance ownership journals and saved setup/connection profiles now use the existing bounded Windows file-replacement handling. Temporary sharing/access refusals retry only the same local rename, retaining the original state until it succeeds; no processor operation, upload or email is replayed. Permanent denial still fails without changing permissions.

A Windows form app saves editable developer, driver and submission inputs in the encrypted private store, including named credentials and a reusable signature image. Frozen snapshots retain the chosen profile revisions. Existing processor, signing, delivery and endurance credential consumers can read a snapshot directly, and preparation generates help/release-note drafts with public support contacts and the repository link.

Drafts retain unresolved review items; saved inputs are not test results or signing/delivery authorization. This is available from source and has not yet been validated through a complete submission. See [setup and workflow integration](docs/submission/SetupApp.md). No public package release is associated with this entry.
Reviewed submission emails can preserve independently approved wording through an optional correspondence override. The package download URL is inserted only from the confirmed upload receipt; changes to the body invalidate approval. Existing generated correspondence remains the default. See [review delivery](docs/submission/ReviewDelivery.md).

Self-test forms mark entirely non-applicable items simply N/A, without an appended note or link. Notes for checked and qualified items remain consecutively numbered and linked. Applicability evidence and checkbox decisions are unchanged.

## 1.18.2 - 2026-09-23

Evidence mapping now accepts the camelCase worker files used by the endurance console, as well as retained PascalCase API worker files. Previously, mapping a completed console run could fail with a generic input error. Both forms retain their original bytes and hashes; strict schema, identity, producer and measured-result validation remain enforced. No collector restart or evidence rewrite is needed.

Successful endurance scheduler and notification checks now remove temporary diagnostics after saving current status and compact history. History rotates at 1 MiB with one previous file retained. Failed and interrupted attempts keep their original output; actual collector samples and evidence criteria are unchanged. Repeated healthy notification observations update the current checkpoint without creating duplicate event files. Delivery transitions and unresolved-send safeguards remain retained.

Completed-run retention documents disabling the exact terminal collector and watcher tasks and waiting for their current invocations to finish before copying evidence. Leave active collectors on their recorded tooling; use the new mapper separately against retained evidence.

Validation covers both worker formats without byte rewriting, original identity and evidence rejection, 1,440 successful diagnostic-retention cycles, interruption recovery and notification transitions. See [evidence mapping](docs/submission/EvidenceMapping.md) and [completed-run retention](docs/submission/WindowsEnduranceWorker.md#retain-a-completed-run).

Copyright (c) 2026 Neil Colvin. MIT licensed. Crestron and Crestron Home are trademarks of Crestron Electronics, Inc. This project is independent and is not affiliated with, endorsed by or sponsored by Crestron Electronics, Inc.

## 1.18.1 - 2026-09-23

Completed endurance runs can now be retained when their scheduler history exceeds 20,000 filesystem entries. A normal 24-hour run can reach that limit because scheduler diagnostics are recorded more often than functional samples. The exporter now defaults to 100,000 entries and supports an explicit `-MaximumEntries` bound up to 1,000,000, recorded in its receipts. File-size limits, evidence hashes, source/copy revalidation and refusal to overwrite partial exports remain enforced. No evidence is removed to fit the limit.

Passive health checks now recognize the scheduler's lock-contention exit code 4 after a completed, released collection. An exporter holding the scheduler lock no longer creates a task-failure alert solely from that code. Completed receipts must still pass the existing identity, duration, sample, freshness and attention checks. Unexpected task failures and contention during collection still require attention.

This corrects evidence retention and operational monitoring, not driver behavior or test criteria. Use a fresh export directory after inspecting any earlier failure. The exporter can retain an older pinned run using its original tick script and CLI; do not replace those recorded inputs. PowerShell 7.6 or later is required on the exporting Windows computer.

Validation covers inventory-budget refusal and successful fresh retention without lost diagnostics, completed-run contention, real failure/identity/freshness rejection and notification regressions. These checks use synthetic inputs and do not contact processors or send email.

See [completed-run retention](docs/submission/WindowsEnduranceWorker.md#retain-a-completed-run) and [PowerShell installation](docs/PowerShell.md).

Copyright (c) 2026 Neil Colvin. MIT licensed. Crestron and Crestron Home are trademarks of Crestron Electronics, Inc. This project is independent and is not affiliated with, endorsed by or sponsored by Crestron Electronics, Inc.

## 1.18.0 - 2026-09-22

Windows automation now requires PowerShell 7.6 or later. Resource assessment, OpenSSH and runner setup, remote Windows credential transfer and endurance observation use PowerShell 7 and reject older engines before performing their operation. New endurance scheduled tasks use the PowerShell 7 executable that registers them.

Install PowerShell for all users on each Windows automation host before upgrading. The standard MSI installation places it in `C:\Program Files\PowerShell\7\pwsh.exe`; remote Windows accounts also need `pwsh.exe` on their command path. See [installation and migration](docs/PowerShell.md). Windows PowerShell 5.1 is no longer a supported automation engine for this release. Processor configuration APIs themselves do not require PowerShell.

Local endurance observation now passes its readable script directly to PowerShell instead of using the compressed remote-command wrapper. Its integration test requires an actual task-status result, so a blocked process cannot satisfy a missing-task check.

Leave active endurance runs on their recorded tooling and task configuration until they finish. Installing the prerequisite does not migrate a running task, and this release does not require repeating earlier validated endurance evidence.

Validation covers version refusal before execution, Windows setup/observation/credential-transfer regressions and the scheduler, notification, export, health and directory-permission scenarios on PowerShell 7.6. These checks use synthetic inputs and do not operate processors or send email.

Copyright (c) 2026 Neil Colvin. MIT licensed. Crestron and Crestron Home are trademarks of Crestron Electronics, Inc. This project is independent and is not affiliated with, endorsed by or sponsored by Crestron Electronics, Inc.

## 1.17.5 - 2026-09-21

Self-test review PDFs now retain the explanations attached to verified passing observations. Previously, checked items could omit important context such as whether configuration was verified through an API or by visually inspecting a dialog. The numbered notes now include that context in both complete and declared-gaps reviews, printing repeated identical explanations once per official item.

Checkbox decisions, evidence validation and signing/delivery approvals are unchanged. A claimed pass that fails verification remains incomplete and its explanation is not presented as a verified observation. Existing signed forms are not modified. Review any new form and its explanations before authorizing it.

Regression tests reproduce the missing text in generated PDFs, then verify its preservation, deduplication and unchanged checkbox values. They also confirm that an insufficient-duration claim stays unchecked and is not presented as verified evidence. See [form generation](docs/submission/FormGeneration.md) and [declared-gaps reviews](docs/submission/DeclaredGaps.md).

Copyright (c) 2026 Neil Colvin. MIT licensed. Crestron and Crestron Home are trademarks of Crestron Electronics, Inc. This project is independent and is not affiliated with, endorsed by or sponsored by Crestron Electronics, Inc.

## 1.17.4 - 2026-09-21

Protected JSON input now works when Windows PowerShell 5.1 supplies a UTF-8 byte-order marker. Previously, the same saved-credential import or remote endurance observation could succeed from PowerShell 7 and fail before making a connection from Windows PowerShell 5.1. Redirected input is now decoded as UTF-8, preserving non-ASCII credential values, and the private JSON readers accept one leading marker.

The correction covers credential import, runner setup, endurance observation/watch/notification and both submission delivery commands. Existing input bounds, private-buffer cleanup, endpoint checks, evidence checks and exact signing/delivery approvals are unchanged. It does not change driver behavior or require restarting an active endurance run.

Validation includes the actual credential-import executable with marked and unmarked UTF-8, non-ASCII synthetic credentials, encrypted-store verification, and command tests for observation, notification and delivery. The original Windows PowerShell 5.1 reproduction now succeeds using dummy data. No real credentials or external providers were used by these regression tests.

See [reusable private inputs](docs/PrivateInputs.md). Use the updated console for new setup or observation commands; leave active collectors on their pinned tools.

Copyright (c) 2026 Neil Colvin. MIT licensed. Crestron and Crestron Home are trademarks of Crestron Electronics, Inc. This project is independent and is not affiliated with, endorsed by or sponsored by Crestron Electronics, Inc.

## 1.17.3 - 2026-09-21

The supplied GitHub workflow templates now support signing and preparing delivery of a submission with explicitly declared gaps. Previously, the review template rejected that signing mode even though the public console supported it, and the dispatcher did not expose its required inputs.

The dispatcher accepts an explicit review mode and independently reviewed declarations digest. Declared-gap delivery preparation verifies the exact signed-review plan and separate correspondence approval; final delivery still revalidates all evidence before upload and email. Complete mode remains the default. No omitted test becomes a pass, and the workflow does not determine whether Crestron will accept a submission.

Copy the matching updated templates together and follow [the declared-gap workflow sequence](docs/submission/WorkflowSetup.md#signed-submissions-with-declared-gaps). The console commands already existed; this patch changes the distributed templates and documentation. It also distinguishes the remote observer's credential input from the combined notification command, with an optional named-credential alternative.

Validation exercises the actual complete and declared-gap template sequences against the packaged console, including rejected mismatched pins, synthetic signatures and simulated upload/email providers. Dispatcher contract checks also pass. These checks do not validate a particular developer's GitHub permissions, service account or real provider delivery. No driver runtime changes or active-monitor upgrades are required.

Copyright (c) 2026 Neil Colvin. MIT licensed. Crestron and Crestron Home are trademarks of Crestron Electronics, Inc. This project is independent and is not affiliated with, endorsed by or sponsored by Crestron Electronics, Inc.

## 1.17.2 - 2026-09-21

Preparing a new Windows endurance monitor no longer requires writing a custom folder-permission script. The console ZIP includes the optional `Set-EnduranceDirectoryPermissions.ps1` helper: preview the dedicated directory, then use `-Apply` to make the reviewed change.

The helper preserves full access for the directory owner, Administrators and SYSTEM; LocalService receives read/execute on inputs and modify access only to the empty run and scheduler-state directories. It handles pre-existing files as well as future inherited permissions, removes unrelated access, and verifies the resulting rules. Preview makes no changes. Used runs, escaped or overlapping output paths, system/profile roots and reparse points are refused before mutation.

Six permission checks pass on Windows PowerShell 5.1 and PowerShell 7, including inherited broad-user removal, unchanged file contents, new-file access and refusal behavior. These checks do not impersonate LocalService or contact a processor; validate the real scheduled invocation in the consuming environment.

See [Windows endurance worker setup](docs/submission/WindowsEnduranceWorker.md). The helper does not copy credentials, install services, contact a processor or change an active collector. Keep existing runs on their pinned tools. No driver runtime behavior changes.

Copyright (c) 2026 Neil Colvin. MIT licensed. Crestron and Crestron Home are trademarks of Crestron Electronics, Inc. This project is independent and is not affiliated with, endorsed by or sponsored by Crestron Electronics, Inc.

## 1.17.1 - 2026-09-21

Fix an unhandled exception in passive Windows endurance observation. When the task-status reader failed or returned malformed output, its `InvalidDataException` escaped the observer and could leave a Windows application-error dialog. The observer now returns a fresh, run-bound `observer-query-failed` attention report and exit code 3. It does not reuse old healthy status, retry the query, restart a collector or contact the processor.

The console also handles otherwise uncaught invalid-data errors with a concise nonzero result. Regression coverage includes the actual local observer command, failed and malformed snapshot parsing, and delivery of an attention report through a simulated notifier. An independent synthetic reproduction of the original failure now terminates normally.

The included GitHub signing and delivery workflow examples now support the named encrypted inputs introduced in 1.17.0. The optional GitHub environment variable `CRESTRON_SUBMISSION_CREDENTIAL_BINDINGS` selects this saved-input mode in the supplied workflow templates. Set it to the absolute path of the private bindings file on the runner; it is not required by DevTools itself. The variable contains only a file path, never passwords, tokens or signature data; the bindings select entries in the local encrypted store. Direct CLI callers can pass the path with `--credentials` instead. Existing image-file and protected-stdin inputs remain supported. Mixed delivery credential sources are refused. See [workflow setup](docs/submission/WorkflowSetup.md) and [private inputs](docs/PrivateInputs.md).

Update the independent observer installation after stopping and inspecting any failed observer invocation. Keep a running collector on its existing pinned bundle; this fix does not require restarting its endurance period. Windows task permissions, service credential access and provider delivery still need validation in the consuming environment.

Copyright (c) 2026 Neil Colvin. MIT licensed. Crestron and Crestron Home are trademarks of Crestron Electronics, Inc. This project is independent and is not affiliated with, endorsed by or sponsored by Crestron Electronics, Inc.

## 1.17.0 - 2026-09-21

DevTools can now collect private inputs once and reuse named encrypted entries across processor commands, monitoring, signing and delivery. Windows setup and resource assessment help prepare one or more development and monitoring computers.

- Add Windows DPAPI private stores for processor, Windows, SMTP and uploader logins and signature images. Purpose, endpoint and trust checks bind each use. Provision selected entries to restricted service stores or another Windows computer over pinned SSH; do not copy user-encrypted files between accounts or machines.
- Add `credentials verify` for an access check under the actual service identity. Stored signatures feed both signing commands through a pipe without decrypted temporary images; exact-form signing approval remains required. Existing processor profiles and protected stdin integrations remain supported.
- Add scheduled endurance observation with named credential bindings, persistent notification journals, startup/resume support, input pins and overlap protection. The observer does not modify or restart its collector. Authorize the SMTP destination and test delivery before relying on alerts.
- Add named resource inventories and read-only Windows workload assessment. Planned resources cannot be selected. Recorded roles, installed software and a logged-in desktop are distinguished from verified capabilities and representative workload results.
- Add reviewed OpenSSH prerequisite setup with restart handling, and new GitHub runner/service setup with pinned archives, private registration-token input, collision checks and inspection after uncertain outcomes. Existing runners are not replaced, and registration is not automatically replayed.

See [private inputs](docs/PrivateInputs.md), [Windows resources](docs/WindowsResources.md), [OpenSSH setup](docs/WindowsSetup.md), [GitHub runner setup](docs/WindowsRunnerSetup.md) and [endurance notifications](docs/submission/EnduranceNotifications.md).

Validation includes the discovered .NET suite, isolated console signing/delivery acceptance checks, synthetic scheduler processes, a real two-computer synthetic credential transfer and a LocalService access-isolation rehearsal. The new installers have not yet completed a real clean-machine OpenSSH installation/reboot or new GitHub runner registration/service installation. Those limits are explicit in their guides. A completed local service setup does not establish GitHub online status, desktop access, workload readiness or Crestron acceptance.

This release does not change a driver or automatically migrate credentials, install services, change desktop logon, sign documents or send messages. Keep a running collector on its pinned tooling; install a new observer bundle separately.

Copyright (c) 2026 Neil Colvin. MIT licensed. Crestron and Crestron Home are trademarks of Crestron Electronics, Inc. This project is independent and is not affiliated with, endorsed by or sponsored by Crestron Electronics, Inc.

## 1.16.3 - 2026-09-20

Dependency-notice preparation now accepts merged DLL filenames containing spaces. Previously, a valid filename such as `Example Library.dll` failed validation before its reviewed notices could be staged.

The inventory still requires plain filenames and exact dependency hashes. Paths, control characters, missing or additional DLLs, and changed packaged notice text remain rejected. Regression tests cover staging and package verification with a spaced filename, including rejection after the dependency changes.

See [reviewed dependency notices](docs/submission/DependencyNotices.md) for the inventory and packaging workflow. This fixes desktop packaging tools; it does not change driver runtime behavior.

Copyright (c) 2026 Neil Colvin. MIT licensed. Crestron and Crestron Home are trademarks of Crestron Electronics, Inc. This project is independent and is not affiliated with, endorsed by or sponsored by Crestron Electronics, Inc.

## 1.16.2 - 2026-09-20

Failed installation attempts now retain the processor's preparation or commissioning reply so developers can investigate without repeating the command merely to recover its result.

- The console saves the reply beside its private lease receipt as `<owner>.failure.json` and prints only the file location. Existing records are never overwritten; a diagnostic write failure preserves the original processor failure.
- C# callers can retain `ProcessorApiException.DiagnosticCommand` and `DiagnosticResponse` in their own private run journal.
- Malformed commissioning IDs are reported through the same diagnostic path. Installation still requires an explicit success result and a positive integer device ID; no request is retried and uncertain operations retain their reservation.
- Clarify the SSH fingerprint format, obtaining the complete ManifestUtil NuGet distribution, and collecting preparatory help screenshots before freezing a candidate.

See [deployment and activation](docs/UserGuide.md#deploy-and-activate), [library diagnostics](docs/LibraryGuide.md#advanced-commands-and-failures), and [help preparation](docs/submission/HelpBuild.md). Processor responses may contain private data and must remain outside public source and CI artifacts. This release changes desktop diagnostics, not driver runtime behavior or Crestron's acceptance criteria.

Copyright (c) 2026 Neil Colvin. MIT licensed. Crestron and Crestron Home are trademarks of Crestron Electronics, Inc. This project is independent and is not affiliated with, endorsed by or sponsored by Crestron Electronics, Inc.

## 1.16.1 - 2026-09-20

Generated self-test forms now identify the driver and developer on the first page, so the checklist remains identifiable without turning to its notes.

- Print the supplied submission title and developer name in the first page's upper margin. Use the driver name and version in the title.
- Preserve the official printed content, interactive fields, checkboxes and numbered notes. Signing retains the identification heading.
- Continue to require approval of the exact resulting PDF; approval of an earlier form does not authorize a revised document.

See [form generation](docs/submission/FormGeneration.md). The developer uses the existing title and author inputs; no new configuration or separate document tool is required. This change makes no claim about Crestron acceptance or certification.

Copyright (c) 2026 Neil Colvin. MIT licensed. Crestron and Crestron Home are trademarks of Crestron Electronics, Inc. This project is independent and is not affiliated with, endorsed by or sponsored by Crestron Electronics, Inc.

## 1.16.0 - 2026-09-20

Submission checklists now place numbered, linked notes after the unchanged official form. Reviewers can record a scoped interpretation of retained evidence without rewriting the original automatic findings.

- Add `SubmissionInterpretationReview` and the `AcceptedInterpretation` assessment status. Each decision requires a named reviewer, an explanation and retained evidence. Missing, failed and unperformed tests cannot be accepted through this route.
- Show accepted interpretations as checked items with explicit notes. Original outcomes and validation issues remain in the evidence archive, and the review retains its disclosed-qualification status.
- Bind interpretation decisions to the exact form, declarations, signing authorization and delivery checks. A review decision does not authorize signing or sending.
- Keep the official checklist first, followed by numbered notes with links in both directions. Entirely non-applicable items remain labelled N/A; unperformed items remain unchecked.
- Include the notes module in the self-contained console so developers need no separate Python installation or script changes.

See [reviewing declared gaps and interpretations](docs/submission/DeclaredGaps.md) and [form signing](docs/submission/FormSigning.md). These are reviewer decisions against our interpretation of the requirements, not a prediction of Crestron acceptance or certification. Driver-specific submissions remain private.

Copyright (c) 2026 Neil Colvin. MIT licensed. Crestron and Crestron Home are trademarks of Crestron Electronics, Inc. This project is independent and is not affiliated with, endorsed by or sponsored by Crestron Electronics, Inc.

## 1.15.0 - 2026-09-20

Submission reviewers can now retain relevant passing evidence from an earlier candidate through an explicit, scoped change-impact review. Original package identities, execution times, measurements and failures remain intact; a reviewed earlier pass is never labelled as a new test run.

- The C# `SubmissionPriorEvidence` API and `submission-import-prior-evidence` console command validate the original evidence, unchanged assertion requirements and independently pinned review decisions.
- Forms distinguish fresh results from reviewed earlier evidence. Portable bundles retain and revalidate the original records and supporting change analysis.
- Form items can be checked when all applicable subconditions pass and optional absent controls have validated non-applicability explanations. Entirely non-applicable items remain unchecked; incomplete applicable checks still prevent a checkmark.
- Unchecked official-form items now have a visible margin label: `N/A` for non-applicable items or `Notes` for declared qualifications, with details in the companion matrix. The original printed form and checkbox values are preserved.
- Failed, partial, inconclusive and unperformed checks cannot be promoted through this path. Changed evidence or missing provenance cannot be waived as a declared gap.

See [reviewing prior evidence](docs/submission/PriorEvidence.md) for the C# and console workflow. The reviewer remains responsible for assessing affected code and dependencies. These checks do not predict Crestron acceptance or certification. Driver-specific submission files remain private.

Validation covers prior-evidence import, changed assertions, invalid original measurements, tampered records, form checkboxes and portable bundle verification after the original evidence directory is moved.

Copyright (c) 2026 Neil Colvin. MIT licensed. Crestron and Crestron Home are trademarks of Crestron Electronics, Inc. This project is independent and is not affiliated with, endorsed by or sponsored by Crestron Electronics, Inc.

## 1.14.0 - 2026-09-20

Submission reviews with declared test gaps can now proceed through approved form signing and delivery. Developers can document unavailable equipment or unperformed checks without converting those results into passes. Crestron alone decides whether to accept a submission.

- Prepare a signing copy with `submission prepare-review --review-mode declared-gaps --prepare-for-signing`. The authorization pins the exact form, declarations and review status.
- After reviewing the signed pages, use the review-request approval and delivery route with attachment kind `SignedSelfTest`. The C# `SubmissionReviewRequestDelivery` API supports the same route. Existing complete-only delivery remains available.
- Repeated form disclosures are grouped for readability while every affected scope and original outcome remains recorded. The private evidence archive stays local; outbound delivery contains the reviewed driver package and signed form.

See [form signing](docs/submission/FormSigning.md) and [review approval and delivery](docs/submission/ReviewApproval.md). Use the complete console archive; no Python editing or installation is required from the developer.

Validation covers declared-gap signing, changed or revoked authorization, document tampering, simulated-provider delivery and uncertain-send handling. These checks do not constitute a real driver submission or Crestron certification.

Copyright (c) 2026 Neil Colvin. MIT licensed. Crestron and Crestron Home are trademarks of Crestron Electronics, Inc. This project is independent and is not affiliated with, endorsed by or sponsored by Crestron Electronics, Inc.

## 1.13.1 - 2026-09-19

Fix package verification when the caller supplies the full catalogue ID returned by the processor. `compare-payload` and `DriverPayloadInspection.CompareAsync` now resolve the unversioned driver storage key and separate version directory, instead of looking for a folder named after the entire catalogue ID. This also fixes installed-driver test workflows that stopped before running their fixtures.

The catalogue version must match the candidate package. Existing unversioned storage-key callers remain supported, and file, hash, link and ambiguity checks are unchanged. No processor driver is installed, updated or reloaded by this correction.

Validation includes the complete .NET regression suite and a successful read-only comparison against an unchanged driver on a physical processor. See [package verification](docs/DriverPayloadInspection.md) for the corrected command example.

Copyright (c) 2026 Neil Colvin. MIT licensed. Crestron and Crestron Home are trademarks of Crestron Electronics, Inc. This project is independent and is not affiliated with, endorsed by or sponsored by Crestron Electronics, Inc.

## 1.13.0 - 2026-09-19

This release connects candidate-bound evidence from multiple test phases and adds an explicit submission route for developers who choose to disclose unmet requirements. The normal signed submission path remains complete-only by default.

- Combine scoped observations through the C# API and `submission-combine-evidence`, preserving failed phases and the full reviewed policy.
- Assess declared gaps, retain the original evidence and prepare unsigned review requests with reasons for omitted forms or signatures. Missing evidence never becomes a passing test.
- Preview and verify approval of the exact outgoing package, document and correspondence. Protected request delivery checks the retained evidence and approval again before each upload or email.
- Use the existing durable delivery journal for both routes, preserving known upload results and preventing automatic replay after an uncertain provider response.
- Add a short [submission runbook](docs/submission/Runbook.md) linking setup, stage inputs, expected results and recovery instructions.

The NuGet package supplies the C# APIs. The complete Windows console ZIP includes the document commands and their internal runtime; developers do not need to install or write Python. Credentials, evidence and signatures remain private.

Validation includes 943 passing .NET tests, generated unsigned/disclosure requests through the protected command with simulated providers, and isolated packaged-console checks. Document checks passed after an unchanged rerun of a temporary-file replacement failure. The release workflow also runs its full build and packaged-console acceptance checks before publishing. These are tooling checks; end-to-end submission of a real driver through the protected workflow remains to be validated.

The ordinary workflow still requires complete candidate evidence, visual review and separate signing and delivery authorization. Declared-gap signing and omissions of other required documents are not implemented. See [declared gaps](docs/submission/DeclaredGaps.md), [request preparation](docs/submission/ReviewRequest.md) and [approval](docs/submission/ReviewApproval.md). Passing checks means complete only against our interpretation of Crestron's requirements; neither passing checks nor successful delivery implies Crestron acceptance, publication or certification.

Copyright (c) 2026 Neil Colvin. MIT licensed. Crestron and Crestron Home are trademarks of Crestron Electronics, Inc. This project is independent and is not affiliated with, endorsed by or sponsored by Crestron Electronics, Inc.

## 1.12.0 - 2026-09-19

This release adds passive endurance monitoring APIs and console commands. Developers can assess a local or remote Windows collector and send authorized operational email alerts without changing the running collector or contacting its processor.

- Add `SubmissionEnduranceHealth` and `endurance-health` to assess task state, sample freshness, interruptions and run identity. Include the read-only Windows snapshot script in the console archive.
- Add `SubmissionEnduranceWindowsObserver` and `endurance-observe` for local or pinned-SSH observation. The library includes its reader; developers do not need to write or transfer a remote script. Failed queries produce fresh attention reports where possible, rather than reusing an old healthy result.
- Add `SubmissionEnduranceNotifier` and `endurance-notify` for TLS-protected SMTP alerts and completion notices. A persistent private journal suppresses duplicate notifications across restarts and holds uncertain sends for explicit reconciliation.
- Add `endurance-watch` to pass a fresh observation directly to the notifier, including observations that require attention. Windows and SMTP credentials are supplied separately on standard input. Its result reports monitoring health and notification status independently.
- Validate passive snapshot behavior in the release workflow and require its script in the downloadable archive.

Validation includes the discovered .NET regression suite, simulated SMTP and observation failures, duplicate and uncertain delivery, Windows snapshot tests, and isolated packaged-console acceptance. A read-only remote observation also succeeded against an existing Windows collector using a verified ED25519 SSH key. Scheduled alert delivery, real inbox receipt and restart recovery still require validation in the developer's deployment; this release does not claim those checks or completed driver submission.

See [endurance notifications and observation](docs/submission/EnduranceNotifications.md) and [Windows endurance workers](docs/submission/WindowsEnduranceWorker.md). The NuGet package provides C# APIs; the complete Windows console ZIP provides the commands and scripts. An operator must configure supervision, private credential access and authorized recipients. Existing collectors can keep their pinned versions. Ordinary driver, library and client releases do not require submission tooling.

Copyright (c) 2026 Neil Colvin. MIT licensed. Crestron and Crestron Home are trademarks of Crestron Electronics, Inc. This project is independent and is not affiliated with, endorsed by or sponsored by Crestron Electronics, Inc.

## 1.11.0 - 2026-09-19

This release adds a reviewed evidence handoff API and CLI, plus completed endurance snapshots. These optional submission tools remain independent of ordinary driver, library and client releases.

- Add `SubmissionEvidenceMapping.MapFiles` and `submission-map-evidence` to import explicitly reviewed, equivalent observations from a narrower producer policy. Preserve original records and their hashes, measured results and restoration evidence. Validate both source and destination requirements; list uncovered requirements instead of treating a partial import as a completed submission.
- Accept the original `endurance-export` format directly, including collectors whose behavioral policy and executable worker contract are separate. Retain both originals and verify the worker's candidate and producer inventory identities. Mapping never executes the producer, invents missing measurements or changes the running collector.
- Include `Export-EnduranceScheduledRun.ps1` in the console archive. Snapshot only a successfully completed, released run; retain scheduler history and reconciled incidents, revalidate the copied journal and require identical original/copied exports. Existing pinned workers can be archived without replacing their running tools. Credentials and external settings are excluded from the snapshot.
- Handle unavailable or malformed scheduler-status output without masking the original problem with a secondary parsing exception. Uncertain runs remain held for inspection; no probe or processor command is automatically replayed.

Validation covers the complete discovered .NET regression suite, scheduler/snapshot checks, document-tool tests and isolated console acceptance. Compatibility checks used an actual released collector with a separate synthetic journal; they do not constitute driver endurance or submission acceptance.

See [evidence mapping](docs/submission/EvidenceMapping.md), [completed endurance snapshots](docs/submission/WindowsEnduranceWorker.md) and [console setup](docs/submission/ConsoleTools.md). The NuGet package contains the C# APIs; the complete Windows console ZIP includes the CLI, document runtime and scheduling scripts. Existing evidence, producer authentication, behavioral coverage review, signed forms and exact delivery authorization remain prerequisites. No Crestron certification or completed end-to-end driver submission is claimed.

Copyright (c) 2026 Neil Colvin. MIT licensed. Crestron and Crestron Home are trademarks of Crestron Electronics, Inc. This project is independent and is not affiliated with, endorsed by or sponsored by Crestron Electronics, Inc.

## 1.10.0 - 2026-09-18

Protected submission delivery can now use the complete Windows console distribution without configuring separate runtime or validator paths. This optional workflow remains independent of ordinary driver, library and client releases.

- Add `submission-delivery-settings` to generate private schema-2 dispatch settings from a completed, pinned delivery preparation and the installed console. It derives the approved package and form paths, inventories the complete console, rejects overlapping storage and existing outputs, and returns a settings hash for independent review. It grants no approval and sends nothing.
- Add `SubmissionBundledRevalidationSettings`, its preparation API and a `CheckAsync` overload for C# integrations. Existing explicit-runtime settings and schema-1 dispatch remain supported.
- Revalidate through the pinned bundled console immediately before each pending upload or email. Preserve confirmed uploads if later approval fails, return completed journals without sending again, and retain the existing uncertain-outcome handling.
- Update the final-stage CI template to use the tooling inventory already included in generated settings. Developers supply private configuration and approved hashes; they do not hand-author a second runtime inventory.

Validation covers the complete .NET suite, offline document tests and isolated console acceptance with synthetic forms and providers. An unattended NETWORK SERVICE rehearsal exercised actual bundled revalidation before simulated upload and email, changed-tooling refusal, approval revocation after upload, and completed replay. This is tooling validation, not a real signed driver submission or Crestron certification.

See [delivery setup](docs/submission/DeliverySetup.md), [protected execution and recovery](docs/submission/DeliveryCommand.md), and [the submission roadmap](docs/CrestronSubmission.md). Extract the complete console ZIP to a new protected directory; do not mix release files. The NuGet library contains the C# APIs, while the document runtime is provided in the console ZIP. Final delivery still requires complete candidate evidence, a reviewed signed form, provisioned accounts and exact authorization.

Copyright (c) 2026 Neil Colvin. MIT licensed. Crestron and Crestron Home are trademarks of Crestron Electronics, Inc. This project is independent and is not affiliated with, endorsed by or sponsored by Crestron Electronics, Inc.

## 1.9.0 - 2026-09-18

The Windows console now includes the optional submission preparation tools and their isolated document runtime. Driver authors use console commands, configuration and C# fixtures without installing or maintaining Python. Ordinary driver, library and client releases remain independent of submission.

- Add `submission` commands for help, coverage planning, Android evidence auditing, forms, review, authorized signing and delivery preparation. The complete console archive verifies its pinned runtime and uses its own matching evidence validator. DOCX-to-PDF rendering still requires the documented renderer and fonts.
- Add `DriverPayloadInspection.CompareAsync` and `compare-payload` to compare a trusted candidate package with its exact extracted catalogue/version files. The console holds the shared processor reservation. Comparison does not deploy, reload or attest running process memory; callers must separately verify the selected installed instance.
- Carry complete Android evidence audits through review, signing and delivery preparation/revalidation. Selected-case runs preserve discovered, selected and excluded inventories and require an independent selection pin; they do not imply full-suite or official-plan completion.
- Handle transient Windows access/sharing/lock refusals during atomic journal replacement with bounded retries of that file operation only. Provider requests and whole delivery operations are never replayed automatically. Authorization expiry and cancellation are checked again after intent persistence and before provider entry.
- Keep library package and console assembly versions consistent when building a specified release.

Validation includes the complete offline .NET regression suite, document-tool tests and eight acceptance checks against the standalone console. Isolated NuGet consumers verified implementation bytes and the corresponding adapter/CLI preflight paths without source-project dependency overrides. Synthetic journal checks cover permanent refusal, expiry during intent persistence and repeated complete lifecycles without duplicate provider calls. These checks send no real submission and do not establish protected-service acceptance, a complete signed driver submission or certification.

Extract the entire console ZIP; do not copy only the executable or combine releases. Start with [console setup](docs/submission/ConsoleTools.md), [payload comparison](docs/DriverPayloadInspection.md), [delivery journal behavior](docs/submission/DeliveryJournal.md) and the [submission roadmap](docs/CrestronSubmission.md). Final delivery still requires complete candidate evidence, reviewed signed forms, provisioned accounts and exact approval. The NuGet configuration library does not contain the document runtime.

Copyright (c) 2026 Neil Colvin. MIT licensed. Crestron and Crestron Home are trademarks of Crestron Electronics, Inc. This project is independent and is not affiliated with, endorsed by or sponsored by Crestron Electronics, Inc.

## 1.8.0 - 2026-09-18

This release adds optional submission delivery and Windows scheduling for endurance observations. Ordinary driver, client and library releases remain independent of Crestron submission.

- Add `CrestronSubmissionUploader`, verified download receipts and read-only reconciliation of retained uploads; add TLS SMTP delivery and durable acceptance receipts through `SubmissionSmtpMailer` and `CrestronSubmissionTransport`.
- Add fresh authorization and evidence revalidation before each external delivery step. The `submission-deliver` console command requires explicitly approved execution, independently pinned private settings and credentials through redirected standard input. Uncertain upload or email outcomes require reconciliation before retrying.
- Include the supervised Windows endurance scheduler scripts in the console ZIP under `scripts/endurance`. They preserve private configuration and interrupted-worker state; consumers supply their functional probe, account setup and independent alerting.
- Fail version verification promptly when the requested driver version reports `FailedToLoad`. A failure for the previous version still permits the incoming update to become ready. No recovery command is sent automatically.
- Fix help rendering from deeply nested Windows build directories. The matching source tools now audit a complete Android test-program inventory, including dependencies and runtime settings, against an independent pre-execution pin. This audit requires the workflow from CrestronHomeNUnit 1.11.1 or later; old evidence cannot be pinned retroactively.
- Include development history in the NuGet package as well as the console ZIP so the packaged documentation links resolve. Document extension property writes, independent read-back and remote-syslog collection limits.

Validation covers the complete offline .NET and Python submission-tool suites, scheduler interruption guards, isolated package contents and console startup. Cross-language verification checked a manifest generated by the actual .NET workflow against real test dependencies and rejected an altered NUnit assembly. Provider checks separately established an authorized small-file upload/download, read-only reconciliation, and receipt of a plaintext self-addressed SMTP test. Those checks do not establish a complete signed driver submission, the signed-PDF mail path, protected CI delivery, or a candidate's 24-hour endurance period.

The library and console are packaged binaries. Python help, evidence, review and signing tools remain in the matching **v1.8.0 source tag**, with their pinned dependencies; they are not embedded in the NuGet package or console ZIP. Use the [delivery command guide](docs/submission/DeliveryCommand.md), [Windows worker setup](docs/submission/WindowsEnduranceWorker.md), [Android evidence audit](docs/submission/AndroidEvidence.md) and [submission roadmap](docs/CrestronSubmission.md). Final delivery still requires complete candidate evidence, a reviewed signed form, provisioned accounts and exact delivery authorization. This release does not claim Crestron acceptance or certification.

Copyright (c) 2026 Neil Colvin. MIT licensed. Crestron and Crestron Home are trademarks of Crestron Electronics, Inc. This project is independent and is not affiliated with, endorsed by or sponsored by Crestron Electronics, Inc.

## 1.7.0 - 2026-09-17

Add read-only processor uptime observations and plan validation for independently supplied monitoring producers. These APIs are useful for ordinary development monitoring as well as optional driver submission.

- `ProcessorUptime.ReadAsync` opens an authenticated, SSH-key-pinned console, waits for the prompt and sends `uptime` once. It bounds execution and response size, handles fragmented replies and interleaved logs, and never automatically retries an uncertain command.
- `ProcessorUptimeSnapshot` retains the reported duration, diagnostic local start time and request/response UTC timestamps. Its inferred UTC start window supports an explicitly reviewed clock tolerance. The displayed local start time is not a stable boot identifier; consumers must keep their original baseline and independently check driver lifetime and fresh function.
- `SubmissionEndurance.ValidatePlan` exposes the collector's structural validation without creating files or contacting a processor. External producers can reject malformed requests before comparing them with their reviewed acceptance bindings.
- Monitoring guidance now distinguishes processor uptime, driver lifetime, fresh functional observations and the complete endurance requirement. A supplied probe and scheduler remain separate from the shared library.

Validation: the complete .NET test suite passed. Real CP4-R console observations confirmed the uptime parser and bounded boot window, including a captured prompt/log interleaving case and a one-second variation in the displayed local start time. A separately supplied producer completed short real-function observations through the existing process/monitor protocol and exported valid evidence. A deliberate same-version driver reload changed its lifetime and caused the old monitor run to fail; reservations were released.

These are development validations, not an immutable candidate's 24-hour endurance period. No processor reboot was performed for these checks. The release does not contain a driver-specific probe, scheduled-worker installer, automatic certification, final signature or supported uploader/mail sender. Full submission readiness still depends on the consuming project's remaining validation and delivery stages.

See [processor uptime](docs/ProcessorUptime.md), [endurance collection](docs/submission/EnduranceCollection.md), [submission stages](docs/CrestronSubmission.md) and [release history](CHANGELOG.md).

Copyright (c) 2026 Neil Colvin. MIT licensed. Crestron and Crestron Home are trademarks of Crestron Electronics, Inc. This project is independent and is not affiliated with, endorsed by or sponsored by Crestron Electronics, Inc.

## 1.6.0 - 2026-09-17

Add journaled managed-child setup, test bindings and cleanup, resumable endurance collection, and the remaining offline signing and delivery-preparation stages. These tools support ordinary development CI independently of optional driver submission.

- `DriverConfiguration.BeginManagedDeviceAsync` enters a newly commissioned child's initial configuration even when it already reports configured. `ManagedDeviceCommissioning` records setup/readiness and removes only its journal-owned leaf child after the caller verifies restoration. The console exposes `commission-child` and `remove-created-child`.
- `ManagedDeviceValidation.RunAsync` supplies actual created IDs to a caller's test producer, verifies reservation ownership around every stage, and removes children in reverse order only after confirmed restoration. Failed tests remain failed after successful cleanup. Partial setup, unknown outcomes and unconfirmed restoration retain receipts for reconciliation.
- `SubmissionEndurance`, its reservation monitor and the endurance CLI persist candidate-bound observations across worker invocations. They reject missed-interval, identity and interrupted-probe conditions, pin the external producer bundle, and retain uncertain ownership. Functional checks remain the responsibility of the explicitly selected producer.
- Release archives preserve nested documentation paths and verify every source document is present exactly once. Package and help checks accept reviewed support websites, normalize archive separators before candidate hashing, bind dependency notices to packaged/merged content, and reject unnamed supporting PDFs.
- Tagged source tools audit Android evidence, prepare an exact signing copy, apply an explicitly authorized private signature image, revalidate a signed review, and prepare delivery artifacts. These Python tools require the matching source checkout and pinned dependencies; they are not embedded in the NuGet package or console ZIP.

Validation: the complete offline .NET suite passed. Real CP4-R checks passed managed-child commissioning and cleanup through both library and CLI, independent readiness/inventory checks, and a caller-supplied Android editor test with verified hub/UI restoration and owned-child removal. No physical heating command or processor reboot was sent in those checks. Endurance reservation/collection infrastructure passed real processor checks with **synthetic functional observations**, not a final driver endurance run. Offline document/signing/preparation checks use synthetic forms and signatures.

The normal combined NUnit CLI/Test Explorer plan still needs integration of the new managed-child APIs. A trusted driver-specific endurance producer, unattended scheduler deployment/restart validation and the actual immutable candidate period remain outstanding. Real final-form signing, supported uploader/mail adapters and end-to-end delivery are not implemented or validated by this release. No submission or certification is claimed.

See [managed-child validation](docs/ManagedChildValidation.md), [driver configuration](docs/DriverConfiguration.md), [endurance collection](docs/submission/EnduranceCollection.md), [submission stages](docs/CrestronSubmission.md) and [release history](CHANGELOG.md).

Copyright (c) 2026 Neil Colvin. MIT licensed. Crestron and Crestron Home are trademarks of Crestron Electronics, Inc. This project is independent and is not affiliated with, endorsed by or sponsored by Crestron Electronics, Inc.

## 1.5.0 - 2026-09-16

Add reusable UI instance association and offline tools for preparing optional Crestron driver submission evidence. Ordinary development, testing and publication remain independent of submission.

- `DriverNameChallenge` temporarily renames an exact loaded driver, invokes caller-provided UI observations, and restores its name. It requires existing processor/UI reservations, records intent before mutation, and preserves uncertainty after lost replies or ownership.
- Offline package/evidence checks and `submission-evidence-check` reject mismatched candidates, missing or stale evidence, incomplete scoped observations and unconfirmed restoration. Endurance checks require retained samples and an approved maximum gap.
- `submission-bundle-create` and `submission-bundle-check` retain and revalidate private evidence with candidate, policy and template digests.
- `SubmissionDelivery` provides a transport interface, durable delivery intent/receipts and explicit reconciliation. No built-in uploader, mail sender or CLI send command is included. Synthetic transport tests do not establish real delivery.
- Source tools generate draft coverage policies, help from a supplied official template, unsigned self-test forms and a private review bundle. These Python scripts require the matching tagged checkout and pinned dependencies; they are not embedded in the NuGet package or console ZIP.

Validation: all 377 offline .NET tests passed. A source-library pilot on a development V2 MC4-R observed a sample driver Debug gateway's temporary name in the minimized Android emulator, opened its page, and restored the name and Home screen. Inventory and observed control labels were preserved, and both reservations were released. No physical control command was sent. The Python submission tooling's synthetic review checks also passed under the installed Windows runner service.

Exact Release-candidate UI validation, physical control coverage, Android operation under a service/logout, crash recovery, approved complete submission evidence, signing and real delivery remain pending. These tools do not certify a driver or imply Crestron acceptance.

See [UI binding](docs/DriverUiBinding.md), [submission plan](docs/CrestronSubmission.md), [delivery journal](docs/submission/DeliveryJournal.md), [CHANGELOG.md](CHANGELOG.md) and [third-party notices](THIRD-PARTY-NOTICES.md).

Copyright (c) 2026 Neil Colvin. MIT licensed. Crestron and Crestron Home are trademarks of Crestron Electronics, Inc. This project is independent and is not affiliated with, endorsed by or sponsored by Crestron Electronics, Inc.

## 1.4.0 - 2026-09-16

Add opt-in removal of a V1 driver instance when other installed instances share its reload scope.

- `DriverRebootHandler.AdditionalRemovalRebootDeviceIds` explicitly lists the existing instances to preserve and requires `RebootAfterRemoval`.
- The processor's affected scope must match exactly. Shared instances must have the expected model/version and be Loaded; only the selected instance receives the removal command.
- After the authorized Home configuration reboot, verify disappearance of the selected instance and preservation of the other instances' identities, rooms, versions, configured state and reported configuration items.
- Default removal remains restricted to a single instance. Unknown or expanded scope, lost responses and uncertain recovery do not trigger retries or automatic broader removal.

V1 driver initial installation and removal were verified on a development MC4-R. Startup verification was resumed after a timeout without repeating installation or reboot; subsequent removal and its configuration reboot preserved all original devices, removed the temporary instance and released the reservation. This does not establish pairing or playback behavior. No CP4-R validation was repeated for this change.

See [V1 installation and removal](docs/V1DriverRemoval.md), [compatibility](docs/Compatibility.md), [CHANGELOG.md](CHANGELOG.md) and [third-party notices](THIRD-PARTY-NOTICES.md). The standalone CLI's ordinary remove command remains reboot-free; advanced orchestration uses the library or NUnit workflow.

Copyright (c) 2026 Neil Colvin. MIT licensed. Crestron and Crestron Home are trademarks of Crestron Electronics, Inc. This project is independent and is not affiliated with, endorsed by or sponsored by Crestron Electronics, Inc.

## 1.3.0 - 2026-09-15

Add room inventory and guarded movement of a loaded driver between existing rooms.

- `locations` lists configured room IDs and names.
- `move --device ID --model NAME --version VERSION --from-room ID --room ID` moves a matching childless driver and verifies its unchanged identity and loaded version.
- The library exposes `GetLocationsAsync`, `MoveDriverInstanceAsync`, `ProcessorLocation` and `DriverRoomMoveResult`.
- Reboot-required drivers, managed children, ambiguous destinations and changed identity are refused. The CLI holds the shared processor reservation and retains uncertain outcomes for inspection.
- CI and release validation compare executed test identities with discovery instead of maintaining a fixed test count.
- Enforce the numeric room-ID format required by the move command. Installation uses a different string-valued format; interchanging them can remove an instance.

Hardware validation moved a temporary Entity V2 test instance between two rooms and back on an MC4-R, including a fresh authenticated connection. Instance ID and loaded version were preserved. The temporary instance and CI archive were removed afterwards. No actual installed driver was moved.

See [room moves](docs/RoomMoves.md), [protocol reference](docs/ProtocolReference.md), [CHANGELOG.md](CHANGELOG.md) and [third-party notices](THIRD-PARTY-NOTICES.md). The library requires .NET 10 and a V2 Crestron Home processor. This is unrelated to driver V1/V2 naming. No Crestron SDK is required.

Copyright (c) 2026 Neil Colvin. MIT licensed. Crestron and Crestron Home are trademarks of Crestron Electronics, Inc. This project is independent and is not affiliated with, endorsed by or sponsored by Crestron Electronics, Inc.

## Documentation - 2026-09-15 (no package release)

- State that configuration-management commands require V2 Crestron Home processors; V1 processors do not support them. Distinguish processor compatibility from supported V1 driver update/reboot workflows.
- Correct the README installation example to the current 1.2.0 library release.

## 1.2.0 - 2026-09-15

Add reusable initial-driver configuration and read-only inspection of current settings to the .NET 10 library, interactive console and CLI.

- `driver-configuration --device ID` reads the advertised settings, current values and configuration status. Display follows only the driver's `Masked` flag; omitted stored values are not invented or replaced by defaults.
- `configure-driver --device ID --model NAME --version VERSION --input FILE` applies private flat values or ordered wizard steps, validates the exact target and writable fields, and waits for confirmed configuration completion.
- Already configured instances are preserved. Submitted settings are never retried after uncertain failure. The CLI holds the shared processor lease, retains uncertain outcomes and keeps values out of routine configuration diagnostics.
- `DriverConfigurationInspection.GetAsync`, `DriverConfiguration.ReadInputs` and `DriverConfiguration.ConfigureAsync` expose the same behavior to other development tools.

All 191 offline tests pass. MC4-R / Home 4.11.322 validation read installed sample driver settings, preserved the configured instance, and completed the two-step wizard on a separate temporary instance. Cleanup removed the temporary instance after explicit inspection of its shared reload scope and confirmed the original driver was ready with unchanged settings. No heating controls were operated.

Install CrestronHomeDevTools 1.2.0 from NuGet, or extract the complete matching Windows console ZIP from GitHub. No Crestron SDK is required by this library or console.

See [driver configuration](docs/DriverConfiguration.md), [protocol reference](docs/ProtocolReference.md), [processor coordination](docs/ProcessorCoordination.md), [CHANGELOG.md](CHANGELOG.md) and [third-party notices](THIRD-PARTY-NOTICES.md). This does not add arbitrary reconfiguration, rollback or automatic retained-package deletion.

Copyright (c) 2026 Neil Colvin. MIT licensed. Crestron and Crestron Home are trademarks of Crestron Electronics, Inc. This project is independent and is not affiliated with, endorsed by or sponsored by Crestron Electronics, Inc.

## 1.1.0 — 2026-09-14

- Support catalogue searches longer than three words by issuing bounded search batches and intersecting catalogue IDs. Fixes HTTP 422 when a workflow looks up models such as Example Multi Word Device Model.

- Coordinate console mutations with NUnit workflows, desktop runners and updated processor test hosts through the same processor-side lease. Verify owner contents, prevent release during active tests and retain uncertain outcomes for inspection.
- Keep standalone reboot reservations through shutdown and authenticated Home startup instead of releasing at acknowledgement; use a bounded 600-second default startup wait.
- Add `capabilities` and a shared PowerShell build deployment entry point that requires lease support, passes credentials privately, waits for catalogue readiness and reports failures to the build.
- Add read-only `stored-packages` inspection with manifest identities, storage sizes and conservative device/catalogue references. No package deletion is performed.
- Document coordinated CI/build/manual development and the distinction between instance removal and retained packages. Link to the separately released NUnit Test Explorer adapter.

- Validate 162 offline tests, cross-tool processor locking, active-test release refusal, reboot recovery, retained-package inspection and complete representative Entity V2 update workflows on MC4-R / Home 4.11.322.

## 1.0.0 — 2026-09-14

- Add a configuration-management protocol reference covering verified discovery, authentication, request/response envelopes, commands, asynchronous events, V1/V2 lifecycle sequences, configuration reboot, retry rules and validation limits. Link it from README and the API/compatibility guides. — initial development preview

- Use Home's configuration reboot operation after a confirmed V1 swap. An immediate SSH console reboot did not retain the staged driver version during validation; standalone SSH reboot remains a separate command.

- Fix V1 update sequencing: wait for the exact driver-swap completion event, validate reboot/reconfiguration requirements, and request reboot once. A missing event or lost update response never implies permission to reboot. Found during hardware validation.

### Added

- Confirmed whole-processor reboot API and interactive console command, with target-specific confirmation, pinned SSH and no automatic retry. Unattended reboot requires an explicit matching target confirmation. Lifecycle reboot support is opt-in and preserves scope/version guards.

- Independent .NET 10 configuration-management library, interactive console and automation CLI.
- Credential-free processor discovery, exact system-name resolution and explicit address selection.
- Authenticated WebSocket/HTTPS sessions with verified certificate pins, cancellation and asynchronous operation tracking.
- Driver/device inventory, update eligibility, reviewed update plans and guarded reboot-free reload.
- SFTP package inspection/upload/import with SSH key verification, SHA-256 recording and catalogue readiness checks.
- Guarded install/update/reuse of an explicitly identified instance, including first installation and reinstallation after removal.
- Identity/version/dependency-checked removal with disappearance verification.
- Named Windows DPAPI profiles, masked credential entry, private settings/environment input, descriptive command help, JSON CLI results and documented exit codes.
- Complete user/API/compatibility/release documentation, third-party notices and a link to the separate NUnit CI orchestration guide.

### Fixed

- Retry bounded restart-readiness reads when Home temporarily returns HTTP 500/502/503/504 during startup. Preserve authentication/request failures and never retry mutation commands. Found during the authorized hardware reboot test.

- Interactive setup no longer rejects valid credentials because of an empty catalogue filter; it validates the management service directly.
- Unfiltered catalogue reads use advertised categories; HTTP errors identify the failed session/read/command stage.
- Driver-version comparisons retain all numeric components while accepting different zero-padding across package, catalogue and installed versions.

### Validation

- Validate CP4-R / Home 4.11.322: authenticated inventory, temporary Entity V2 import/install/update/reuse/reload, one configuration reboot and recovery, 116 processor tests before and after restart, and removal/lease cleanup. Verify all seven existing driver instances retain their identities, room assignments and versions and are Loaded. Document per-model limits; CP4-R V1 lifecycle remains unverified.
- 157 offline NUnit tests pass without processor credentials or Crestron SDK dependencies.
- On MC4-R / Home 4.11.322: authenticated reads, discovery/name resolution, SFTP import, install/update/reuse, targeted V2 reload and removal/reinstallation were verified.
- DevTools supported the complete gated sample outlet driver workflow and the later six-driver processor validation. The broader workflow and its test policies belong to Crestron Home NUnit.

### Release status

Initial public release: the library is distributed through NuGet and the Windows console through GitHub Releases. The release workflow builds and validates artifacts before publishing with NuGet Trusted Publishing. See [release notes](RELEASE-NOTES.md) and the [release procedure](docs/Releasing.md).

### V1 hardware validation

A complete unattended V1 driver workflow subsequently passed on the development MC4-R: 116 local tests, 105 processor driver tests, 11 processor SDK lifecycle tests and three read-only installed-driver health checks. It installed a fresh Entity V2 test host, staged the V1 update, received the matching swap-completion event, requested one Home configuration reboot, reconnected and verified lease ownership, verified the new driver version was Loaded, online, ready and configured, then removed the test host and released the lease. Independent checks confirmed the other 19 driver instances retained their identities and versions and were Loaded. The first V1 attempt exposed an incorrect assumption that swap initiates reboot; it required one separately recorded assisted reboot and was not counted as an unattended pass. A second attempt confirmed swap completion but an immediate SSH reboot returned with the previous version; it was stopped, reconciled and retained as a failed validation. The passing run used Home configuration reboot instead. V1 initial-install/removal reboot paths still have simulated coverage only. The SDK lifecycle tests and read-only health checks do not establish playback or device-control behavior.



