# Automated Crestron driver submission

Start with the [normal-path runbook](submission/Runbook.md), then [workflow setup](submission/WorkflowSetup.md). The [self-contained Windows console](submission/ConsoleTools.md) and C# APIs provide the shared workflow. Driver authors supply their own C# tests and reviewed configuration; no Python or Linux knowledge is required.

Submission is optional and applies only to Crestron drivers. Client, library and processor-test releases do not enter these stages. Ordinary driver development, hardware/UI testing and GitHub releases also remain independent of submission.

**Complete** means complete against our interpretation of Crestron's requirements and the declared verification plan. Passing checks, signing a form or delivering a submission does not imply Crestron acceptance, publication or certification. Record developer verification, provider delivery and Crestron's actual decision separately.

The published protected console has completed an authorized real submission: it revalidated the retained evidence, uploaded the exact approved package, verified the downloaded bytes, then sent the confirmed download URL and exact signed checklist. The mail server acknowledged acceptance. This was a local protected-console execution, not a GitHub Actions service run. The [validation status](submission/ValidationStatus.md) distinguishes actual provider validation, automated tests, CI template rehearsals and environment-specific prerequisites.

## Repository and privacy boundaries

Keep generic submission APIs, commands and setup documentation in DevTools. Keep ordinary tests and development instructions with their driver or library.

Driver-specific submission profiles, official requirement mappings, submission help sources, private workflow configuration, evidence, signatures, approvals, correspondence and receipts belong in private developer-controlled storage or a private orchestration repository. They are not required public driver-repository contents. Machine paths, credentials and device bindings must not be committed or published.

A driver's public README, changelog, release notes and help should describe its operation, installation, support and actual product changes. Do not use them to report submission plans or progress. Any later announcement of portal availability requires Crestron's actual acceptance. The reusable workflow does not depend on this project's author's private repositories or scripts.

The help PDF itself is intended for users and must contain only approved public content. Embed it in the candidate package before testing. Keep private preparation and evidence separate from that public-facing help.

## End-to-end sequence

1. Select trusted release source, generate and review the help PDF, build the package once with that help embedded, and retain the source, toolchain, driver GUID/version and package hash.
2. Inspect exact package/DLL/help filenames, generated metadata, support information, dependency notices and actual PDF rendering.
3. Run desktop tests and reserve the processor and Android instance for the hardware/UI sequence. Respect device-use policies and restore any changed state.
4. Install/configure the exact production candidate and run its applicable processor, live-device and Android checks. A passing test package alone does not prove the production driver works.
5. Exercise the applicable official requirements, including persistence, recovery, endurance and removal. Record justified non-applicability and preserve failed or incomplete attempts.
6. Validate and retain the evidence with the official form inventory and full requirement mapping. Review producer provenance and behavioral coverage, not just file hashes.
7. Generate the official checklist and its numbered notes. Review every page, then obtain authorization to apply the signature to that exact form. A revised form needs its own approval.
8. Review the exact outgoing package, signed PDF, sender, recipient and email wording. Obtain separate sending approval. Keep help review distinct from signing and delivery approval.
9. Upload the package, retain the downloader URL, verify the downloaded package, then email that URL with the signed checklist. Never email a placeholder or assume a URL before upload.
10. Retain upload and SMTP receipts. Record Crestron's later response separately; provider acceptance does not establish inbox delivery or certification.

The complete route requires all applicable interpreted requirements. A developer who cannot or chooses not to complete a step can explicitly select the [declared-gaps route](submission/DeclaredGaps.md). It preserves original results and discloses omissions. Reviewed [prior evidence](submission/PriorEvidence.md) and interpretations must identify their scope and rationale; they cannot turn a failed or unperformed test into a new passing observation. Crestron decides whether any submission is acceptable.

Ordinary publication can proceed when a processor or local runner is unavailable under the release workflow's policy. That does not supply submission evidence. Reuse an immutable release artifact when resuming; do not rebuild from a moving branch tip.

## Capabilities and required developer inputs

