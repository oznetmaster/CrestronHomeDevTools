# CrestronHomeDevTools 1.15.0

Submission reviewers can now retain relevant passing evidence from an earlier candidate through an explicit, scoped change-impact review. Original package identities, execution times, measurements and failures remain intact; a reviewed earlier pass is never labelled as a new test run.

- The C# `SubmissionPriorEvidence` API and `submission-import-prior-evidence` console command validate the original evidence, unchanged assertion requirements and independently pinned review decisions.
- Forms distinguish fresh results from reviewed earlier evidence. Portable bundles retain and revalidate the original records and supporting change analysis.
- Form items can be checked when all applicable subconditions pass and optional absent controls have validated non-applicability explanations. Entirely non-applicable items remain unchecked; incomplete applicable checks still prevent a checkmark.
- Failed, partial, inconclusive and unperformed checks cannot be promoted through this path. Changed evidence or missing provenance cannot be waived as a declared gap.

See [reviewing prior evidence](docs/submission/PriorEvidence.md) for the C# and console workflow. The reviewer remains responsible for assessing affected code and dependencies. These checks do not predict Crestron acceptance or certification. Driver-specific submission files remain private.

Validation covers prior-evidence import, changed assertions, invalid original measurements, tampered records, form checkboxes and portable bundle verification after the original evidence directory is moved.

Copyright (c) 2026 Neil Colvin. MIT licensed. Crestron and Crestron Home are trademarks of Crestron Electronics, Inc. This project is independent and is not affiliated with, endorsed by or sponsored by Crestron Electronics, Inc.
