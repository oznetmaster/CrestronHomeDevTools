# Automated Crestron driver submission

This describes the optional submission stage and the state of its shared tooling. Driver-specific rollout plans, public identity, supported models, help content, UI pages and validation results belong in each driver repository. Processor test packages and independent client libraries are not portal submissions.

Submission is an explicit opt-in extension, never a mandatory consequence of using the development tools. A client or library workflow ends after its configured tests and ordinary publication. A driver workflow can also update/test the installed driver and its UI without submitting anything. Only a driver release that explicitly enables submission enters the additional evidence, form, signature and delivery gates below. Those gates must not be imported as prerequisites of general test, build or GitHub/NuGet release jobs.

End-to-end submission is not implemented or enabled. Development workflow validation does not establish final Release acceptance, and no submission has been sent by this work.

Each driver profile supplies its portal status, developer/company name, filename component and approved public support website and/or email. Private correspondence and sender credentials are separate, untracked inputs. A GitHub repository may provide public support when its support route is usable.

## Offline package preflight

DevTools 1.5.0 introduced `SubmissionPackage.Inspect` and `submission-check`. DevTools 1.6.0 also accepts website-only support; the example below requires that version or later:

```text
submission-check --package ExampleDeveloper_Platform_Example_IP.pkg --driver DRIVER_GUID --version 1.0.000.0000 --kind new --developer-name-token ExampleDeveloper --support-website https://example.org/driver-support
```

For an update to a driver already on the portal, use `--kind update`. That exempts only the developer filename component. All matching filename, manifest and help checks still apply. A GitHub release of a driver does not by itself make it an existing portal driver.

The command emits a JSON report and exits 0 when structural checks pass, 1 when defects are found, or 2 for invalid arguments. No processor profile is required. It checks exact root package/DLL/DAT/help naming, expected GUID/version, generated developer/dependency metadata, the configured public support email and/or website, DLL reference, PDF header, duplicate/unsafe archive paths and certain known private test-input filenames. It hashes the inspected bytes while holding the file open. Every packaged PDF must also have a nonempty filename before its extension; an unnamed supporting document is rejected even when the main help PDF is correctly named.

Crestron requires support contact information; the checker's configured contact must match the package. It accepts a reviewed HTTP/HTTPS support website, email, or both. A website URL check does not prove that its support route is usable. The archive separator convention is this tool's consistency check, not a demonstrated Crestron rejection.

This is deliberately a structural report, not submission readiness. It does not establish PDF readability/content/support details, authenticity of developer data, ManifestUtil toolchain provenance, absence of all possible secrets, supported device behavior, completed self-test evidence or signing authorization. Subsequent gates must validate those separately.

## Intended release path

1. Select a release candidate from trusted source, generate its final help PDF, build the package once with that PDF embedded, and record source commit, toolchain, driver GUID/version and package SHA-256.
2. Check package contents, help PDF, support/developer metadata and release identity.
3. Run desktop tests and reserve the development processor and Android instance for the whole hardware/UI sequence.
4. Install the exact candidate, configure it, run processor tests and live device checks, and verify the installed driver UI using the Android app. A passing processor test package does not prove that the production package works.
5. Complete the applicable official self-test requirements, including persistence, multiple instances, recovery, endurance and removal. Restore device state, reconcile interrupted operations and remove only automation-owned temporary instances/packages.
6. Evaluate a versioned evidence manifest. Each applicable requirement must have complete, passing evidence for this candidate and environment. Record justified non-applicability separately. Missing, partial, failed or inconclusive evidence blocks submission.
7. Generate the completed official self-test form from the passing evidence, verify rendered pages, and apply the developer's authorized signature to the exact completed form. Help is generated in step 1 and must already be inside the tested package; never insert it after hardware validation.
8. Upload the validated package to Crestron's file-sharing service, save its returned download URL, and email that URL with the signed form through the configured sender.
9. Retain a durable submission receipt and expose a concise CI summary. Crestron's later review/acceptance is a separate external state.

Ordinary GitHub/NuGet publication remains possible when the processor or local runner is unavailable. That override must never turn missing hardware evidence into a successful Crestron submission. A release can be public with its portal submission pending. Re-running submission for an existing release reuses its immutable artifact, not a new build from the branch tip.

## Ownership and repository layout

