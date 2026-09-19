# Copyright (c) 2026 Neil Colvin.
# Licensed under the MIT License. See LICENSE in the repository root.
# Private completion snapshot; compatible with Windows PowerShell 5.1.
param(
	[Parameter(Mandatory)][string]$Configuration,
	[Parameter(Mandatory)][ValidatePattern('\A[0-9a-fA-F]{64}\z')][string]$ConfigurationSha256,
	[Parameter(Mandatory)][string]$OutputDirectory,
	[string]$TickScript
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if (-not $TickScript) { $TickScript=Join-Path $PSScriptRoot 'Invoke-EnduranceScheduledTick.ps1' }
$guard = $null
$created = $false
$utf8 = New-Object Text.UTF8Encoding($false)

function Local-Path([string]$Path) {
	if (-not [IO.Path]::IsPathRooted($Path) -or $Path.StartsWith('\\')) { throw 'Use absolute local paths.' }
	$full = [IO.Path]::GetFullPath($Path).TrimEnd('\')
	$current = $full
	while ($current) {
		if (Test-Path -LiteralPath $current) {
			if ((Get-Item -LiteralPath $current -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Linked paths are not accepted.' }
		}
		$current = [IO.Path]::GetDirectoryName($current)
	}
	return $full
}
function Digest([string]$Path) { return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash }
function Save-Json([string]$Name, $Value) {
	$bytes = $utf8.GetBytes(($Value | ConvertTo-Json -Depth 40))
	$file = [IO.File]::Open((Join-Path $OutputDirectory $Name), 'CreateNew', 'Write', 'None')
	try { $file.Write($bytes, 0, $bytes.Length); $file.Flush($true) } finally { $file.Dispose() }
}
function Inventory([string]$Root, [string[]]$Exclude = @()) {
	$queue = New-Object 'System.Collections.Generic.Queue[string]'
	$queue.Enqueue($Root)
	$files = New-Object 'System.Collections.Generic.List[object]'
	[long]$bytes = 0
	[int]$entries = 0
	while ($queue.Count -gt 0) {
		foreach ($item in @(Get-ChildItem -LiteralPath $queue.Dequeue() -Force)) {
			if (++$entries -gt 20000) { throw 'Snapshot tree exceeds its entry limit.' }
			if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Linked evidence is not accepted.' }
			if ($item.PSIsContainer) { $queue.Enqueue($item.FullName); continue }
			$relative = $item.FullName.Substring($Root.Length + 1).Replace('\','/')
			if ($relative -in $Exclude) { continue }
			$bytes += $item.Length
			if ($item.Length -gt 64MB -or $bytes -gt 1GB) { throw 'Snapshot evidence exceeds its size limit.' }
			$files.Add(@{Path=$relative; Length=$item.Length; Sha256=(Digest $item.FullName)})
		}
	}
	return @($files | Sort-Object { $_.Path })
}
function Same-Inventory($Before, $After) {
	if ($Before.Count -ne $After.Count) { return $false }
	for ($i=0; $i -lt $Before.Count; $i++) {
		if ($Before[$i].Path -cne $After[$i].Path -or $Before[$i].Length -ne $After[$i].Length -or $Before[$i].Sha256 -ne $After[$i].Sha256) { return $false }
	}
	return $true
}
function Copy-Tree([string]$Source, [string]$Destination, $Files) {
	[void][IO.Directory]::CreateDirectory($Destination)
	foreach ($entry in $Files) {
		$to = Join-Path $Destination $entry.Path
		[void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($to))
		[IO.File]::Copy((Join-Path $Source $entry.Path), $to, $false)
		if ((Digest $to) -ne $entry.Sha256) { throw 'Copied evidence does not match its source inventory.' }
	}
}
function Quote-Argument([string]$Value) {
	if ($Value.Contains('"') -or $Value.Contains([char]0) -or $Value.Contains("`r") -or $Value.Contains("`n")) { throw 'Invalid argument.' }
	return '"' + [regex]::Replace($Value, '(\\+)$', '$1$1') + '"'
}
function Invoke-Offline([string]$Command, [string]$Run, [string]$Worker, [string]$Label) {
	$start = New-Object Diagnostics.ProcessStartInfo
	$start.FileName = Join-Path $config.CliDirectory $config.CliExecutable
	$start.WorkingDirectory = $config.CliDirectory
	$start.Arguments = (@($Command, '--worker', $Worker, '--run', $Run) | ForEach-Object { Quote-Argument $_ }) -join ' '
	$start.UseShellExecute=$false; $start.CreateNoWindow=$true
	$start.RedirectStandardOutput=$true; $start.RedirectStandardError=$true
	foreach ($name in @($start.EnvironmentVariables.Keys)) {
		if ($name.StartsWith('CRESTRON_HOME_', [StringComparison]::OrdinalIgnoreCase)) { $start.EnvironmentVariables.Remove($name) }
	}
	$process = New-Object Diagnostics.Process
	$process.StartInfo = $start
	try {
		if (-not $process.Start()) { throw 'Offline collector did not start.' }
		Save-Json ($Label + '.process.json') @{ProcessId=$process.Id; StartedUtc=[DateTimeOffset]::UtcNow.ToString('O'); Command=$Command}
		$stdout=$process.StandardOutput.ReadToEndAsync(); $stderr=$process.StandardError.ReadToEndAsync()
		# Never leave a live child behind and release the scheduler guard on a timeout.
		$process.WaitForExit()
		$text=$stdout.GetAwaiter().GetResult()
		[IO.File]::WriteAllText((Join-Path $OutputDirectory ($Label + '.stdout.json')), $text, $utf8)
		[IO.File]::WriteAllText((Join-Path $OutputDirectory ($Label + '.stderr.txt')), $stderr.GetAwaiter().GetResult(), $utf8)
		if ($process.ExitCode -ne 0 -or [string]::IsNullOrWhiteSpace($text) -or -not $text.TrimStart().StartsWith('{')) { throw 'Offline collector did not return a successful object.' }
		return @{Text=$text; Value=($text | ConvertFrom-Json)}
	} finally { $process.Dispose() }
}
function Require-Complete($Reply) {
	if ($Reply.Value.ReservationState -cne 'Released' -or $Reply.Value.Checkpoint.State -cne 'Passed') { throw 'Only a passed run with confirmed reservation release can be retained.' }
}
try {
	$Configuration=Local-Path $Configuration
	$TickScript=Local-Path $TickScript
	$OutputDirectory=Local-Path $OutputDirectory
	if (Test-Path -LiteralPath $OutputDirectory) { throw 'Use a new private output directory; inspect incomplete attempts instead of replaying them.' }
	if (-not (Test-Path -LiteralPath ([IO.Path]::GetDirectoryName($OutputDirectory)) -PathType Container)) { throw 'The private output parent must already exist.' }
	if ((Digest $Configuration) -ne $ConfigurationSha256) { throw 'Scheduler configuration does not match its independently retained pin.' }
	$config=Get-Content -LiteralPath $Configuration -Raw | ConvertFrom-Json
	if ($config.SchemaVersion -ne 1) { throw 'Unsupported scheduler configuration.' }
	foreach ($name in @('CliDirectory','WorkerFile','RunDirectory','StateDirectory','SettingsFile')) { $config.$name=Local-Path $config.$name }
	foreach ($root in @($config.CliDirectory,$config.RunDirectory,$config.StateDirectory)) {
		if (-not (Test-Path -LiteralPath $root -PathType Container)) { throw 'Required source directory is missing.' }
		if (($OutputDirectory + '\').StartsWith($root + '\', [StringComparison]::OrdinalIgnoreCase) -or ($root + '\').StartsWith($OutputDirectory + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Keep the snapshot outside all source directories.' }
		foreach ($inputFile in @($config.WorkerFile,$config.SettingsFile,$Configuration,$TickScript)) {
			if ($inputFile.StartsWith($root + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Keep inputs and private connection settings outside evidence and CLI directories.' }
		}
	}
	$lockPath=Local-Path (Join-Path $config.StateDirectory 'scheduler.lock')
	try { $guard=[IO.File]::Open($lockPath,'Open','ReadWrite','None') } catch [IO.IOException] { exit 4 }
	if (Test-Path -LiteralPath (Join-Path $config.StateDirectory 'attention.json')) { throw 'An unresolved scheduler attention record requires review.' }
	if ((Digest $config.WorkerFile) -ne $config.WorkerSha256 -or (Digest $TickScript) -ne $config.ScriptSha256) { throw 'Worker plan or scheduled script changed.' }
	$cli=@(Inventory $config.CliDirectory)
	$pins=@{}
	foreach ($pin in $config.CliFiles) {
		if ($pins.ContainsKey($pin.Path)) { throw 'Duplicate CLI pin.' }
		$pins[$pin.Path]=$pin.Sha256
	}
	if ($pins.Count -ne $cli.Count -or -not $pins.ContainsKey($config.CliExecutable)) { throw 'CLI inventory is incomplete.' }
	foreach ($entry in $cli) { if (-not $pins.ContainsKey($entry.Path) -or $pins[$entry.Path] -ne $entry.Sha256) { throw 'CLI inventory changed.' } }
	[void][IO.Directory]::CreateDirectory($OutputDirectory); $created=$true
	Save-Json 'intent.json' @{SchemaVersion=1; StartedUtc=[DateTimeOffset]::UtcNow.ToString('O'); ConfigurationSha256=$ConfigurationSha256; ExportScriptSha256=(Digest $PSCommandPath)}
	Require-Complete (Invoke-Offline 'endurance-status' $config.RunDirectory $config.WorkerFile 'source-status')
	$export=Invoke-Offline 'endurance-export' $config.RunDirectory $config.WorkerFile 'source-export'
	if ($export.Value.Outcome -cne 'Passed') { throw 'Export did not report a passed observation.' }
	$runExclusions=@('monitor.lock','observations/collector.lock')
	$run=@(Inventory $config.RunDirectory $runExclusions)
	$state=@(Inventory $config.StateDirectory @('scheduler.lock'))
	Copy-Tree $config.RunDirectory (Join-Path $OutputDirectory 'run') $run
	Copy-Tree $config.StateDirectory (Join-Path $OutputDirectory 'scheduler-state') $state
	[IO.File]::Copy($config.WorkerFile,(Join-Path $OutputDirectory 'worker.json'),$false)
	[IO.File]::Copy($Configuration,(Join-Path $OutputDirectory 'schedule.json'),$false)
	[IO.File]::Copy($TickScript,(Join-Path $OutputDirectory 'Invoke-EnduranceScheduledTick.ps1'),$false)
	Require-Complete (Invoke-Offline 'endurance-status' (Join-Path $OutputDirectory 'run') (Join-Path $OutputDirectory 'worker.json') 'copy-status')
	$copiedExport=Invoke-Offline 'endurance-export' (Join-Path $OutputDirectory 'run') (Join-Path $OutputDirectory 'worker.json') 'copy-export'
	if ($export.Text -cne $copiedExport.Text) { throw 'The copied run exported a different observation.' }
	if (-not (Same-Inventory $run @(Inventory $config.RunDirectory $runExclusions)) -or -not (Same-Inventory $run @(Inventory (Join-Path $OutputDirectory 'run') $runExclusions)) -or
		-not (Same-Inventory $state @(Inventory $config.StateDirectory @('scheduler.lock'))) -or -not (Same-Inventory $cli @(Inventory $config.CliDirectory)) -or
		(Digest $Configuration) -ne $ConfigurationSha256 -or (Digest (Join-Path $OutputDirectory 'schedule.json')) -ne $ConfigurationSha256 -or
		(Digest $config.WorkerFile) -ne $config.WorkerSha256 -or (Digest (Join-Path $OutputDirectory 'worker.json')) -ne $config.WorkerSha256 -or
		(Digest $TickScript) -ne $config.ScriptSha256 -or (Digest (Join-Path $OutputDirectory 'Invoke-EnduranceScheduledTick.ps1')) -ne $config.ScriptSha256) { throw 'Source or copied evidence changed during retention.' }
	$files=@(Inventory $OutputDirectory @('run/monitor.lock','run/observations/collector.lock'))
	Save-Json 'complete.json' @{SchemaVersion=1; CompletedUtc=[DateTimeOffset]::UtcNow.ToString('O'); CollectionPassed=$true; ReservationReleased=$true; SubmissionReady=$false; ConfigurationSha256=$ConfigurationSha256; Files=$files}
	Write-Output 'Retained and revalidated the completed collection. This private snapshot is not a completed submission bundle. The scheduled task was not changed.'
	exit 0
} catch {
	if ($created) { Save-Json 'failure.json' @{Type=$_.Exception.GetType().Name; Line=$_.InvocationInfo.ScriptLineNumber; ErrorId=$_.FullyQualifiedErrorId; SubmissionReady=$false} }
	Write-Error 'Completion snapshot failed. Inspect its private records; the monitor was not restarted or reconfigured.' -ErrorAction Continue
	exit 3
} finally { if ($null -ne $guard) { $guard.Dispose() } }
