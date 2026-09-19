# CrestronHomeDevTools

**Processor compatibility:** Configuration-management commands require a **V2 Crestron Home processor**. V1 Crestron Home processors do not support these commands. This refers to the processor platform, not the driver type: supported V2 processors can host V1 drivers, whose update workflow requires an explicitly authorized reboot.

An independent .NET 10 library, interactive console and automation CLI for Crestron Home configuration management. Discover processors, inspect installed devices, deploy driver packages, install or update Entity V2 driver instances, and verify their loaded versions without operating Configure Pro.

The configuration-management interface is unofficial and firmware-dependent, [documented here from verified behavior](docs/ProtocolReference.md). This is not an official Crestron API SDK. The library has no dependency on Crestron SDK assemblies, proprietary client binaries or NUnit.

This source includes initial driver configuration and read-only inspection of current settings. See [driver configuration](docs/DriverConfiguration.md) for the commands, private input format, masking rules and library API. Shared processor reservations continue to coordinate development operations.

Reviewed V1 removal can preserve other instances sharing the same driver code across an explicitly authorized Home reboot. See [V1 installation and removal](docs/V1DriverRemoval.md) for the opt-in scope, preservation checks and validation limits.

## Contents

- [What is included](#what-is-included)
- [Get started](#get-started)
- [Library example](#library-example)
- [Driver configuration](docs/DriverConfiguration.md)
- [Managed-child validation lifecycle](docs/ManagedChildValidation.md)
- [Moving drivers between rooms](docs/RoomMoves.md)
- [Associating Android tiles with installed drivers](docs/DriverUiBinding.md)
- [Deployment and tests](#deployment-and-tests)
- [Submission preparation console](docs/submission/ConsoleTools.md) (no Python setup)
- [Reviewed evidence mapping](docs/submission/EvidenceMapping.md) (C# API and CLI, requires 1.11.0)
- [Passive endurance health assessment](docs/submission/WindowsEnduranceWorker.md#passive-health-assessment-source-development) (source development; no notification delivery)
- [Automated Crestron submission plan and progress](docs/CrestronSubmission.md)
- [Development configurations](docs/submission/DevelopmentConfigurations.md) (one PC and one processor, optional additional hardware)
- [Submission workflow setup](docs/submission/WorkflowSetup.md) (dispatcher and protected stages for your own repository)
- [Submission delivery journal and reconciliation](docs/submission/DeliveryJournal.md) (authorized upload, SMTP delivery and uncertain-outcome handling)
- [Final submission delivery preparation](docs/submission/DeliveryPreparation.md) (approved signed artifacts to a private plan; no sending)
- [Bundled delivery settings](docs/submission/DeliverySetup.md) (requires 1.10.0; generate protected settings without runtime paths)
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

Supported operations include discovery by processor name, credentials and certificate validation, driver/device inventory, SFTP upload, catalogue import, reviewed updates, guarded install/update/reuse, targeted reload, guarded room moves for loaded childless drivers, and explicit instance removal. See [room moves](docs/RoomMoves.md) for scope and validation. Reboots require explicit authorization; submitted configuration changes are never replayed after a connection failure.

The `reboot` command supports interactive confirmation or explicit unattended authorization:

```text
reboot --processor DEVELOPMENT --confirm-reboot DEVELOPMENT
```

The two target values must match; credentials and verified SSH trust still come from the selected profile/settings. Applications can supply their own confirmation dialog through the library API. The NUnit workflow supports reboot-required updates with `allowProcessorReboot: true`; see [V1 development and reboot policy](https://github.com/oznetmaster/CrestronHomeNUnit/blob/HEAD/docs/ContinuousIntegration.md#v1-development-and-reboot-policy).

## Get started

Version 1.8.0 adds an optional [authorized delivery command](docs/submission/DeliveryCommand.md), upload and SMTP providers, and [Windows endurance scheduling](docs/submission/WindowsEnduranceWorker.md). It also stops promptly when the requested driver version fails to load. These components do not establish a completed submission or endurance period. Version 1.7.0 added bounded [processor uptime observations](docs/ProcessorUptime.md) and a public plan-validation entry point for external endurance producers. These APIs support monitoring independently of optional submission. Version 1.6.0 added [managed-child setup, validation and cleanup](docs/ManagedChildValidation.md), resumable endurance collection and further offline submission preparation. It retains the package/evidence checks, guarded UI name binding and delivery journals introduced in 1.5.0. The console includes [private evidence bundle creation and verification](docs/submission/EvidenceBundle.md).

Version 1.9.0 provides [submission preparation commands](docs/submission/ConsoleTools.md) in the complete console download, including [help generation](docs/submission/HelpBuild.md), [Android evidence auditing](docs/submission/AndroidEvidence.md), [self-test forms](docs/submission/FormGeneration.md) and [private review](docs/submission/ReviewStage.md). The console includes its isolated document runtime and validator; developers use documented commands, configuration and C# fixtures without installing or maintaining Python. DOCX-to-PDF rendering still needs the documented renderer and fonts. Preparation does not send anything or establish self-test completion.

The same console includes [authorized image signing](docs/submission/FormSigning.md), the [private signing stage](docs/submission/SigningStage.md) and [final delivery preparation](docs/submission/DeliveryPreparation.md). Complete candidate evidence, reviewed forms and exact signing/delivery authorization remain prerequisites. The optional [submission roadmap](docs/CrestronSubmission.md) distinguishes implemented tools from remaining end-to-end validation. The NuGet library does not include the document runtime.

Version 1.9.0 also adds [read-only candidate payload comparison](docs/DriverPayloadInspection.md) under the shared processor reservation and bounded recovery from transient delivery-journal file replacement refusals. It never retries a provider request because a file operation failed.

Version 1.11.0 adds [reviewed evidence mapping](docs/submission/EvidenceMapping.md) and [completed endurance snapshots](docs/submission/WindowsEnduranceWorker.md). These preserve original evidence and explicitly report uncovered requirements; they do not turn a partial test run into submission approval. Existing running collectors can remain pinned to their original version.

Install the library with `dotnet add package CrestronHomeDevTools --version 1.11.0`. Download the self-contained Windows x64 console from [GitHub Releases](https://github.com/oznetmaster/CrestronHomeDevTools/releases/latest), extract the complete ZIP, and run `CrestronHomeDevTools.Console.exe`. Its first run opens processor/profile setup; `--help` lists commands.

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

See the [end-to-end CI guide](https://github.com/oznetmaster/CrestronHomeNUnit/blob/HEAD/docs/ContinuousIntegration.md). The released NUnit workflow CLI consumes the published DevTools NuGet package; a source checkout is optional for development. The workflow Test Explorer adapter is also published as `CrestronHomeNUnit.TestAdapter`, with VSTest and processor workflow validation; see [adapter setup and validation limits](https://github.com/oznetmaster/CrestronHomeNUnit/blob/HEAD/docs/VisualStudioTestExplorer.md).

## Documentation

- [User and CLI guide](docs/UserGuide.md): setup, profiles, commands, exit codes and troubleshooting.
- [Library API guide](docs/LibraryGuide.md): component responsibilities and examples.
- [Processor coordination and storage](docs/ProcessorCoordination.md): shared reservations, build deployment, reboot waits and retained package inspection.
- [Windows endurance worker](docs/submission/WindowsEnduranceWorker.md): optional scheduled observations, private service-account setup, interruption handling and alert requirements; scheduler scripts are included in the 1.8.0 console ZIP under `scripts/endurance`.
- [Remote processor logging](docs/RemoteSystemLogging.md): verified console queries, TCP/UDP/TLS choices and collector validation requirements; no automatic collector is included.
- [Processor uptime observations](docs/ProcessorUptime.md): bounded, read-only SSH observations for consumer-owned monitoring; requires 1.7.0 or later.
- [Configuration-management protocol reference](docs/ProtocolReference.md): discovery packets, authentication, request/response formats, commands, events, V1/V2 lifecycle sequences and recovery rules.
- [Compatibility and validation](docs/Compatibility.md): tested environment, V1/V2 limits and failure semantics.
- [Release procedure](docs/Releasing.md): versioning, packages, console distribution, documentation and validation.
- [Changelog](CHANGELOG.md) and [release notes](RELEASE-NOTES.md) for packaged releases; [development history](DEVELOPMENT-HISTORY.md) for the development evidence and limitations behind those releases.
- [Third-party notices](THIRD-PARTY-NOTICES.md) and [MIT license](LICENSE).

## Build and validate

Open `CrestronHomeDevTools.slnx` in Visual Studio with .NET 10 support, or run:

```powershell
dotnet test CrestronHomeDevTools.slnx -c Release
dotnet pack CrestronHomeDevTools/CrestronHomeDevTools.csproj -c Release -o artifacts
dotnet publish CrestronHomeDevTools.Console/CrestronHomeDevTools.Console.csproj -c Release -o artifacts/console
```

The offline suite needs no processor or credentials. CI compares executed tests with discovery, so new cases do not require maintaining a fixed test-count gate. Push/PR CI builds, runs the offline tests and packs the library. The separate release workflow validates the versioned library and self-contained Windows console, retains their checksums, then publishes NuGet and GitHub assets.

## Privacy and compatibility

Connection profiles are stored outside the checkout under `%LOCALAPPDATA%/CrestronHomeDevTools/Profiles`, protected with Windows DPAPI for the current user. Other platforms can supply environment variables or a private settings file; encrypted profile persistence is Windows-only. No password command-line option is provided.

Real connection files, device bindings, update plans and logs stay outside source control or are locally excluded using `.git/info/exclude`. Do not commit those exclusions or private paths. Public examples use placeholders. A processor supplies its own Crestron runtime; this library does not need or redistribute `Newtonsoft.Json.Compact.dll`.

Development management operations were verified on MC4-R and CP4-R / Crestron Home 4.11.322. Both passed Entity V2 lifecycle and configuration reboot/recovery checks. The complete unattended V1 driver update/reboot/verification workflow was verified on MC4-R only. Discovery alone does not establish management compatibility with another processor. Read the [validation limits](docs/Compatibility.md). Cancellation stops waiting; it cannot undo a submitted processor operation.

## License and Crestron disclaimer

Copyright (c) 2026 Neil Colvin. Original project code is licensed under the [MIT License](LICENSE). Dependencies retain their own licenses; see [third-party notices](THIRD-PARTY-NOTICES.md).

Crestron and Crestron Home are trademarks or registered trademarks of Crestron Electronics, Inc. This project is not affiliated with, endorsed by or sponsored by Crestron Electronics, Inc. It uses an independently implemented, unofficial configuration interface. The MIT license does not grant rights to Crestron's software or documentation. Software is provided as-is, without warranty, as specified in LICENSE.
