# Library API guide

The .NET 10 `CrestronHomeDevTools` library supplies configuration-management operations. It does not run NUnit tests, save credentials, provide a Crestron driver runtime or authorize processor reboots implicitly. Its API uses asynchronous calls and cancellation tokens.

For wire messages, parameter types, event correlation and the verified lifecycle sequences, see the [configuration-management protocol reference](ProtocolReference.md).

## API map

| Type | Responsibility |
|---|---|
| `ProcessorDiscovery` | Native processor discovery and exact system-name resolution. |
| `ProcessorConnectionOptions` | Host, ports, timeout and trusted certificate fingerprint. |
| `ConfigurationClient` | Authenticated inventory, commands, update/reload/removal and operation waits. |
| `DriverRemovalValidation` | [Final actual-driver removal](DriverRemovalValidation.md) with caller-supplied UI evidence and before/after logs. |
| `ProcessorErrorLog` | Pinned read-only current-boot log snapshots and conservative interval comparison. |
| `DriverDeployment` | Inspect a package, verify an SSH key and deploy/import by SFTP. |
| `ProcessorReboot` | Pinned SSH whole-processor reboot with mandatory caller-provided confirmation. |
| `DriverInstanceLifecycle` | Guarded install/update/reuse with version readiness checks. |
| `DriverUpdatePlan` / `DriverUpdateEligibility` | Reviewed versions, affected instance IDs and swap/reboot capabilities. |
| `DriverInstanceReady` | Confirmed device ID, model, version and action (`Installed`, `Updated`, `Existing`). |
| `OperationResult` | Processor operation ID, status and optional message. |

## Connect and discover

```csharp
var processors = await ProcessorDiscovery.FindAsync(cancellationToken);
var processor = await ProcessorDiscovery.ResolveAsync(systemName, cancellationToken);
var options = new ProcessorConnectionOptions
{
    Host = processor.Address,
    CertificateSha256 = verifiedCertificateSha256
};
await using var client = await ConfigurationClient.ConnectAsync(
    options, credential, cancellationToken);
var catalogue = await client.GetDriversAsync("Example", cancellationToken);
var installed = await client.GetDevicesAsync(cancellationToken);
```

Discovery is not authentication. With a configured certificate fingerprint, the connection verifies that processor certificate. Without a pin, normal system certificate validation applies. Defaults are HTTPS 443, WebSocket 49000 and a 30-second request timeout; authenticated firmware can report its HTTPS port. Passwords remain the caller's responsibility and are not saved by the library.

## Upload, then install or update

```csharp
var deployed = await DriverDeployment.DeployAsync(
    client, options.Host, credential, verifiedSshFingerprint,
    packagePath, TimeSpan.FromMinutes(3), cancellationToken);
if (!deployed.Available)
    throw new InvalidOperationException("Catalogue availability was not confirmed.");

var ready = await DriverInstanceLifecycle.EnsureAsync(
    client, deployed.CatalogueId, instanceName, roomId,
    expectedDeviceId, TimeSpan.FromMinutes(3), cancellationToken);
```

`Inspect(packagePath)` reads bounded package metadata without extracting files or executing driver code. Deployment retains the input while hashing/transferring it, verifies the SSH host key, stages a uniquely named temporary file, requests import and confirms the expected catalogue entry. Its result includes package identity, hash, catalogue ID and refresh status.

`EnsureAsync` requires an unambiguous name/model/room match and, when supplied, the expected installed device ID. It installs a missing target, updates an older version only when scope is exactly that instance and its reboot policy permits the operation, or confirms an already-current version. It waits for Loaded state. The caller must still perform application-level verification; Loaded does not establish that live devices work.

## Review a multi-instance update

```csharp
var plan = await client.PlanDriverUpdateAsync(catalogueId, cancellationToken);
// Review plan.Eligibility, especially versions and EligibleDeviceIds.
var operationId = await client.BeginDriverUpdateAsync(plan, cancellationToken);
var operation = await client.WaitForOperationAsync(
    operationId, TimeSpan.FromMinutes(3), cancellationToken);
if (operation.Status == "Failed")
    throw new InvalidOperationException("The processor rejected the update.");
var loaded = await client.WaitForDriverVersionAsync(
    plan.Eligibility.EligibleDeviceIds!, plan.Eligibility.AvailableDriverVersion!,
    TimeSpan.FromMinutes(3), cancellationToken);
```

Submission rechecks current eligibility against the reviewed plan. Do not substitute an arbitrary instance ID for a catalogue ID. Some firmware emits an operation ending without explicit success; use the independent Loaded/version confirmation as well as operation status.

In version 1.8.0 and later, `WaitForDriverVersionAsync` throws `InvalidOperationException` when the requested version reports `FailedToLoad`. It continues waiting if that failure belongs to an older version. It sends no recovery command. Inspect the load error and reconcile the installed state before deciding whether another update or reload is appropriate.

## Reload and remove

`GetReloadAffectedDevicesAsync` exposes the related scope for inspection; `BeginReloadDriverAsync` requests a supported targeted reload. Rediscover any dynamically assigned service port after activation or reload.

```csharp
await client.RemoveDriverInstanceAsync(
    ready.DeviceId, ready.Model, ready.Version,
    TimeSpan.FromMinutes(2), cancellationToken);
```

Only call removal after preserving results and confirming the test/application activity has stopped. It validates identity and dependency scope and verifies disappearance. It does not remove the imported catalogue package or infer that a test run is idle.

## Advanced commands and failures

`GetDeviceAsync` returns advertised command names and current properties. `ExecuteDeviceCommandAsync` accepts a named command and named parameters, supplied as an anonymous object or dictionary. This low-level entry point bypasses typed lifecycle guards; callers must validate the operation, target and expected effects themselves. The preview does not claim complete backup/restore or general system configuration coverage.