| Location | Responsibility |
|---|---|
| CrestronHomeDevTools | Package preflight, candidate/evidence identities, submission bundle assembly and delivery primitives/CLI. No dependency on proprietary client binaries. |
| CrestronHomeNUnit | NUnit Android UI tests, existing hardware workflow/adapter integration, shared execution leases, structured test evidence and public CI examples. |
| Each driver repository | Public submission profile, supported model/control inventory, help source, applicability mapping, driver-specific UI/live tests and final CI invocation. |
| Private hardware orchestration | Processor/device bindings, UI login, sender credentials, authorized signature, private raw evidence, interruption recovery and persistent delivery receipts. |

Reusable functionality belongs in the public tools. Private paths, credentials, device bindings and the signature image remain outside source control and public artifacts. Other developers provide their own environment and identity. Ordinary test development does not require a signature or delivery account.

## Ordered implementation work

Each item is complete only after its stated validation. The list is updated as work lands; planned work is not presented as an existing capability.

| ID | Work | Validation / completion criterion | Status |
|---|---|---|---|
| S01 | Submission contract and requirements inventory | Official Extension/Video Server requirements mapped to stable IDs; applicability and all subconditions explicit | Field inventory complete; driver-specific subcondition mapping pending |
| S02 | Offline package preflight | Reject identity, help, metadata and archive defects; inspect real artifacts | Structural checks tested; final candidate validation remains required |
| S03 | Candidate/evidence manifest and strict gate | Reject stale/cross-package evidence, skipped controls, incomplete restoration, missing files and duplicate IDs; no hard-coded total test counts | Evaluator, combined offline CLI and private evidence archive/check commands tested; authenticated producer, final signing/delivery integration and subcondition policies pending |
| S04 | Android automation and NUnit foundation | Target verification, scoped selectors, bounded waits, retained captures and restoration | Debug integrations passed; exact Release-candidate validation pending |
| S05 | Worker and resource reservation | Shared processor/Android locks, service execution, unavailable-emulator and recovery behavior | Inspection fixtures passed as NETWORK SERVICE against an existing emulator; full service deployment, startup and crash recovery pending |
| S06 | Per-driver installation/configuration and UI coverage | Exact Release, all supported pages/controls, physical feedback and restoration; include Configure/Setup | Consuming driver must provide and validate its fixtures |
| S07 | Recovery, multi-instance and endurance coverage | Real required outage durations, 24-hour observation, isolation, persistence and deletion; recover safely after cancellation/reboot | [Collector, persistent reservation and scheduled CLI](submission/EnduranceCollection.md) released; real infrastructure checks and short consumer-owned functional observations passed. [Windows scheduler source](submission/WindowsEnduranceWorker.md) adds background execution and interruption handling. Actual credential provisioning, alerts, OS restart and the final candidate endurance period remain pending. Outage hardware deferred |
| S08 | Help/form generation | Pinned templates, support metadata, matching embedded PDF, rendered forms and complete evidence | Source hooks and unsigned drafts tested; final candidate, approved figures/mapping, signature and evidence pending |
| S09 | Signature and delivery authorization | Private signature asset and sender configured; exact bundle/form hashes bound to signing authorization; test delivery to a controlled destination | Source signer and revalidating private signing stage tested with synthetic inputs; protected-job template provided. Real signature, protected-worker validation and sender provisioning pending |
| S10 | Crestron upload and email adapters | Confirm service behavior, capture download URL and mail receipt; uncertain outcomes require reconciliation; crash-safe duplicate prevention | Delivery journal/transport boundary and reconciliation covered by synthetic tests; current source also rechecks trusted authorization before each external step, with a pinned offline revalidation process bridge tested through simulated dispatch, preserving confirmed upload state when email is refused. [Controlled uploader validation](submission/UploaderInspection.md) passed one authorized small-file upload and byte-identical download; the current-source [upload provider](submission/CrestronUploader.md) passes offline tests and actual-response replay. The live adapter encountered an already-uploaded response, and GET-only reconciliation verified the original bytes while preserving its first failed attempt. The current-source [SMTP provider](submission/SmtpDelivery.md) composes the approved message and records server acceptance, with offline interruption/retry tests. Fresh-file creation through the adapter, live mailbox provisioning/delivery, broader recovery and final delivery remain pending |
| S11 | Optional final driver-submission CI stage | Explicit driver-release opt-in, trusted artifact, reviewed dry-run bundle, authorized delivery with a durable receipt and duplicate prevention. Ordinary CI remains independent | Synthetic private review integration passed under a Windows service account; real candidate review, signing and delivery remain pending |
| S12 | Reusable integration and developer documentation | Fresh-machine setup, per-driver profile/fixture instructions and explicit V1 reboot policies | Shared guides exist; complete end-to-end validation remains pending |

