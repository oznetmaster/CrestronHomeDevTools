#requires -Version 7.6
#requires -PSEdition Core
# Copyright (c) 2026 Neil Colvin. Licensed under the MIT License; see LICENSE.
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $OutputDirectory,
    [ValidatePattern('^\d+\.\d+\.\d+$')][string] $Version
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $output) { throw 'Use a fresh console output directory.' }
$bundle = Join-Path $root ('artifacts/submission-runtime-' + [Guid]::NewGuid().ToString('N'))
& (Join-Path $PSScriptRoot 'BuildSubmissionRuntime.ps1') -OutputDirectory $bundle
$versionProperties = @()
if ($Version) { $versionProperties += "-p:Version=$Version" }
dotnet publish (Join-Path $root 'CrestronHomeDevTools.Console/CrestronHomeDevTools.Console.csproj') -c Release -r win-x64 --self-contained true -o $output -p:RuntimeFrameworkVersion=10.0.12 -p:DebugType=None -p:DebugSymbols=false "-p:SubmissionBundleDirectory=$bundle" @versionProperties
if ($LASTEXITCODE -ne 0) { throw 'Console publish failed.' }
# Ship the continuation worker with its own runtime/dependencies so installing the
# complete archive does not require an SDK or a separate source build. Keep its
# NUnit dependencies separate from the protected document console's dependencies.
$automation = Join-Path $output 'automation'
dotnet publish (Join-Path $root 'CrestronHomeDevTools.Automation/CrestronHomeDevTools.Automation.csproj') -c Release -r win-x64 --self-contained true -o $automation -p:RuntimeFrameworkVersion=10.0.12 -p:DebugType=None -p:DebugSymbols=false @versionProperties
if ($LASTEXITCODE -ne 0) { throw 'Automation worker publish failed.' }
& (Join-Path $automation 'CrestronHomeDevTools.Automation.exe') --help
if ($LASTEXITCODE -ne 0) { throw 'Automation worker smoke test failed.' }
$assets = Get-Content (Join-Path $root 'CrestronHomeDevTools.Console/obj/project.assets.json') -Raw | ConvertFrom-Json -AsHashtable
$runtimePack = @($assets.packageFolders.Keys | ForEach-Object { Join-Path $_ 'microsoft.netcore.app.runtime.win-x64/10.0.12' } | Where-Object { Test-Path $_ }) | Select-Object -First 1
if (-not $runtimePack) { throw 'Bundled runtime license source not found.' }
$runtimeNotices = Join-Path $output 'licenses/bundled-runtime'
New-Item -ItemType Directory -Path $runtimeNotices -Force | Out-Null
foreach ($file in @('LICENSE.TXT','THIRD-PARTY-NOTICES.TXT')) { Copy-Item -LiteralPath (Join-Path $runtimePack $file) -Destination $runtimeNotices }
& (Join-Path $output 'CrestronHomeDevTools.Console.exe') submission runtime-check
if ($LASTEXITCODE -ne 0) { throw 'Bundled submission runtime smoke test failed.' }
# Publication copied these generated files into the verified output. Retain only
# failed staging attempts for investigation; never accumulate successful copies.
$stagingRoot = [IO.Path]::GetFullPath((Join-Path $root 'artifacts'))
$ownedBundle = [IO.Path]::GetFullPath($bundle)
if ((Split-Path $ownedBundle -Parent) -ne $stagingRoot -or (Split-Path $ownedBundle -Leaf) -notmatch '^submission-runtime-[a-f0-9]{32}$' -or (Get-Item -LiteralPath $ownedBundle).Attributes.HasFlag([IO.FileAttributes]::ReparsePoint)) { throw 'Refusing cleanup outside the owned runtime staging directory.' }
Remove-Item -LiteralPath $ownedBundle -Recurse -Force
