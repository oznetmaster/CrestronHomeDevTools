# CrestronHomeDevTools 1.10.0

Protected submission delivery can now use the complete Windows console distribution without configuring separate runtime or validator paths. This optional workflow remains independent of ordinary driver, library and client releases.

- Add `submission-delivery-settings` to generate private schema-2 dispatch settings from a completed, pinned delivery preparation and the installed console. It derives the approved package and form paths, inventories the complete console, rejects overlapping storage and existing outputs, and returns a settings hash for independent review. It grants no approval and sends nothing.
- Add `SubmissionBundledRevalidationSettings`, its preparation API and a `CheckAsync` overload for C# integrations. Existing explicit-runtime settings and schema-1 dispatch remain supported.
- Revalidate through the pinned bundled console immediately before each pending upload or email. Preserve confirmed uploads if later approval fails, return completed journals without sending again, and retain the existing uncertain-outcome handling.
- Update the final-stage CI template to use the tooling inventory already included in generated settings. Developers supply private configuration and approved hashes; they do not hand-author a second runtime inventory.

Validation covers the complete .NET suite, offline document tests and isolated console acceptance with synthetic forms and providers. An unattended NETWORK SERVICE rehearsal exercised actual bundled revalidation before simulated upload and email, changed-tooling refusal, approval revocation after upload, and completed replay. This is tooling validation, not a real signed driver submission or Crestron certification.

See [delivery setup](docs/submission/DeliverySetup.md), [protected execution and recovery](docs/submission/DeliveryCommand.md), and [the submission roadmap](docs/CrestronSubmission.md). Extract the complete console ZIP to a new protected directory; do not mix release files. The NuGet library contains the C# APIs, while the document runtime is provided in the console ZIP. Final delivery still requires complete candidate evidence, a reviewed signed form, provisioned accounts and exact authorization.

Copyright (c) 2026 Neil Colvin. MIT licensed. Crestron and Crestron Home are trademarks of Crestron Electronics, Inc. This project is independent and is not affiliated with, endorsed by or sponsored by Crestron Electronics, Inc.
