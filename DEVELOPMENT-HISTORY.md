# Development history

## 18 September 2026 - Local package acceptance after 1.8.0

Release builds now pass the requested version to both the library pack and standalone console publish, keeping assembly and package versions consistent. A fresh standalone console passed all eight bundle acceptance checks. An isolated consumer restored the locally built DevTools dependency through NuGet without a source-project override; the corresponding adapter and CLI passed their offline package checks with implementation-byte verification. These are local validation artifacts, not published releases. Hardware and protected-service acceptance remain separate.

## 18 September 2026 - Delivery journal resilience (local source after 1.8.0)

An isolated local filesystem probe reproduced four access-denied atomic replacements in 120 small synthetic journal sequences, without using delivery code. Each failed rename later succeeded without permission changes. The responsible reader/filter was not identified; no antivirus or system settings were changed. The earlier failed regression runs remain retained.

The journal now retries only a Windows access/sharing/lock refusal from the same flushed temporary-file rename, at most four additional times with a total of 375 ms of scheduled waits. Persistent denial and other storage errors still fail; no provider request, revalidation callback or whole delivery attempt is automatically retried. The prior record remains intact until replacement succeeds. Authorization expiry and cancellation are checked again after intent persistence, before entering the provider; if no request started, a successful corrective write restores the known prior state, while an unwriteable Pending record still requires reconciliation.

All 660 discovered offline .NET tests passed, including a real held-reader atomic replacement, permanent refusal before upload, expiry during either intent write, and 100 rapid synthetic complete lifecycles with exactly one upload and one email callback each. These callbacks perform no external delivery. The prior 645-case run remains failed history, not a retroactive pass. This source is local and unpublished; no processor, real upload, email or signing operation was used. See [delivery journal behavior](docs/submission/DeliveryJournal.md).

## 18 September 2026 - Candidate payload comparison (local source after 1.8.0)

Add `DriverPayloadInspection.CompareAsync` and the `compare-payload` console command to compare a pinned candidate with its extracted processor files. The CLI holds the shared processor reservation; the C# API participates in its caller's existing reservation. Comparisons reject changed, missing, additional, ambiguous, linked and oversized files and retain only identities, sizes and hashes. This is a file observation, not an attestation of running memory or proof of the installed-device association. No install, reload or package update is performed. See [usage and limitations](docs/DriverPayloadInspection.md).

All 31 new payload/console tests passed, both in the full run and in a 55-case isolated run including the existing delivery-journal fixtures. The full 645-case run had three intermittent access-denied failures while replacing delivery journal files; those same fixtures passed unchanged in isolation. Their cause remains unresolved, and the full run is not recorded as passed. The initial sandbox run could not initialize NUnit's default working directory; the subsequent runs used the isolated working-directory harness outside the sandbox. No hardware, upload or email was involved. This local source work has not been released.

## 18 September 2026 - Bundled submission console (source implementation after 1.8.0)

The Windows console now provides the optional submission preparation commands with an isolated, hash-pinned runtime and matching evidence validator. Driver authors use console commands, JSON profiles and C# fixtures; they do not need to install or maintain Python. The complete archive retains vendor licenses, rejects missing/changed/unlisted runtime files and does not accept a settings-file validator override. Existing source-script invocations remain compatible. MSBuild help packaging accepts `SubmissionConsole`, and the protected workflow templates build the complete console automatically. Ordinary driver/library releases remain independent of submission.

Local validation passed the complete 614-case .NET suite, all 193 source document-tool tests, the ten MSBuild cases and nine renderer cases after the final integration changes, and eight acceptance cases against the actual self-contained console. Download acceptance exercised empty PATH/conflicting interpreter settings, all command help, real synthetic form/evidence review, authorized synthetic signing, delivery preparation/revalidation without sending, real MSBuild help packaging without an interpreter property, and friendly rejection of an unexpected module. A synthetic-renderer attempt created cache files inside its bundled interpreter and exposed an uncaught integrity exception; that failed run was retained. The fixture now disables bytecode writes and the console handles the integrity exception. The fresh corrected build passed. This is local source validation, not a published release or service-account/hosted workflow validation, and not driver acceptance evidence.

