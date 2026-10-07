# CrestronHomeDevTools 1.26.0

Developers can invoke phase three directly after the defined submission test suite passes. The new `--phase-three` command and `SubmissionWorkflow.AdvancePhaseThreeAsync` require all six completed test stages, including final assessment, before changing the checkpoint or invoking any stage adapter. They never start missing tests.

Document preparation, signing, delivery and retention continue through the same shared implementation. Detailed evidence verification, exact authorizations and recovery of existing operations remain required. Failed or uncertain delivery is not automatically repeated. The shared CI dispatch adds an explicit phase-three selection while preserving its full-workflow default.

Validation passed all 2,396 discovered .NET tests, 281 document tests and 11 dispatch-template tests on Windows and Linux, and 32 packaged-console tests. These are software checks; they do not establish completion of any hardware rehearsal or Crestron certification. Existing frozen runs retain their pinned tooling and evidence.

Requires Crestron Home NUnit 2.3.0. See the [native test suite and phase-three command](https://github.com/oznetmaster/CrestronHomeDevTools/blob/v1.26.0/docs/submission/NativeNUnitSuite.md).

Copyright (c) 2026 Neil Colvin. MIT licensed. Crestron and Crestron Home are trademarks of Crestron Electronics, Inc. This project is independent and is not affiliated with, endorsed by or sponsored by Crestron Electronics, Inc.
