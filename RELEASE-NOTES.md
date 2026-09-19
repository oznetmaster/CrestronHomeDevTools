# CrestronHomeDevTools 1.12.0

This release adds passive endurance monitoring APIs and console commands. Developers can assess a local or remote Windows collector and send authorized operational email alerts without changing the running collector or contacting its processor.

- Add `SubmissionEnduranceHealth` and `endurance-health` to assess task state, sample freshness, interruptions and run identity. Include the read-only Windows snapshot script in the console archive.
- Add `SubmissionEnduranceWindowsObserver` and `endurance-observe` for local or pinned-SSH observation. The library includes its reader; developers do not need to write or transfer a remote script. Failed queries produce fresh attention reports where possible, rather than reusing an old healthy result.
- Add `SubmissionEnduranceNotifier` and `endurance-notify` for TLS-protected SMTP alerts and completion notices. A persistent private journal suppresses duplicate notifications across restarts and holds uncertain sends for explicit reconciliation.
- Add `endurance-watch` to pass a fresh observation directly to the notifier, including observations that require attention. Windows and SMTP credentials are supplied separately on standard input. Its result reports monitoring health and notification status independently.
- Validate passive snapshot behavior in the release workflow and require its script in the downloadable archive.

Validation includes the discovered .NET regression suite, simulated SMTP and observation failures, duplicate and uncertain delivery, Windows snapshot tests, and isolated packaged-console acceptance. A read-only remote observation also succeeded against an existing Windows collector using a verified ED25519 SSH key. Scheduled alert delivery, real inbox receipt and restart recovery still require validation in the developer's deployment; this release does not claim those checks or completed driver submission.

See [endurance notifications and observation](docs/submission/EnduranceNotifications.md) and [Windows endurance workers](docs/submission/WindowsEnduranceWorker.md). The NuGet package provides C# APIs; the complete Windows console ZIP provides the commands and scripts. An operator must configure supervision, private credential access and authorized recipients. Existing collectors can keep their pinned versions. Ordinary driver, library and client releases do not require submission tooling.

Copyright (c) 2026 Neil Colvin. MIT licensed. Crestron and Crestron Home are trademarks of Crestron Electronics, Inc. This project is independent and is not affiliated with, endorsed by or sponsored by Crestron Electronics, Inc.
