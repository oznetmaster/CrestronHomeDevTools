# CrestronHomeDevTools 1.22.4

This release fixes worker shutdown and terminal-failure retirement, adds visible-screen readiness before app tests, and delivers worker notices directly to the controlling computer.

- Bind finite worker processes and their descendants to their launcher at creation. Stopping the scheduled task or terminating the launcher now stops the owned process tree, preserving failure evidence instead of leaving a detached alert worker. Task retirement validates the exact terminal outcome; interrupted work is not marked successful.
- Capture the masked Android screen before installed-driver app tests and require an unobstructed, configured starting Home. Retain the screen and readiness result when an Android crash dialog or unexpected page prevents testing. This check does not send input or automatically retry failed tests.
- Package the worker lifetime helper and gate releases on real synthetic parent/child termination tests. An optional scheduled-task test exercises Windows Task Scheduler shutdown without involving a device.
- Finite workers now retire on confirmed terminal failures as well, retaining the failed outcome and final notice and exiting with code 2. Active, uncertain and approval-waiting runs remain open. The same process lifetime protection covers one-run operator inbox launchers.
- The public controlling-PC listener optionally reads the worker status directory and shows failures, uncertain outcomes, unsigned review and final retention directly. Dismissals persist locally; unchanged failures and routine polling do not reopen windows. Closing a notice grants no test, signing or delivery authority.

Validation: 20 task-retirement guard cases; real synthetic process termination, scheduled-task stop, successful self-retirement and failed self-retirement with its failure preserved; offline screen-readiness and controller notice/dismissal tests. The release pipeline requires every discovered offline test plus package and documentation checks to pass. These checks do not establish a completed hardware rehearsal or certification.

Use this version for newly prepared attempts. Existing frozen runs keep their original tooling and evidence. Inspect and explicitly retire legacy failed watchers; do not rewrite their outcome or restart them just to obtain cleanup. A persistent public controller listener is intentionally reused across releases.

See https://github.com/oznetmaster/CrestronHomeDevTools/blob/main/docs/submission/AutomationWorker.md and https://github.com/oznetmaster/CrestronHomeDevTools/blob/main/docs/submission/OperatorSteps.md for installation and notification behavior.

Copyright (c) 2026 Neil Colvin. MIT licensed. Crestron and Crestron Home are trademarks of Crestron Electronics, Inc. This project is independent and is not affiliated with, endorsed by or sponsored by Crestron Electronics, Inc.
