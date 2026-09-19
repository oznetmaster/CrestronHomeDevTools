# CrestronHomeDevTools 1.13.1

Fix package verification when the caller supplies the full catalogue ID returned by the processor. `compare-payload` and `DriverPayloadInspection.CompareAsync` now resolve the unversioned driver storage key and separate version directory, instead of looking for a folder named after the entire catalogue ID. This also fixes installed-driver test workflows that stopped before running their fixtures.

The catalogue version must match the candidate package. Existing unversioned storage-key callers remain supported, and file, hash, link and ambiguity checks are unchanged. No processor driver is installed, updated or reloaded by this correction.

Validation includes the complete .NET regression suite and a successful read-only comparison against an unchanged driver on a physical processor. See [package verification](docs/DriverPayloadInspection.md) for the corrected command example.

Copyright (c) 2026 Neil Colvin. MIT licensed. Crestron and Crestron Home are trademarks of Crestron Electronics, Inc. This project is independent and is not affiliated with, endorsed by or sponsored by Crestron Electronics, Inc.
