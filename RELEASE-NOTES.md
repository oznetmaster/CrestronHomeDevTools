# CrestronHomeDevTools 1.5.0

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
