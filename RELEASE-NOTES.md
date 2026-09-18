# CrestronHomeDevTools 1.7.0

These notes describe the packaged 1.7.0 release. Later additions published to the source repository are listed separately in [development history](DEVELOPMENT-HISTORY.md).

Add read-only processor uptime observations and plan validation for independently supplied monitoring producers. These APIs are useful for ordinary development monitoring as well as optional driver submission.

- `ProcessorUptime.ReadAsync` opens an authenticated, SSH-key-pinned console, waits for the prompt and sends `uptime` once. It bounds execution and response size, handles fragmented replies and interleaved logs, and never automatically retries an uncertain command.
- `ProcessorUptimeSnapshot` retains the reported duration, diagnostic local start time and request/response UTC timestamps. Its inferred UTC start window supports an explicitly reviewed clock tolerance. The displayed local start time is not a stable boot identifier; consumers must keep their original baseline and independently check driver lifetime and fresh function.
- `SubmissionEndurance.ValidatePlan` exposes the collector's structural validation without creating files or contacting a processor. External producers can reject malformed requests before comparing them with their reviewed acceptance bindings.
- Monitoring guidance now distinguishes processor uptime, driver lifetime, fresh functional observations and the complete endurance requirement. A supplied probe and scheduler remain separate from the shared library.

Validation: the complete .NET test suite passed. Real CP4-R console observations confirmed the uptime parser and bounded boot window, including a captured prompt/log interleaving case and a one-second variation in the displayed local start time. A separately supplied producer completed short real-function observations through the existing process/monitor protocol and exported valid evidence. A deliberate same-version driver reload changed its lifetime and caused the old monitor run to fail; reservations were released.

These are development validations, not an immutable candidate's 24-hour endurance period. No processor reboot was performed for these checks. The release does not contain a driver-specific probe, scheduled-worker installer, automatic certification, final signature or supported uploader/mail sender. Full submission readiness still depends on the consuming project's remaining validation and delivery stages.

See [processor uptime](docs/ProcessorUptime.md), [endurance collection](docs/submission/EnduranceCollection.md), [submission stages](docs/CrestronSubmission.md) and [release history](CHANGELOG.md).

Copyright (c) 2026 Neil Colvin. MIT licensed. Crestron and Crestron Home are trademarks of Crestron Electronics, Inc. This project is independent and is not affiliated with, endorsed by or sponsored by Crestron Electronics, Inc.
