# CrestronHomeDevTools 1.16.0

Submission checklists now place numbered, linked notes after the unchanged official form. Reviewers can record a scoped interpretation of retained evidence without rewriting the original automatic findings.

- Add `SubmissionInterpretationReview` and the `AcceptedInterpretation` assessment status. Each decision requires a named reviewer, an explanation and retained evidence. Missing, failed and unperformed tests cannot be accepted through this route.
- Show accepted interpretations as checked items with explicit notes. Original outcomes and validation issues remain in the evidence archive, and the review retains its disclosed-qualification status.
- Bind interpretation decisions to the exact form, declarations, signing authorization and delivery checks. A review decision does not authorize signing or sending.
- Keep the official checklist first, followed by numbered notes with links in both directions. Entirely non-applicable items remain labelled N/A; unperformed items remain unchecked.
- Include the notes module in the self-contained console so developers need no separate Python installation or script changes.

See [reviewing declared gaps and interpretations](docs/submission/DeclaredGaps.md) and [form signing](docs/submission/FormSigning.md). These are reviewer decisions against our interpretation of the requirements, not a prediction of Crestron acceptance or certification. Driver-specific submissions remain private.

Copyright (c) 2026 Neil Colvin. MIT licensed. Crestron and Crestron Home are trademarks of Crestron Electronics, Inc. This project is independent and is not affiliated with, endorsed by or sponsored by Crestron Electronics, Inc.
