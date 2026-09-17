# Inspect and configure an installed driver

**Processor compatibility:** Configuration-management commands require a **V2 Crestron Home processor**. V1 Crestron Home processors do not support these commands. This refers to the processor platform, not the driver type: supported V2 processors can host V1 drivers, whose update workflow requires an explicitly authorized reboot.

DevTools 1.2.0 adds `driver-configuration` and `configure-driver` to both the interactive console and the one-command CLI. The first reads current configuration; the second performs initial configuration after installation. `configure` continues to mean choosing a processor and saving its encrypted connection profile.

## Read current settings

```text
driver-configuration --device 17
```

Use an installed device ID from `devices`. Connection selection uses the usual `--profile`, `--processor` or private `--settings` options. The JSON response contains the device identity and version, configuration status, whether reconfiguration is advertised, and each advertised setting's ID, title, value type, required/read-only flags and current value.

The command performs one device read. It does not start a wizard, submit values, reload a driver, or obtain a mutation lease. `ItemsAvailable: false` means Home did not expose an item list. An empty list on an unconfigured driver may mean that the initial setup wizard is required.

Masking follows **only the driver's `Masked` metadata**. A true flag suppresses `CurrentValue`; an absent or false flag allows a returned scalar value to be displayed. Field names are not used to guess sensitivity. `HasCurrentValue` distinguishes a returned value from an omitted/null one; defaults and display overrides are not presented as saved values. Some drivers omit stored passwords or tokens entirely, so this output is not a credential backup or a complete round-trip export.

Output may therefore contain whatever the driver returns as unmasked, including local addresses or credentials. Keep captured output private when that applies. The API does not claim to classify secrets independently of the driver.

## Apply initial configuration

Install the intended package/instance with `deploy` and `activate`, then supply its exact device ID, model and version:

```text
configure-driver --device 17 --model "Example Gateway" --version 1.2.003.0004 --input C:/private/driver-configuration.json
```

Values come from the private input file, never command-line credential arguments. Keep the file outside the checkout or in `.git/info/exclude`. A driver exposing flat configuration items can use:

```json
{
  "Host": "gateway.example.invalid",
  "PollingSeconds": "30"
}
```

For an initial setup wizard, provide ordered step IDs and their explicit string values:

```json
{
  "steps": [
    { "id": "Connection", "values": { "Host": "gateway.example.invalid", "Token": "REPLACE_LOCALLY" } },
    { "id": "Options", "values": { "PollingSeconds": "30", "Enabled": "true" } }
  ]
}
```

These IDs are illustrative; use the actual driver's advertised configuration schema and protocol documentation. Numeric and Boolean choices are submitted as strings. Supply required displayed defaults explicitly; an omitted value is not assumed to accept a wizard default.

The command takes the shared processor lease, verifies the current identity/version/configuration state, and preserves an already configured instance without writing. For an unconfigured instance, it validates writable item IDs and each expected wizard step, submits each step once, and requires the wizard to end after the final planned step. It then waits for `isConfigured: true`, bounded by `--timeout` (120 seconds by default).

The result contains `DeviceId`, `Changed` and `Configured`. A configured result confirms the setup state, not device connectivity or operation; run the application's online/ready and live checks separately. Changing an already configured driver's settings is not implemented by this initial-configuration command.

Validation errors, unexpected/repeated steps and connection uncertainty stop the operation. No submitted command is retried. An incomplete mutation retains the processor lease and a private receipt for inspection. There is no rollback of settings already accepted by earlier steps.

## Library use

```csharp
var snapshot = await DriverConfigurationInspection.GetAsync(client, deviceId, cancellationToken);
var inputs = DriverConfiguration.ReadInputs(privateInputPath);
// Hold the shared ProcessorOperationLease across install/configure/verification.
var configured = await DriverConfiguration.ConfigureAsync(
    client, readyInstance, inputs, TimeSpan.FromMinutes(2), cancellationToken);
```

`readyInstance` is the `DriverInstanceReady` returned by `DriverInstanceLifecycle.EnsureAsync`, or an explicitly verified device/model/version target. The reusable configuration helper requires the caller to coordinate mutations with the shared lease, as other lifecycle helpers do. The console manages that lease automatically.

## Initialize a newly commissioned managed child

**Requires DevTools 1.6.0:** `DriverConfiguration.BeginManagedDeviceAsync` enters a newly commissioned child's initial configuration. The existing console `configure-driver` command is unchanged; this operation is for code coordinating managed-child commissioning.

After `cp.platformController:commissionManagedDevice` reports success, record its returned child ID, verify the child's parent/model/version, and enter its initial configuration wizard once. Configure Pro performs this step even when the wizard immediately reports that there are no prompts. An empty advertised configuration-item list, or an `isConfigured` flag which has already become true, does not reliably replace that commissioning step.

```csharp
// Hold the shared processor lease. Record commissioning and this request's intent durably.
var newlyCommissioned = new DriverInstanceReady(childId, expectedChildModel,
    expectedVersion, "Installed");
var firstStep = await DriverConfiguration.BeginManagedDeviceAsync(
    client, newlyCommissioned, expectedParentId, cancellationToken);
```

