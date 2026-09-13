# CrestronHomeDevTools 1.0.0

CrestronHomeDevTools provides a .NET 10 library and console/CLI for development-time Crestron Home configuration management. It can discover processors, authenticate, inspect packages/devices, deploy by SFTP, install or update supported driver instances, verify their loaded versions and remove explicitly identified instances.

## Included

- Confirmed whole-processor reboot API and interactive console command, with target-specific confirmation, pinned SSH and no automatic retry. Unattended reboot requires an explicit matching target confirmation. Lifecycle reboot support is opt-in and preserves scope/version guards.

- Reusable `CrestronHomeDevTools` library with asynchronous operations, update-scope validation, cancellation and operation evidence.
- Separate console supporting both an interactive prompt and unattended commands.
- Named Windows-encrypted credential profiles; noninteractive environment/private-file settings.
- Install/update readiness waits, exact numeric version comparisons and guarded reboot-free lifecycle operations.
- User/API/compatibility/release guides and license notices.

The NuGet package contains the configuration library, not the console. The console is a separate distribution. Neither needs Crestron SDK assemblies, Configure Pro or the NUnit runner. SSH.NET supplies SFTP and SSH console access; Microsoft ProtectedData supplies the console's Windows profile encryption.

## Validation and limitations

157 offline tests pass. Hardware management was validated on MC4-R and CP4-R / Crestron Home 4.11.322. CP4-R validation covered temporary Entity V2 installation, update, reuse, targeted reload, one configuration reboot and authenticated recovery, 116 processor tests before and after restart, and verified cleanup with all seven existing drivers preserved. V1 update/reboot hardware evidence is MC4-R only. See the [compatibility matrix](docs/Compatibility.md#hardware-evidence) for the scope on each model. Other processor/firmware combinations remain unverified.

The configuration interface is unofficial; a [protocol reference](docs/ProtocolReference.md) documents the verified subset, message formats, lifecycle sequences and remaining unknowns. Lifecycle operations refuse reboot-required changes by default. Callers may explicitly authorize a reboot handler; the separate reboot command accepts interactive or target-matched CLI authorization. The V1 update/reboot workflow has passed unattended hardware validation; V1 initial-install/removal reboot paths remain unverified on hardware. Cancellation does not undo an accepted operation, and interrupted writes are not automatically replayed. Automatic rollback, arbitrary system configuration and a Visual Studio Test Explorer adapter are not included. Test execution and gated development orchestration are provided separately by Crestron Home NUnit.

## Installation

Install `CrestronHomeDevTools` version `1.0.0` from NuGet. Extract the complete `CrestronHomeDevTools.Console-win-x64.zip` from the matching GitHub release to use the self-contained console. No separate .NET installation is required for this Windows console distribution. See [README.md](README.md) for source-build instructions.

Release assets include the library NuGet package, self-contained Windows console ZIP and SHA-256 checksums. The console includes documentation and dependency/runtime license notices. Credentials, profiles, local settings, device bindings, logs and private paths are excluded.

Copyright (c) 2026 Neil Colvin. MIT licensed; dependencies retain their own licenses. Crestron and Crestron Home are trademarks of Crestron Electronics, Inc. This project is independent and is not affiliated with, endorsed by or sponsored by Crestron Electronics, Inc. See [LICENSE](LICENSE) and [third-party notices](THIRD-PARTY-NOTICES.md).


The unattended CLI reboot was exercised on the development MC4-R. The first recovery attempt authenticated after about three minutes but stopped on a transient HTTP 500 from device inventory while Home initialized. The fix retries bounded startup read failures (HTTP 500/502/503/504), while authorization/request errors still stop recovery. Read-only verification was then resumed without another reboot: all 21 previously loaded driver instances had unchanged identity/version and were Loaded; the original lease was verified and released. No driver packages changed. The failure and resumed verification are retained separately. The later uninterrupted V1 update validation is described below.

A complete unattended Apple TV V1 workflow subsequently passed on the development MC4-R: 116 local tests, 105 processor driver tests, 11 processor SDK lifecycle tests and three read-only installed-driver health checks. It installed a fresh Entity V2 test host, staged the V1 update, received the matching swap-completion event, requested one Home configuration reboot, reconnected and verified lease ownership, verified the new driver version was Loaded, online, ready and configured, then removed the test host and released the lease. Independent checks confirmed the other 19 driver instances retained their identities and versions and were Loaded. The first V1 attempt exposed an incorrect assumption that swap initiates reboot; it required one separately recorded assisted reboot and was not counted as an unattended pass. A second attempt confirmed swap completion but an immediate SSH reboot returned with the previous version; it was stopped, reconciled and retained as a failed validation. The passing run used Home configuration reboot instead. V1 initial-install/removal reboot paths still have simulated coverage only. The SDK lifecycle tests and read-only health checks do not establish playback or device-control behavior.
