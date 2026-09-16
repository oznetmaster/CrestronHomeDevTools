# Automated Crestron driver submission

This is the implementation plan and progress record for making Crestron submission the final stage of driver release CI. It starts with Wiser Heat, then covers WeatherLink, KasaTapo, Overkiz, Tesla and Apple TV. Processor test packages and independent client libraries are not portal submissions.

Status as of 16 September 2026: a combined Wiser development workflow passed desktop, processor, read-only live and Android NUnit tests, including gated driver update, repeated saved-endpoint/Home restoration and temporary test-package cleanup. End-to-end submission is not implemented or enabled. No submission has been sent by this work.

The developer has confirmed that all six are new portal submissions. Use developer name **Neil Colvin**, filename component **NeilColvin**, and public support email **support@marvelous.com**. A separate private correspondence address has been supplied outside the repository. A GitHub Issues link can supplement the support email in each help document.

## Offline package preflight

The source includes `SubmissionPackage.Inspect` and this CLI command (not yet released):

```text
submission-check --package NeilColvin_Platform_Example_IP.pkg --driver DRIVER_GUID --version 1.0.000.0000 --kind new --developer-name-token NeilColvin --support-email support@marvelous.com
```

For an update to a driver already on the portal, use `--kind update`. That exempts only the developer filename component. All matching filename, manifest and help checks still apply. A GitHub release of a driver does not by itself make it an existing portal driver.

The command emits a JSON report and exits 0 when structural checks pass, 1 when defects are found, or 2 for invalid arguments. No processor profile is required. It checks exact root package/DLL/DAT/help naming, expected GUID/version, generated developer/dependency metadata, the approved public support email, DLL reference, PDF header, duplicate/unsafe archive paths and certain known private test-input filenames. It hashes the inspected bytes while holding the file open.

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
| S02 | Offline package preflight | Tests reject mismatched names/version/GUID, missing help/developer data and malformed archives; audit actual released packages | Structural checker tested; Wiser release audited; other drivers pending |
| S03 | Candidate/evidence manifest and strict gate | Reject stale/cross-package evidence, skipped controls, incomplete restoration, missing files and duplicate IDs; no hard-coded total test counts | Evaluator and combined offline CLI tested; authenticated producer/bundle integration and subcondition policies pending |
| S04 | Android automation library and NUnit fixture foundation | Target verification, stable selectors, bounded waits, screenshots/hierarchy, cancellation and serial selection; repeated read-only runs pass | Three Android NUnit cases passed in a complete Wiser development workflow, including saved-endpoint inspection and Home restoration twice; scoped fields, overlays and uncertain navigation covered offline; exact Release candidate, active-route and installed-instance binding pending |
| S05 | Unattended worker and resource reservation | Prove operation from the installed runner service or a controlled interactive-session worker; enforce shared processor lease plus Android lock; survive unavailable emulator | Combined processor/Android reservation and confirmed release passed in the logged-in CLI workflow; direct player startup/ADB capture separately verified; installed runner service, logout/session-0 and crash recovery pending |
| S06 | Wiser installation/configuration and UI coverage | Exact release package, home/room placement, all supported controls/subpages, device feedback and restored starting state; include Setup/Configure UI requirements | Planned |
| S07 | Recovery, multi-instance and endurance coverage | Real required outage durations, 24-hour observation, isolation, persistence and deletion; recover safely after cancellation/reboot | Planned; outage hardware deferred |
| S08 | Help/form generation | SDK help template, correct support metadata, matching embedded PDF filename; pinned official form fields and template hashes; render checks; complete evidence only | Help/MSBuild hooks and unsigned form generator tested; Wiser help/form drafts inspected; final candidate, figures, approved mapping, real evidence-backed form and CI integration pending |
| S09 | Signature and delivery authorization | Private signature asset and sender configured; exact bundle/form hashes bound to signing authorization; test delivery to a controlled destination | Waiting for later provisioning |
| S10 | Crestron upload and email adapters | Confirm service behavior, capture download URL and mail receipt; uncertain outcomes require reconciliation; crash-safe duplicate prevention | Planned; no documented upload API established |
| S11 | Reusable final CI stage and Wiser pilot | Trusted release only, dry-run bundle first, then one authorized real submission with durable receipt; rerun does not submit twice | Planned |
| S12 | Rollout and developer documentation | WeatherLink, KasaTapo, Overkiz, Tesla and Apple TV profiles/tests; fresh-machine setup instructions; V1 driver reboot handling retained | Planned |

