# Copyright (c) 2026 Neil Colvin.
# Licensed under the MIT License. See LICENSE in the repository root.
param(
	[Parameter(Mandatory)][string]$CliDirectory,
	[string]$CliExecutable = 'CrestronHomeDevTools.Console.exe',
	[Parameter(Mandatory)][string]$WorkerFile,
	[Parameter(Mandatory)][string]$ObserverFile,
	[Parameter(Mandatory)][string]$NotificationsFile,
	[Parameter(Mandatory)][string]$CredentialBindingsFile,
	[Parameter(Mandatory)][string]$JournalDirectory,
	[Parameter(Mandatory)][string]$StateDirectory,
	[Parameter(Mandatory)][string]$Output,
	[string]$TickScript = (Join-Path $PSScriptRoot 'Invoke-EnduranceScheduledWatch.ps1')
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
foreach ($path in @($CliDirectory, $WorkerFile, $ObserverFile, $NotificationsFile, $CredentialBindingsFile, $JournalDirectory, $StateDirectory, $Output, $TickScript)) {
	if (-not [IO.Path]::IsPathRooted($path) -or $path.StartsWith('\\')) { throw 'Use absolute local paths.' }
}
$CliDirectory = (Resolve-Path -LiteralPath $CliDirectory).Path.TrimEnd('\')
$JournalDirectory = [IO.Path]::GetFullPath($JournalDirectory).TrimEnd('\')
$StateDirectory = [IO.Path]::GetFullPath($StateDirectory).TrimEnd('\')
$TickScript = (Resolve-Path -LiteralPath $TickScript).Path
if ($CliExecutable -ne [IO.Path]::GetFileName($CliExecutable) -or -not (Test-Path -LiteralPath (Join-Path $CliDirectory $CliExecutable) -PathType Leaf)) { throw 'Choose an executable in the CLI directory.' }
$directories = @($CliDirectory, $JournalDirectory, $StateDirectory)
for ($i = 0; $i -lt $directories.Count; $i++) {
	for ($j = $i + 1; $j -lt $directories.Count; $j++) {
		$left = $directories[$i] + '\'; $right = $directories[$j] + '\'
		if ($left.StartsWith($right, [StringComparison]::OrdinalIgnoreCase) -or $right.StartsWith($left, [StringComparison]::OrdinalIgnoreCase)) { throw 'CLI, notification journal and watch state directories must be separate.' }
	}
}
$inputs = @()
foreach ($path in @($WorkerFile, $ObserverFile, $NotificationsFile, $CredentialBindingsFile, $TickScript)) {
	$resolved = (Resolve-Path -LiteralPath $path).Path
	$inputs += @{ Path=$resolved; Sha256=(Get-FileHash -LiteralPath $resolved -Algorithm SHA256).Hash }
}
foreach ($path in @($inputs.Path) + @([IO.Path]::GetFullPath($Output))) {
	foreach ($directory in $directories) {
		if ($path.StartsWith($directory + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Keep watch inputs outside CLI, journal and state directories.' }
	}
}
$files = @(Get-ChildItem -LiteralPath $CliDirectory -Force -Recurse)
if (@($files | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }).Count -ne 0) { throw 'Linked CLI files are not accepted.' }
$pins = @($files | Where-Object { -not $_.PSIsContainer } | Sort-Object FullName | ForEach-Object {
	@{Path=$_.FullName.Substring($CliDirectory.Length + 1).Replace('\','/'); Sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash}
})
$config = @{SchemaVersion=1; Kind='EnduranceWatch'; CliDirectory=$CliDirectory; CliExecutable=$CliExecutable; CliFiles=$pins;
	WorkerFile=$inputs[0].Path; ObserverFile=$inputs[1].Path; NotificationsFile=$inputs[2].Path; Inputs=$inputs;
	CredentialBindingsFile=$inputs[3].Path; JournalDirectory=$JournalDirectory; StateDirectory=$StateDirectory; ScriptSha256=$inputs[4].Sha256}
$bytes = (New-Object Text.UTF8Encoding($false)).GetBytes(($config | ConvertTo-Json -Depth 10))
$stream = [IO.File]::Open($Output, 'CreateNew', 'Write', 'None')
try { $stream.Write($bytes, 0, $bytes.Length); $stream.Flush($true) } finally { $stream.Dispose() }
Write-Output 'Prepared the watch configuration. No task, observation or email was started.'
