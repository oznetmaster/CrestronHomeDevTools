# CrestronHomeDevTools 1.2.0

Add reusable initial-driver configuration and read-only inspection of current settings to the .NET 10 library, interactive console and CLI.

- `driver-configuration --device ID` reads the advertised settings, current values and configuration status. Display follows only the driver's `Masked` flag; omitted stored values are not invented or replaced by defaults.
- `configure-driver --device ID --model NAME --version VERSION --input FILE` applies private flat values or ordered wizard steps, validates the exact target and writable fields, and waits for confirmed configuration completion.
- Already configured instances are preserved. Submitted settings are never retried after uncertain failure. The CLI holds the shared processor lease, retains uncertain outcomes and keeps values out of routine configuration diagnostics.
- `DriverConfigurationInspection.GetAsync`, `DriverConfiguration.ReadInputs` and `DriverConfiguration.ConfigureAsync` expose the same behavior to other development tools.

All 191 offline tests pass. MC4-R / Home 4.11.322 validation read installed Wiser settings, preserved the configured instance, and completed the two-step wizard on a separate temporary instance. Cleanup removed the temporary instance after explicit inspection of its shared reload scope and confirmed the original driver was ready with unchanged settings. No heating controls were operated.

Install CrestronHomeDevTools 1.2.0 from NuGet, or extract the complete matching Windows console ZIP from GitHub. No Crestron SDK is required by this library or console.

See [driver configuration](docs/DriverConfiguration.md), [protocol reference](docs/ProtocolReference.md), [processor coordination](docs/ProcessorCoordination.md), [CHANGELOG.md](CHANGELOG.md) and [third-party notices](THIRD-PARTY-NOTICES.md). This does not add arbitrary reconfiguration, rollback or automatic retained-package deletion.

Copyright (c) 2026 Neil Colvin. MIT licensed. Crestron and Crestron Home are trademarks of Crestron Electronics, Inc. This project is independent and is not affiliated with, endorsed by or sponsored by Crestron Electronics, Inc.