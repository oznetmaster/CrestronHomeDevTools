# CrestronHomeDevTools 1.18.1

Completed endurance runs can now be retained when their scheduler history exceeds 20,000 filesystem entries. A normal 24-hour run can reach that limit because scheduler diagnostics are recorded more often than functional samples. The exporter now defaults to 100,000 entries and supports an explicit `-MaximumEntries` bound up to 1,000,000, recorded in its receipts. File-size limits, evidence hashes, source/copy revalidation and refusal to overwrite partial exports remain enforced. No evidence is removed to fit the limit.

Passive health checks now recognize the scheduler's lock-contention exit code 4 after a completed, released collection. An exporter holding the scheduler lock no longer creates a task-failure alert solely from that code. Completed receipts must still pass the existing identity, duration, sample, freshness and attention checks. Unexpected task failures and contention during collection still require attention.

This corrects evidence retention and operational monitoring, not driver behavior or test criteria. Use a fresh export directory after inspecting any earlier failure. The exporter can retain an older pinned run using its original tick script and CLI; do not replace those recorded inputs. PowerShell 7.6 or later is required on the exporting Windows computer.

Validation covers inventory-budget refusal and successful fresh retention without lost diagnostics, completed-run contention, real failure/identity/freshness rejection and notification regressions. These checks use synthetic inputs and do not contact processors or send email.

See [completed-run retention](docs/submission/WindowsEnduranceWorker.md#retain-a-completed-run) and [PowerShell installation](docs/PowerShell.md).

Copyright (c) 2026 Neil Colvin. MIT licensed. Crestron and Crestron Home are trademarks of Crestron Electronics, Inc. This project is independent and is not affiliated with, endorsed by or sponsored by Crestron Electronics, Inc.
