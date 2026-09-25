# Kasa and Tapo functional endurance producer

This C# sample supplies read-only observations to the public endurance worker.
It uses released DevTools 1.19.0 and KasaTapoClient 2.0.0. It never sends a power,
brightness, configuration, discovery-refresh or other driver/device command.
It is sample source, not an additional DevTools package release.

## Build

Use .NET 10 on the Windows build machine:

```powershell
dotnet run --project samples/KasaTapoEnduranceProducer -c Release -- --self-test
dotnet publish samples/KasaTapoEnduranceProducer -c Release -r win-x64 --self-contained true -o C:\Private\KasaProbe
```

The self-test checks property types/ranges and changed managed process identities.
Two preparatory observations on a CP4-R also passed, including reuse of the saved
lifetime baseline in a second producer process. That component check is not a
completed endurance run or validation of the complete release workflow.

## Private configuration

Put `settings.json` inside the published producer directory before computing its
file inventory. The exact contract is `ProducerSettingsInput` in `Program.cs`:

- `credentialBindings`: absolute path to the named encrypted processor bindings.
  Verified HTTPS and SSH trust pins are required.
- `deviceCredentialsFile`: absolute path to an existing restricted Kasa live-test
  settings file. Only `credentials.userName` and `credentials.password` are read;
  device targets in that credential file are not used. Grant access only to the
  selected worker account and administrators. Do not place it inside the public
  repository, producer inventory or evidence archive.
- `packagePath`, `catalogueId`, `processorHost`, `deviceId`, `locationId`,
  `driverName`, `driverModel`, `driverVersion`: exact installed platform identity
  and the frozen package. No first-match device selection is used.
- `identity`, `installationIdentity`, `requirementId`: the same candidate hashes,
  installation and policy requirement as the enclosing public endurance plan.
- `baselineFile`: a new absolute path under the protected run directory, outside
  the immutable producer directory. Never delete or replace it to repair a run.
- `children`: explicitly selected existing managed children. Each entry has a
  distinct `alias`, positive `deviceId`, exact `model`, `name`, `locationId`, and
  nonempty `properties`. A property has `name` and `kind` (`boolean`, `text`, or
  `number`); numbers also require finite `minimum` and `maximum`.
- `outlets`: independent read-only observations for selected child aliases.
  Each entry has `alias`, `discoveryId`, `authenticatedId`, and `childId` (null
  for a root plug; the exact socket ID for a strip). Pin discovery and authenticated
  identities separately: Tapo can report different values. The current network
  address is resolved from discovery, never used as an identity substitute.

For example, an outlet property rule is
`{"name":"outletIsOn","kind":"boolean"}`. A humidity rule is
`{"name":"humidityValuePercent","kind":"number","minimum":0,"maximum":100}`.
Choose ranges and properties from the actual device capabilities. Native lighting
loads use a different parent/identity model and are not supported by this sample's
managed-child validator.

The selected children must remain installed for the full collection. Temporary
Android managed children are removed after their tests and cannot be referenced
by this producer. For an existing installation, the public release workflow can
replace the explicitly selected platform while retaining its configured children;
verify their identity and candidate version before collection. For a fresh
installation, provision and verify the intended lasting children through the public
commissioning APIs before freezing these settings. Do not make the read-only
producer create missing devices or silently select replacements.

## Run through the public worker

Include every published file and `settings.json` in
`SubmissionEnduranceProbeProgram.Files`. Use its absolute `SettingsFile`, compute
`SubmissionEnduranceProcessProbe.GetProducerId`, and bind that ID to the plan.
The public worker owns the processor reservation, scheduling, duration, gap limits
and evidence export. This sample does not reserve hardware when invoked directly.
See [the worker guide](../../docs/submission/AutomationWorker.md).

Release intake can use `EnduranceProbeSettingsTemplate` and
`EnduranceFromDeployment`. Use `"${deployedDeviceId}"` for the root `deviceId`
and `"${deployedCatalogueId}"` for `catalogueId`; those are resolved from verified
deployment receipts. The selected child IDs remain explicit factual inputs.
Use `${run}/kasa-lifetime.json` for the baseline. Keep the source probe's
`SettingsFile` null when using this template route.

The first observation persists a baseline bound to the full plan and settings.
Later invocations reuse it, including after a Windows restart. Changed inputs,
interrupted baseline acquisition, processor reboot or managed PID changes fail;
they never start a new baseline automatically. Keep failed samples and the
original baseline with the run evidence.

## Scope of the evidence

Each sample verifies platform/selected-child identity, loaded/configured/online/
ready state, selected property types and ranges, and the installed package bytes.
It independently connects to each selected physical outlet and reads its state.
The sample records read times and does not compare asynchronous values for exact
equality. Unchanged sensor values do not prove a new physical measurement. This
is availability and functional continuity evidence, not a polling-accuracy test,
UI test, command test, or certification decision.

The processor boot window is checked with a two-second tolerance. The entire
SSP managed PID set is checked, so unrelated driver process changes can interrupt
the run. PID reuse between samples cannot be ruled out. Original failures record
the observation phase and exception type without forwarding potentially sensitive
third-party exception messages.

Copyright (c) 2026 Neil Colvin. MIT licensed.