| ID | Workflow area | Public capability and consuming responsibility |
|---|---|---|
| S01 | Requirements | [Coverage planning](submission/CoveragePlanning.md) uses pinned Extension/Video Server inventories. The driver author maps every applicable subcondition and records exclusions. |
| S02 | Package | `SubmissionPackage.Inspect` and `submission-check` validate structure and identity. The author reviews actual help content, supported models and toolchain provenance. |
| S03 | Evidence | [Validation](submission/EvidenceCli.md), [mapping](submission/EvidenceMapping.md), [composition](submission/EvidenceComposition.md) and [bundles](submission/EvidenceBundle.md) retain original records and check identities, outcomes and restoration. Trusted provenance remains a separate review. |
| S04 | Android | [Android evidence](submission/AndroidEvidence.md) connects candidate-bound NUnit results and captures. The author supplies fixtures for the actual pages, controls, visual feedback and timing. |
| S05 | Resources | [Development configurations](submission/DevelopmentConfigurations.md) cover shared resources and protected workers. Configure accounts, access, scheduling and recovery on the chosen machines. |
| S06 | Driver behavior | Driver-specific C# fixtures use public configuration and Android APIs. Verify production installation, configuration, physical feedback and state restoration. |
| S07 | Endurance/recovery | [Collection](submission/EnduranceCollection.md), [Windows scheduling](submission/WindowsEnduranceWorker.md) and [notifications](submission/EnduranceNotifications.md) retain resumable observations. Actual duration, outage equipment and device availability are environment-specific. |
| S08 | Documents | [Help](submission/HelpBuild.md), [dependency notices](submission/DependencyNotices.md) and [forms](submission/FormGeneration.md) generate reviewable artifacts. Review the packaged help and every completed checklist page. |
| S09 | Signing | [Signing preparation](submission/SigningStage.md) revalidates retained evidence and binds authorization to the exact document. Supply the private signature and authorized signer. |
| S10 | Delivery | [Uploader](submission/CrestronUploader.md), [SMTP](submission/SmtpDelivery.md), [approval](submission/ReviewApproval.md) and the [journal](submission/DeliveryJournal.md) enforce ordered, approved dispatch and retain outcomes. Configure the actual sender and uploader credentials. |
| S11 | Final CI stage | [Five workflow templates](submission/WorkflowSetup.md) connect the public commands in the developer's protected repository. Configure and verify the repository, worker, environment protections and private settings before enabling them. |
| S12 | Reuse | Follow the [runbook](submission/Runbook.md) with the released console and driver-specific C# fixtures. See [validation status](submission/ValidationStatus.md) for the tested scope and remaining deployment responsibilities. |

## Package preflight

For a new portal driver with website-only support:

```text
submission-check --package ExampleDeveloper_Platform_Example_IP.pkg --driver DRIVER_GUID --version 1.0.000.0000 --kind new --developer-name-token ExampleDeveloper --support-website https://example.org/driver-support
```

Use `--kind update` only for a driver already on Crestron's portal. It exempts the new-driver developer filename component, not matching package/DLL/help filenames or other checks. A GitHub release alone does not establish portal status.

The command checks package identity, generated metadata, expected support contact, archive names and selected private-input filenames. Exit 0 indicates structural success, 1 identified defects and 2 invalid arguments. It does not prove PDF readability, behavioral coverage, absence of every possible secret or submission readiness.

## Evidence and device rules

Use stable requirement/test identities rather than duplicated test-count constants. Retain the exact candidate/source/policy/template identities, tool/environment versions, timestamps, original outcomes, evidence-file hashes and restoration/cleanup results. Track the installed instance and actual physical device. A passing observation on one page does not cover all pages or every control.

Scope selectors to the observed page and labelled row. Check foreground package, endpoint, page title and target association before an interaction. Use bounded waits and capture failed preconditions. Hierarchy labels alone do not prove visual layout; timing assertions require suitable event timestamps or recordings.

Restore and verify device settings after control tests, including failed assertions. Coordinate shared household devices and protect pre-existing installations. Clean up only automation-owned resources. A lost reservation or uncertain command must be reconciled before further mutation.

V1 driver updates require the explicit reboot lifecycle; this is separate from DevTools' V2-processor requirement. A software reboot is not a physical power-interruption test. Record the actual network/power conditions and required durations, or disclose an omitted test.

Endurance requires the applicable elapsed period and functional observations. Configure Windows startup/resume behavior, but never silently count a restart, missing interval or unobserved state as uninterrupted evidence. See the collector and scheduler guides for interruption handling.

## Documents, approval and recovery

Use the reviewed SDK help template and official self-test form. Keep original official pages intact, with numbered explanatory notes after the checklist. Version 1.16.1 identifies the driver/version and developer on page one using the existing title/author inputs. Entirely non-applicable items remain labelled N/A; an unperformed item remains unchecked. A signature image is not evidence of completion and must be authorized for the exact resulting document.

[Complete delivery setup](submission/DeliverySetup.md) and [reviewed declared-gap delivery](submission/ReviewApproval.md) are separate routes. Never switch routes automatically after a failed validation. Supply credentials privately through the documented mechanism, not command-line arguments or public workflow inputs.

The delivery journal writes intent before each provider operation and preserves a confirmed upload if email cannot proceed. Unknown outcomes require explicit reconciliation. A completed journal returns the historical receipt without another upload or email. Never delete records or select a new journal merely to force a retry.

The uploader is based on [observed service behavior](submission/UploaderInspection.md). Changed form/terms or unexpected responses stop delivery. Provider success is not a universal guarantee of future service compatibility. SMTP does not necessarily create an Outlook Sent item; preserve its private composed message and acceptance receipt.

## Official sources

- [Submit a Driver](https://sdkcon78221.crestron.com/sdk/Crestron_Certified_Drivers_SDK/Content/Topics/Submit-a-Driver/Submit-a-Driver.htm)
- [Test a Driver](https://sdkcon78221.crestron.com/sdk/Crestron_Certified_Drivers_SDK/Content/Topics/Test-a-Driver/Test-a-Driver.htm)
- [Extension self-test form](https://sdkcon78221.crestron.com/downloads/Test-Plans/Extension-Test-Plan.pdf)
- [Video Server self-test form](https://sdkcon78221.crestron.com/downloads/Test-Plans/Video-Server-Test-Plan.pdf)

This independent tooling is not an official Crestron submission API or certification service. Crestron controls acceptance and publication.
