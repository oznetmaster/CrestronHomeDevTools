# Changelog

## 1.3.0 - 2026-09-15

Add room inventory and guarded movement of a loaded driver between existing rooms.

- `locations` lists configured room IDs and names.
- `move --device ID --model NAME --version VERSION --from-room ID --room ID` moves a matching childless driver and verifies its unchanged identity and loaded version.
- The library exposes `GetLocationsAsync`, `MoveDriverInstanceAsync`, `ProcessorLocation` and `DriverRoomMoveResult`.
- Reboot-required drivers, managed children, ambiguous destinations and changed identity are refused. The CLI holds the shared processor reservation and retains uncertain outcomes for inspection.
- CI and release validation compare executed test identities with discovery instead of maintaining a fixed test count.
- Enforce the numeric room-ID format required by the move command. Installation uses a different string-valued format; interchanging them can remove an instance.

Hardware validation moved a temporary Entity V2 test instance between two rooms and back on an MC4-R, including a fresh authenticated connection. Instance ID and loaded version were preserved. The temporary instance and CI archive were removed afterwards. No actual installed driver was moved.

See [room moves](docs/RoomMoves.md), [protocol reference](docs/ProtocolReference.md), [CHANGELOG.md](CHANGELOG.md) and [third-party notices](THIRD-PARTY-NOTICES.md). The library requires .NET 10 and a V2 Crestron Home processor. This is unrelated to driver V1/V2 naming. No Crestron SDK is required.

Copyright (c) 2026 Neil Colvin. MIT licensed. Crestron and Crestron Home are trademarks of Crestron Electronics, Inc. This project is independent and is not affiliated with, endorsed by or sponsored by Crestron Electronics, Inc.

## Documentation - 2026-09-15 (no package release)

- State that configuration-management commands require V2 Crestron Home processors; V1 processors do not support them. Distinguish processor compatibility from supported V1 driver update/reboot workflows.
- Correct the README installation example to the current 1.2.0 library release.

## 1.2.0 - 2026-09-15

Add reusable initial-driver configuration and read-only inspection of current settings to the .NET 10 library, interactive console and CLI.

- `driver-configuration --device ID` reads the advertised settings, current values and configuration status. Display follows only the driver's `Masked` flag; omitted stored values are not invented or replaced by defaults.
- `configure-driver --device ID --model NAME --version VERSION --input FILE` applies private flat values or ordered wizard steps, validates the exact target and writable fields, and waits for confirmed configuration completion.
- Already configured instances are preserved. Submitted settings are never retried after uncertain failure. The CLI holds the shared processor lease, retains uncertain outcomes and keeps values out of routine configuration diagnostics.
- `DriverConfigurationInspection.GetAsync`, `DriverConfiguration.ReadInputs` and `DriverConfiguration.ConfigureAsync` expose the same behavior to other development tools.

All 191 offline tests pass. MC4-R / Home 4.11.322 validation read installed Wiser settings, preserved the configured instance, and completed the two-step wizard on a separate temporary instance. Cleanup removed the temporary instance after explicit inspection of its shared reload scope and confirmed the original driver was ready with unchanged settings. No heating controls were operated.

Install CrestronHomeDevTools 1.2.0 from NuGet, or extract the complete matching Windows console ZIP from GitHub. No Crestron SDK is required by this library or console.

See [driver configuration](docs/DriverConfiguration.md), [protocol reference](docs/ProtocolReference.md), [processor coordination](docs/ProcessorCoordination.md), [CHANGELOG.md](CHANGELOG.md) and [third-party notices](THIRD-PARTY-NOTICES.md). This does not add arbitrary reconfiguration, rollback or automatic retained-package deletion.

Copyright (c) 2026 Neil Colvin. MIT licensed. Crestron and Crestron Home are trademarks of Crestron Electronics, Inc. This project is independent and is not affiliated with, endorsed by or sponsored by Crestron Electronics, Inc.

## 1.1.0 — 2026-09-14

- Support catalogue searches longer than three words by issuing bounded search batches and intersecting catalogue IDs. Fixes HTTP 422 when a workflow looks up models such as WeatherLink Live Weather Station.

- Coordinate console mutations with NUnit workflows, desktop runners and updated processor test hosts through the same processor-side lease. Verify owner contents, prevent release during active tests and retain uncertain outcomes for inspection.
- Keep standalone reboot reservations through shutdown and authenticated Home startup instead of releasing at acknowledgement; use a bounded 600-second default startup wait.
- Add `capabilities` and a shared PowerShell build deployment entry point that requires lease support, passes credentials privately, waits for catalogue readiness and reports failures to the build.
- Add read-only `stored-packages` inspection with manifest identities, storage sizes and conservative device/catalogue references. No package deletion is performed.
- Document coordinated CI/build/manual development and the distinction between instance removal and retained packages. Link to the separately released NUnit Test Explorer adapter.

