#requires -Version 7.6
#requires -PSEdition Core
# Copyright (c) 2026 Neil Colvin.
# Licensed under the MIT License. See LICENSE in the repository root.
# Requires PowerShell 7.6 or later. Run only trusted, locally reviewed programs.
param([Parameter(Mandatory)][string]$Configuration)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$guard = $null
$attempt = $null
$stateDirectory = $null
$utf8 = New-Object Text.UTF8Encoding($false)

function Write-AtomicJson([string]$Path, $Value) {
	$temp = $Path + '.' + [Guid]::NewGuid().ToString('N') + '.tmp'
	try {
		$bytes = $utf8.GetBytes(($Value | ConvertTo-Json -Depth 30))
		$stream = [IO.File]::Open($temp, 'CreateNew', 'Write', 'None')
		try { $stream.Write($bytes, 0, $bytes.Length); $stream.Flush($true) } finally { $stream.Dispose() }
		if ([IO.File]::Exists($Path)) { [IO.File]::Replace($temp, $Path, [System.Management.Automation.Language.NullString]::Value) }
		else { [IO.File]::Move($temp, $Path) }
	} finally { if ([IO.File]::Exists($temp)) { [IO.File]::Delete($temp) } }
}
function Quote-Argument([string]$Value) {
	if ($Value.Contains('"') -or $Value.Contains([char]0) -or $Value.Contains("`r") -or $Value.Contains("`n")) { throw 'Invalid argument.' }
	# Escape trailing backslashes before the Windows closing quote. No shell is used.
	return '"' + [regex]::Replace($Value, '(\\+)$', '$1$1') + '"'
}
function Invoke-Collector([string]$Command, [string]$Label) {
	$start = New-Object Diagnostics.ProcessStartInfo
	$start.FileName = Join-Path $config.CliDirectory $config.CliExecutable
	$start.WorkingDirectory = $config.CliDirectory
	$arguments = @($Command, '--worker', $config.WorkerFile, '--run', $config.RunDirectory)
	if ($Command -eq 'endurance-tick') { $arguments += @('--settings', $config.SettingsFile) }
	$start.Arguments = ($arguments | ForEach-Object { Quote-Argument $_ }) -join ' '
	$start.UseShellExecute = $false
	$start.CreateNoWindow = $true
	$start.RedirectStandardOutput = $true
	$start.RedirectStandardError = $true
	# A desktop or service environment must not override the pinned connection settings.
	foreach ($name in @($start.EnvironmentVariables.Keys)) {
		if ($name.StartsWith('CRESTRON_HOME_', [StringComparison]::OrdinalIgnoreCase)) { $start.EnvironmentVariables.Remove($name) }
	}
	$process = New-Object Diagnostics.Process
	$process.StartInfo = $start
	try {
		if (-not $process.Start()) { throw 'Collector did not start.' }
		Write-AtomicJson (Join-Path $attempt ($Label + '.process.json')) @{ ProcessId=$process.Id; StartedUtc=[DateTimeOffset]::UtcNow.ToString('O'); Command=$Command }
		$output = $process.StandardOutput.ReadToEndAsync()
		$errorOutput = $process.StandardError.ReadToEndAsync()
		# Do not abandon a child and allow the next tick to overlap it. The collector owns its probe deadline.
		$process.WaitForExit()
		$text = $output.GetAwaiter().GetResult()
		[IO.File]::WriteAllText((Join-Path $attempt ($Label + '.stdout.json')), $text, $utf8)
		[IO.File]::WriteAllText((Join-Path $attempt ($Label + '.stderr.txt')), $errorOutput.GetAwaiter().GetResult(), $utf8)
		return @{ ExitCode=$process.ExitCode; Text=$text }
	} finally { $process.Dispose() }
}
function Read-CollectorStatus($Reply) {
	# Exit 3 can mean either a valid uncertain state or a failure with no JSON.
	# Preserve stdout/stderr in the attempt and never dereference an unvalidated response.
	if ($Reply.ExitCode -notin @(0, 1, 3) -or [string]::IsNullOrWhiteSpace($Reply.Text) -or
		-not $Reply.Text.TrimStart().StartsWith('{', [StringComparison]::Ordinal)) { return $null }
	try { $value = $Reply.Text | ConvertFrom-Json -ErrorAction Stop } catch { return $null }
	if ($null -eq $value -or $value -isnot [System.Management.Automation.PSCustomObject] -or
		$null -eq $value.PSObject.Properties['ReservationState'] -or $value.ReservationState -isnot [string] -or
		$null -eq $value.PSObject.Properties['Checkpoint']) { return $null }
	if ($null -ne $value.Checkpoint -and ($value.Checkpoint -isnot [System.Management.Automation.PSCustomObject] -or
		$null -eq $value.Checkpoint.PSObject.Properties['State'] -or $value.Checkpoint.State -isnot [string])) { return $null }
	return $value
}
function Complete-Attempt([string]$State, [string]$Reason, [int]$Code, $Observed) {
	$result = @{ SchemaVersion=1; ObservedUtc=[DateTimeOffset]::UtcNow.ToString('O'); State=$State; Reason=$Reason; ExitCode=$Code; Collector=$Observed }
	# Persist the failure latch before updating other status files, which might themselves be unavailable.
	if ($State -in @('AttentionRequired', 'Failed')) { Write-AtomicJson (Join-Path $stateDirectory 'attention.json') $result }
	Write-AtomicJson (Join-Path $attempt 'result.json') $result
	Write-AtomicJson (Join-Path $stateDirectory 'status.json') $result
	# This is a durable local signal for an external alert service, not a claim of delivered notification.
	return $Code
}
try {
	$config = Get-Content -LiteralPath $Configuration -Raw | ConvertFrom-Json
	if ($config.SchemaVersion -ne 1) { throw 'Unsupported scheduler configuration.' }
	foreach ($path in @($config.CliDirectory, $config.WorkerFile, $config.RunDirectory, $config.SettingsFile, $config.StateDirectory)) {
		if (-not [IO.Path]::IsPathRooted($path) -or $path.StartsWith('\\')) { throw 'Use absolute local paths.' }
	}
	$stateDirectory = $config.StateDirectory
	[void][IO.Directory]::CreateDirectory($stateDirectory)
	try { $guard = [IO.File]::Open((Join-Path $stateDirectory 'scheduler.lock'), 'OpenOrCreate', 'ReadWrite', 'None') }
	catch [IO.IOException] { exit 4 }
	# A completed failure remains latched. It cannot turn into success on the next scheduled attempt.
	if (Test-Path -LiteralPath (Join-Path $stateDirectory 'attention.json')) { exit 3 }
	$attempts = Join-Path $stateDirectory 'attempts'
	[void][IO.Directory]::CreateDirectory($attempts)
	$unfinished = @(Get-ChildItem -LiteralPath $attempts -Directory | Where-Object {
		-not (Test-Path -LiteralPath (Join-Path $_.FullName 'result.json')) -or (Test-Path -LiteralPath (Join-Path $_.FullName 'error.json'))
	})
	$attempt = Join-Path $attempts ([DateTimeOffset]::UtcNow.ToString('yyyyMMddTHHmmssfffffffZ') + '-' + [Guid]::NewGuid().ToString('N'))
	[void][IO.Directory]::CreateDirectory($attempt)
	Write-AtomicJson (Join-Path $attempt 'intent.json') @{ StartedUtc=[DateTimeOffset]::UtcNow.ToString('O'); ProcessId=$PID }
	if ($unfinished.Count -gt 0) { exit (Complete-Attempt 'AttentionRequired' 'previous-invocation-incomplete' 3 $null) }
	if ((Get-FileHash -LiteralPath $PSCommandPath -Algorithm SHA256).Hash -ne $config.ScriptSha256 -or
		(Get-FileHash -LiteralPath $config.WorkerFile -Algorithm SHA256).Hash -ne $config.WorkerSha256) { throw 'Changed scheduled script or worker plan.' }
	$cliRoot = [IO.Path]::GetFullPath($config.CliDirectory).TrimEnd('\') + '\'
	$seen = @{}
	foreach ($pin in $config.CliFiles) {
		$file = [IO.Path]::GetFullPath((Join-Path $cliRoot $pin.Path))
		if (-not $file.StartsWith($cliRoot, [StringComparison]::OrdinalIgnoreCase) -or $seen.ContainsKey($file)) { throw 'Invalid CLI file manifest.' }
		$seen[$file] = $true
		if ((Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash -ne $pin.Sha256) { throw 'Changed CLI file.' }
	}
	$items = @(Get-ChildItem -LiteralPath $cliRoot -Force -Recurse)
	if (@($items | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }).Count -ne 0 -or
		@($items | Where-Object { -not $_.PSIsContainer }).Count -ne $seen.Count -or
		-not $seen.ContainsKey([IO.Path]::GetFullPath((Join-Path $cliRoot $config.CliExecutable)))) { throw 'CLI bundle does not match its manifest.' }
	$before = Invoke-Collector 'endurance-status' 'before'
	$status = Read-CollectorStatus $before
	if ($null -eq $status) { exit (Complete-Attempt 'AttentionRequired' 'collector-status-unavailable-before-tick' 3 $null) }
	$phase = if ($null -eq $status.Checkpoint) { 'NotStarted' } else { [string]$status.Checkpoint.State }
	if ($status.ReservationState -eq 'Released' -and $phase -in @('Passed', 'Failed')) {
		$code = if ($phase -eq 'Passed') { 0 } else { 1 }
		exit (Complete-Attempt $phase 'already-completed' $code $status)
	}
	if ($status.ReservationState -ne 'Held' -or $phase -notin @('NotStarted', 'Collecting', 'Passed', 'Failed')) {
		exit (Complete-Attempt 'AttentionRequired' 'collector-requires-inspection' 3 $status)
	}
	$tick = Invoke-Collector 'endurance-tick' 'tick'
	$after = Invoke-Collector 'endurance-status' 'after'
	$status = Read-CollectorStatus $after
	if ($null -eq $status) { exit (Complete-Attempt 'AttentionRequired' 'collector-status-unavailable-after-tick' 3 $null) }
	$phase = if ($null -eq $status.Checkpoint) { 'NotStarted' } else { [string]$status.Checkpoint.State }
	if ($tick.ExitCode -eq 0 -and $after.ExitCode -eq 0 -and $status.ReservationState -eq 'Held' -and $phase -eq 'Collecting') {
		exit (Complete-Attempt 'Collecting' 'tick-completed' 0 $status)
	}
	if ($status.ReservationState -eq 'Released' -and (($phase -eq 'Passed' -and $tick.ExitCode -eq 0 -and $after.ExitCode -eq 0) -or
		($phase -eq 'Failed' -and $tick.ExitCode -eq 1 -and $after.ExitCode -eq 1))) {
		$code = if ($phase -eq 'Passed') { 0 } else { 1 }
		exit (Complete-Attempt $phase 'tick-completed' $code $status)
	}
	exit (Complete-Attempt 'AttentionRequired' 'tick-outcome-requires-inspection' 3 $status)
} catch {
	# Do not expose paths, connection settings or potentially sensitive child exception messages.
	if ($null -ne $attempt) {
		Write-AtomicJson (Join-Path $attempt 'error.json') @{ Type=$_.Exception.GetType().Name; Line=$_.InvocationInfo.ScriptLineNumber; ErrorId=$_.FullyQualifiedErrorId }
		exit (Complete-Attempt 'AttentionRequired' 'scheduler-error' 3 $null)
	}
	Write-Error 'The scheduled worker could not initialize. Inspect its private configuration and Task Scheduler result.' -ErrorAction Continue
	exit 3
} finally { if ($null -ne $guard) { $guard.Dispose() } }