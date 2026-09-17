# Configuration-management protocol reference

**Processor compatibility:** Configuration-management commands require a **V2 Crestron Home processor**. V1 Crestron Home processors do not support these commands. This refers to the processor platform, not the driver type: supported V2 processors can host V1 drivers, whose update workflow requires an explicitly authorized reboot.

This is an unofficial configuration-management interface, documented here from verified behavior and the independently implemented CrestronHomeDevTools client. It is not an official Crestron specification or a promise of compatibility with future firmware. We have not found an official specification for this management interface.

The reference describes the subset needed for discovery, authenticated inventory, package import, driver installation/update/removal and restart recovery. It is separate from the public Home control REST API and from the Crestron Home NUnit test protocol.

**Validation baseline:** MC4-R and CP4-R running Crestron Home 4.11.322, with hardware validation recorded on September 13, 2026. Entity V2 lifecycle operations and configuration reboot/recovery were exercised on both models; V1 update/reboot was exercised on MC4-R only. See the [operation-by-model evidence](Compatibility.md#hardware-evidence). Other models and firmware require validation.

All addresses, credentials, tokens, GUIDs, system names and positive device IDs in examples are fictitious. JSON examples are abbreviated to the fields relevant to the operation. Command and property names retain their actual spelling and case.

## Contents

- [Scope and conventions](#scope-and-conventions)
- [Network services](#network-services)
- [Processor discovery](#processor-discovery)
- [Authentication and session lifetime](#authentication-and-session-lifetime)
- [HTTPS requests and responses](#https-requests-and-responses)
- [Identity and inventory](#identity-and-inventory)
- [Command reference](#command-reference)
- [Asynchronous operation events](#asynchronous-operation-events)
- [Package upload and catalogue import](#package-upload-and-catalogue-import)
- [Driver installation and update](#driver-installation-and-update)
- [V1 swap completion and reboot](#v1-swap-completion-and-reboot)
- [Reload and removal](#reload-and-removal)
- [Restart readiness and recovery](#restart-readiness-and-recovery)
- [Failures and retry rules](#failures-and-retry-rules)
- [Automation boundaries and private data](#automation-boundaries-and-private-data)
- [Implementation map and remaining unknowns](#implementation-map-and-remaining-unknowns)

## Scope and conventions

**Verified behavior** means the exchange or outcome has been exercised by this project on the validation baseline. **Client policy** means a precaution, parser bound or automation rule enforced by DevTools; it is not necessarily a firmware requirement. **Unverified** means the project does not yet claim working hardware support for that behavior.

Use the names and identities returned by the processor. Do not construct catalogue IDs, assume positive device IDs are stable across removal/reinstallation, or treat an advertised processor name as proof of identity. Treat unknown fields as extensible data; an absent capability or boolean is unknown, not false.

## Network services

| Purpose | Current client endpoint/default | Role |
|---|---|---|
| Native discovery | IPv4 UDP 41794 | Broadcast queries and system-name replies |
| Anonymous Home identification | `https://192.0.2.10/cws/api/v2` | Identifies Home candidates and reports program/model information |
| Authentication and events | `wss://192.0.2.10:49000/` | Login, then persistent asynchronous event stream |
| Authenticated requests | `https://192.0.2.10/cws/api/` | Version 2 reads and command POSTs; HTTPS defaults to 443 |
| Package transfer | SFTP over SSH, default port 22 | Upload to the processor's import directory |
| Standalone console reboot | SSH shell | Separate from Home's configuration-aware reboot operation |

The authenticated WebSocket response can report `HttpPort`, which the client uses for subsequent HTTPS requests. DevTools allows the initial HTTPS and WebSocket ports to be configured. These defaults are not an exhaustive list of processor services.

The processor's native discovery and the NUnit package's mDNS advertisement serve different purposes. NUnit test TCP ports may change after activation or restart; discover them through the test transport rather than deriving them from these management ports.

## Processor discovery

### Native UDP query

The current client binds an IPv4 socket to local port 41794, enables broadcast and address reuse, and sends a 266-byte query to port 41794 on each active non-loopback IPv4 interface's subnet broadcast address.

| Byte offsets, zero-based | Query contents |
|---|---|
| 0â€“9 | `14 00 00 00 01 04 00 03 00 00` in hexadecimal |
| 10 onward | ASCII workstation hostname, at most 255 bytes |
| Remaining bytes | Zero padding, including a terminator after the hostname |

The meanings of all header bytes have not been established; the table records the constants used by the working implementation. The client queries approximately every two seconds during a five-second discovery window. These timings are client choices.

### Native UDP reply

The parser accepts replies from source port 41794 with a length of 266â€“4096 bytes and prefix `15 00 00 00`. It reads the system name as UTF-8 from offset 10 to the first zero byte within the following 256 bytes, or to offset 266 if none is present. It trims whitespace and rejects empty names or names containing control characters. The packet's source IP supplies the address.

Other reply bytes are not interpreted. The size limits and maximum 1,024 collected addresses are client bounds, not a complete discovery specification.

### Anonymous Home identification

For each native discovery candidate, the client performs `GET /cws/api/v2`. The response is a JSON object, rather than the authenticated `Result` envelope described below. The current Home detector requires:

- `ConfigurationSchema`, an object containing `House` and `Devices` entries;
- `SystemType`, a string used as the reported model;
- `ProgramVersion`, a string used as the Home program version.

The schema entries' complete structures are outside this reference. Report `SystemType` as processor-supplied metadata; this project has not established that it always matches the physical chassis label.

The anonymous identification probe sends no credentials and permits an untrusted certificate solely for discovery. This does **not** establish trust for authentication. Authenticated connections validate the certificate independently. Name selection is exact and case-insensitive; duplicate names are an ambiguity requiring an explicit address. Broadcast discovery does not establish routed-network or IPv6 support.

## Authentication and session lifetime

1. Establish the TLS WebSocket connection at `/` on the configured event port.
2. Send one UTF-8 JSON text message:

```json
{"UserName":"example-developer","Password":"EXAMPLE-PASSWORD"}
```

3. Read the authentication response. The current client requires `Authenticated: true` and nonempty `RestV2Token` and `WebApiToken` strings. An abbreviated successful response is:

```json
{
  "Authenticated": true,
  "RestV2Token": "EXAMPLE-SESSION-KEY",
  "WebApiToken": "EXAMPLE-AUTH-TOKEN",
  "HttpPort": 443
}
```

4. Configure HTTPS requests with these headers:

```http
Crestron-RestAPI-AuthKey: EXAMPLE-SESSION-KEY
Crestron-RestAPI-AuthToken: EXAMPLE-AUTH-TOKEN
```

5. Perform `GET /cws/api/v2/login` with both headers. Require an HTTP success status. If the response includes a `Crestron-RestAPI-AuthKey` header, replace the current key with it. The client does not depend on a response-body schema for this validation request.
6. Continue consuming WebSocket events while making HTTPS requests.

The mapping matters: **`RestV2Token` becomes `AuthKey`; `WebApiToken` becomes `AuthToken`.** These session tokens are separate from stored username/password credentials and from SSH host-key trust.

Client policy is to revalidate with `GET v2/login` before an HTTPS request when more than one minute has elapsed since validation. That interval is not a documented server token lifetime. HTTPS requests are serialized within each connection, redirects are disabled, and the default request timeout is 30 seconds. The client refuses further requests through an established session whose WebSocket is no longer open.

For authentication, both TLS channels use normal system certificate validation unless an explicitly configured SHA-256 certificate pin is supplied. A configured pin must match the presented certificate; it is not a blanket certificate bypass. SSH/SFTP uses a separately verified SHA-256 host-key fingerprint. Authentication failure does not authorize trying a different processor, account or trust setting automatically.

## HTTPS requests and responses

Authenticated request paths used by this implementation are:

| Method | Path below `/cws/api/` | Purpose |
|---|---|---|
| GET | `v2/login` | Validate the session and accept a refreshed key header |
| GET | `v2/Devices` | Device inventory |
| GET | `v2/Locations` | Configured locations; array of objects including numeric `Id`, `Name` and `Category` |
| GET | `v2/Devices/{deviceId}` | One device, including commands and properties |
| POST | `v2/Devices/{deviceId}/Command` | Execute a named capability command |

A command body contains `CommandName` and a JSON object named `Parameters`. Use an empty object when the command has no parameters. Parameter names are case-sensitive in examples and should be sent exactly as shown.

```http
POST /cws/api/v2/Devices/-6/Command
Content-Type: application/json; charset=utf-8
Crestron-RestAPI-AuthKey: EXAMPLE-SESSION-KEY
Crestron-RestAPI-AuthToken: EXAMPLE-AUTH-TOKEN
```

```json
{
  "CommandName": "cp.platformDriverController:getDriver",
  "Parameters": {"driverId":"example-catalogue-entry"}
}
```

Device reads and command responses use a `Result` envelope. `Result` can be an object, array, string, boolean or null depending on the operation. For example, a begin-operation command returns an operation ID string:

```json
{"Result":"11111111-1111-4111-8111-111111111111","Error":null}
```

Require an HTTP success status before interpreting this envelope. A present, non-null `Error` is a failure even with HTTP success. Its detailed schema is not relied upon and the client withholds raw error content because it can include private configuration. A missing `Result` is an unexpected response, not an empty success. `GET v2/login` and anonymous identification have their own handling described above.

## Identity and inventory

Four identities must remain distinct:

| Identity | Meaning |
|---|---|
| Package `.dat` `driverId` | GUID inside the built package |
| Catalogue `Id` / command parameter `driverId` | Opaque identifier returned by the processor for a catalogue entry/version |
| Installed device `Id` / parameter `deviceId` | Integer identifying a configured device instance |
| `OperationId` | String correlating a submitted operation with its events |

The package GUID is not substituted for a catalogue ID. DevTools currently locates the imported catalogue entry by model, manufacturer, numeric version and `AvailabilityState == "LocalByUser"`; ambiguous matches stop deployment. This is a matching policy, not a published catalogue-ID construction algorithm.

`GET v2/Devices` returns a `Result` object keyed by device ID strings. Its values contain device objects. A single-device request returns one device object; the client treats HTTP 404 as an absent device.

```json
{
  "Result": {
    "42001": {
      "Id": 42001,
      "ParentDeviceId": 42000,
      "Name": "Example Driver",
      "Model": "Example Model",
      "LocationId": 41001,
      "Commands": ["cp.deviceConfiguration:setLocation"],
      "PropertyValues": {
        "cp.driverInformation:version": "1.2.003.0004",
        "cp.driverConfiguration:driverLoadingStatus": "Loaded",
        "cp.driverConfiguration:supportsUnloadReloadDriver": true,
        "cp.driverConfiguration:swapDriverRequiresReboot": false
      }
    }
  },
  "Error": null
}
```

Additional device properties may include credentials. Select specific properties for diagnostics instead of serializing whole inventories. Relevant observed property names include:

| Property | Use |
|---|---|
| `cp.driverInformation:version` | Installed driver version string |
| `cp.driverConfiguration:driverLoadingStatus` | Require `Loaded` for activation verification |
| `cp.driverConfiguration:supportsUnloadReloadDriver` | Explicit capability check for reload/removal policy |
| `cp.driverConfiguration:swapDriverRequiresReboot` | Explicit reboot requirement |
| `cp.driverConfiguration:isConfigured` | Optional installed-driver health check |
| `onlineIndicator:isOnline` | Optional device online check |
| `readyIndicator:isReady` | Optional readiness check |

Health properties are not guaranteed on every device. `Loaded` and a matching version establish activation, not correct physical-device behavior. Numeric version comparison preserves every component, including Debug build numbers; padding alone is ignored. Do not compare version strings lexically or discard their fourth component.

## Command reference

### Driver Management Gateway

The verified implementation addresses gateway device **`-6`**. Every command in this table has the prefix **`cp.platformDriverController:`**. The table shows command suffixes for readability; the POST body must contain the full name.

| Suffix | `Parameters` | Expected `Result` and use |
|---|---|---|
| `getDriverMetadataFilterOptions` | `{"filterType":"PrimaryFunction"}` | Array of category objects; client uses each `Id` |
| `getDrivers` | `filterType`, `filterIds`, `substringFilterTextTokens` (at most three tokens), `excludeFilterIds` | Array of catalogue entries |
| `getDriver` | `{"driverId":"example-catalogue-entry"}` | Catalogue entry or null |
| `getDevicesEligibleForDriverUpdate` | `{"driverId":"example-catalogue-entry"}` | Eligibility object below |
| `beginLocalDriverRefresh` | `{}` | Operation ID; import local staged packages |
| `getIsDriversRefreshInProgress` | `{}` | Boolean indicating refresh activity |
| `prepareDriverForUse` | `{"driverId":"example-catalogue-entry"}` | Current installation path requires string `"Success"` |
| `commissionDevice` | `{"driverId":"example-catalogue-entry","name":"Example Driver","locationId":"41001"}` | Object containing `CommissioningResult` and new `Id` |
| `beginSwapDriverForAllEligibleDevices` | `{"driverId":"example-catalogue-entry"}` | Operation ID; scope comes from eligibility, not an instance parameter |
| `findUnloadReloadDriversAffectedDevices` | `{"deviceId":42001}` | Array of affected integer device IDs |
| `beginReloadDrivers` | `{"deviceId":42001,"reloadReferenceDeviceOnly":true}` | Operation ID for a targeted reload |

Example catalogue query parameters:

```json
{
  "filterType": "PrimaryFunction",
  "filterIds": [],
  "substringFilterTextTokens": ["Example"],
  "excludeFilterIds": []
}
```

The observed processor rejects empty categories combined with empty search tokens. To request all entries, DevTools first obtains the `PrimaryFunction` category IDs and uses them in `filterIds`. It splits a nonempty search into space-separated tokens and does not request exclusion of the Other category.

Catalogue fields consumed by the client include `Id`, `Model`, `Manufacturer`, `Version`, `Developer`, `PrimaryUxCategory` and `AvailabilityState`. This is a partial field list, not a closed schema.

An abbreviated update-eligibility result is:

```json
{
  "InstalledDriverVersion": "1.2.003.0003",
  "AvailableDriverVersion": "1.2.003.0004",
  "IsSupportsSwapDriver": true,
  "IsSwapDriverRequiresReboot": false,
  "EligibleDeviceIds": [42001]
}
```

These fields can be absent or null; DevTools refuses to infer support from missing data. Additional eligibility fields may be present. Immediately before submission, the client rechecks both versions, reboot requirement and the full eligible ID set against the reviewed plan. `EnsureAsync` restricts automatic updates to exactly one intended instance; the lower-level reviewed-plan API can represent multiple eligible instances.

### Commands on discovered device targets

| Target | Full command | Parameters | Outcome |
|---|---|---|---|
| Intended installed instance | `cp.deviceConfiguration:setLocation` | `{"locationId":null}` | Verified removal path for supported instances; confirm inventory disappearance |
| Loaded childless instance | `cp.deviceConfiguration:setLocation` | `{"locationId":41002}` | Move to an existing room; verify unchanged instance identity and loaded version |
| Unique processor device advertising this command | `cp.processorOperations:beginReboot` | `{"rebootReasonInAFewWords":"Authorized development driver update"}` | Operation ID acknowledging a Home configuration reboot request |

Do not hard-code the positive processor device ID for reboot. DevTools discovers the unique device advertising `cp.processorOperations:beginReboot`, obtains explicit caller confirmation, rechecks its identity/capability and sends the command once. A missing or ambiguous target stops the operation.

## Asynchronous operation events

Events arrive as JSON text messages on the authenticated WebSocket. Read continuously and reassemble fragmented WebSocket messages before JSON parsing. DevTools imposes an 8 MiB event-message bound. HTTP request completion and event arrival can race: cache completion by operation ID even if it arrives before the HTTP command response.

The general event consumed by the client is:

```json
{
  "EventType": "cp.types:operationStatusChanged",
  "OperationId": "11111111-1111-4111-8111-111111111111",
  "LatestStatus": 3,
  "Message": "Example operation completed"
}
```

`Message` is optional. The client supports these numeric status values and their corresponding string names:

| Numeric value | String name | Interpretation |
|---|---|---|
| 0 | `Started` | In progress |
| 1 | `Progress` | In progress |
| 2 | `Failed` | Failure |
| 3 | `Succeeded` | Explicit operation success |
| 4 | `Ended` | Operation ended; does not itself prove success |

The tracker retains the first terminal result for an ID, so a later `Ended` cannot overwrite an earlier failure. It bounds its completion cache to 256 results; this is client policy. Connection closure fails pending waits. There is no implemented event replay/resume mechanism across reconnection.

After a refresh, update or reload, independently verify the intended catalogue or installed state. In particular, a generic `Succeeded` or `Ended` event is insufficient to authorize a V1 reboot. The specific swap completion below is required.

## Package upload and catalogue import

Uploading a package, making it available in the catalogue, and activating an instance are separate steps. There is an observed delay between upload and catalogue availability; use bounded polling rather than an assumed fixed delay.

The current verified upload sequence is:

1. Inspect the `.pkg` as a ZIP archive without extracting or executing it. DevTools requires one root `.dat` manifest of at most 1 MiB, a matching root `.dll`, a GUID `driverId`, and a parseable `driverVersion`. It also reads `baseModel` and `manufacturer`. These are the client's inspection requirements, not a complete package specification.
2. Retain the same local file while hashing and transferring it. Record its SHA-256 hash.
3. Authenticate SFTP with the separately verified SSH host key. The import directory is `/user/ThirdPartyDrivers/Import`.
4. Upload to a unique `.upload-<random-id>.tmp` filename inside that directory. Verify the uploaded size equals the retained local file length.
5. Rename the complete file to its final `.pkg` filename. DevTools refuses to overwrite an already staged destination. Do not expose the `.pkg` extension while an upload is incomplete.
6. Submit `beginLocalDriverRefresh` once and correlate its operation event. An explicit failure stops the flow.
7. Poll the catalogue until one entry matches model, manufacturer and numeric version with availability `LocalByUser`. Preserve the processor-returned catalogue ID. Multiple matches are an ambiguity; no match before the deadline is not permission to repeat the upload.

The recorded SHA-256 identifies the local bytes transferred. The current SFTP path verifies remote size, not a remotely computed hash. An interrupted connection can leave the uniquely named temporary upload; inspect it rather than deleting unrelated staged files. Removal of a driver instance does not remove its catalogue package.

## Driver installation and update

### Initial installation

The tested automated path is for a package that can be commissioned successfully through the gateway:

1. Obtain the exact catalogue entry and intended room/location ID.
2. Inspect inventory for an existing model/name/location match and any conflicting name. An ambiguous match stops the flow.
3. Call `prepareDriverForUse`; require `Result` to be the string `"Success"`.
4. Call `commissionDevice` with catalogue ID, chosen instance name and **string-valued** `locationId`.
5. Require an object with `CommissioningResult: "Success"` and a positive integer `Id`.
6. Poll that device until the expected numeric version is reported and loading status is `Loaded`.

Other commissioning outcomes, missing IDs or dropped responses require inspection before another attempt. DevTools does not automatically answer arbitrary commissioning forms. Initial Entity V2 test-host installation, removal and subsequent reinstallation have hardware evidence. The optional V1 reboot-after-install path has simulated coverage only; a swap reboot flag does not establish initial-install behavior.

### Configure a newly installed Entity V2 driver

Installation and configuration are separate. A newly installed driver can report `Loaded` and `isConfigured: false` while its normal `configurationItems` list is empty. Its initial wizard may still be required.

The following command shapes were inspected in Configure Pro, and a representative two-step driver wizard was exercised on MC4-R / Home 4.11.322. Call them on the verified new instance under the processor lease used for installation:

| Command | Parameters | Result |
|---|---|---|
| `cp.driverConfiguration:getFirstConfigurationStep` | `{"isReconfiguring":false}` | Step object or null |
| `cp.driverConfiguration:applyConfigurationStep` | `{"stepId":"Connection","configurationItemValues":{"_Host_":"192.0.2.10","CredentialSettingId":"REPLACE_LOCALLY"},"isReconfiguring":false}` | Next step, a step containing validation errors, or null on completion |

The step and item IDs in the example are placeholders; obtain the actual IDs from the driver's advertised wizard. A step exposes `Id`, `ConfigurationErrors` and `Items`. Each item has an `Id` and `Value` metadata including `ReadOnly`. Submitted values are strings, including numeric and Boolean choices. Supply an explicit ordered plan, validate each advertised step and writable item, and stop on validation errors, repeated/unexpected steps or transport uncertainty. Null after the final planned step confirms wizard completion; verify `isConfigured`, online and ready state separately.

Do not assume omitted values accept UI defaults. One tested step rejected an empty values dictionary but accepted its displayed choices supplied explicitly. Validate the actual wizard schema for each driver. Successful configuration does not establish physical-device control.

`cp.driverConfiguration:applyConfiguration` uses `{"configurationItemValues":{"SettingId":"value"},"isoCulture":"en-GB"}` and returns configuration errors or null/empty success. That shape was inspected in Configure Pro; the hardware evidence above uses the step-based wizard. These commands are available through `ExecuteDeviceCommandAsync`; DevTools 1.2.0 provides `DriverConfiguration.ConfigureAsync` and the `configure-driver` command for initial configuration; `driver-configuration` reads current settings without advancing the wizard. See [the configuration guide](DriverConfiguration.md). The installation helper itself does not automatically submit settings. The NUnit workflow in version 1.2.1 supports private initial-configuration files and preserves already configured instances. Keep settings and raw error responses private, since they can contain credentials.

### Existing instance and Entity V2 update

Validate the target's ID, model, name, location and installed version. If it is already at the requested version, confirm Loaded state. If it is older, obtain and review update eligibility, then recheck it immediately before `beginSwapDriverForAllEligibleDevices`.

For an explicitly reboot-free update, wait for the operation outcome and independently verify the new version and Loaded state. `EnsureAsync` refuses automatic downgrade and refuses an eligible scope containing any other instance. Reboot-required updates require the separate path below and explicit caller authorization.

## V1 swap completion and reboot

**A V1 swap command does not automatically reboot the processor.** The validated sequence waits for a matching driver-swap completion event, then requests Home's configuration reboot.

```json
{
  "EventType": "cp.platformDriverController:swapDriverCompleted",
  "OperationId": "11111111-1111-4111-8111-111111111111",
  "DriverId": "example-catalogue-entry",
  "IsRebootRequired": true,
  "DeviceIdsRequiringReconfiguration": []
}
```

Match **both** `OperationId` and `DriverId` to the submitted swap. Require an explicit boolean `IsRebootRequired` and an array of integer `DeviceIdsRequiringReconfiguration`. The current automatic V1 path requires reboot to be true and that array to be empty. Reconfiguration requirements must be resolved outside this automatic path.

`cp.platformDriverController:swapDriverFailed` for the operation, or a matching general failed-operation event, prevents automatic continuation. Missing or malformed completion, an unexpected driver ID, timeout or lost submission response never authorizes an inferred reboot or a repeated swap. Preserve the first failure and enough evidence to inspect the actual state.

After valid completion:

1. Save target/version, swap operation ID, confirmed completion and authorization evidence.
2. On the authenticated connection, discover the processor device advertising `cp.processorOperations:beginReboot`.
3. Submit the configuration reboot once, with `rebootReasonInAFewWords`.
4. Require its nonempty operation ID as request acknowledgement, then wait for the old event connection to close. A lost response leaves an uncertain outcome; do not resubmit.
5. Establish a fresh authenticated connection and verify the intended instance, new version and Loaded state before reporting activation.

The client-side operation bookkeeping used by an interactive application is not an additional server command required in this sequence. The validated DevTools workflow uses the swap and reboot commands listed here.

### Why the SSH reboot is separate

The standalone SSH console command is `REBOOT`. Its observed acknowledgement is `Rebooting system.` followed by `Please wait...`. DevTools waits for a console prompt, requests explicit confirmation and sends it once; acknowledgement establishes acceptance, not readiness.

An immediate SSH reboot after confirmed V1 swap completion returned with the previous driver version during validation. Using Home's configuration reboot operation subsequently retained and loaded the intended new version in a fully unattended run. This establishes which sequence worked; it does not establish the internal persistence mechanism or a safe sleep duration that would make SSH reboot equivalent. Use the configuration reboot for the verified V1 workflow.

## Reload and removal

For targeted reload, inspect `supportsUnloadReloadDriver` and require `swapDriverRequiresReboot` to be explicitly false. `beginReloadDrivers` accepts `deviceId` and `reloadReferenceDeviceOnly: true`. `findUnloadReloadDriversAffectedDevices` exposes related scope for review. Reconfirm loaded state and discover any newly assigned service port afterward.

The tested removal path sends `cp.deviceConfiguration:setLocation` with `locationId: null` to the intended installed instance. DevTools first checks model/version, advertised command, applicable capabilities and affected scope. Automatic removal requires the affected ID array to contain exactly that instance. It then polls inventory until the instance disappears; losing its room assignment or tile alone is insufficient confirmation of disposal.

Removal may dispose code and services. A test orchestrator must first preserve results and confirm test execution has stopped. Those test-aware checks are not supplied by the configuration protocol itself. The optional explicit reboot-after-removal policy has simulated coverage; V1 removal followed by reboot remains hardware-unverified.

## Restart readiness and recovery

A reopened TCP port, responsive SSH console or successful login is insufficient to establish that Home has finished starting. The verified recovery path:

1. Observes the old authenticated event stream ending.
2. Disposes the old session; it does not reuse pre-restart tokens.
3. Optionally resolves the exact system name again. No match may be temporary while booting; multiple matches stop automatic selection.
4. Reauthenticates with the expected TLS trust and performs a device-inventory read.
5. Verifies retained orchestration ownership where applicable, then checks the expected driver identity, exact numeric version and `Loaded` state.
6. Performs application-specific readiness/health checks. Online, ready and configured were checked for the validated V1 driver update; these checks do not establish playback behavior.

During hardware startup, an inventory request returned HTTP 500 after authentication succeeded. The bounded readiness loop now retries read-only startup transport failures and HTTP 500/502/503/504. Authentication and request errors remain failures. These retries do not apply to configuration mutation commands.

The caller selects an overall deadline large enough for import, restart and initialization. Five-second discovery windows, 500 ms lifecycle polls and one-second reconnect polling are current client policies, not readiness guarantees. An expired deadline does not grant permission for another reboot, update or removal.

## Failures and retry rules

| Condition | Interpretation and required handling |
|---|---|
| Authentication rejected or certificate/key mismatch | Stop; do not silently change credentials, target or trust |
| HTTP 422 or other request rejection | Diagnose the request/parameters; do not retry unchanged automatically |
| HTTP success with non-null `Error` | Processor rejection; preserve a sanitized failure, not a success result |
| Missing response envelope, operation ID or commissioning ID | Outcome is not confirmed; inspect state before further mutations |
| `Failed` event | Preserve failure; a later `Ended` does not make it pass |
| `Ended` without explicit success | Independently verify the intended state; insufficient for V1 reboot authorization |
| Connection lost after a POST/upload rename/reboot attempt | The operation may have taken effect; never replay solely because the response was lost |
| Wrong catalogue/installed version or expanded eligible scope | Stop and review a new plan |
| Home startup transport error or HTTP 500/502/503/504 on readiness reads | Bounded read-only retry is allowed in the restart-readiness loop |
| Cancellation or timeout | Stops client waiting; does not undo a processor operation |

Even a read-like capability such as `getDrivers` uses HTTP POST. Classify retry behavior by the command's meaning, not only its HTTP method. The low-level DevTools connection does not automatically replay commands. Higher-level readiness loops repeat only the explicitly selected reads.

## Automation boundaries and private data

Crestron Home NUnit adds source hashes, retained packages, test gates, test-input transfer, private evidence and a cooperating-client processor lease. Its `/user/CrestronHomeNUnit-WorkflowLease` directory is an application convention, **not a Crestron protocol feature**. DevTools calls alone do not acquire that lease or establish that tests are stopped. See the [continuous integration guide](https://github.com/oznetmaster/CrestronHomeNUnit/blob/HEAD/docs/ContinuousIntegration.md).

Keep passwords, session headers, saved profiles, actual deployment paths, real device bindings and raw processor logs out of published examples. Session tokens and raw `PropertyValues` can be as sensitive as credentials. Use fictitious data when reporting a protocol issue. Preserve only the specific identity, version, status and operation fields required for public explanations.

Stored processor logs can lag behind current activity; separate SSH live logging was used during development. Log capture is not a protocol readiness signal, and DevTools does not currently expose a logging service API. An operator/CI runner should retain uncertain operation evidence and reconcile the actual state before another mutation. There is no implemented automatic rollback or general transaction mechanism.

## Implementation map and remaining unknowns

The source is the executable companion to this reference:

| Area | Implementation |
|---|---|
| Native UDP packet and name parsing | [NativeDiscovery.cs](../CrestronHomeDevTools/NativeDiscovery.cs) |
| Home identification and name selection | [ProcessorDiscovery.cs](../CrestronHomeDevTools/ProcessorDiscovery.cs) |
| TLS, login, headers, envelopes and event reception | [ProcessorConnection.cs](../CrestronHomeDevTools/ProcessorConnection.cs) |
| Inventory, gateway commands, reload, removal and configuration reboot | [ConfigurationClient.cs](../CrestronHomeDevTools/ConfigurationClient.cs) |
| Partial response models | [Models.cs](../CrestronHomeDevTools/Models.cs) |
| Generic status and swap completion | [OperationTracker.cs](../CrestronHomeDevTools/OperationTracker.cs), [DriverSwapTracker.cs](../CrestronHomeDevTools/DriverSwapTracker.cs) |
| Package inspection and SFTP import | [DriverDeployment.cs](../CrestronHomeDevTools/DriverDeployment.cs) |
| Install/update sequence and identity checks | [DriverInstanceLifecycle.cs](../CrestronHomeDevTools/DriverInstanceLifecycle.cs) |
| Reboot handler and restart readiness | [DriverRebootHandler.cs](../CrestronHomeDevTools/DriverRebootHandler.cs) |
| Separate SSH console reboot | [ProcessorReboot.cs](../CrestronHomeDevTools/ProcessorReboot.cs) |

The complete anonymous configuration schema, complete device/command schemas, discovery header semantics, exact session expiry policy, permission/role matrix, server concurrency rules and cross-session event recovery are not specified by this reference. V1 initial installation/removal requiring reboot, arbitrary commissioning/reconfiguration, backup/restore, broader firmware/model support and rollback are not claimed as validated features.

When extending this reference, record the tested model/firmware, command spelling and parameter types, response/event fields, correlation requirements and independent final-state checks. Clearly label new observations and unverified assumptions. A passing simulated test establishes client behavior; it does not by itself establish processor behavior.

Copyright (c) 2026 Neil Colvin. This original reference is distributed under the project's [MIT license](../LICENSE). Crestron and Crestron Home retain their respective trademarks. This project is independent and is not affiliated with, endorsed by or sponsored by Crestron Electronics, Inc.; see the [project disclaimer](../README.md#license-and-crestron-disclaimer).

### Long catalogue searches

Home rejects more than three search tokens with HTTP 422; Configure Pro prevents these requests in its search UI. DevTools supports longer model names by querying batches of at most three tokens and intersecting catalogue IDs. It preserves all requested terms and propagates a failed or missing response rather than returning a partial match. This is a sequence of read-only searches, not a retry of a mutation.
## Room-move validation

On MC4-R / Home 4.11.322, a temporary Entity V2 test instance moved to another room and back without changing its ID or version, including verification on a fresh connection. `setLocation` requires a numeric room ID, unlike the string used by `commissionDevice`. A string supplied to `setLocation` was treated as removal; never substitute that representation. The typed helper restricts moves to loaded childless drivers with confirmed reboot-free lifecycle support. See [room moves](RoomMoves.md).


## Managed-child configuration entry

On the observed V2 processor platform, `cp.platformController:commissionManagedDevice` returns a new child ID and commissioning result. Configure Pro subsequently enters the child's configuration wizard through `cp.driverConfiguration:getFirstConfigurationStep` with `isReconfiguring: false`. This can return null because no prompts are required; the call is still part of the observed commissioning sequence. Skipping it left a child offline in controlled tests, while including it initialized the same diagnostic promptly. Polling `isConfigured` alone did not substitute for this step.

The DevTools 1.6.0 helper and its scope are documented under [new managed children](DriverConfiguration.md#initialize-a-newly-commissioned-managed-child). This is an observation of the undocumented configuration interface, not an official Crestron contract. Returned wizard data remains private, mutation requests are never automatically replayed, and callers must separately verify readiness and cleanup.


## Remove a journal-owned managed child

For a new leaf child of a reloadable Entity V2 platform, the observed cleanup command is `cp.deviceConfiguration:setLocation` on the **child ID**, with `locationId: null`. This disposes the child assignment; it is distinct from replacing the platform package or unloading the root driver. A managed child can report `supportsUnloadReloadDriver: false` while its parent supports unload/reload: that child flag does not describe this assignment-removal operation.

The DevTools 1.6.0 `ManagedDeviceCommissioning.RemoveCreatedAsync` and `remove-created-child` CLI require matching terminal commissioning receipts, the unchanged child and parent identities, no child descendants and the parent's non-reboot capability. They journal one removal intent, observe disappearance and verify other devices' identities, assignments, versions and loading/readiness state. They do not restore physical equipment or certify unrelated devices' functionality. The caller must verify test-state restoration before cleanup and retain the same processor identity and shared lease discipline throughout its workflow.

The library and actual CLI round trip passed on the CP4-R development processor. A previous cleanup attempt is never replayed, even after an uncertain response; its private receipt must be reconciled. These observations describe the undocumented interface, not an official vendor guarantee or completed submission validation.
