# CrestronHomeDevTools 1.16.2

Failed installation attempts now retain the processor's preparation or commissioning reply so developers can investigate without repeating the command merely to recover its result.

- The console saves the reply beside its private lease receipt as `<owner>.failure.json` and prints only the file location. Existing records are never overwritten; a diagnostic write failure preserves the original processor failure.
- C# callers can retain `ProcessorApiException.DiagnosticCommand` and `DiagnosticResponse` in their own private run journal.
- Malformed commissioning IDs are reported through the same diagnostic path. Installation still requires an explicit success result and a positive integer device ID; no request is retried and uncertain operations retain their reservation.
- Clarify the SSH fingerprint format, obtaining the complete ManifestUtil NuGet distribution, and collecting preparatory help screenshots before freezing a candidate.

See [deployment and activation](docs/UserGuide.md#deploy-and-activate), [library diagnostics](docs/LibraryGuide.md#advanced-commands-and-failures), and [help preparation](docs/submission/HelpBuild.md). Processor responses may contain private data and must remain outside public source and CI artifacts. This release changes desktop diagnostics, not driver runtime behavior or Crestron's acceptance criteria.

Copyright (c) 2026 Neil Colvin. MIT licensed. Crestron and Crestron Home are trademarks of Crestron Electronics, Inc. This project is independent and is not affiliated with, endorsed by or sponsored by Crestron Electronics, Inc.
