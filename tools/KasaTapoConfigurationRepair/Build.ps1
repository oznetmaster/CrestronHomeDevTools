# Build locally; never signs, deploys or publishes.
[CmdletBinding()]
param([Parameter(Mandatory)][string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$taskOutput = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $taskOutput) { throw 'Use a new output directory.' }
dotnet publish (Join-Path $PSScriptRoot 'KasaTapoConfigurationRepair.csproj') -c Release -r win-x64 --self-contained true --disable-build-servers -p:RuntimeFrameworkVersion=10.0.13 -p:DebugType=None -p:DebugSymbols=false -o $taskOutput
if ($LASTEXITCODE -ne 0) { throw 'Utility build failed.' }
$taskRepo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$taskLicenses = Join-Path $taskOutput 'licenses'
New-Item -ItemType Directory -Path $taskLicenses | Out-Null
Copy-Item -LiteralPath (Join-Path $taskRepo 'LICENSE') -Destination (Join-Path $taskOutput 'LICENSE.txt')
foreach ($taskName in @('SSH.NET','MailKit','MimeKit','BouncyCastle','Microsoft-ProtectedData')) {
    Copy-Item -LiteralPath (Join-Path $taskRepo "licenses/$taskName-LICENSE.txt") -Destination $taskLicenses
}
$taskAssets = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'obj/project.assets.json') -Raw | ConvertFrom-Json
$taskPackageFolders = $taskAssets.packageFolders.PSObject.Properties.Name
foreach ($taskRuntime in @('microsoft.netcore.app.runtime.win-x64','microsoft.windowsdesktop.app.runtime.win-x64')) {
    $taskRuntimeSource = @($taskPackageFolders | ForEach-Object { Join-Path $_ "$taskRuntime/10.0.13" } | Where-Object { Test-Path -LiteralPath $_ }) | Select-Object -First 1
    if (-not $taskRuntimeSource) { throw "Missing runtime notices: $taskRuntime" }
    $taskDestination = Join-Path $taskLicenses $taskRuntime
    New-Item -ItemType Directory -Path $taskDestination | Out-Null
    foreach ($taskNotice in @('LICENSE','LICENSE.TXT','THIRD-PARTY-NOTICES.TXT')) {
        $taskSource = Join-Path $taskRuntimeSource $taskNotice
        if (Test-Path -LiteralPath $taskSource) { Copy-Item -LiteralPath $taskSource -Destination $taskDestination }
    }
}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'THIRD-PARTY-NOTICES.txt') -Destination $taskOutput
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Instructions.txt') -Destination $taskOutput
Get-ChildItem -LiteralPath $taskOutput -File -Recurse | ForEach-Object {
    $taskRelative = $_.FullName.Substring($taskOutput.Length + 1).Replace('\','/')
    '{0}  {1}' -f (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $taskRelative
} | Set-Content -LiteralPath (Join-Path $taskOutput 'SHA256SUMS.txt') -Encoding ascii
