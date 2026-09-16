# Compatibility and validation

**Processor compatibility:** Configuration-management commands require a **V2 Crestron Home processor**. V1 Crestron Home processors do not support these commands. This refers to the processor platform, not the driver type: supported V2 processors can host V1 drivers, whose update workflow requires an explicitly authorized reboot.

## Runtime and dependencies

The library and console target .NET 10. There is no net472 build and no requirement to install this library on a processor. It is a development-computer/CI client of the processor configuration interface.

The library depends on SSH.NET for SFTP and SSH console access. The console additionally uses Microsoft ProtectedData for Windows DPAPI profiles. Neither runtime project references the Crestron SDK, a Configure Pro executable, proprietary protocol source or the NUnit runner. In particular, the desktop `Newtonsoft.Json.Compact.dll` required by some driver SDK test harnesses is not a DevTools dependency.

Profile persistence is Windows-only. Environment/private-file credentials are available without DPAPI; management operations on other desktop operating systems have not received the same hardware validation. Read [third-party notices](../THIRD-PARTY-NOTICES.md).

## Network and trust

- Processor discovery uses native Crestron UDP port 41794 and checks the anonymous Home V2 HTTPS endpoint. Broadcast discovery is local to reachable subnets.
- Authenticated configuration uses WebSocket port 49000 and HTTPS port 443 by default, with configurable connection options.
- SFTP deployment uses the processor's SSH service and an independently verified SHA-256 SSH host key.
- Test-package mDNS and dynamically assigned test TCP ports belong to Crestron Home NUnit, not DevTools discovery.

An advertised system name is not a trust anchor. Verify the certificate/key before sending credentials. Passwords and certificate/host-key changes are per processor.

## Hardware evidence

Recorded development validation used **MC4-R and CP4-R processors running Crestron Home 4.11.322**. Discovery identified three Home processors; management validation covered the two development processors. These observations are not a compatibility promise for other models or firmware.

| Operation | MC4-R | CP4-R |
|---|---|---|
| Discovery, authenticated inventory and SFTP import | Verified | Verified |
| Entity V2 initial installation, update and current-instance reuse | Verified | Verified |
| Targeted Entity V2 reload and instance removal | Verified | Verified |
| Entity V2 reinstallation after removal | Verified | Not separately exercised |
| Home configuration reboot and authenticated recovery | Verified | Verified |
| V1 update followed by configuration reboot | Verified | Not exercised |
| V1 initial-install/removal reboot paths | Verified, with resumed startup verification | Not exercised |

The CP4-R validation installed a temporary Entity V2 test host and passed 105 driver tests plus 11 SDK lifecycle tests. It upgraded that host, verified current-instance reuse, performed a targeted reload, requested one authorized Home configuration reboot, reauthenticated and verified the original workflow lease. The same 116 processor tests passed again after restart. Cleanup removed the temporary host and released the lease; all seven pre-existing driver instances retained their identities, room assignments and versions and were Loaded. No existing driver package was updated. These tests do not establish physical V1 driver playback or control behavior.

On MC4-R, verified operations include authentication, system-name resolution, catalogue/device reads, import, initial test-instance installation, guarded update, current-instance reuse, targeted Entity V2 reload, and removal followed by installation. Six representative driver test packages subsequently ran 400 distinct offline/lifecycle tests twice on that processor. A separate sample outlet driver end-to-end workflow passed its then-current 57 local and 57 processor tests, three live checks and seven checks of the updated actual driver, then removed its test instance and released its lease.

The offline DevTools suite is checked against discovered test identities, rather than a maintained fixed test count. Hosted CI checks offline behavior; it cannot establish hardware compatibility unless a separately configured LAN agent runs the hardware workflow.

Confirmed reboot handling has simulated coverage for acceptance, cancellation and uncertain outcomes. Its underlying SSH command was observed during an earlier authorized recovery; the wrapper has now been exercised on hardware, exposing a startup-readiness case that was fixed; see the evidence below.

## Boundaries

- The management interface is unofficial and firmware-dependent; the verified subset is described in the [protocol reference](ProtocolReference.md). It is distinct from the documented public Home control REST API and may change.
- Supported reboot-free lifecycle operations have been exercised with Entity V2 drivers. The default policy refuses reboot-required changes. Opt-in lifecycle handling supports confirmed driver-swap completion followed by an authorized reboot and explicitly configured reboot-after-install/removal paths. The V1 update path has passed end-to-end hardware validation on MC4-R; initial-install/removal paths have also been verified with explicit shared-scope preservation; see [V1 lifecycle evidence](V1DriverRemoval.md).
- The general command API is available, but room administration, full backups/restores and arbitrary device commissioning are not complete typed features.
- Loaded/version confirmation establishes activation, not correct live device behavior.
- DevTools does not provide automatic rollback, test cancellation, processor log streaming or a Visual Studio test adapter.
- A timed-out operation may already have taken effect. Submitted writes are not automatically replayed.

One earlier test-host cleanup stalled while Home configuration processing was unresponsive; the cause was not established. Recovery required a separately authorized reboot. A later complete workflow removed the test host successfully without manual intervention. Do not treat a missing tile as proof of removal or silently clear a retained workflow lease. The orchestration guide explains result retention and recovery boundaries.

## Reporting problems

Include the library version/source revision, processor model/firmware, command or API method, exact package/catalogue/installed versions, operation status and a minimal reproduction. Remove credentials, certificate/private key material, real settings, personal paths and sensitive device properties before sharing logs. Saved processor logs can lag; live SSH logs are often more useful when an operation is currently stalled.

The unattended CLI reboot was exercised on the development MC4-R. The first recovery attempt authenticated after about three minutes but stopped on a transient HTTP 500 from device inventory while Home initialized. The fix retries bounded startup read failures (HTTP 500/502/503/504), while authorization/request errors still stop recovery. Read-only verification was then resumed without another reboot: all 21 previously loaded driver instances had unchanged identity/version and were Loaded; the original lease was verified and released. No driver packages changed. The failure and resumed verification are retained separately. The later uninterrupted V1 update validation is described below.

A complete unattended V1 driver workflow subsequently passed on the development MC4-R: 116 local tests, 105 processor driver tests, 11 processor SDK lifecycle tests and three read-only installed-driver health checks. It installed a fresh Entity V2 test host, staged the V1 update, received the matching swap-completion event, requested one Home configuration reboot, reconnected and verified lease ownership, verified the new driver version was Loaded, online, ready and configured, then removed the test host and released the lease. Independent checks confirmed the other 19 driver instances retained their identities and versions and were Loaded. The first V1 attempt exposed an incorrect assumption that swap initiates reboot; it required one separately recorded assisted reboot and was not counted as an unattended pass. A second attempt confirmed swap completion but an immediate SSH reboot returned with the previous version; it was stopped, reconciled and retained as a failed validation. The passing run used Home configuration reboot instead. V1 initial-install/removal hardware evidence is recorded separately in [V1 installation and removal](V1DriverRemoval.md). The SDK lifecycle tests and read-only health checks do not establish playback or device-control behavior.