## Evidence rules

Use stable requirement and test IDs, not a fixed number of passing tests. Pin the official form/template revision and retain the mapping from each requirement to all required tests and observations. A multi-part checkbox needs every applicable part. A passing observation on one subpage or one instance cannot satisfy all controls, all subpages or multiple instances.

The [Extension field inventory](submission/extension-inventory.json) and [Video Server field inventory](submission/video-server-inventory.json) record stable requirement IDs, official field names, page positions and source template digests. These are inventories, not approved executable policies. PDF annotation storage order differs from visual order in the Video Server form; mappings were checked in page/vertical order. Every requirement is initially unmapped and no checkbox is treated as passed.

The source [coverage blueprint generator](submission/CoveragePlanning.md) produces draft policies, complete form mappings and scoped execution contracts. Each consuming driver must enumerate every applicable official item and declared UI target, bind executable producers, and supply real Release evidence. Confirm the formal multiple-instance interpretation for platform drivers; two managed children are not automatically two independent platform instances.

The source `SubmissionEvidence.Evaluate` API checks unique/nonempty policy IDs, complete observations, exact candidate/source/policy/template identity, allowed non-applicability with reasons, timestamps/minimum durations, and retained-file digests. It rejects relative-path escapes and links in the evidence tree. The [offline evidence CLI](submission/EvidenceCli.md) combines this with package inspection and verifies the actual candidate, policy and form bytes against pinned digests. The [private evidence bundle commands](submission/EvidenceBundle.md) retain only referenced files, revalidate the archived copies and support later checks against independently retained archive/candidate digests. These structural checks do not authenticate who produced an observation or establish that a claimed observation actually measured all subconditions. Trusted producer authentication, approved policy completeness, protected retention and signing/delivery integration remain required. Timestamps alone do not establish continuous 24-hour operation.

Each evidence record must identify the exact source commit and production package digest, driver version, test/tool versions, processor platform/firmware, Android/app version, timestamps, fixture identity and outcome. Persist evidence file hashes and restoration/cleanup outcomes. Track which installed instance and physical device were observed. Retain private raw evidence and generate a redacted public summary, if wanted.

Numeric temperatures/power readings are checked against device observations and tolerances rather than fixed values. Timing requirements need event timestamps or suitable video instrumentation; slow UI hierarchy dumps are unsuitable for proving an immediate response or three presses per second. Accessibility labels help locate elements but cannot by themselves verify an icon, clipping or visual layout. Those need rendered-image checks at a controlled display configuration.

Android navigation, saved-endpoint inspection, Home restoration and temporary-name association have development validation. They do not prove all icons, response timing, physical feedback, Configure/Setup behavior or submission readiness. [Temporary-name binding](DriverUiBinding.md) provides stronger instance association but does not authenticate the app route or candidate artifact.

