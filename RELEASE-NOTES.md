# CrestronHomeDevTools 1.24.0

Physical action replies are now accepted against elapsed time measured on the recording worker, not by comparing the desktop's clock with the worker's clock. Original desktop replies and separate worker decisions are retained. A live shared-file lock prevents the desktop from displaying a recording prompt after its recording process has exited.

Manual app steps can opt in to `PrepareBeforeReadiness`. The cooperating fixture completes preparation and navigation before asking Ready, waits indefinitely for that response, refreshes its baseline and arms observation before publishing the action prompt. The desktop listener detects the next prompt every half second. Readiness waits are excluded from the runner and automation active-work budgets; cancellation still works. Prepared sessions hold their processor/emulator reservations while waiting.

The same readiness boundary is available to manual outage recorders after preflight and before a fresh baseline. Cannot perform retains the operator's explanation. No acknowledgement establishes an event or pass, and failed or interrupted attempts are never replayed automatically.

Validation covers clock differences and clock corrections, expiry, retained responses, overnight waits, preparation ordering and synthetic Ready-to-action handoff. The complete physical rehearsal under this version is still pending. Earlier evidence and accepted driver submissions are unchanged.

Requires Crestron Home NUnit 2.2.0 for the opt-in runner contract. See https://github.com/oznetmaster/CrestronHomeDevTools/blob/main/docs/submission/OperatorSteps.md for preparation, prompts and retained evidence.

Copyright (c) 2026 Neil Colvin. MIT licensed. Crestron and Crestron Home are trademarks of Crestron Electronics, Inc. This project is independent and is not affiliated with, endorsed by or sponsored by Crestron Electronics, Inc.
