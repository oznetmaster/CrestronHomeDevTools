# CrestronHomeDevTools 1.1.0

This stable release coordinates development operations with Crestron Home NUnit 1.2.0 and fixes catalogue searches for longer driver names.

## Changes

- Share processor reservations across DevTools commands, NUnit workflows, desktop runners and updated test hosts. Verify exact ownership, refuse release during an active test, and retain uncertain operations for inspection.
- Hold standalone reboot reservations through shutdown and authenticated Home startup. The default startup deadline is 600 seconds.
- Fix HTTP 422 for catalogue searches longer than three words, using bounded batches and intersected results.
- Add `capabilities` and the shared `scripts/DeployDriver.ps1` build entry point. Deployment uses pinned connections, private credentials and catalogue-readiness checks.
- Add read-only `stored-packages` inspection with package identities, sizes and conservative installed/catalogue references. It does not delete retained packages.

## Installation

Install `CrestronHomeDevTools` version `1.1.0` from NuGet. Extract the complete matching Windows console ZIP from GitHub Releases. The console includes its .NET 10 runtime, documentation and license notices. The library requires .NET 10 and has no Crestron SDK dependency.

Upgrade cooperating desktop tools and rebuild deployed processor test packages to gain shared reservation coverage. Older clients and test hosts do not acquire the new lock. Manual Configure operations also require coordination; see [processor coordination](docs/ProcessorCoordination.md).

## Validation and limitations

All 162 offline tests pass. MC4-R / Home 4.11.322 validation covered cross-tool reservation exclusion, active-test release refusal, reboot/startup recovery, stored-package inspection, and complete WeatherLink and Overkiz gated updates with processor live tests and installed-driver health checks. Existing CP4-R and V1 update evidence remains documented in the [compatibility matrix](docs/Compatibility.md); this release does not add V1 initial-install/removal validation.

The configuration interface remains unofficial and firmware-dependent. The [protocol reference](docs/ProtocolReference.md) documents the verified behavior. Commands are not automatically replayed after uncertain failures. No automatic rollback or retained-package deletion is included. The Visual Studio test adapter is distributed separately by [Crestron Home NUnit](https://github.com/oznetmaster/CrestronHomeNUnit).

Credentials, profiles, device bindings, private paths, logs and test inputs are excluded from release assets. See [CHANGELOG.md](CHANGELOG.md), [LICENSE](LICENSE) and [third-party notices](THIRD-PARTY-NOTICES.md).

Copyright (c) 2026 Neil Colvin. MIT licensed; dependencies retain their own licenses. Crestron and Crestron Home are trademarks of Crestron Electronics, Inc. This project is independent and is not affiliated with, endorsed by or sponsored by Crestron Electronics, Inc.