# Build a self-contained Windows setup app; no signing, deployment or publication.
[CmdletBinding()]
param([string]$OutputDirectory = (Join-Path $PSScriptRoot '..\artifacts\submission-setup-win-x64'))
$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot '..\CrestronHomeDevTools.Setup\CrestronHomeDevTools.Setup.csproj'
dotnet publish $project -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $OutputDirectory
if ($LASTEXITCODE -ne 0) { throw 'Setup application publication failed.' }
Write-Output "Built Windows setup application in $OutputDirectory"
