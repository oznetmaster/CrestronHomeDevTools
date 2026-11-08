# Import receipts and read-only state inspection

These additive APIs use the existing authenticated connection. They do not
require a new Configure Pro app or change connection ports. Home 4.12 may omit
newer data. Missing data stays unavailable, rather than becoming empty success.

## Import receipts

The existing refresh command and JSON result remain unchanged by default:

~~~powershell
CrestronHomeDevTools.Console.exe refresh --receipt true --processor 192.168.8.244 --credentials C:\Private\processor-bindings.json
~~~

This is still a **mutating import command**, protected by the console's existing
processor reservation. It imports packages already staged in the processor's
import directory. Inspect that directory and coordinate other tasks before use.
It does not update installed driver instances.

The opt-in result contains the operation result and the correlated dedicated
completion event, if observed. Library callers can observe a refresh they have
already started without issuing it again:

~~~csharp
string operationId = await client.BeginLocalDriverRefreshAsync(token);
DriverRefreshReceipt receipt = await client.WaitForDriverRefreshReceiptAsync(
    operationId, TimeSpan.FromSeconds(60), cancellationToken: token);
~~~

- Ended is not proof of success. Failed remains a failure.
- Completion null means no dedicated completion was observed within the grace
  period (default two seconds, configurable from zero to ten).
- Completion.NewDriverIds null means the list was not supplied. Home 4.12's
  completion contract has no such field.
- An empty NewDriverIds list differs from a missing list. Preserve the IDs as
  reported; do not infer package filenames or installed device identities.
- A list does not prove that an expected package imported. Deployment must still
  verify expected catalogue identity, manufacturer, model, version and local
  availability. DriverDeployment.DeployAsync retains its existing verification.

The overall timeout bounds terminal-operation and completion observation.
Early completions are retained in bounded connection-local history (up to 256
operations). Duplicate identical completions are harmless; conflicting or
malformed correlated completions fail observation. There is no automatic retry.
A custom IConfigurationConnection need not implement the optional
IDriverRefreshEventSource: its old operation result still works, and
CompletionMonitoringAvailable is false.

## Managed-child snapshots and monitoring

~~~powershell
CrestronHomeDevTools.Console.exe managed-children --device -6 --processor 192.168.8.244 --credentials C:\Private\processor-bindings.json
CrestronHomeDevTools.Console.exe monitor-managed-children --parents -6,52677 --duration 30 --buffer 32 --processor 192.168.8.244 --credentials C:\Private\processor-bindings.json
~~~

Use IDs advertised by the actual processor. Read-only inspection accepts system
IDs, including negative IDs and zero. Existing positive-ID requirements for
installing/removing/configuring driver instances remain unchanged.

GetManagedChildrenAsync reads cp.platformController:managedDevicesExtended,
falling back to platform:managedDevices only when the extended property is
absent. A null advertised list stays unavailable; an empty list is available
and empty. Child IDs are opaque strings, not necessarily installed device IDs.
Missing/null PropertyValues is unavailable; an empty object is available empty
state.

~~~csharp
await using var monitor = await client.MonitorManagedChildrenAsync(
    new[] { parentId }, cancellationToken: token);
// InitialSnapshots is an HTTP baseline, never a synthesized change event.
foreach (var baseline in monitor.InitialSnapshots) { /* inspect baseline */ }
await foreach (var delta in monitor.ReadChangesAsync(token))
{
    // Raw partial delta, not complete child state.
    // OverlapsInitialSnapshot marks events received during baseline collection.
}
~~~

The listener registers locally before reading baselines. Sequence and ObservedUtc
are local observations, not server revisions/timestamps. Without a server
revision, overlapping HTTP snapshots and WebSocket deltas cannot be ordered into
a guaranteed latest-state cache. The API does not attempt that merge.

The console emits an InitialSnapshots JSON-lines record, individual
ManagedChildStateChange records and a MonitoringCompleted summary. CanReceiveEvents
describes the connection's listening capability, not firmware support.
HasObservedStateEvent false means none was observed in that session. Older
firmware can supply snapshots without emitting the newer event.

Values are excluded from high-level snapshots and monitor output by default.
Use --include-values true (or includePropertyValues: true) only for private
diagnostics. No values are automatically logged. Direct low-level
IManagedChildEventSource subscriptions contain raw values and are private data.

Each monitor has a bounded buffer (1–256 events, default 32) and an 8 MiB queued
payload budget. Overflow, relevant malformed events or disconnects fail the
monitor with unconfirmed continuity rather than silently dropping events.
They do not block the WebSocket receiver or fail unrelated operations/parents.
There are at most 16 monitors per connection and 64 distinct parents per monitor.
Only one reader can consume a monitor/subscription at a time. Cancellation
allows a later reader of remaining events; disposal discards its queue.
There is no replay across reconnects.

## Dynamic device settings: inspection only

~~~powershell
CrestronHomeDevTools.Console.exe device-settings --device DEVICE_ID --processor PROCESSOR --credentials C:\Private\processor-bindings.json
~~~

InspectDeviceSettingsAsync reads only the chosen device resource. It projects
deviceSettings:groups and deviceSettings:items into typed metadata with group
parents, property/command identifiers, description, availability, read-only flags
and render hints. It checks whether each command is advertised but never
executes it. There is no setter, configuration replay or automatic migration.
This metadata is separate from existing driver-configuration wizard inspection.

Both native and JSON-encoded metadata arrays are supported. Missing flags remain
nullable. Unknown additive fields and render-hint enum values remain compatible.
A render hint is not a declared type or validation range; those are not invented.

HasValue and ObservedValueKind distinguish missing values from explicit JSON
null. Values are excluded by default; --include-values true or includeValues:
true explicitly includes them. String values resembling JSON remain strings.
Settings and child values may contain credentials or other private information.

## Compatibility and validation

Existing IConfigurationConnection implementations require no changes. Optional
event interfaces add listening where available; snapshots and settings reads work
independently. No version number is used to invent support: discovery depends on
advertised properties and observed events.

Offline tests replay the 4.12 completion layout and 4.13 optional-ID layout,
positive settings/child-state payloads, event ordering, missing/null/empty data,
privacy defaults, cancellation, conflicting receipts, malformed input, buffer
limits and disconnects. Regression/API checks remain separate from live results.

Live findings and limits are recorded in development history. No settings
advertiser or no observed child event is not positive live qualification of those
features. No hardware controls, mock installation or reboot is required for the
read-only checks.
