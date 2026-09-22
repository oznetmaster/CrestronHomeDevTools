#requires -Version 7.6
#requires -PSEdition Core
# Copyright (c) 2026 Neil Colvin.
# Licensed under the MIT License. See LICENSE in the repository root.
# Run once after reviewing the published CLI, worker plan and private directory permissions.
param(
	[Parameter(Mandatory)][string]$CliDirectory,
	[string]$CliExecutable = 'CrestronHomeDevTools.Console.exe',
	[Parameter(Mandatory)][string]$WorkerFile,
	[Parameter(Mandatory)][string]$RunDirectory,
	[Parameter(Mandatory)][string]$SettingsFile,
	[Parameter(Mandatory)][string]$StateDirectory,
	[Parameter(Mandatory)][string]$Output,
	[string]$TickScript = (Join-Path $PSScriptRoot 'Invoke-EnduranceScheduledTick.ps1')
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
foreach ($path in @($CliDirectory, $WorkerFile, $RunDirectory, $SettingsFile, $StateDirectory, $Output, $TickScript)) {
	if (-not [IO.Path]::IsPathRooted($path) -or $path.StartsWith('\\')) { throw 'Use absolute local paths.' }
}
$CliDirectory = (Resolve-Path -LiteralPath $CliDirectory).Path.TrimEnd('\')
$WorkerFile = (Resolve-Path -LiteralPath $WorkerFile).Path
$SettingsFile = (Resolve-Path -LiteralPath $SettingsFile).Path
$TickScript = (Resolve-Path -LiteralPath $TickScript).Path
$RunDirectory = [IO.Path]::GetFullPath($RunDirectory).TrimEnd('\')
$StateDirectory = [IO.Path]::GetFullPath($StateDirectory).TrimEnd('\')
if ($CliExecutable -ne [IO.Path]::GetFileName($CliExecutable) -or -not (Test-Path -LiteralPath (Join-Path $CliDirectory $CliExecutable) -PathType Leaf)) { throw 'Choose an executable in the CLI directory.' }
foreach ($left in @($CliDirectory, $RunDirectory, $StateDirectory)) {
	foreach ($right in @($CliDirectory, $RunDirectory, $StateDirectory)) {
		if ($left -ne $right -and ($right + '\').StartsWith($left + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'CLI, run and scheduler state directories must be separate.' }
	}
}
if ($CliDirectory -eq $RunDirectory -or $CliDirectory -eq $StateDirectory -or $RunDirectory -eq $StateDirectory) { throw 'Use separate directories.' }
foreach ($path in @($WorkerFile, $SettingsFile, $Output, $TickScript)) {
	foreach ($directory in @($CliDirectory, $RunDirectory, $StateDirectory)) {
		if ([IO.Path]::GetFullPath($path).StartsWith($directory + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Keep inputs and configuration outside CLI, run and scheduler state directories.' }
	}
}
$items = @(Get-ChildItem -LiteralPath $CliDirectory -Force -Recurse)
if (@($items | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }).Count -ne 0) { throw 'Linked CLI files or directories are not accepted.' }
$pins = @($items | Where-Object { -not $_.PSIsContainer } | Sort-Object FullName | ForEach-Object {
	@{Path=$_.FullName.Substring($CliDirectory.Length + 1).Replace('\','/'); Sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash}
})
$configuration = @{SchemaVersion=1; CliDirectory=$CliDirectory; CliExecutable=$CliExecutable; CliFiles=$pins;
	WorkerFile=$WorkerFile; WorkerSha256=(Get-FileHash -LiteralPath $WorkerFile -Algorithm SHA256).Hash;
	RunDirectory=$RunDirectory; SettingsFile=$SettingsFile; StateDirectory=$StateDirectory;
	ScriptSha256=(Get-FileHash -LiteralPath $TickScript -Algorithm SHA256).Hash}
$bytes = (New-Object Text.UTF8Encoding($false)).GetBytes(($configuration | ConvertTo-Json -Depth 10))
$file = [IO.File]::Open($Output, 'CreateNew', 'Write', 'None')
try { $file.Write($bytes, 0, $bytes.Length); $file.Flush($true) } finally { $file.Dispose() }
Write-Output 'Prepared the schedule configuration. No task was registered and no endurance run was started.'