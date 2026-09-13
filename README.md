# CrestronHomeDevTools

An independent .NET 10 library, interactive console and automation CLI for Crestron Home configuration management. Discover processors, inspect installed devices, deploy driver packages, install or update Entity V2 driver instances, and verify their loaded versions without operating Configure Pro.

**Version 1.0.0.** The configuration-management interface is unofficial and firmware-dependent, [documented here from verified behavior](docs/ProtocolReference.md). This is not an official Crestron API SDK. The library has no dependency on Crestron SDK assemblies, proprietary client binaries or NUnit.

## Contents

- [What is included](#what-is-included)
- [Get started](#get-started)
- [Library example](#library-example)
- [Deployment and tests](#deployment-and-tests)
- [Documentation](#documentation)
- [Build and validate](#build-and-validate)
- [Privacy and compatibility](#privacy-and-compatibility)
- [License and Crestron disclaimer](#license-and-crestron-disclaimer)

## What is included

| Component | Purpose |
|---|---|
| `CrestronHomeDevTools` | Reusable .NET 10 configuration-management library; the NuGet package contains this library. |
| `CrestronHomeDevTools.Console` | Interactive `ch>` prompt and one-command CLI using the same library. Distributed separately from the library. |
| `CrestronHomeDevTools.Tests` | Offline NUnit regression tests for discovery parsing, authentication, command handling, update/removal guards, profiles and deployment validation. |

Supported operations include discovery by processor name, credentials and certificate validation, driver/device inventory, SFTP upload, catalogue import, reviewed updates, guarded install/update/reuse, targeted reload and explicit instance removal. Reboots require explicit authorization; submitted configuration changes are never replayed after a connection failure.

The `reboot` command supports interactive confirmation or explicit unattended authorization:

```text
reboot --processor DEVELOPMENT --confirm-reboot DEVELOPMENT
```

The two target values must match; credentials and verified SSH trust still come from the selected profile/settings. Applications can supply their own confirmation dialog through the library API. The NUnit workflow supports reboot-required updates with `allowProcessorReboot: true`; see [V1 development and reboot policy](https://github.com/oznetmaster/CrestronHomeNUnit/blob/HEAD/docs/ContinuousIntegration.md#v1-development-and-reboot-policy).

## Get started

Install the library with `dotnet add package CrestronHomeDevTools --version 1.0.0`. Download the self-contained Windows x64 console from [GitHub Releases](https://github.com/oznetmaster/CrestronHomeDevTools/releases/latest), extract the complete ZIP, and run `CrestronHomeDevTools.Console.exe`. Its first run opens processor/profile setup; `--help` lists commands.

To build from source with the .NET 10 SDK:

```powershell
dotnet build CrestronHomeDevTools.slnx -c Release
dotnet run --project CrestronHomeDevTools.Console -- configure
dotnet run --project CrestronHomeDevTools.Console -- devices
```

`configure` discovers local Crestron Home processors, offers a numbered selection, accepts a masked password, and verifies the processor certificate and SSH host key before saving a Windows account-encrypted profile. Each processor can have its own named profile and credentials. Starting the console without arguments opens setup followed by the interactive prompt.

The same commands work in both modes:

```text
discover
drivers --search "Example"
deploy --package "C:/CI/Artifacts/Example.Driver.pkg"
activate --driver CATALOGUE_ID --name "Example Driver" --room ROOM_ID
```

Use actual IDs returned by inventory. Deploying imports a package; activating installs or updates its instance. See the [command guide](docs/UserGuide.md) before automating changes.

## Library example

```csharp
using System.Net;
using CrestronHomeDevTools;

var options = new ProcessorConnectionOptions
{
    Host = processorHost,
    CertificateSha256 = verifiedCertificateFingerprint
};
await using var client = await ConfigurationClient.ConnectAsync(
    options, new NetworkCredential(userName, password), cancellationToken);

var drivers = await client.GetDriversAsync("Example", cancellationToken);
var devices = await client.GetDevicesAsync(cancellationToken);
```

Credentials and trusted fingerprints come from the calling application. Device properties may contain private configuration; do not publish whole inventory responses. The [API guide](docs/LibraryGuide.md) covers upload, activation, update plans, removal and operation outcomes.

## Deployment and tests

DevTools handles configuration management. Crestron Home NUnit handles test discovery, inputs and execution. Its workflow backend combines them to run local tests, build and retain exact packages, deploy and activate a test host, run processor/live tests, optionally update the actual driver after the gates pass, inspect that installed driver, and optionally remove the test instance.

See the [end-to-end CI guide](https://github.com/oznetmaster/CrestronHomeNUnit/blob/HEAD/docs/ContinuousIntegration.md). The NUnit workflow CLI consumes the published DevTools NuGet package; a source checkout is optional for development. A Visual Studio Test Explorer adapter is not implemented yet; local tests continue using NUnit's existing adapter, and automation can invoke the CLI today.

## Documentation

- [User and CLI guide](docs/UserGuide.md): setup, profiles, commands, exit codes and troubleshooting.
- [Library API guide](docs/LibraryGuide.md): component responsibilities and examples.
- [Configuration-management protocol reference](docs/ProtocolReference.md): discovery packets, authentication, request/response formats, commands, events, V1/V2 lifecycle sequences and recovery rules.
- [Compatibility and validation](docs/Compatibility.md): tested environment, V1/V2 limits and failure semantics.
- [Release procedure](docs/Releasing.md): versioning, packages, console distribution, documentation and validation.
- [Changelog](CHANGELOG.md) and [release notes](RELEASE-NOTES.md).
- [Third-party notices](THIRD-PARTY-NOTICES.md) and [MIT license](LICENSE).

## Build and validate

Open `CrestronHomeDevTools.slnx` in Visual Studio with .NET 10 support, or run:

```powershell
dotnet test CrestronHomeDevTools.slnx -c Release
dotnet pack CrestronHomeDevTools/CrestronHomeDevTools.csproj -c Release -o artifacts
dotnet publish CrestronHomeDevTools.Console/CrestronHomeDevTools.Console.csproj -c Release -o artifacts/console
```

The offline suite currently contains **157 tests** and needs no processor or credentials. Push/PR CI builds, runs the offline tests and packs the library. The separate release workflow validates the versioned library and self-contained Windows console, retains their checksums, then publishes NuGet and GitHub assets.

## Privacy and compatibility

Connection profiles are stored outside the checkout under `%LOCALAPPDATA%/CrestronHomeDevTools/Profiles`, protected with Windows DPAPI for the current user. Other platforms can supply environment variables or a private settings file; encrypted profile persistence is Windows-only. No password command-line option is provided.

Real connection files, device bindings, update plans and logs stay outside source control or are locally excluded using `.git/info/exclude`. Do not commit those exclusions or private paths. Public examples use placeholders. A processor supplies its own Crestron runtime; this library does not need or redistribute `Newtonsoft.Json.Compact.dll`.

Development management operations were verified on MC4-R and CP4-R / Crestron Home 4.11.322. Both passed Entity V2 lifecycle and configuration reboot/recovery checks. The complete unattended Apple TV V1 update/reboot/verification workflow was verified on MC4-R only. Discovery alone does not establish management compatibility with another processor. Read the [validation limits](docs/Compatibility.md). Cancellation stops waiting; it cannot undo a submitted processor operation.

## License and Crestron disclaimer

Copyright (c) 2026 Neil Colvin. Original project code is licensed under the [MIT License](LICENSE). Dependencies retain their own licenses; see [third-party notices](THIRD-PARTY-NOTICES.md).

Crestron and Crestron Home are trademarks or registered trademarks of Crestron Electronics, Inc. This project is not affiliated with, endorsed by or sponsored by Crestron Electronics, Inc. It uses an independently implemented, unofficial configuration interface. The MIT license does not grant rights to Crestron's software or documentation. Software is provided as-is, without warranty, as specified in LICENSE.
