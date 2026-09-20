# Processor coordination and stored packages

This document describes DevTools 1.1.0 and Crestron Home NUnit 1.2.0. Update both desktop tools and processor test packages before relying on this coverage. Older tools and already-installed older test hosts do not acquire the new shared gate.

## One operation owner per processor

DevTools mutation commands, NUnit workflows, standalone NUnit CLI test commands, the Windows runner, and new test-host Home tile commands cooperate through `/user/CrestronHomeNUnit-WorkflowLease`. This resides on the processor, so separate computers and GitHub repositories share the same gate. GitHub's job concurrency setting alone cannot coordinate those entry points.

Desktop tools claim that path with an atomic SFTP directory creation and save an unpredictable owner marker inside it. Home tile execution claims the same path as an exclusive file. Neither operation can replace an existing file or directory. A workflow passes its existing owner to its test host; the host verifies it before running. The sibling `.ActiveTest` file serializes delegated test execution and prevents release while tests are active. Release also claims that file exclusively, closing the gap between testing for active work and removing a reservation.

An operation that times out or loses its connection does not imply the processor stopped. Unknown execution, update or removal outcomes retain the reservation. There is no automatic expiry, recursive deletion or lock stealing. Keep receipts private; verify remote state and exact ownership before recovery. Disposing the library lease closes its connection but intentionally does not release it.

The standalone reboot CLI retains its lease through shutdown and authenticated Home startup, then verifies the retained owner before releasing. Its default startup wait is 600 seconds; `--timeout` overrides it. A failed startup check retains the reservation and returns failure. The CLI opens its management connection before submitting the reboot so it can observe that connection closing; the existing low-level SSH reboot API continues to report acknowledgement separately from startup.

Library consumers wrap their complete operation in `ProcessorOperationLease.AcquireAsync`, retain `Owner` durably before submitting changes, and call `ReleaseAsync` only after confirming the operation stopped. Do not nest another independent reservation inside a workflow that already owns the processor. See the NUnit [manual reservation commands](https://github.com/oznetmaster/CrestronHomeNUnit/blob/HEAD/docs/GitHubHardwareCI.md).

Crestron's Configure software, arbitrary SFTP/SSH clients and other programs cannot be forced to honor this protocol. Reserve the processor before manual Configure work and release it only when those operations finish. One reservation does not make simultaneous manual and automated changes within that reservation safe.

## Visual Studio deployment

Use [DeployDriver.ps1](../scripts/DeployDriver.ps1) for an automatic build deployment. The driver repositories and NUnit processor-package build share this entry point's implementation. It requires PowerShell 7 and a DevTools console that reports the shared lease and verified import capabilities. It uploads a complete package, refreshes the catalogue, and waits for the expected package version. It does not update installed instances or edit Home's internal manifests.

Keep these properties in the locally excluded project `.csproj.user` file:

| Property | Purpose |
|---|---|
| `CrestronHomeIP` | Processor address for this deployment. |
| `CrestronHomeFtpUser` | Processor user name. |
| `CrestronHomeSftpPassword` | Processor password. |
| `CrestronHomeSshFingerprint` | Independently verified SSH SHA-256 fingerprint. |
| `CrestronHomeCertificateSha256` | Independently verified management HTTPS certificate fingerprint. |
| `CrestronHomeDevToolsPath` | Absolute path to the updated console executable or DLL. |

`CRESTRON_HOME_DEVTOOLS_PATH` or the script's `-DevToolsPath` argument can supply the tool path instead. Credentials travel in the child process environment, never command-line arguments. They are not printed. Scripts fail on an occupied reservation, missing capability, failed import or unconfirmed version. Build targets must propagate that failure: do not use `ContinueOnError="true"`, and only write a deployment stamp after success. An import is not evidence that an installed device was updated; use activation or a gated workflow for that step.

SSH fingerprints use the non-padded base64 SHA-256 digest returned by `DriverDeployment.ReadSshFingerprintAsync` and the console setup flow. If transferring an independently verified OpenSSH fingerprint, omit its `SHA256:` display prefix; DevTools compares the digest exactly. This SSH value is different from the hexadecimal HTTPS certificate fingerprint. A format correction must preserve the independently trusted key, not accept a newly observed key without verification.

## Catalogue and storage are separate from installed instances

`remove` and NUnit's automatic cleanup remove a selected installed instance. Imported packages remain in `/user/ThirdPartyDrivers/Storage/Rad`; Home also maintains extracted copies beneath `/user/Data/UsedThirdPartyDrivers`. Emptying the import folder does not remove those retained packages.

The read-only `stored-packages` command inspects retained package manifests without loading assemblies. It reports package identity/version, bytes, matching installed-device IDs and matching current catalogue IDs. Unknown formats remain visible with an inspection error. Generic filenames such as `processor.pkg` are not identities. Matching any installed model conservatively protects all its versions; no matching device is **not** proof that a file can be deleted. Pending updates, retained configuration and firmware-managed caches require additional checks. The command is a point-in-time inventory, not a locked deletion plan.

Crestron's [documented side-loaded driver deletion procedure](https://docs.crestron.com/en-us/8525/Content/CP4R/Appendix/Third-Party-Drivers/Delete-Crestron-Drivers.htm) deletes the driver from `Storage/Rad` and restarts the processor. That manual covers the older Home interface. Reboot-free catalogue removal on Home 4.11 has not been validated, and no package purge command or automatic storage deletion is implemented. Never use a broad cache-directory deletion or modify `DeviceManifest.cfg` to achieve cleanup.

## Hardware evidence

On the development MC4-R / Home 4.11.322, separate NUnit and DevTools clients excluded one another in both directions. A temporary Entity V2 test host passed 69 processor tests; its Home tile passed 35 lifecycle tests. A desktop reservation blocked tile execution, and tile execution blocked DevTools. Manual release was refused while an execution marker existed. The temporary test instance was then removed and both gates were absent. The revised build entry point refused a held reservation, then imported and verified the test package while preserving all installed instances. Additional firmware/platform coverage remains separate from these checks.