The development [Android workflow stage](https://github.com/oznetmaster/CrestronHomeNUnit/blob/main/docs/AndroidUiTesting.md) records package hashes, installed IDs and a local source digest under the shared processor reservation. It builds in a fresh directory, discovers the complete NUnit inventory and requires matching successful execution, including repeated-name multiplicity. Retained Debug TRX/captures are development evidence. Submission additionally requires exact Release installation, trusted source/environment provenance and approved requirement bindings. Matching restoration is required separately from passing results; running every test in an incomplete project does not satisfy missing official requirements.

The NUnit source now includes an opt-in [prebuilt Release handoff](https://github.com/oznetmaster/CrestronHomeNUnit/blob/main/docs/ReleaseCandidateTesting.md). It validates a clean checkout at the pinned commit, retains the supplied package under its original filename and hash, verifies its GUID/version, and propagates the release source commit into Android evidence. It never rebuilds or renumbers the actual candidate; a catalogue version conflict stops activation. The local/processor test builds remain Debug. This path has automated coverage but has not yet passed Release hardware validation. The declared pins must come from trusted release CI; they do not themselves authenticate the producer or prove compliance with an official requirement.

The source coverage compiler also carries scoped execution requirements into the candidate-pinned policy. The evidence validator now checks target/method, required outcome, recorded response deadlines, restoration ordering/confirmation and referenced measurement files. Endurance requires an approved maximum sample gap and passing retained observations covering the entire claimed interval. The generated gap remains unset until reviewed, so an endurance claim cannot pass by supplying elapsed time alone. These checks apply through combined evidence validation and bundle/form validation. Older structural policies without execution fields are not complete submission policies. Producer authentication, measured physical behavior, outage instrumentation and policy approval remain outstanding.

The source [optional private CI review stage](submission/ReviewStage.md) now connects the real validator, unsigned form generator and evidence bundle. It rejects client/library/test artifacts and Debug revisions, compares form/bundle identities and writes a completion receipt only after all checks pass. Its synthetic integration passed in a separate workflow under the installed Windows runner service. The reusable template still needs a real candidate pilot; no real driver review, signature or delivery has been enabled by this addition.

The source [Android evidence audit](submission/AndroidEvidence.md) checks retained discovery/TRX coverage, candidate/run identity, fixture and capture digests, capture intervals and matching restoration. It requires independently trusted pins and rejects Debug candidates. Regression checks and development-artifact parsing passed. Producer authentication, complete requirement mapping and actual Release execution remain necessary; the audit creates no passing official observations.

## Hardware and failure handling

The DevTools [endurance collector and scheduled CLI](submission/EnduranceCollection.md), released in 1.6.0, retain candidate-bound functional samples across invocations and stop on gaps, changed environments and interrupted probes. Real processor infrastructure checks and short consumer-owned functional observations have passed, including deliberate driver-restart rejection. DevTools 1.7.0 adds public plan validation and bounded processor uptime observations. [Windows scheduler scripts](submission/WindowsEnduranceWorker.md) are source additions after 1.7.0, with offline interruption checks; they are not in that release archive. Real credential provisioning, independent alerts, OS restart validation and the final candidate endurance period remain incomplete.

- Reserve the processor for the full test/install/configure/reboot/remove sequence. All participating tools must use the existing shared lease. Reserve the Android session separately and acquire resources in one documented order.
- Verify the selected processor through the saved connection and visible home identity before navigation or controls. Avoid choosing a physical action through ambiguous labels or stale coordinates.
- Capture original device settings before commands and restore them even when an assertion fails. Verify restoration from the device. Record unconfirmed restoration and stop dependent work.
- Use automation-owned test instances when the requirement allows it. Do not replace an existing room device just to obtain a test target. Multi-instance tests require suitable independent device bindings.
- V1 drivers require the already supported explicit reboot lifecycle. This distinction concerns driver type, separately from DevTools' V2-processor requirement.
- The official power and network interruption tests include the processor, device and network, with required disconnection durations and recovery deadlines. A software reboot is not equivalent. Controlled outage equipment remains a prerequisite; do not interrupt the household network to approximate it.
- The endurance test needs at least 24 elapsed hours with periodic functional observations. Use resumable checkpoints across bounded CI jobs and protect the test environment from unrelated changes. A restart or unaccounted gap must not silently count as uninterrupted evidence.
- On cancellation, persist reconciliation state and clean up only known automation-owned resources. A lost lease or uncertain command outcome prevents further mutation until reconciled.

## Documents, signature and delivery

Create help from the SDK's help template, including supported models, setup, limitations, troubleshooting and public support details. For a new portal driver, use an agreed developer/company component in matching `.pkg`, `.dll` and help `.pdf` basenames. Existing portal drivers have a documented naming exemption; preserve their published identity. Renaming an already-built package is insufficient.

The [headless help build notes](submission/HelpBuild.md) record the official template pin, verified local renderer and remaining document-build integration. Word and an interactive desktop are not required for the conversion step.

Fetch official self-test PDFs from the published sources, pin and inspect their hashes/field mappings, and stop on an unexpected template change. Do not redistribute SDK templates or other proprietary assets without checking their terms. Keep the original official pages intact when filling forms. Record non-applicability on a companion matrix; confirm how Crestron expects it represented before final delivery.

The source [self-test form generator](submission/FormGeneration.md) creates blank review drafts or validates evidence before populating an unsigned interactive form. It checks checkbox mappings and widget appearances, preserves printed official pages and leaves signature/date blank. Non-applicable items remain unchecked with rationales on a companion matrix. Drafts carry no attestations; complete approved candidate evidence is required.

A signature image is a private input, not proof of test completion. Bind signing to an explicitly authorized signer, policy and exact completed form/candidate. Use a review gate until unattended signing has an authorized policy. Public support and private submission correspondence are separate settings; the latter must not appear in driver metadata or help.

The source [form-signing tool](submission/FormSigning.md) prepares an evidence-backed signing copy and applies a private image/date only with a separately pinned authorization for its exact bytes. Synthetic checks and rendered pages verify preserved form content and visible signatures. This is not a cryptographic signature; real candidate signing and protected CI authorization still require validation.

The private review stage can now retain that unsigned signing copy alongside its validated evidence bundle when explicitly selected. Its receipt binds both the PDF and form-report hashes, so later authorization can refer to the reviewed files without regenerating them. The actual review-command-to-signing handoff passed with synthetic evidence and a synthetic signature; all 124 offline document-tool tests passed. This source-level validation does not establish real signature approval, protected-job identity or delivery.

The source [private signing stage](submission/SigningStage.md) revalidates the retained bundle at signing time and prepares only the package and signed form in its delivery folder. Its own completion receipt keeps final visual review and delivery authorization pending. The [final delivery preparation stage](submission/DeliveryPreparation.md) then requires a separate approval for the exact signed files and sender, revalidates the retained evidence and prepares a private delivery plan without sending it. Synthetic integration tests cover these handoffs and interrupted publication; protected workflow templates still require real environment setup and validation.

Crestron's published delivery procedure uses its file-sharing service and email. A controlled test verified one authorized upload and byte-identical download through the actual HTML landing page and download-button target; see [observed uploader behavior](submission/UploaderInspection.md). This does not establish a stable public API or a production adapter. Recheck changed service behavior and terms before automating delivery. The source [delivery journal](submission/DeliveryJournal.md) persists intent before invoking a transport, retains provider receipts and blocks automatic replay of uncertain outcomes. Its synthetic tests cover duplicates, simultaneous calls and explicit reconciliation. The current-source [upload provider](submission/CrestronUploader.md) has also verified an existing upload against live downloaded bytes through read-only reconciliation; fresh-file creation through that adapter and live mail validation remain pending. The [SMTP provider](submission/SmtpDelivery.md) and combined transport are now available in source with offline tests. A deterministic mail Message-ID alone does not guarantee exactly-once delivery; reconcile uncertain sends through the provider/sent mailbox.

Only the intended package, signed form and approved support material go to Crestron. Test credentials, signature source image, raw processor logs and household screenshots are not automatically attached. Submission completion means the upload and email were confirmed, not that Crestron has approved or certified the driver.

## External prerequisites

The work can proceed before these are supplied, but the corresponding final tests/delivery cannot:

- Supply each driver's portal status, approved public identity and support route in its own profile.
- Supply the signature image and authorize its use once the form and signing policy are reviewable.
- Configure a supported sender and access to submission responses; complete the production uploader adapter and validate failure/recovery behavior beyond the successful small-file transport test.
- Provide controlled interruption hardware/network isolation if full power/network tests are required. These were deferred in the current development setup.
- Provide independent targets for multi-instance and device-control tests where existing equipment cannot satisfy the plan.

## Official sources

- [Submit a Driver](https://sdkcon78221.crestron.com/sdk/Crestron_Certified_Drivers_SDK/Content/Topics/Submit-a-Driver/Submit-a-Driver.htm)
- [Test a Driver](https://sdkcon78221.crestron.com/sdk/Crestron_Certified_Drivers_SDK/Content/Topics/Test-a-Driver/Test-a-Driver.htm)
- [Extension self-test form](https://sdkcon78221.crestron.com/downloads/Test-Plans/Extension-Test-Plan.pdf)
- [Video Server self-test form](https://sdkcon78221.crestron.com/downloads/Test-Plans/Video-Server-Test-Plan.pdf)

This is independent development tooling, not an official Crestron submission API or certification service. Crestron controls acceptance and publication on its portal.
