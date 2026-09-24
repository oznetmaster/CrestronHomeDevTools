# WeatherLink functional endurance producer

This C# sample implements the public `SubmissionEnduranceProcessProbe` contract
for the WeatherLink Live driver. All implementation and offline fixtures are in
this repository. Local equipment details and credentials are configuration, not
source-code edits. This is source-preview tooling; the generalized sample has
not yet completed its hardware rehearsal.

The supported observation profile is a configured local WeatherLink station
with metric display units and an available forecast. A station-free or
forecast-free installation needs a different reviewed functional profile;
do not use this sample unchanged and classify its failures as passes.

## Build and configure

Use .NET 10 on Windows. The observation worker uses named encrypted DevTools
credentials; only its selected processor credential should be provisioned.

```powershell
dotnet run --project samples/WeatherLinkEnduranceProducer -c Release -- --self-test
dotnet publish samples/WeatherLinkEnduranceProducer -c Release -r win-x64 --self-contained true -o C:\Private\WeatherProbe
```

Before computing the producer manifest, write `settings.json` inside that fresh
published directory. It must match `ProducerSettingsInput` in `Program.cs`:

- `stationAddress`, `processorHost`, `deviceId`, `locationId`, `driverName`,
  `driverModel`, `driverVersion`, `catalogueId`: the selected, verified instance.
- `credentialBindings`: absolute path to the worker's named processor bindings.
  Passwords and signature data do not belong in this settings file.
- `packagePath`: absolute path to the exact frozen candidate package.
- `identity`: package SHA-256, source commit, policy SHA-256 and form-template
  SHA-256, matching the enclosing public endurance plan.
- `installationIdentity` and `requirementId`: the same as that plan.
- `baselineFile`: fresh absolute path in an existing protected run-state
  directory **outside** the producer publication directory.

Include every published file, including `settings.json`, in
`SubmissionEnduranceProbeProgram.Files`. Set `SettingsFile` to its absolute path,
compute `GetProducerId`, and bind that ID into the endurance plan. Use the public
worker/scheduler to invoke the producer; it supplies the request on standard
input and retains each result. The settings file must remain inside the pinned
publication directory. Credentials remain in their separately protected store.

The first probe acquires a lifetime baseline automatically, after the collector
has obtained its reservation. It binds that baseline to the full plan and saved
settings. Later probes and PC restarts reuse it. An interrupted acquisition,
changed input, or invalid baseline fails instead of replacing it. Retain the
baseline with the run's evidence; never delete it to repair a running attempt.
The mutable file is outside the immutable program inventory.

The controller's per-release preparation must produce these instance-specific
settings before freezing the producer inventory. This sample supplies the
functional implementation; it does not itself deploy a driver, generate a
complete release profile, or reserve equipment when invoked directly.

## What it establishes

Each sample checks the instance identity, loaded/configured/online/ready states,
metric displays, forecast availability, and an observation update time within
600 seconds of the processor's own clock. It compares the installed payload
against the frozen package, verifies the processor boot-time window with a
two-second tolerance, and checks the SSP parent and managed child PID set.

It also reads the physical station independently. Both sets of weather values
are retained with their read times and checked for finite, valid ranges. They
are **not compared for numerical equality**: the driver's last refresh and the
independent read are not simultaneous. This is functional continuity evidence,
not a weather-accuracy benchmark or proof of UI rendering.

PID sampling cannot exclude PID reuse between observations. The entire SSP
managed child set is checked, so another driver's process changes can interrupt
the run. Use an appropriately reserved processor and record these limitations.
The producer does not certify a submission or turn omissions into checklist
passes. Duration, interval and allowed gaps come from the enclosing policy.

The self-test and normal NUnit suite use synthetic data for PID changes,
boot-time windows, freshness, nonfinite values and deliberately unequal but
valid weather readings. Baseline tests cover restart reuse, changed inputs and
interrupted acquisition. Hardware evidence is recorded separately.

Copyright (c) 2026 Neil Colvin. MIT licensed.