See [submission console setup](docs/submission/ConsoleTools.md). No processor was changed and no real signature, upload or email was used for this work.

## 18 September 2026 - Explicit Android case inventories (source work after 1.8.0)

The Android evidence auditor now understands selected-phase producer receipts from the matching NUnit workflow source. Selected phases require an independently retained selection hash, complete discovered/selected/excluded inventories, unchanged execution settings and exact successful TRX coverage. Missing pins, changed selection, omitted cases and split duplicate names are rejected. Reports state excluded cases rather than presenting a passing subset as full-project coverage. The private review stage carries the selection pin through each required run.

The developer-facing integration must provision and manage the internal runtime automatically. Developers may use C# fixtures and documented commands/configuration without Python knowledge; the current source-script entry points do not yet satisfy that requirement. No new public binary or actual submission is implied by this source entry.

Validation: all 33 focused Android-audit tests and all 193 submission-tool tests passed. The first full run encountered a Windows access error during atomic replacement of a newly built temporary package; the unchanged isolated case and full rerun passed. That failure is retained, its cause is not established, and no automatic retry was added to packaging or external operations. The matching C# workflow passed 281 regressions, including actual adapter selection with duplicate names, quotes, backslashes and an unselected deliberately failing test.

## 18 September 2026 - Android audit through submission review (source work after 1.8.0)

The review stage can now re-audit every independently pinned Android run before form generation and after bundle validation. Android-method policies require these inputs. Changed, missing or incomplete runs prevent review completion. The review receipt pins both the retained run declaration and audit report; signing, delivery preparation and pre-send revalidation preserve and check these files privately without adding them to outgoing attachments. This does not authenticate the worker or translate a passing test into an official requirement automatically.

All 188 offline submission-tool tests passed, including real Python/.NET review and signing operations with clearly synthetic forms and approvals and changes between stages. The first full run hit a Windows access error while moving a temporary signing folder; the isolated case and unchanged full rerun passed. The failed run remains retained, and its environmental cause is not established. A real unchanged Release candidate also passed the released Android stage and 1.8.0 standalone auditor, then its retained evidence passed the new review input bridge. That hardware result verifies only its complete read-only suite and cleanup, not the whole submission plan. See [Android evidence](docs/submission/AndroidEvidence.md) and [review setup](docs/submission/ReviewStage.md). No actual signature, upload or email was used for this development work.

This records changes published to the source repository separately from packaged releases. The [changelog](CHANGELOG.md) and [release notes](RELEASE-NOTES.md) describe released versions. The entries below record development after 1.7.0 and are incorporated in **1.8.0**. Historical source-only descriptions identify their availability at the time; see the 1.8.0 release notes for packaged binaries, console scripts and tools distributed only in the matching source tag.

## 18 September 2026 - Release package acceptance

Added development history to the NuGet archive and made it a required release document. The offline coverage runner now gives fixtures a fresh working directory inside each retained test run instead of sharing NUnit's default location. Two local runs encountered intermittent Windows access errors during atomic journal replacement; both failed runs were preserved. All journal cases and the complete discovered suite passed in the isolated directory. No retry, assertion bypass or external delivery was added; the original access-error cause is not established by this isolation check.

## 18 September 2026 - Complete Android producer inventory

The offline Android evidence audit now requires an independently pinned manifest of the complete retained test output, including dependencies, runtime settings and nested resources. It rejects added, missing or changed files and mismatched coordinator receipts, instead of checking only the main test assembly. The matching NUnit workflow source records the inventory before execution and rejects changes afterward using its in-memory reference hashes. Older evidence cannot acquire a pre-execution pin retroactively.

This is source development after the packaged 1.7.0 release. See [Android evidence](docs/submission/AndroidEvidence.md) for the matching workflow requirement and additional command argument. The audit still does not authenticate the worker or claim official requirement coverage or submission readiness.

