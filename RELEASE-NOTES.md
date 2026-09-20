# CrestronHomeDevTools 1.14.0

Submission reviews with declared test gaps can now proceed through approved form signing and delivery. Developers can document unavailable equipment or unperformed checks without converting those results into passes. Crestron alone decides whether to accept a submission.

- Prepare a signing copy with `submission prepare-review --review-mode declared-gaps --prepare-for-signing`. The authorization pins the exact form, declarations and review status.
- After reviewing the signed pages, use the review-request approval and delivery route with attachment kind `SignedSelfTest`. The C# `SubmissionReviewRequestDelivery` API supports the same route. Existing complete-only delivery remains available.
- Repeated form disclosures are grouped for readability while every affected scope and original outcome remains recorded. The private evidence archive stays local; outbound delivery contains the reviewed driver package and signed form.

See [form signing](docs/submission/FormSigning.md) and [review approval and delivery](docs/submission/ReviewApproval.md). Use the complete console archive; no Python editing or installation is required from the developer.

Validation covers declared-gap signing, changed or revoked authorization, document tampering, simulated-provider delivery and uncertain-send handling. These checks do not constitute a real driver submission or Crestron certification.

Copyright (c) 2026 Neil Colvin. MIT licensed. Crestron and Crestron Home are trademarks of Crestron Electronics, Inc. This project is independent and is not affiliated with, endorsed by or sponsored by Crestron Electronics, Inc.
