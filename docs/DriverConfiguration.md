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

## Validation

Offline tests cover current-value types, driver-defined masking, omitted values, read-only inspection, flat and multi-step configuration, identity mismatch, unknown/read-only items, duplicate input IDs, rejected/repeated steps, secret-safe error messages, no retries, configured-instance preservation and completion timeouts.

MC4-R / Home 4.11.322 validation read an installed configuration, preserved it on repeated configure, and completed a two-step wizard on a separate temporary instance. Cleanup preserved the original instance and its settings. The ordinary removal helper refused a shared reload scope, which was inspected and handled explicitly. No physical control was operated. Validate each driver's actual wizard fields and shared-instance scope separately.

Arbitrary driver wizards, reconfiguration and V1 commissioning are not claimed as validated. See the [protocol reference](ProtocolReference.md#configure-a-newly-installed-entity-v2-driver), [processor coordination](ProcessorCoordination.md) and [compatibility matrix](Compatibility.md).
