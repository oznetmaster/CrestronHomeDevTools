# CrestronHomeDevTools 1.13.0

This release connects candidate-bound evidence from multiple test phases and adds an explicit submission route for developers who choose to disclose unmet requirements. The normal signed submission path remains complete-only by default.

- Combine scoped observations through the C# API and `submission-combine-evidence`, preserving failed phases and the full reviewed policy.
- Assess declared gaps, retain the original evidence and prepare unsigned review requests with reasons for omitted forms or signatures. Missing evidence never becomes a passing test.
- Preview and verify approval of the exact outgoing package, document and correspondence. Protected request delivery checks the retained evidence and approval again before each upload or email.
- Use the existing durable delivery journal for both routes, preserving known upload results and preventing automatic replay after an uncertain provider response.
- Add a short [submission runbook](docs/submission/Runbook.md) linking setup, stage inputs, expected results and recovery instructions.

The NuGet package supplies the C# APIs. The complete Windows console ZIP includes the document commands and their internal runtime; developers do not need to install or write Python. Credentials, evidence and signatures remain private.

Validation includes 943 passing .NET tests, generated unsigned/disclosure requests through the protected command with simulated providers, and isolated packaged-console checks. Document checks passed after an unchanged rerun of a temporary-file replacement failure. The release workflow also runs its full build and packaged-console acceptance checks before publishing. These are tooling checks; end-to-end submission of a real driver through the protected workflow remains to be validated.

The ordinary workflow still requires complete candidate evidence, visual review and separate signing and delivery authorization. Declared-gap signing and omissions of other required documents are not implemented. See [declared gaps](docs/submission/DeclaredGaps.md), [request preparation](docs/submission/ReviewRequest.md) and [approval](docs/submission/ReviewApproval.md). Passing checks means complete only against our interpretation of Crestron's requirements; neither passing checks nor successful delivery implies Crestron acceptance, publication or certification.

Copyright (c) 2026 Neil Colvin. MIT licensed. Crestron and Crestron Home are trademarks of Crestron Electronics, Inc. This project is independent and is not affiliated with, endorsed by or sponsored by Crestron Electronics, Inc.
