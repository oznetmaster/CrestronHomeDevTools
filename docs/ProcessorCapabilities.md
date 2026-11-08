# Processor capabilities and I/O connections

`ConfigurationClient.GetCapabilitiesAsync()` reads the authenticated processor's
identification and device inventory. It reports the raw Home and firmware
versions, resource schema versions, advertised release level, command IDs,
property IDs and available I/O ports. It excludes configuration property values,
serial settings, credentials and command parameters. It does not run device
commands or fetch arbitrary URLs advertised by a processor.

The console equivalent is:

```powershell
CrestronHomeDevTools.Console.exe processor-capabilities --processor 192.168.8.244 --credentials C:\Private\processor-bindings.json
```

The existing `capabilities` command still reports the console's own automation
features without connecting. The new report describes the processor instead.
Custom `IConfigurationConnection` implementations remain compatible; they may
optionally implement `IProcessorIdentitySource`. Without that interface the
report retains advertised device capabilities but leaves processor identity and
automatic port formatting unknown.

These are observations, not a promise of successful command execution. A missing
property is unconfirmed, not necessarily unsupported. A missing or malformed
`cp.ioPorts` value produces `IoPorts: null`; an advertised empty array produces an
empty list. Unknown future port types remain visible. Matching schema versions
and an Early Access release level do not establish SDK 30 support.

## Preparing a port connection

`IoPortConnections.Parse` reads both `deviceId|portNumber` and
`deviceId|portType|portNumber`. It normalizes the documented `Com` alias to
`Serial`. It does not scan or modify existing configuration strings.

Automatic formatting is based on the processor's **Home** version, not Configure
Pro's installed version or the resource schema version:

| Home version | Prepared value |
| --- | --- |
| 4.7 through 4.12 | `52001|1` |
| 4.13 | `52001|Serial|1` |
| Missing, malformed, older or future version | Refuse automatic preparation |

The compatibility rule follows the reviewed Configure Pro 4.13 conversion path
for older Home versions. Live read-only reports were verified on Home 4.012.0194
and 4.013.0134. On 4.013.0134, an isolated SDK 29 V2 mock accepted a typed serial
selection through its initial configuration wizard. Home presented the value as
`deviceId|Serial|portNumber`, delivered the `Com` alias to the mock and retained
that alias in saved configuration. The displayed value and saved selection
survived reload. Treat `Com` and `Serial` as the same type rather than requiring
byte-for-byte equality across this boundary.

The read-only preparation helper also passed against that completed live setting.
The initial flat item list was empty; the advertised wizard supplied the initial
prompt. This does not add automatic wizard handling to the helper.

The first mock had no transport or physical I/O calls. After reload Home delayed
applying its saved settings pending restoration of the previous port connection,
which that mock never establishes. That test did not qualify runtime reconnection.

A subsequent isolated SDK 29 V2 mock followed the SDK serial-channel sample,
registering the transport callbacks and channel ID without calling Send or a TX
delegate. On a physically disconnected, unassigned processor COM3 with unchanged
9600/8/N/1 RS-232 settings and no handshaking, Home accepted the typed selection,
attached the channel and reported the mock configured, online and ready. After
an isolated reload, a new instance token, restored channel, ready state and
connection to the same processor/Serial/port-3 tuple independently confirmed
reconnection without configuration replay. The operation's Ended notification
alone was not treated as success.

This qualifies that serial configuration/reconnection path on Home 4.013.0134.
Communication with an external serial device, other physical port types and
server assignment on Home 4.12 remain unqualified. Existing raw configuration
APIs retain their behaviour.

Teardown verified removal of both test instances, stored packages, extracted
payloads and catalogue listings while preserving the original configuration,
programming and port settings. The last cached listing remained after full
catalogue refreshes despite clear persistent sources, then disappeared after a
separately approved processor reboot. The V2 reconnection test itself passed
without a reboot; that later reboot was catalogue-cache cleanup.

To prepare one value with live checks, use
`IoPortConnections.PrepareValueAsync(client, target, itemId, selection)`, or:

```powershell
CrestronHomeDevTools.Console.exe port-value --device 100 --model Mock --version 1.0.0.0 --item serialPort --provider 52001 --port-type Serial --port 1 --processor 192.168.8.241 --credentials C:\Private\processor-bindings.json
```

The command returns a JSON string only. It verifies the expected driver identity,
unique writable configuration item, its I/O usage context, and a uniquely
advertised provider/type/number. Generic CEC requires an explicit `CecInput` or
`CecOutput`; a legacy value that would lose an ambiguous direction is rejected.
Unknown types, unavailable metadata and future versions stop before commands.
This first helper covers advertised flat configuration items. It does not start
a wizard to discover additional prompts.

Preparation does not grant permission to apply configuration. For an authorized
configuration operation, hold the usual processor operation lease, prepare
against current metadata immediately before applying, review the resulting
value, and place it only in the intended item in the existing configuration
input. Retain unrelated string values exactly. A separately prepared value can
become stale; the helper does not make preparation and application atomic.
