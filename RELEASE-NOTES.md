# CrestronHomeDevTools 1.27.0

Adds name-based processor-test operations for package-instance provisioning, installed-target assignment checks and managed-child validation. `DriverInstanceLifecycle.EnsureInRoomAsync`, `ProcessorTestRooms` and `ManagedDeviceValidation.RunInRoomsAsync` resolve the intended room on the authenticated processor. Missing or ambiguous room names stop before mutation; managed-child setup checks every requested room before commissioning the first child. Named public results preserve the room name rather than a resolved room-ID plan. Native restoration journals remain private lower-layer evidence.

Pins `System.Security.Cryptography.Pkcs` to 10.0.13, replacing the vulnerable transitive version reported by NuGet audit. Three disposable synthetic CMS regressions cover signature verification and altered-content rejection, recipient encryption/decryption, and malformed input. These compatibility checks are not an advisory exploit test.

The room APIs are additive. Existing native configuration APIs and the Automation/SubmissionTests consumer of Crestron Home NUnit 2.3.0 remain compatible. This release supplies the core operations needed by the next name-only NUnit workflow contract; it does not introduce a CI room-ID adapter or migrate existing private plans automatically.

Validation covers named plans on processors with different native room identities, missing/ambiguous names before commands, installed-device assignment, cancellation during lookup and managed-child preflight, plus the normal discovered .NET suite, package consumers and packaged-console checks. These are software checks; no new hardware, physical action or submission acceptance is claimed. See [named processor-test rooms](https://github.com/oznetmaster/CrestronHomeDevTools/blob/main/docs/ProcessorTestRooms.md).

Requires Crestron Home NUnit 2.3.0 for the existing Automation consumer. No driver or client production payload changes.

Copyright (c) 2026 Neil Colvin. MIT licensed. Crestron and Crestron Home are trademarks of Crestron Electronics, Inc. This project is independent and is not affiliated with, endorsed by or sponsored by Crestron Electronics, Inc.
