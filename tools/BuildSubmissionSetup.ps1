#requires -Version 7.6
#requires -PSEdition Core
# Build a self-contained Windows setup app; no signing, deployment or publication.
[CmdletBinding()]
param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '..\artifacts\submission-setup-win-x64'),
    [ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version
)
$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot '..\CrestronHomeDevTools.Setup\CrestronHomeDevTools.Setup.csproj'
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $output) { throw 'Use a fresh setup output directory.' }
$versionProperties = @()
if ($Version) { $versionProperties = @("-p:Version=$Version") }
dotnet publish $project -c Release -r win-x64 --self-contained true -p:RuntimeFrameworkVersion=10.0.12 -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false -o $output @versionProperties
if ($LASTEXITCODE -ne 0) { throw 'Setup application publication failed.' }
if (-not (Test-Path -LiteralPath (Join-Path $output 'CrestronHomeDevTools.Setup.exe') -PathType Leaf)) { throw 'Published setup application is missing.' }
$assets = Get-Content (Join-Path (Split-Path $project -Parent) 'obj/project.assets.json') -Raw | ConvertFrom-Json -AsHashtable
$desktopRuntime = @($assets.packageFolders.Keys | ForEach-Object { Join-Path $_ 'microsoft.windowsdesktop.app.runtime.win-x64/10.0.12' } | Where-Object { Test-Path -LiteralPath $_ }) | Select-Object -First 1
if (-not $desktopRuntime) { throw 'Desktop runtime license source not found.' }
$licenses = Join-Path $output 'licenses/windowsdesktop-runtime'
New-Item -ItemType Directory -Path $licenses -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $desktopRuntime 'LICENSE') -Destination $licenses
Write-Output "Built Windows setup application in $output"
