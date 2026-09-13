# Release procedure

## Scope and version

DevTools is a new library/tool release, separate from production driver and processor test-package releases. The initial public version is `1.0.0`; select later versions according to the changes being released. Do not infer a public release from a successful local pack.

For driver repositories using these tools, release runtime fixes or changed production dependencies. Source-only test additions need no production driver release. Processor test packages, when separately released, are GitHub assets and never NuGet packages.

## Prepare source and documentation

1. Review the intended public source. Exclude real credentials/profiles, private settings, deployment bindings, personal paths, captured traffic and local logs/results. Check Git's tracked file list; local exclusions do not hide files already tracked.
2. Set the chosen version in `Directory.Build.props`. Assign the changelog heading/date and update `RELEASE-NOTES.md` to describe the final release.
3. Check README links, supported commands, dependency versions, license notices and compatibility evidence. Record known limitations without implying support for untested firmware or an unimplemented VS adapter.
4. Create/review the public repository and CI configuration. Push/PR CI tests and packs. The separate `release.yml` workflow is dispatched from `main` with the version already committed in `Directory.Build.props`. Configure NuGet Trusted Publishing for owner/repository `oznetmaster/CrestronHomeDevTools`, workflow `release.yml`, environment `release`, package glob `CrestronHomeDevTools`, and permission to publish new packages and versions. Set repository variable `NUGET_USER` to the NuGet account name. The workflow builds and validates before requesting its short-lived publishing credential.

## Validate and assemble

```powershell
dotnet test CrestronHomeDevTools.slnx -c Release
dotnet pack CrestronHomeDevTools/CrestronHomeDevTools.csproj -c Release -o artifacts/library
dotnet publish CrestronHomeDevTools.Console/CrestronHomeDevTools.Console.csproj -c Release -r win-x64 --self-contained true -o artifacts/console-win-x64
```

These commands build artifacts; they do not create a GitHub release, push NuGet or modify a processor. Cross-platform publication should follow appropriate platform validation. A framework-dependent console can instead be published without `--self-contained true`; document its .NET 10 requirement.

Include README, changelog, release notes, LICENSE, third-party notices, dependency license files and `docs/` with the console. The library NuGet package includes the corresponding release documentation. Preserve any notices required by an included .NET runtime. Do not ship the test project, private connection files or machine-specific test data.

Inspect the actual `.nupkg` and console archive, not just build directories. Verify metadata/version, entry names, required binaries/documents and absence of private content. Generate SHA-256 checksums for final archives. Record the source commit used to build the assets. Smoke-test console `--help` and a read-only authenticated command on the designated development processor; explicitly select any mutation used for hardware release validation.

## Publish and verify

Commit and push the reviewed source/documentation, then dispatch `release.yml` from `main` with the committed version. The workflow validates the release, publishes the library to NuGet, creates the annotated version tag and publishes the matching GitHub release with the console ZIP, library package and checksums. Do not rebuild or replace assets silently after publication. Verify the visible version, download names, checksums, documentation and package metadata.

A later dependency-only documentation/test update need not create a runtime release unless it changes the distributed product or fixes behavior. The NUnit workflow release has its own source/dependency pins; update those deliberately after DevTools becomes publicly consumable.