- Validate 162 offline tests, cross-tool processor locking, active-test release refusal, reboot recovery, retained-package inspection and complete WeatherLink/Overkiz update workflows on MC4-R / Home 4.11.322.

## 1.0.0 — 2026-09-14

- Add a configuration-management protocol reference covering verified discovery, authentication, request/response envelopes, commands, asynchronous events, V1/V2 lifecycle sequences, configuration reboot, retry rules and validation limits. Link it from README and the API/compatibility guides. — initial development preview

- Use Home's configuration reboot operation after a confirmed V1 swap. An immediate SSH console reboot did not retain the staged driver version during validation; standalone SSH reboot remains a separate command.

- Fix V1 update sequencing: wait for the exact driver-swap completion event, validate reboot/reconfiguration requirements, and request reboot once. A missing event or lost update response never implies permission to reboot. Found during hardware validation.

### Added

- Confirmed whole-processor reboot API and interactive console command, with target-specific confirmation, pinned SSH and no automatic retry. Unattended reboot requires an explicit matching target confirmation. Lifecycle reboot support is opt-in and preserves scope/version guards.

- Independent .NET 10 configuration-management library, interactive console and automation CLI.
- Credential-free processor discovery, exact system-name resolution and explicit address selection.
- Authenticated WebSocket/HTTPS sessions with verified certificate pins, cancellation and asynchronous operation tracking.
- Driver/device inventory, update eligibility, reviewed update plans and guarded reboot-free reload.
- SFTP package inspection/upload/import with SSH key verification, SHA-256 recording and catalogue readiness checks.
- Guarded install/update/reuse of an explicitly identified instance, including first installation and reinstallation after removal.
- Identity/version/dependency-checked removal with disappearance verification.
- Named Windows DPAPI profiles, masked credential entry, private settings/environment input, descriptive command help, JSON CLI results and documented exit codes.
- Complete user/API/compatibility/release documentation, third-party notices and a link to the separate NUnit CI orchestration guide.

### Fixed

- Retry bounded restart-readiness reads when Home temporarily returns HTTP 500/502/503/504 during startup. Preserve authentication/request failures and never retry mutation commands. Found during the authorized hardware reboot test.

- Interactive setup no longer rejects valid credentials because of an empty catalogue filter; it validates the management service directly.
- Unfiltered catalogue reads use advertised categories; HTTP errors identify the failed session/read/command stage.
- Driver-version comparisons retain all numeric components while accepting different zero-padding across package, catalogue and installed versions.

### Validation

- Validate CP4-R / Home 4.11.322: authenticated inventory, temporary Entity V2 import/install/update/reuse/reload, one configuration reboot and recovery, 116 processor tests before and after restart, and removal/lease cleanup. Verify all seven existing driver instances retain their identities, room assignments and versions and are Loaded. Document per-model limits; CP4-R V1 lifecycle remains unverified.
- 157 offline NUnit tests pass without processor credentials or Crestron SDK dependencies.
- On MC4-R / Home 4.11.322: authenticated reads, discovery/name resolution, SFTP import, install/update/reuse, targeted V2 reload and removal/reinstallation were verified.
- DevTools supported the complete gated KasaTapo workflow and the later six-driver processor validation. The broader workflow and its test policies belong to Crestron Home NUnit.

### Release status

Initial public release: the library is distributed through NuGet and the Windows console through GitHub Releases. The release workflow builds and validates artifacts before publishing with NuGet Trusted Publishing. See [release notes](RELEASE-NOTES.md) and the [release procedure](docs/Releasing.md).

### V1 hardware validation

A complete unattended Apple TV V1 workflow subsequently passed on the development MC4-R: 116 local tests, 105 processor driver tests, 11 processor SDK lifecycle tests and three read-only installed-driver health checks. It installed a fresh Entity V2 test host, staged the V1 update, received the matching swap-completion event, requested one Home configuration reboot, reconnected and verified lease ownership, verified the new driver version was Loaded, online, ready and configured, then removed the test host and released the lease. Independent checks confirmed the other 19 driver instances retained their identities and versions and were Loaded. The first V1 attempt exposed an incorrect assumption that swap initiates reboot; it required one separately recorded assisted reboot and was not counted as an unattended pass. A second attempt confirmed swap completion but an immediate SSH reboot returned with the previous version; it was stopped, reconciled and retained as a failed validation. The passing run used Home configuration reboot instead. V1 initial-install/removal reboot paths still have simulated coverage only. The SDK lifecycle tests and read-only health checks do not establish playback or device-control behavior.
