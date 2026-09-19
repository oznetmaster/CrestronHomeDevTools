# Submission preparation through the console

**Availability:** the complete Windows console archive includes these commands and their document runtime starting in version 1.9.0. Earlier console downloads do not contain them.

Driver authors use the DevTools console and their own C# test fixtures. There is no requirement to install Python, edit Python scripts, manage a virtual environment or run a package installer. Submission is optional and applies only to drivers; ordinary development and client/library releases do not require it.

## Start here

Extract the complete Windows console archive to a new directory. Keep the `submission-tools` directory beside the executable. Do not copy only the executable or combine files from different releases.

```powershell
.\CrestronHomeDevTools.Console.exe submission runtime-check
.\CrestronHomeDevTools.Console.exe submission --help
.\CrestronHomeDevTools.Console.exe submission prepare-review --help
```

The console includes its runtime, document libraries and matching evidence validator. It verifies the bundled files before starting a command. No installation, PATH change or online dependency download occurs during preparation. A missing, altered or mixed installation is rejected; extract a fresh complete archive to repair it.

The commands take the same reviewed profiles and private settings described in the linked guides. **Omit `dotnet` and `validator` from packaged-command settings.** The console selects its own validator and rejects executable overrides. Hash pins still refer to the original input bytes; settings are not silently rewritten. Existing source-script invocations retain explicit validator paths for maintainer compatibility.

| Task | Console command | Guide |
|---|---|---|
| Help source and PDF | `submission build-help`, `submission render-help` | [Help](HelpBuild.md) |
| Dependency acknowledgements | `submission dependency-notices` | [Notices](DependencyNotices.md) |
| Declared coverage | `submission coverage-plan` | [Coverage](CoveragePlanning.md) |
| Android evidence audit | `submission audit-android` | [Android evidence](AndroidEvidence.md) |
| Draft or evidence-backed form | `submission self-test-form` | [Forms](FormGeneration.md) |
| Unsigned review and evidence bundle | `submission prepare-review` | [Review](ReviewStage.md) |
| Authorized signature and retained review | `submission sign-self-test-form`, `submission prepare-signed-review` | [Signing](SigningStage.md) |
| Approved delivery preparation | `submission prepare-delivery`, `submission revalidate-delivery` | [Delivery preparation](DeliveryPreparation.md) |

The `submission-delivery-settings` command requires 1.10.0 and prepares the protected delivery configuration from the installed bundle. See [Delivery setup](DeliverySetup.md) for inputs and independent approval requirements.

Source additions after 1.12.0, not yet released: [unsigned requests with disclosed omissions](ReviewRequest.md) and their [exact approval and protected delivery commands](ReviewApproval.md). They use a separate request disposition; the existing signed complete-only commands do not silently accept incomplete submissions.

Append `--help` to any command for its arguments. Preparation does not upload, send email or establish Crestron acceptance. Actual delivery remains a separate protected `submission-deliver` operation with its existing authorization and durable journal requirements.

## What remains environment-specific

Supply reviewed Crestron templates, driver help/profile data, passing candidate-bound evidence and private paths. LibreOffice and the required fonts are still needed for DOCX-to-PDF rendering; specify the renderer path as documented. The renderer is not included in this download. Hardware tests still need the configured processor, real devices and Android test environment. Credentials, signature images and raw household evidence must remain private.

A command uses a fresh output location and retains its completion marker only when that stage succeeds. After cancellation or failure, inspect the retained output and follow the stage's recovery instructions; preparation is not automatically retried. Never infer a successful upload or email from a prepared form.

## Building the console from source

Maintainers need PowerShell 7, the .NET 10 SDK and network access for pinned build dependencies:

```powershell
.\tools\BuildSubmissionConsole.ps1 -OutputDirectory C:\Build\NewConsole
.\tools\TestSubmissionConsole.ps1 -ConsoleDirectory C:\Build\NewConsole
```

The build script downloads the hash-pinned internal runtime and libraries into build artifacts, preserves their licenses and embeds the inventory in the console. The test command uses that bundled runtime automatically. It runs synthetic reviews, signing and delivery preparation with an empty executable search path and conflicting interpreter environment settings; it never contacts a processor or sends anything. These are tooling checks, not driver certification evidence.

The ordinary release build runs the same package acceptance checks. Any runtime dependency update requires a reviewed lock-file change and new validation. [Third-party notices](../../THIRD-PARTY-NOTICES.md) identify the bundled components.