Validation: the complete offline submission-tool suite passed. The actual compiled .NET workflow inventory was also consumed directly by the Python auditor against real test output, including dependencies and satellite resources; altering the retained NUnit dependency was rejected. No processor access, driver upload or email was involved.

## 18 September 2026 - Remote processor logging guidance

Documented read-only remote-syslog queries verified on a Home CP4-R and its TCP/UDP/SSL syntax. The guide distinguishes the current 4-Series manual's conditional TLS requirement from the Toolbox page's wording. It records collector delivery, diagnostic coverage and restart behavior as unverified, and links optional log retention from the endurance guide. No collector, processor configuration change or new packaged release is included.

## 18 September 2026 - Report terminal driver loading failures

Version verification now stops with an error when a reviewed instance reports the requested version and `FailedToLoad`, rather than waiting until the update deadline expires. A failure reported for the previous version does not reject an incoming update. This observation does not retry, reload or remove the driver; existing failure reconciliation remains required.

Validation: all 595 offline DevTools tests passed, including prompt rejection of the requested version's failure and successful transition from an older failed version. This is a source addition after 1.7.0, not a change already included in the published package.

## 18 September 2026 - Help rendering from nested build directories

The help renderer now converts documents in a short, isolated system temporary directory before copying the verified PDF to the requested build output. This fixes a Windows submission build failure under deeply nested MSBuild receipt directories. Windows builds use LibreOffice's `soffice.com` console launcher.

Validation: the renderer, package and MSBuild checks passed, including a regression that rejects long paths at the external renderer boundary. A real Release submission candidate then built successfully; its PDF and dependency notices were verified inside the package, and every help page was visually reviewed. This establishes packaging behavior, not completed hardware acceptance or submission.
## 18 September 2026 - Submission delivery additions in source

- Added a Crestron uploader provider with private upload receipts and verification of retained uploads without submitting them again. See [uploader transport](docs/submission/CrestronUploader.md).
- Added TLS SMTP delivery and a combined upload/email transport. The mailer checks the reviewed sender, signed-form hash and message identity, records acceptance durably, and preserves private rejection details. See [SMTP delivery](docs/submission/SmtpDelivery.md).
- Added fresh delivery-authorization and evidence validation before each upload or email step, including revalidation of completed signing handoffs. Connected the delivery journal to a pinned external validation process, corrected evidence-root normalization, and improved synthetic CI failure diagnostics. See [delivery process bridge](docs/submission/DeliveryProcessBridge.md).
- Added the `submission-deliver` console command and an optional protected GitHub Actions workflow example. The command requires explicitly approved execution, hash-pinned private settings and credentials through standard input. The example verifies a preinstalled tool bundle and is not enabled automatically. See [delivery command](docs/submission/DeliveryCommand.md).

Added command-to-revalidator integration coverage using synthetic documents and transport, including authorization changing between upload and email and replay without duplicate delivery. Console archives include this development history so their documentation links remain usable.

Validation: the offline .NET and submission-help CI checks passed for the delivery implementation. Provider-specific checks and their limits are documented in the linked guides. A complete signed driver submission, execution of the protected delivery workflow and Crestron acceptance have not been established by these checks.

Source range: [delivery authorization](https://github.com/oznetmaster/CrestronHomeDevTools/commit/94b0673) through [delivery command](https://github.com/oznetmaster/CrestronHomeDevTools/commit/deeb0de). These are additions after 1.7.0, not changes retroactively included in that release.

## 17 September 2026 - Monitoring and protocol documentation after 1.7.0

- Added optional supervised Windows scheduling for endurance observations, private service-account setup and interruption handling. The scheduler scripts require the corresponding source checkout. See [Windows endurance worker](docs/submission/WindowsEnduranceWorker.md).
- Documented extension property writes and independent read-back verification in the [configuration-management protocol reference](docs/ProtocolReference.md).

These changes do not establish a completed 24-hour endurance period or independently delivered offline alerts.

Copyright (c) 2026 Neil Colvin. MIT licensed. Crestron and Crestron Home are trademarks of Crestron Electronics, Inc. This project is independent and is not affiliated with, endorsed by or sponsored by Crestron Electronics, Inc.