## Evidence rules

Use stable requirement and test IDs, not a fixed number of passing tests. Pin the official form/template revision and retain the mapping from each requirement to all required tests and observations. A multi-part checkbox needs every applicable part. A passing observation on one subpage or one instance cannot satisfy all controls, all subpages or multiple instances.

The [Extension field inventory](submission/extension-inventory.json) and [Video Server field inventory](submission/video-server-inventory.json) record stable requirement IDs, official field names, page positions and source template digests. These are inventories, not approved executable policies. PDF annotation storage order differs from visual order in the Video Server form; mappings were checked in page/vertical order. Every requirement is initially unmapped and no checkbox is treated as passed.

The source `SubmissionEvidence.Evaluate` API checks unique/nonempty policy IDs, complete observations, exact candidate/source/policy/template identity, allowed non-applicability with reasons, timestamps/minimum durations, and retained-file digests. It rejects relative-path escapes and links in the evidence tree. The [offline evidence CLI](submission/EvidenceCli.md) combines this with package inspection and verifies the actual candidate, policy and form bytes against pinned digests. It includes the JSON format and trust requirements for CI. These structural checks do not authenticate who produced an observation or establish that a claimed observation actually measured all subconditions. The trusted producer, approved policy and immutable bundle remain required integration work. Timestamps alone do not establish continuous 24-hour operation.

Each evidence record must identify the exact source commit and production package digest, driver version, test/tool versions, processor platform/firmware, Android/app version, timestamps, fixture identity and outcome. Persist evidence file hashes and restoration/cleanup outcomes. Track which installed instance and physical device were observed. Retain private raw evidence and generate a redacted public summary, if wanted.

Numeric temperatures/power readings are checked against device observations and tolerances rather than fixed values. Timing requirements need event timestamps or suitable video instrumentation; slow UI hierarchy dumps are unsuitable for proving an immediate response or three presses per second. Accessibility labels help locate elements but cannot by themselves verify an icon, clipping or visual layout. Those need rendered-image checks at a controlled display configuration.

The Android proofs cover navigation, displayed weather sections and repeated .NET inspection of the selected system's saved local endpoint with Home restoration. They do not establish the correctness of all Home/Room icons, control timing, physical feedback, Configure/Setup dialogs or submission readiness. The app's saved connection details are not independent proof of its active route or a particular installed driver instance.

