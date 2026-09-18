# Copyright (c) 2026 Neil Colvin. Licensed under the MIT License; see LICENSE.
[CmdletBinding()]
param([Parameter(Mandatory)][string] $OutputDirectory)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $output) { throw 'Use a fresh console output directory.' }
$bundle = Join-Path $root ('artifacts/submission-runtime-' + [Guid]::NewGuid().ToString('N'))
& (Join-Path $PSScriptRoot 'BuildSubmissionRuntime.ps1') -OutputDirectory $bundle
dotnet publish (Join-Path $root 'CrestronHomeDevTools.Console/CrestronHomeDevTools.Console.csproj') -c Release -r win-x64 --self-contained true -o $output -p:RuntimeFrameworkVersion=10.0.12 -p:DebugType=None -p:DebugSymbols=false "-p:SubmissionBundleDirectory=$bundle"
if ($LASTEXITCODE -ne 0) { throw 'Console publish failed.' }
& (Join-Path $output 'CrestronHomeDevTools.Console.exe') submission runtime-check
if ($LASTEXITCODE -ne 0) { throw 'Bundled submission runtime smoke test failed.' }