Supply only the new child identified by the successful commissioning receipt. The helper validates its parent, model, version and advertised wizard command. It calls `cp.driverConfiguration:getFirstConfigurationStep` with `isReconfiguring: false` once; it does not apply values or retry failures. A returned step can contain private settings and must remain in private evidence. If a step is returned, process its actual requirements with explicitly provided values. A null response means there are no prompts; it does **not** prove that initialization is complete. Independently observe both online and ready, and retain the result.

Do not use this operation as a periodic health check, on an existing configured child, or to replay an uncertain commissioning attempt. Keep the durable intent and reconcile the existing child before continuing after a lost response.

CP4-R / Home 4.11.322 comparisons used identical package bytes. Omitting this entry call left a managed child offline for two minutes; including it produced ready/online observations in less than one second, confirmed through fresh connections. Both Hvac and Other children passed on a newly installed platform. A separate existing platform driver also passed a new managed-child check. These are observed commissioning cases, not a claim that every driver wizard is supported.

## Journaled commissioning in the console and CI

**Requires DevTools 1.6.0:** `ManagedDeviceCommissioning.CommissionAsync` combines new-child commissioning, configuration entry and readiness observation. The interactive console and CLI expose it as:

```text
commission-child --input C:/private/child.json --journal C:/private/run-001/child
```

Use the normal processor/profile selection options. The console acquires the shared processor lease for this command. A larger workflow using the library must hold its existing shared lease across commissioning, tests and cleanup; do not invoke the separately locking console inside that held lease.

The private input file identifies the exact installed platform, one advertised managed-device ID, the expected child model and an existing Home room:

```json
{
  "ParentId": 17,
  "ParentModel": "Example Gateway",
  "ParentVersion": "1.0.000.0001",
  "ManagedDeviceId": "example-child",
  "Name": "CI Example Child",
  "ChildModel": "Example Child",
  "LocationId": 3
}
```

Read these identities from the selected processor; the example IDs are placeholders. The child is expected to inherit the platform package version. Common device credentials are not automatically supplied or changed by this command. If commissioning reports that they are required, retain the receipt and resolve that prerequisite explicitly.

The journal directory must not already exist. The coordinator durably records intent before each command, the returned child ID, the private configuration response and the observed ready state. Requests are sent once. Do not delete a partial journal to retry an uncertain attempt: inspect its recorded identity and current processor state, then explicitly reconcile or clean up that owned child. A pre-existing device ID returned by commissioning is rejected before initialization.

`State: "Ready"` gives CLI exit 0 after both online and ready were observed. `State: "ConfigurationRequired"` gives exit 3, with the pending wizard step in the private journal; the coordinator never guesses or applies its values. Failures/timeouts retain their journal. Uncertain console mutations also retain the processor lease for reviewed recovery. Never interpret a null configuration step alone as readiness.

After successful setup, use the returned child ID for tests. Successful commissioning deliberately leaves the child installed so the next stage can use it. Once the test producer has verified physical-state restoration, the encompassing workflow can call `ManagedDeviceCommissioning.RemoveCreatedAsync` under its existing processor lease. For separately orchestrated console runs, the CLI command is:

```text
remove-created-child --journal C:/private/run-001/child
```

Use the same processor profile as commissioning. This command acquires its own shared lease; do not invoke it inside a workflow already holding that lease. The private journal is not a cross-processor device identifier. Its commissioning result must be terminal (`Ready` or `ConfigurationRequired`), and the recorded child ID, parent, model, version, name and room must still match. Only a leaf child of the confirmed reloadable Entity V2 platform can be removed. This operation removes the child assignment; it does not replace a package or reboot/unload the platform.

The cleanup subdirectory records intent before the single removal command, the response, disappearance and preservation of every other device's identity, room, version and observed loading/readiness state. Physical devices and settings are outside this removal check: the test producer must restore and verify them first. A partial or previous cleanup journal is never replayed, including after a lost response. Inspect current state and reconcile that attempt explicitly. Successful recovery cannot turn a failed test into a pass.

Keep manually installed devices outside this scope. The journal can contain configuration and device data and belongs outside GitHub and public artifacts. Commissioning and journal-owned cleanup require DevTools 1.6.0 or later.

The complete library coordinator passed a real CP4-R check, including a fresh-connection readiness confirmation and caller-owned cleanup. The actual commissioning and `remove-created-child` CLI commands also passed a real round trip: independently observed child readiness, journal-owned removal, preserved original inventory and released reservations. No physical control was sent. These observations are development validation, not final submission-candidate acceptance.

## Validation

Offline tests cover current-value types, driver-defined masking, omitted values, read-only inspection, flat and multi-step configuration, identity mismatch, unknown/read-only items, duplicate input IDs, rejected/repeated steps, secret-safe error messages, no retries, configured-instance preservation and completion timeouts.

MC4-R / Home 4.11.322 validation read an installed configuration, preserved it on repeated configure, and completed a two-step wizard on a separate temporary instance. Cleanup preserved the original instance and its settings. The ordinary removal helper refused a shared reload scope, which was inspected and handled explicitly. No physical control was operated. Validate each driver's actual wizard fields and shared-instance scope separately.

Arbitrary driver wizards, reconfiguration and V1 commissioning are not claimed as validated. See the [protocol reference](ProtocolReference.md#configure-a-newly-installed-entity-v2-driver), [processor coordination](ProcessorCoordination.md) and [compatibility matrix](Compatibility.md).