The development [Android workflow stage](https://github.com/oznetmaster/CrestronHomeNUnit/blob/main/docs/AndroidUiTesting.md) records inspected package hashes, installed IDs and a local source digest under the shared processor reservation. It builds into a fresh directory, discovers the complete NUnit inventory and requires matching successful execution of that build, including duplicate-name multiplicity. The full Wiser development run passed 96 desktop tests, 53 processor tests, three read-only live hub tests, installed-driver checks and all three Android NUnit cases. Seven Android capture observations, detailed TRX, complete discovery coverage and matching restoration were retained; both reservations were released. Temporary test storage was deleted, while Home retained a cached catalogue entry until a planned reboot. Its TRX and private captures are not yet approved submission observations: exact Release candidate installation, authenticated source/environment provenance and the requirement mapping still need to be connected. A matching restoration record is required independently of passing test results. Full execution of an incomplete test project does not satisfy missing official requirements.

The NUnit source now includes an opt-in [prebuilt Release handoff](https://github.com/oznetmaster/CrestronHomeNUnit/blob/main/docs/ReleaseCandidateTesting.md). It validates a clean checkout at the pinned commit, retains the supplied package under its original filename and hash, verifies its GUID/version, and propagates the release source commit into Android evidence. It never rebuilds or renumbers the actual candidate; a catalogue version conflict stops activation. The local/processor test builds remain Debug. This path has automated coverage but has not yet passed Release hardware validation. The declared pins must come from trusted release CI; they do not themselves authenticate the producer or prove compliance with an official requirement.

## Hardware and failure handling

- Reserve the processor for the full test/install/configure/reboot/remove sequence. All participating tools must use the existing shared lease. Reserve the Android session separately and acquire resources in one documented order.
- Verify the selected processor through the saved connection and visible home identity before navigation or controls. Avoid choosing a physical action through ambiguous labels or stale coordinates.
- Capture original device settings before commands and restore them even when an assertion fails. Verify restoration from the device. Record unconfirmed restoration and stop dependent work.
- Use automation-owned test instances when the requirement allows it. Do not replace an existing room device just to obtain a test target. Multi-instance tests require suitable independent device bindings.
- V1 drivers such as Apple TV require the already supported explicit reboot lifecycle. This distinction concerns driver type, separately from DevTools' V2-processor requirement.
- The official power and network interruption tests include the processor, device and network, with required disconnection durations and recovery deadlines. A software reboot is not equivalent. Controlled outage equipment remains a prerequisite; do not interrupt the household network to approximate it.
- The endurance test needs at least 24 elapsed hours with periodic functional observations. Use resumable checkpoints across bounded CI jobs and protect the test environment from unrelated changes. A restart or unaccounted gap must not silently count as uninterrupted evidence.
- On cancellation, persist reconciliation state and clean up only known automation-owned resources. A lost lease or uncertain command outcome prevents further mutation until reconciled.

## Documents, signature and delivery

Create help from the SDK's help template, including supported models, setup, limitations, troubleshooting and public support details. For a new portal driver, use an agreed developer/company component in matching `.pkg`, `.dll` and help `.pdf` basenames. Existing portal drivers have a documented naming exemption; preserve their published identity. Renaming an already-built package is insufficient.

The [headless help build notes](submission/HelpBuild.md) record the official template pin, verified local renderer and remaining document-build integration. Word and an interactive desktop are not required for the conversion step.

Fetch official self-test PDFs from the published sources, pin and inspect their hashes/field mappings, and stop on an unexpected template change. Do not redistribute SDK templates or other proprietary assets without checking their terms. Keep the original official pages intact when filling forms. Record non-applicability on a companion matrix; confirm how Crestron expects it represented before final delivery.

The source [self-test form generator](submission/FormGeneration.md) now creates blank review drafts or invokes the offline evidence validator before populating an unsigned interactive form. It checks every checkbox's mapping and canonical/widget appearance consistency, preserves the printed official pages, and leaves signature/date blank. Non-applicable items remain unchecked with their rationales on the companion matrix. The actual Wiser draft has no attestations; a real candidate with complete approved evidence remains required.

The signature image will be supplied later. Its presence does not prove test completion. Signing must be bound to an explicitly authorized signer, policy and exact completed form/candidate. Initially use a review gate; unattended signing can be enabled after the developer authorizes a defined policy. The sender address and support address are separate settings. The developer has provided a private correspondence address and approved support@marvelous.com for public support; only the latter belongs in tracked driver metadata/help.

The published delivery path uses the Crestron file-sharing service and email. A stable supported API has not been established. Inspect the actual service before choosing an HTTP adapter or browser worker, and verify any terms/automation restrictions. Never silently replay uploads or sends after timeouts. Persist states such as Prepared, UploadPending, Uploaded, SendPending, Submitted and OutcomeUnknown with candidate/form digests and remote receipts. A deterministic mail Message-ID alone does not guarantee exactly-once delivery; reconcile uncertain sends through the provider/sent mailbox.

Only the intended package, signed form and approved support material go to Crestron. Test credentials, signature source image, raw processor logs and household screenshots are not automatically attached. Submission completion means the upload and email were confirmed, not that Crestron has approved or certified the driver.

## External prerequisites

The work can proceed before these are supplied, but the corresponding final tests/delivery cannot:

- Portal status and public identity are confirmed: all six are new; Neil Colvin / NeilColvin / support@marvelous.com. Apply those to the actual submission candidates.
- Supply the signature image and authorize its use once the form and signing policy are reviewable.
- Configure a supported sender and access to submission responses; determine whether the uploader can be automated reliably.
- Provide controlled interruption hardware/network isolation if full power/network tests are required. These were deferred in the current development setup.
- Provide independent targets for multi-instance and device-control tests where existing equipment cannot satisfy the plan.

## Official sources

- [Submit a Driver](https://sdkcon78221.crestron.com/sdk/Crestron_Certified_Drivers_SDK/Content/Topics/Submit-a-Driver/Submit-a-Driver.htm)
- [Test a Driver](https://sdkcon78221.crestron.com/sdk/Crestron_Certified_Drivers_SDK/Content/Topics/Test-a-Driver/Test-a-Driver.htm)
- [Extension self-test form](https://sdkcon78221.crestron.com/downloads/Test-Plans/Extension-Test-Plan.pdf)
- [Video Server self-test form](https://sdkcon78221.crestron.com/downloads/Test-Plans/Video-Server-Test-Plan.pdf)

This is independent development tooling, not an official Crestron submission API or certification service. Crestron controls acceptance and publication on its portal.