Catch `ProcessorApiException` for processor/API failures, `ArgumentException` for invalid inputs, and cancellation/timeout or transport exceptions as appropriate. An interrupted wait is not an undo operation. Do not automatically retry a write whose acceptance is unknown. Retain enough private evidence to inspect versions and affected instances before retrying.

From 1.16.2, an unconfirmed `prepareDriverForUse` or `commissionDevice` result exposes `DiagnosticCommand` and `DiagnosticResponse` on `ProcessorApiException`. The response is a retained JSON value, including an explicit JSON null when no result was returned. Store it in the caller's private run journal; do not log it to public output. These diagnostics preserve the reply for reconciliation and do not imply that an attempted installation is stopped or safe to retry. Other API failures may leave both properties unset.

Use [Crestron Home NUnit's workflow](https://github.com/oznetmaster/CrestronHomeNUnit/blob/HEAD/docs/ContinuousIntegration.md) when these operations must be gated by local/processor/live tests. Its orchestration adds source identity, retained artifacts, processor leases, evidence and test-aware cleanup; those are not implicit features of each DevTools call.

## Confirmed whole-processor reboot

```csharp
var outcome = await ProcessorReboot.RequestAsync(
    new ProcessorRebootTarget(processor.Address, systemName),
    credential, verifiedSshFingerprint,
    async (target, token) => await ShowRebootConfirmationAsync(target, token),
    TimeSpan.FromSeconds(30), cancellationToken);
```

`ShowRebootConfirmationAsync` is supplied by your application. Show the target name/address and explain that every Home program/driver will be interrupted; return true only after explicit confirmation or validation of a preauthorized processor-specific automation policy. GUI callers must marshal their dialog to the UI thread. The library has no graphical UI dependency, stores no credentials and requires the callback on every request. It authenticates and waits for the SSH console before requesting confirmation. The display name is caller metadata; the verified SSH fingerprint authenticates the target.

`ProcessorRebootResult.Status` is `Cancelled`, `Accepted` or `Unconfirmed`. Declining sends nothing. Accepted requires the console acknowledgement and does not verify restart completion. Transport failure, timeout or cancellation after the write is attempted produces Unconfirmed because reboot may already be underway. Failures before submission throw normally. The command is sent once and is never retried; this SSH API is a standalone console reboot. Driver-update workflows instead use `ConfigurationClient.RequestProcessorRebootAsync` so Home can finish its configuration restart sequence. V1 updates require the matching driver-swap completion event before requesting reboot. The timeout bounds connection and response waits, not the user's time reading the confirmation dialog.

This wrapper is covered by simulated console/confirmation tests. The SSH command and acknowledgement were observed during an earlier separately authorized hardware recovery; the wrapper has subsequently been exercised during an authorized hardware reboot. Recovery exposed and corrected a startup-readiness case; see the compatibility guide for scope and evidence.

## Reboot-aware driver lifecycle

`EnsureAsync(..., reboot: handler)` and `RemoveDriverInstanceAsync(..., rebootHandler: handler)` accept an optional `DriverRebootHandler`. Omitting it preserves the default reboot-free behavior. `BeforeSubmitAsync` must validate authorization and save durable evidence before any operation that can reboot. `RecoverAsync` returns a fresh authenticated `ConfigurationClient`; its caller owns that returned connection. The handler must preserve verified certificate/SSH pins and the intended processor identity.

For updates, eligibility must explicitly say swap is supported and whether reboot is required. Reboot authorization never permits unknown capabilities, expanded instance scope or automatic downgrade. A reboot-required update submits the swap command once, then waits for `swapDriverCompleted` matching both its operation ID and driver ID. Generic operation success is insufficient. The event must confirm `IsRebootRequired` and no devices requiring reconfiguration. Only then does the explicitly authorized reboot handler call `ConfigurationClient.RequestProcessorRebootAsync`. This method requires a caller confirmation callback, identifies the unique advertised processor reboot capability, rechecks its identity and invokes `cp.processorOperations:beginReboot` once. A plain SSH reboot is not equivalent: immediate SSH restart returned with the previous driver version during hardware validation. A lost response or missing/failed completion event stops the flow without a reboot or repeated update. `DriverRebootRequest.SwapCompletion` carries the confirmed event into durable workflow evidence. `ProcessorRestartRecovery.WaitAsync` waits for the old event connection to end, retries read-only authenticated reconnection/inventory requests while the processor starts, and returns the new connection. Lifecycle code then verifies Loaded state, the exact version and instance identity. A timeout does not submit another update or reboot.

Initial installation and removal can be configured separately using `RebootAfterInstall` / `RebootAfterRemoval` on the handler. These mean an explicit reboot after the operation has returned successfully. Use them only where that sequence is known to be required; a swap-requires-reboot flag does not by itself establish initial installation/removal behavior. The workflow avoids a second explicit reboot if the old management connection has already ended. A lost installation/removal response remains uncertain and is not replayed.

For a reviewed shared V1 reload scope, `AdditionalRemovalRebootDeviceIds` explicitly identifies existing instances that must be preserved. It requires `RebootAfterRemoval`; the scope must match exactly and preservation is verified after restart. See [V1 installation and removal](V1DriverRemoval.md) for the contract and hardware evidence.

The low-level `BeginDriverUpdateAsync(..., allowProcessorReboot: true)` permits submission of a reboot-required swap. It does not request the reboot or perform recovery; callers must wait for matching swap completion and then use the configuration reboot sequence. Prefer the lifecycle handler or NUnit workflow when test execution must continue afterward.
