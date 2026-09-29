# CrestronHomeDevTools 1.22.1

A fresh rehearsal can now activate an exact release package already stored in a processor's local catalogue without importing the same version again.

- Automation uses released CrestronHomeNUnit 2.1.1. Explicitly set `NUnit.ReleaseCandidate.ReuseVerifiedStoredPackage` to enable verified storage reuse; the default remains false.
- Reuse requires matching candidate SHA-256, manifest and catalogue identity, no installed package model or alias, and unchanged catalogue state during verification. Normal activation and deployed checks still run. The receipt distinguishes reuse from a new import. Version equality alone never proves candidate identity.
- The setup guide now requires operational permissions and actual-account access checks before release intake. Configuration completeness alone is not a live access check. Physical test prompts and exact-artifact signing/delivery authorizations remain separate.

Validation: all 1,682 offline DevTools regression tests passed with NUnit 5.0.0 and the public 2.1.1 tooling package, including both enabled and disabled reuse choices through frozen release intake. A fresh-cache restore from public NuGet succeeded. These checks do not establish successful live reuse or a completed end-to-end rehearsal; those remain to be verified through the workflow.

Apply the update only to newly prepared attempts. Existing frozen runs retain their original tools, settings and evidence. Rehearsal ends at unsigned review and cannot sign or deliver.

See https://github.com/oznetmaster/CrestronHomeDevTools/blob/main/docs/submission/AutomationWorker.md for configuration and evidence boundaries.

Copyright (c) 2026 Neil Colvin. MIT licensed. Crestron and Crestron Home are trademarks of Crestron Electronics, Inc. This project is independent and is not affiliated with, endorsed by or sponsored by Crestron Electronics, Inc.
