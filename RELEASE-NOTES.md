# CrestronHomeDevTools 1.22.2

Managed-child setup now handles a light becoming a native Crestron wrapper and load after initial configuration. Previously, setup could reject that successful transition because the wrapper no longer exposed generic configuration fields.

- The new `ManagedDeviceCommissioning.ConfigureCreatedAsync` validates the commissioning receipt before applying initial configuration once. Completion verifies the parent, wrapper, managed-device identity, native load, room and usable controls. Ordinary children must still report configured and ready state.
- The automation worker uses this completion path for persistent managed children. Missing fields alone, offline loads and mismatched identities never count as success. Uncertain commands are not automatically retried; evidence and reservations remain available for inspection.

Validation: all 1,690 offline regression tests passed with NUnit 5.0.0, including eight new completion cases. A retained processor observation confirmed the native-light transition that caused the original failure. The updated completion path and complete rehearsal still require live validation; this release does not claim a successful end-to-end run.

Use this version for newly prepared attempts. Existing frozen runs keep their original tools and evidence. Rehearsal stops at unsigned review and cannot sign or deliver.

See https://github.com/oznetmaster/CrestronHomeDevTools/blob/main/docs/submission/ManagedChildren.md for managed-child setup and evidence requirements.

Copyright (c) 2026 Neil Colvin. MIT licensed. Crestron and Crestron Home are trademarks of Crestron Electronics, Inc. This project is independent and is not affiliated with, endorsed by or sponsored by Crestron Electronics, Inc.
