# Copyright (c) 2026 Neil Colvin.
# Licensed under the MIT License. See LICENSE in the repository root.
param([Parameter(Mandatory)][string]$ResultsDirectory)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../scripts/endurance'))
$ResultsDirectory = [IO.Path]::GetFullPath($ResultsDirectory)
if (Test-Path -LiteralPath $ResultsDirectory) { throw 'Choose a new test output directory.' }
[void][IO.Directory]::CreateDirectory($ResultsDirectory)
$bundle = Join-Path $ResultsDirectory 'stub'
& dotnet publish (Join-Path $PSScriptRoot 'CollectorStub/CollectorStub.csproj') -c Release -o $bundle --nologo *> (Join-Path $ResultsDirectory 'build.log')
if ($LASTEXITCODE -ne 0) { throw 'Test double build failed.' }
$shell = "$env:SystemRoot\System32\WindowsPowerShell\v1.0\powershell.exe"
$tickScript = Join-Path $root 'Invoke-EnduranceScheduledTick.ps1'
$passed = New-Object 'System.Collections.Generic.List[string]'
function Assert($Condition, [string]$Message) { if (-not $Condition) { throw $Message } }
function Prepare([string]$Name, [string]$Scenario) {
	$case = Join-Path $ResultsDirectory $Name
	[void][IO.Directory]::CreateDirectory($case)
	[IO.File]::WriteAllText((Join-Path $case 'worker.json'), (@{Scenario=$Scenario} | ConvertTo-Json))
	[IO.File]::WriteAllText((Join-Path $case 'settings.json'), '{}')
	& (Join-Path $root 'New-EnduranceScheduleConfiguration.ps1') -CliDirectory $bundle -CliExecutable 'CollectorStub.exe' -WorkerFile (Join-Path $case 'worker.json') -RunDirectory (Join-Path $case 'run') -SettingsFile (Join-Path $case 'settings.json') -StateDirectory (Join-Path $case 'state') -Output (Join-Path $case 'schedule.json') | Out-Null
	return $case
}
function Start-Tick([string]$Case) {
	$start = New-Object Diagnostics.ProcessStartInfo
	$start.FileName = $shell
	$start.Arguments = '-NoProfile -NonInteractive -ExecutionPolicy Bypass -File "' + $tickScript + '" -Configuration "' + (Join-Path $Case 'schedule.json') + '"'
	$start.UseShellExecute = $false; $start.CreateNoWindow = $true
	$start.RedirectStandardOutput = $true; $start.RedirectStandardError = $true
	# ProcessStartInfo does not apply pwsh's compatibility adjustment for Windows PowerShell module paths.
	$start.EnvironmentVariables.Remove('PSModulePath')
	# Deliberately inherited garbage must be removed by the wrapper before launching the collector.
	$start.EnvironmentVariables['CRESTRON_HOME_HOST'] = 'wrong.example.invalid'
	$process = New-Object Diagnostics.Process
	$process.StartInfo = $start
	[void]$process.Start()
	return $process
}
function Run-Tick([string]$Case) {
	$process = Start-Tick $Case
	try {
		$out = $process.StandardOutput.ReadToEndAsync(); $err = $process.StandardError.ReadToEndAsync()
		if (-not $process.WaitForExit(30000)) { $process.Kill($true); throw 'Synthetic tick exceeded 30 seconds.' }
		[IO.File]::WriteAllText((Join-Path $Case 'last-stdout.txt'), $out.GetAwaiter().GetResult())
		[IO.File]::WriteAllText((Join-Path $Case 'last-stderr.txt'), $err.GetAwaiter().GetResult())
		return $process.ExitCode
	} finally { $process.Dispose() }
}
foreach ($scenario in @('collecting','first','passing','failure','complete','failed','pending','interrupted','acquiring','releasing','not-started','unknown','malformed','error')) {
	$case = Prepare $scenario $scenario
	$expectedCode = switch ($scenario) { {$_ -in @('collecting','first','passing','complete')} {0} {$_ -in @('failure','failed')} {1} default {3} }
	$actual = Run-Tick $case
	Assert ($actual -eq $expectedCode) "$scenario returned $actual instead of $expectedCode."
	$state = Get-Content -LiteralPath (Join-Path $case 'state/status.json') -Raw | ConvertFrom-Json
	$expectedState = switch ($scenario) { {$_ -in @('collecting','first')} {'Collecting'} {$_ -in @('passing','complete')} {'Passed'} {$_ -in @('failure','failed')} {'Failed'} default {'AttentionRequired'} }
	Assert ($state.State -eq $expectedState) "$scenario has unexpected state."
	$calls = @(Get-Content -LiteralPath (Join-Path $case 'run/calls.txt'))
	$allowed = $scenario -in @('collecting','first','passing','failure','error')
	Assert (($calls -contains 'endurance-tick') -eq $allowed) "$scenario performed an unexpected command."
	Assert (-not ($calls -contains 'endurance-start')) 'Scheduler started a new run.'
	if ($expectedCode -ne 0) {
		Assert ((Run-Tick $case) -eq 3) 'A failed or uncertain result did not remain latched.'
		Assert (@(Get-Content -LiteralPath (Join-Path $case 'run/calls.txt')).Count -eq $calls.Count) 'Latched failure executed another command.'
	}
	else {
		$previous = $state.ObservedUtc
		Assert ((Run-Tick $case) -eq 0) 'A second successful invocation failed to replace its status.'
		$updated = Get-Content -LiteralPath (Join-Path $case 'state/status.json') -Raw | ConvertFrom-Json
		Assert ($updated.ObservedUtc -ne $previous -and $updated.State -eq $state.State) 'The scheduled status did not advance.'
		if ($expectedState -eq 'Passed') {
			Assert (@(Get-Content -LiteralPath (Join-Path $case 'run/calls.txt') | Where-Object {$_ -eq 'endurance-tick'}).Count -eq @($calls | Where-Object {$_ -eq 'endurance-tick'}).Count) 'A completed run executed another tick.'
		}
	}
	$passed.Add($scenario)
}
$case = Prepare 'changed-worker' 'collecting'
Add-Content -LiteralPath (Join-Path $case 'worker.json') -Value ' '
Assert ((Run-Tick $case) -eq 3) 'Changed worker was accepted.'
Assert (-not (Test-Path -LiteralPath (Join-Path $case 'run/calls.txt'))) 'Changed plan reached collector.'
$passed.Add('changed-worker')
$case = Prepare 'changed-cli' 'collecting'
[IO.File]::WriteAllText((Join-Path $bundle 'unexpected.txt'), 'unexpected')
try { Assert ((Run-Tick $case) -eq 3) 'Extra CLI file was accepted.' } finally { [IO.File]::Delete((Join-Path $bundle 'unexpected.txt')) }
Assert (-not (Test-Path -LiteralPath (Join-Path $case 'run/calls.txt'))) 'Changed bundle reached collector.'
$passed.Add('changed-cli')
$case = Prepare 'incomplete-attempt' 'collecting'
[void][IO.Directory]::CreateDirectory((Join-Path $case 'state/attempts/unfinished'))
Assert ((Run-Tick $case) -eq 3) 'An unfinished wrapper invocation was ignored.'
Assert (-not (Test-Path -LiteralPath (Join-Path $case 'run/calls.txt'))) 'Incomplete wrapper replayed the collector.'
$passed.Add('incomplete-attempt')
$case = Prepare 'exclusive-lock' 'collecting'
[void][IO.Directory]::CreateDirectory((Join-Path $case 'state'))
$handle = [IO.File]::Open((Join-Path $case 'state/scheduler.lock'), 'OpenOrCreate', 'ReadWrite', 'None')
try { Assert ((Run-Tick $case) -eq 4) 'Overlapping invocation was accepted.' } finally { $handle.Dispose() }
Assert ((Run-Tick $case) -eq 0) 'Ordinary lock contention incorrectly latched failure.'
$passed.Add('exclusive-lock')
$case = Prepare 'status-write-failure' 'collecting'
[void][IO.Directory]::CreateDirectory((Join-Path $case 'state/status.json'))
Assert ((Run-Tick $case) -ne 0) 'An unwritable status file appeared successful.'
$calls = @(Get-Content -LiteralPath (Join-Path $case 'run/calls.txt')).Count
Assert ((Run-Tick $case) -eq 3) 'A status-write failure was not latched.'
Assert (@(Get-Content -LiteralPath (Join-Path $case 'run/calls.txt')).Count -eq $calls) 'Status-write failure allowed another collector command.'
$passed.Add('status-write-failure')
$case = Prepare 'terminated-wrapper' 'hang'
$process = Start-Tick $case
try {
	$deadline = [DateTime]::UtcNow.AddSeconds(25)
	while (-not (Test-Path -LiteralPath (Join-Path $case 'run/ticked')) -and [DateTime]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 100 }
	Assert (Test-Path -LiteralPath (Join-Path $case 'run/ticked')) 'Synthetic hanging collector did not start.'
	Assert ((Run-Tick $case) -eq 4) 'Second wrapper overlapped the running collector.'
	$process.Kill($true); $process.WaitForExit()
} finally { if (-not $process.HasExited) { $process.Kill($true); $process.WaitForExit() }; $process.Dispose() }
$calls = @(Get-Content -LiteralPath (Join-Path $case 'run/calls.txt')).Count
Assert ((Run-Tick $case) -eq 3) 'Killed wrapper was silently resumed.'
Assert (@(Get-Content -LiteralPath (Join-Path $case 'run/calls.txt')).Count -eq $calls) 'Killed wrapper replayed a collector command.'
$passed.Add('terminated-wrapper')
@{Passed=$passed.Count;Cases=@($passed);WindowsPowerShell=$shell;CompletedUtc=[DateTimeOffset]::UtcNow.ToString('O')} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $ResultsDirectory 'results.json') -Encoding utf8
Write-Output "$($passed.Count) scheduled-worker scenarios passed using Windows PowerShell 5.1."