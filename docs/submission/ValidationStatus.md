# Submission workflow validation

This page records the shared tooling's verified scope as of 21 September 2026. It contains no driver-specific submission files or progress details. Use the [normal-path runbook](Runbook.md) to configure your own workflow.

## Released capabilities

DevTools 1.17.1 includes the C# APIs and self-contained Windows console for package inspection, coverage planning, evidence validation and retention, help generation, official checklist generation, exact-form signing, independent delivery approval, upload and SMTP delivery. It also includes named encrypted private inputs and Windows observer scheduling. Driver authors provide their own C# fixtures and reviewed configuration; they do not need to maintain Python or know Linux.

The optional [CI templates](WorkflowSetup.md) connect review, signing, delivery preparation and delivery. They are disabled until the consuming developer configures the protected repository, worker, private settings and approvals. Client/library releases and ordinary driver releases remain independent of submission.

## Evidence and its limits

| Area | Verified behavior | What the consuming developer must still establish |
|---|---|---|
| Candidate and evidence | Package inspection, stable requirements, candidate/file identities, original observations, reviewed prior evidence, declared gaps, archive revalidation and refusal of inconsistent inputs have automated coverage. | Accurate applicability, complete behavioral coverage, trusted evidence provenance and the suitability of any reviewed interpretation. |
| Hardware and Android | The shared tools have been used with real processor installation/configuration, Android observations, live control/restoration, network recovery and retained endurance evidence. | The exact candidate, supported models, every applicable page/control and the chosen test environment. Shared tests cannot prove another driver's behavior. |
| Help and forms | A real Release package was built with embedded help using ManifestUtil. Its exact help PDF was rendered and reviewed. An evidence-backed official Extension checklist was rendered, reviewed, explicitly authorized and signed through the published console. | Review each new help file and exact completed form. Video Server field-inventory checks are not a real Video Server submission. |
| Upload | The published provider uploaded an actual approved driver package to Crestron's service and downloaded it again to verify identical bytes before proceeding. | Current service behavior, accepted terms and authorized credentials. This observed HTML service is not a documented stable upload API. |
| Email | The public protected coordinator then sent the confirmed download URL and exact signed PDF to the intended recipient. The SMTP server acknowledged acceptance; retained MIME bytes were checked against the approved message and attachment. | The developer's sender account and permission to send. Server acceptance does not establish inbox delivery or Crestron's decision. |
| Recovery and duplicate prevention | Automated tests cover concurrent journal access, failed/uncertain operations, changed approval/evidence, reconciliation and completed-operation replay without another send. A real local file-access failure was corrected before any provider operation, retaining the original attempt. | Investigate actual uncertain outcomes using the same journal. Never create a new journal or automatically resend to work around uncertainty. |
| CI handoff | Packaged-console acceptance tests exercise the actual review/signing/preparation template scripts and simulated provider dispatch. Separate Windows-service access checks verified worker separation. | Configure and validate the consuming repository's protections, service identity, filesystem access and secret delivery. The actual external submission ran through the protected console under the interactive Windows account, not a GitHub Actions service job. |
| Saved private inputs | Synthetic checks verified encrypted entries, selected-entry transfer between two Windows computers, LocalService decryption and access isolation, and saved-signature use through the packaged signing workflow. | Provision only the intended entries on each computer and verify access under the actual execution account. Stored credentials and signatures do not authorize operations. |
| Endurance observation | Real passive observation of a separate Windows collector has been verified. The released 1.17.1 console also returns a fresh attention report for a failed local task query, without the earlier unhandled-exception dialog. | Validate the scheduled observer's task/file access and the complete authorized notification-to-inbox path. Synthetic failure handling does not establish service-account access or actual email delivery. |

The real submission used the explicitly reviewed declared-gaps route. It demonstrates signing and delivery with preserved disclosures; it is not evidence that every official test was performed or that Crestron accepts those disclosures. Complete means complete against our interpretation of Crestron's requirements. Acceptance, publication and certification remain Crestron's decisions.

## Deployment prerequisites

- One Windows PC and one suitable processor are a supported baseline. Additional PCs/processors are optional; see [development configurations](DevelopmentConfigurations.md).
- Configure the Android emulator and Crestron Home app when UI evidence is required. Supply driver-specific C# tests, actual device bindings and state-restoration rules.
- Configure endurance scheduling and startup recovery on the chosen Windows monitor. A restart must be recorded and assessed; restarting the collector must not silently erase or count an observation gap.
- Obtain the applicable official templates and configure the help renderer/fonts. Keep candidate packages immutable after validation.
- Provide private signing material and exact-form authorization, followed by separate approval of the outgoing packet and correspondence.
- Configure uploader and SMTP credentials for the account that will execute delivery. A credential encrypted for an interactive Windows user is not automatically available to a service account. Check that the worker can read the approved inputs and write its receipts without granting ordinary build jobs that access.
- Supply any physical interruption equipment or additional device targets required by the chosen verification plan. If a step cannot be completed, deliberately use the [declared-gaps route](DeclaredGaps.md) and disclose it; do not mark it passed.

No signature, credential, private receipt or driver-specific test result is distributed with the tools. See the individual guides for supported commands and environment setup. Broad provider-failure coverage is simulated; no universal provider, clean-machine or hardware compatibility guarantee is implied.

Copyright (c) 2026 Neil Colvin. MIT licensed. This independent tooling is not a Crestron certification or acceptance service.
