#requires -Version 7.6
#requires -PSEdition Core
# Copyright (c) 2026 Neil Colvin. Licensed under the MIT License; see LICENSE.
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $OutputDirectory,
    [ValidatePattern('^\d+\.\d+\.\d+$')][string] $Version,
    [string] $NUnitWorkflowProject
)
$ErrorActionPreference = 'Stop'
function Assert-OrdinaryRuntimeAncestors ([string] $Path) {
    $current = [IO.Path]::GetFullPath($Path)
    while ($null -ne $current) {
        try {
            $attributes = [IO.File]::GetAttributes($current)
            if (($attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw 'Runtime output path cannot traverse a link or reparse point.'
            }
        } catch [IO.FileNotFoundException] {
            # A fresh output path may not exist; still inspect every existing ancestor.
        } catch [IO.DirectoryNotFoundException] {
        }
        $current = [IO.Path]::GetDirectoryName($current)
    }
}

function Assert-SubmissionNativeOutputPaths ([string] $Output, $Manifest) {
    Assert-OrdinaryRuntimeAncestors $Output
    foreach ($entry in $Manifest.files) {
        if ([IO.Path]::GetExtension($entry.path) -notin @('.pyd','.dll','.exe')) { continue }
        if ([IO.Path]::IsPathRooted($entry.path) -or $entry.path.Contains('\') -or $entry.path.Contains(':') -or
            @($entry.path.Split('/') | Where-Object { $_ -in @('', '.', '..') }).Count -gt 0) {
            throw 'Invalid trusted native runtime manifest path.'
        }
        $relative = Join-Path 'submission-tools' $entry.path
        $nativePath = [IO.Path]::GetFullPath((Join-Path $Output $relative))
        if ($nativePath.Length -gt 259) {
            throw "Native output exceeds the conservative 259-character budget: $relative. Select a shorter OutputDirectory; no console was published."
        }
    }
}

$root = Split-Path $PSScriptRoot -Parent
$output = [IO.Path]::GetFullPath($OutputDirectory)
Assert-OrdinaryRuntimeAncestors $output
if (Test-Path -LiteralPath $output) { throw 'Use a fresh console output directory.' }
$bundle = Join-Path $root ('artifacts/submission-runtime-' + [Guid]::NewGuid().ToString('N'))
& (Join-Path $PSScriptRoot 'BuildSubmissionRuntime.ps1') -OutputDirectory $bundle

$bundleManifest = Get-Content -LiteralPath (Join-Path $bundle 'manifest.json') -Raw | ConvertFrom-Json
Assert-SubmissionNativeOutputPaths $output $bundleManifest
$versionProperties = @()
if ($Version) { $versionProperties += "-p:Version=$Version" }
# Explicit source-reference build: do not silently package an older published workflow.
if ($NUnitWorkflowProject) {
    $workflowProject = [IO.Path]::GetFullPath($NUnitWorkflowProject)
    if (-not (Test-Path -LiteralPath $workflowProject -PathType Leaf) -or [IO.Path]::GetFileName($workflowProject) -ne 'CrestronHomeNUnit.Workflow.csproj') { throw 'Supply the exact maintained NUnit workflow project.' }
    $devToolsProject = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../CrestronHomeDevTools/CrestronHomeDevTools.csproj'))
    $versionProperties += "-p:NUnitWorkflowProject=$workflowProject", "-p:DevToolsProject=$devToolsProject"
}
dotnet publish (Join-Path $root 'CrestronHomeDevTools.Console/CrestronHomeDevTools.Console.csproj') -c Release -r win-x64 --self-contained true -o $output -p:RuntimeFrameworkVersion=10.0.12 -p:DebugType=None -p:DebugSymbols=false "-p:SubmissionBundleDirectory=$bundle" @versionProperties
if ($LASTEXITCODE -ne 0) { throw 'Console publish failed.' }
foreach ($script in @('InstallSubmissionOperatorListener.ps1','InstallSubmissionOperatorInbox.ps1','WatchSubmissionOperatorInbox.ps1')) {
    if (-not (Test-Path -LiteralPath (Join-Path $output "scripts/automation/$script") -PathType Leaf)) { throw "Missing operator deployment script: $script" }
}
# Ship the continuation worker with its own runtime/dependencies so installing the
# complete archive does not require an SDK or a separate source build. Keep its
# NUnit dependencies separate from the protected document console's dependencies.
$automation = Join-Path $output 'automation'
dotnet publish (Join-Path $root 'CrestronHomeDevTools.Automation/CrestronHomeDevTools.Automation.csproj') -c Release -r win-x64 --self-contained true -o $automation -p:RuntimeFrameworkVersion=10.0.12 -p:DebugType=None -p:DebugSymbols=false @versionProperties
if ($LASTEXITCODE -ne 0) { throw 'Automation worker publish failed.' }
& (Join-Path $automation 'CrestronHomeDevTools.Automation.exe') --help
if ($LASTEXITCODE -ne 0) { throw 'Automation worker smoke test failed.' }
$setupArguments = @{ OutputDirectory = (Join-Path $output 'setup') }
if ($Version) { $setupArguments.Version = $Version }
if ($NUnitWorkflowProject) { $setupArguments.NUnitWorkflowProject = $workflowProject }
& (Join-Path $PSScriptRoot 'BuildSubmissionSetup.ps1') @setupArguments
$assets = Get-Content (Join-Path $root 'CrestronHomeDevTools.Console/obj/project.assets.json') -Raw | ConvertFrom-Json -AsHashtable
$runtimePack = @($assets.packageFolders.Keys | ForEach-Object { Join-Path $_ 'microsoft.netcore.app.runtime.win-x64/10.0.12' } | Where-Object { Test-Path $_ }) | Select-Object -First 1
if (-not $runtimePack) { throw 'Bundled runtime license source not found.' }
$runtimeNotices = Join-Path $output 'licenses/bundled-runtime'
New-Item -ItemType Directory -Path $runtimeNotices -Force | Out-Null
foreach ($file in @('LICENSE.TXT','THIRD-PARTY-NOTICES.TXT')) { Copy-Item -LiteralPath (Join-Path $runtimePack $file) -Destination $runtimeNotices }
$diagnostics = Join-Path $output 'build-diagnostics'
New-Item -ItemType Directory -Path $diagnostics | Out-Null
$smokeStarted = [DateTimeOffset]::UtcNow
& (Join-Path $output 'CrestronHomeDevTools.Console.exe') submission runtime-check --private-diagnostics-directory $diagnostics
$smokeExit = $LASTEXITCODE
$smokeReceipt = [ordered]@{
    SchemaVersion = 1
    Command = 'submission runtime-check'
    StartedUtc = $smokeStarted.ToString('O')
    FinishedUtc = [DateTimeOffset]::UtcNow.ToString('O')
    ActualExit = $smokeExit
    ChildPidCaptured = $false
    Meaning = 'Immediate command exit, not a restoration or test-compliance receipt.'
}
[IO.File]::WriteAllText((Join-Path $diagnostics 'runtime-check.result.json'), ($smokeReceipt | ConvertTo-Json), [Text.UTF8Encoding]::new($false))
if ($smokeExit -ne 0) { throw 'Bundled submission runtime smoke test failed; inspect the retained private build diagnostics.' }
# Publication copied these generated files into the verified output. Retain only
# failed staging attempts for investigation; never accumulate successful copies.
$stagingRoot = [IO.Path]::GetFullPath((Join-Path $root 'artifacts'))
$ownedBundle = [IO.Path]::GetFullPath($bundle)
if ((Split-Path $ownedBundle -Parent) -ne $stagingRoot -or (Split-Path $ownedBundle -Leaf) -notmatch '^submission-runtime-[a-f0-9]{32}$' -or (Get-Item -LiteralPath $ownedBundle).Attributes.HasFlag([IO.FileAttributes]::ReparsePoint)) { throw 'Refusing cleanup outside the owned runtime staging directory.' }
Remove-Item -LiteralPath $ownedBundle -Recurse -Force
