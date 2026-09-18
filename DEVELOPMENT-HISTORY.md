# Development history

This records changes published to the source repository separately from packaged releases. The [changelog](CHANGELOG.md) and [release notes](RELEASE-NOTES.md) describe released versions. The latest packaged release is **1.7.0, dated 17 September 2026**; the additions below are available from source and are not included in that NuGet package or console download.

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
