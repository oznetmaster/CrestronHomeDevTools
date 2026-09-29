# CrestronHomeDevTools 1.22.3

This release corrects mixed automatic/manual test execution and adds finite worker closeout for fixed submission or rehearsal runs.

- Updated public CrestronHomeNUnit tooling to 2.1.2. Exact Android selections containing both ordinary tests and explicit physical-action tests now execute every selected case. Unselected tests stay excluded, and missing results still fail coverage. Operator acknowledgements do not replace observed test results.
- `--exit-when-finished` ends a fixed-registry worker after all submissions reach final retention or rehearsals reach unsigned review. Installer `-CurrentUser -ExitWhenFinished` additionally archives and removes its exact scheduled task after validating the registry, terminal identities, task action/account and released worker lock. Failures, uncertain outcomes and approval waits retain the task and evidence. Continuous release-discovery workers remain persistent.
- Worker notifications and operator windows display readable dates and explicit time zones. Machine-readable evidence keeps precise UTC timestamps.
- Documented direct physical-action prompts on a separate controlling computer, including shared-storage setup and validation without an AI relay. Independent obsolete observers and failed attempts superseded by recovery still require explicit retirement; this release does not claim to automate that separate closeout.
- The regression launcher uses a runsettings file to preserve native Windows work-directory paths without command-line escaping changes.

Validation includes mixed-selection regression cases, worker completion tests, 15 task-retirement guard cases and a real synthetic Windows scheduled task that removed itself while retaining its definition and receipt. The release pipeline requires all discovered offline tests and package/document checks to pass. These checks do not establish a completed hardware rehearsal or certification.

Use this version for newly prepared attempts. Existing frozen runs keep their original tools and evidence. Rehearsal stops at unsigned review and cannot sign or deliver.

See https://github.com/oznetmaster/CrestronHomeDevTools/blob/main/docs/submission/AutomationWorker.md for installation, controller prompts and closeout.

Copyright (c) 2026 Neil Colvin. MIT licensed. Crestron and Crestron Home are trademarks of Crestron Electronics, Inc. This project is independent and is not affiliated with, endorsed by or sponsored by Crestron Electronics, Inc.
