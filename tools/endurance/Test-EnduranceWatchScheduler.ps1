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
& dotnet publish (Join-Path $PSScriptRoot 'WatchStub/WatchStub.csproj') -c Release -o $bundle --nologo *> (Join-Path $ResultsDirectory 'build.log')
if ($LASTEXITCODE -ne 0) { throw 'Test double build failed.' }
$shell = "$env:SystemRoot\System32\WindowsPowerShell\v1.0\powershell.exe"
$tick = Join-Path $root 'Invoke-EnduranceScheduledWatch.ps1'
$utf8 = New-Object Text.UTF8Encoding($false)
$secret = 'synthetic-' + [char]0xA3 + '-' + [char]0x6E29 + '-secret'
function Assert($Condition, [string]$Message) { if (-not $Condition) { throw $Message } }
function Prepare([string]$Name, [string]$Scenario) {
	$case = Join-Path $ResultsDirectory $Name
	[void][IO.Directory]::CreateDirectory($case)
	[void][IO.Directory]::CreateDirectory((Join-Path $case 'journal'))
	[IO.File]::WriteAllText((Join-Path $case 'worker.json'), $Scenario)
	foreach ($name in @('observer.json', 'notifications.json')) { [IO.File]::WriteAllText((Join-Path $case $name), '{}') }
	[IO.File]::WriteAllText((Join-Path $case 'credentials.json'), (@{Smtp='synthetic-mail';StoreDirectory='SYNTHETIC-UNUSED-STORE'} | ConvertTo-Json), $utf8)
	& (Join-Path $root 'New-EnduranceWatchConfiguration.ps1') -CliDirectory $bundle -CliExecutable 'WatchStub.exe' -WorkerFile (Join-Path $case 'worker.json') -ObserverFile (Join-Path $case 'observer.json') -NotificationsFile (Join-Path $case 'notifications.json') -CredentialBindingsFile (Join-Path $case 'credentials.json') -JournalDirectory (Join-Path $case 'journal') -StateDirectory (Join-Path $case 'state') -Output (Join-Path $case 'watch.json') | Out-Null
	return $case
}
function Start-Watch([string]$Case) {
	$start = New-Object Diagnostics.ProcessStartInfo
	$start.FileName = $shell
	$start.Arguments = '-NoProfile -NonInteractive -ExecutionPolicy Bypass -File "' + $tick + '" -Configuration "' + (Join-Path $Case 'watch.json') + '"'
	$start.UseShellExecute = $false; $start.CreateNoWindow = $true
	$start.RedirectStandardOutput = $true; $start.RedirectStandardError = $true
	$start.EnvironmentVariables.Remove('PSModulePath')
	$start.EnvironmentVariables['CRESTRON_HOME_HOST'] = 'wrong.example.invalid'
	$process = New-Object Diagnostics.Process
	$process.StartInfo = $start
	[void]$process.Start()
	return $process
}
function Finish-Watch($Process) {
	try {
		$out = $Process.StandardOutput.ReadToEndAsync(); $err = $Process.StandardError.ReadToEndAsync()
		if (-not $Process.WaitForExit(30000)) { $Process.Kill($true); throw 'Synthetic watcher exceeded 30 seconds.' }
		$text = $out.GetAwaiter().GetResult() + $err.GetAwaiter().GetResult()
		Assert (-not $text.Contains($secret)) 'Credential leaked to scheduler output.'
		return $Process.ExitCode
	} finally { $Process.Dispose() }
}
foreach ($scenario in @('healthy', 'attention')) {
	$case = Prepare $scenario $scenario
	$expected = if ($scenario -eq 'attention') {3} else {0}
	Assert ((Finish-Watch (Start-Watch $case)) -eq $expected) 'Watcher lost child exit status.'
	$status = Get-Content -LiteralPath (Join-Path $case 'state/status.json') -Raw | ConvertFrom-Json
	Assert ($status.RequiresAttention -eq ($expected -ne 0)) 'Watcher hid attention.'
	$previous = $status.ObservedUtc
	Assert ((Finish-Watch (Start-Watch $case)) -eq $expected) 'Fresh passive observation failed.'
	$status = Get-Content -LiteralPath (Join-Path $case 'state/status.json') -Raw | ConvertFrom-Json
	Assert ($status.ObservedUtc -ne $previous) 'Status did not advance.'
	Assert (@(Get-Content -LiteralPath (Join-Path $case 'journal/calls.txt')).Count -eq 2) 'Unexpected command count.'
	foreach ($file in Get-ChildItem -LiteralPath (Join-Path $case 'state') -Recurse -File) {
		Assert (-not ([IO.File]::ReadAllText($file.FullName)).Contains($secret)) 'Credential leaked to private diagnostic output.'
	}
}
$case = Prepare 'changed-worker' 'healthy'
Add-Content -LiteralPath (Join-Path $case 'worker.json') -Value 'changed'
Assert ((Finish-Watch (Start-Watch $case)) -eq 3) 'Changed worker accepted.'
Assert (-not (Test-Path -LiteralPath (Join-Path $case 'journal/calls.txt'))) 'Changed worker launched watcher.'
$case = Prepare 'changed-destination' 'healthy'
Add-Content -LiteralPath (Join-Path $case 'notifications.json') -Value 'changed'
Assert ((Finish-Watch (Start-Watch $case)) -eq 3) 'Changed destination accepted.'
Assert (-not (Test-Path -LiteralPath (Join-Path $case 'journal/calls.txt'))) 'Changed destination launched watcher.'
$case = Prepare 'changed-credential-binding' 'healthy'
[IO.File]::WriteAllText((Join-Path $case 'credentials.json'), '{"Smtp":"different-account"}')
Assert ((Finish-Watch (Start-Watch $case)) -eq 3) 'Changed credential binding accepted.'
Assert (-not (Test-Path -LiteralPath (Join-Path $case 'journal/calls.txt'))) 'Changed credential binding launched watcher.'
$case = Prepare 'overlap' 'slow'
$first = Start-Watch $case
$deadline = [DateTime]::UtcNow.AddSeconds(15)
while (-not (Test-Path -LiteralPath (Join-Path $case 'journal/calls.txt')) -and [DateTime]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 50 }
Assert (Test-Path -LiteralPath (Join-Path $case 'journal/calls.txt')) 'First observer did not start.'
Assert ((Finish-Watch (Start-Watch $case)) -eq 3) 'Overlapping observation accepted.'
Assert ((Finish-Watch $first) -eq 0) 'Original observation did not finish.'
Assert (@(Get-Content -LiteralPath (Join-Path $case 'journal/calls.txt')).Count -eq 1) 'Overlap launched a second child.'
Write-Output 'Passed watcher bindings, empty credential stdin, output privacy, exit propagation, changed pins and overlap checks. No task or network operation was performed.'
