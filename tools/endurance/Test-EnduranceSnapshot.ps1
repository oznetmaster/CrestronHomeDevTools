#requires -Version 7.6
#requires -PSEdition Core
# Copyright (c) 2026 Neil Colvin.
# Licensed under the MIT License. See LICENSE in the repository root.
param([Parameter(Mandatory)][string]$ResultsDirectory)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$ResultsDirectory=[IO.Path]::GetFullPath($ResultsDirectory)
if (Test-Path -LiteralPath $ResultsDirectory) { throw 'Choose a new test results directory.' }
[void][IO.Directory]::CreateDirectory($ResultsDirectory)
$scripts=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../scripts/endurance'))
$export=Join-Path $scripts 'Export-EnduranceScheduledRun.ps1'
$bundle=Join-Path $ResultsDirectory 'stub'
& dotnet publish (Join-Path $PSScriptRoot 'SnapshotStub/SnapshotStub.csproj') -c Release -o $bundle --nologo *> (Join-Path $ResultsDirectory 'build.log')
if ($LASTEXITCODE -ne 0) { throw 'Synthetic collector build failed.' }
$passed=New-Object 'System.Collections.Generic.List[string]'
function Assert($Value,[string]$Message) { if (-not $Value) { throw $Message } }
function Prepare([string]$Name,[string]$Scenario='complete') {
	$case=Join-Path $ResultsDirectory $Name
	foreach ($dir in @($case,(Join-Path $case 'run'),(Join-Path $case 'state/reconciliation'))) { [void][IO.Directory]::CreateDirectory($dir) }
	[IO.File]::WriteAllText((Join-Path $case 'worker.json'),(@{Scenario=$Scenario;OriginalRun=(Join-Path $case 'run');Calls=(Join-Path $case 'calls.txt')} | ConvertTo-Json))
	[IO.File]::WriteAllText((Join-Path $case 'settings.json'),'SYNTHETIC-PRIVATE-SETTINGS-DO-NOT-COPY')
	[IO.File]::WriteAllText((Join-Path $case 'run/sample.json'),'synthetic sample')
	[IO.File]::WriteAllText((Join-Path $case 'state/reconciliation/retained-error.json'),'synthetic retained incident')
	[IO.File]::WriteAllText((Join-Path $case 'state/scheduler.lock'),'')
	& (Join-Path $scripts 'New-EnduranceScheduleConfiguration.ps1') -CliDirectory $bundle -CliExecutable 'SnapshotStub.exe' -WorkerFile (Join-Path $case 'worker.json') -RunDirectory (Join-Path $case 'run') -SettingsFile (Join-Path $case 'settings.json') -StateDirectory (Join-Path $case 'state') -Output (Join-Path $case 'schedule.json') | Out-Null
	return $case
}
function Run([string]$Case,[string]$Pin='', [string]$Destination='', [int]$MaximumEntries=0) {
	if (-not $Pin) { $Pin=(Get-FileHash (Join-Path $Case 'schedule.json') -Algorithm SHA256).Hash }
	if (-not $Destination) { $Destination=Join-Path $Case 'snapshot' }
	$start=New-Object Diagnostics.ProcessStartInfo
	$start.FileName=(Join-Path $PSHOME 'pwsh.exe')
	$start.Arguments='-NoProfile -NonInteractive -ExecutionPolicy Bypass -File "'+$export+'" -Configuration "'+(Join-Path $Case 'schedule.json')+'" -ConfigurationSha256 '+$Pin+' -OutputDirectory "'+$Destination+'"'
	if ($MaximumEntries -ne 0) { $start.Arguments += ' -MaximumEntries ' + $MaximumEntries }
	$start.UseShellExecute=$false; $start.CreateNoWindow=$true
	$start.RedirectStandardOutput=$true; $start.RedirectStandardError=$true
	$start.EnvironmentVariables.Remove('PSModulePath')
	$start.EnvironmentVariables['CRESTRON_HOME_HOST']='wrong.example.invalid'
	$process=New-Object Diagnostics.Process
	$process.StartInfo=$start
	try {
		[void]$process.Start()
		$out=$process.StandardOutput.ReadToEndAsync(); $err=$process.StandardError.ReadToEndAsync()
		if (-not $process.WaitForExit(30000)) { $process.Kill($true); throw 'Synthetic snapshot exceeded 30 seconds.' }
		[IO.File]::WriteAllText((Join-Path $Case 'stdout.txt'),$out.GetAwaiter().GetResult())
		[IO.File]::WriteAllText((Join-Path $Case 'stderr.txt'),$err.GetAwaiter().GetResult())
		return $process.ExitCode
	} finally { $process.Dispose() }
}
foreach ($scenario in @('complete','collecting','held','failed','malformed','export-error','copy-error','mismatch','source-change','copy-change')) {
	$case=Prepare $scenario $scenario
	$code=Run $case
	Assert ($code -eq $(if($scenario -eq 'complete'){0}else{3})) "$scenario returned $code."
	Assert ((Test-Path (Join-Path $case 'snapshot/complete.json')) -eq ($scenario -eq 'complete')) "$scenario published an incorrect completion receipt."
	$calls=[IO.File]::ReadAllLines((Join-Path $case 'calls.txt'))
	Assert (@($calls | Where-Object {$_ -notmatch '^endurance-(status|export):(source|copy)$'}).Count -eq 0) 'Snapshot issued a mutating command.'
	if ($scenario -eq 'complete') {
		Assert (($calls -join ',') -ceq 'endurance-status:source,endurance-export:source,endurance-status:copy,endurance-export:copy') 'Completed snapshot did not revalidate the copy.'
		$receipt=Get-Content (Join-Path $case 'snapshot/complete.json') -Raw | ConvertFrom-Json
		Assert (-not $receipt.SubmissionReady) 'Snapshot claims submission acceptance.'
		Assert (Test-Path (Join-Path $case 'snapshot/scheduler-state/reconciliation/retained-error.json')) 'Retained incident was lost.'
		Assert (-not (Test-Path (Join-Path $case 'snapshot/settings.json'))) 'Private settings were copied.'
		foreach ($file in $receipt.Files) { Assert ((Get-FileHash (Join-Path $case ('snapshot/'+$file.Path)) -Algorithm SHA256).Hash -eq $file.Sha256) 'Snapshot inventory mismatch.' }
		Assert ((Run $case) -eq 3) 'Existing snapshot was overwritten.'
		Assert ([IO.File]::ReadAllLines((Join-Path $case 'calls.txt')).Count -eq 4) 'Existing snapshot reran collector.'
	}
	$passed.Add($scenario)
}
foreach ($scenario in @('wrong-pin','worker-changed','extra-cli','attention','busy','nested-output')) {
	$case=Prepare $scenario
	$lock=$null
	try {
		switch ($scenario) {
			'worker-changed' { [IO.File]::AppendAllText((Join-Path $case 'worker.json'),' ') }
			'extra-cli' { [IO.File]::WriteAllText((Join-Path $bundle 'unexpected.txt'),'unexpected') }
			'attention' { [IO.File]::WriteAllText((Join-Path $case 'state/attention.json'),'{}') }
			'busy' { $lock=[IO.File]::Open((Join-Path $case 'state/scheduler.lock'),'Open','ReadWrite','None') }
		}
		$pin=if($scenario -eq 'wrong-pin'){'a'*64}else{''}
		$destination=if($scenario -eq 'nested-output'){Join-Path $case 'run/snapshot'}else{''}
		$code=Run $case $pin $destination
		Assert ($code -eq $(if($scenario -eq 'busy'){4}else{3})) "$scenario returned $code."
		Assert (-not (Test-Path (Join-Path $case 'calls.txt'))) "$scenario launched the collector."
	} finally {
		if ($lock) { $lock.Dispose() }
		if (Test-Path (Join-Path $bundle 'unexpected.txt')) { [IO.File]::Delete((Join-Path $bundle 'unexpected.txt')) }
	}
	$passed.Add($scenario)
}
# A scheduler journals every wake-up, not just each collected sample. Exercise a
# bounded refusal and recovery to a fresh destination, retaining every source file.
$case=Prepare 'inventory-budget'
for($index=0;$index -lt 250;$index++) {
	[IO.File]::WriteAllText((Join-Path $case ('state/reconciliation/entry-'+$index+'.json')), '{"Synthetic":true}')
}
Assert ((Run $case -MaximumEntries 200) -eq 3) 'Explicit inventory limit was ignored.'
Assert (Test-Path (Join-Path $case 'snapshot/failure.json')) 'Inventory-limit failure was not retained.'
Assert (-not (Test-Path (Join-Path $case 'snapshot/complete.json'))) 'Partial export claimed completion.'
$destination=Join-Path $case 'snapshot-with-default-budget'
Assert ((Run $case -Destination $destination) -eq 0) 'Default inventory budget did not retain the completed run.'
$receipt=Get-Content (Join-Path $destination 'complete.json') -Raw | ConvertFrom-Json
Assert ($receipt.MaximumEntries -eq 100000) 'Completion receipt did not retain its actual budget.'
Assert (@($receipt.Files | Where-Object {$_.Path -like 'scheduler-state/reconciliation/entry-*'}).Count -eq 250) 'Scheduler evidence was dropped to fit the budget.'
Assert (Test-Path (Join-Path $case 'snapshot/failure.json')) 'A new export removed the original failure.'
$passed.Add('inventory-budget')
@{Passed=$passed.Count; Scenarios=@($passed); HardwareContacted=$false; SubmissionEvidence=$false} | ConvertTo-Json | Set-Content (Join-Path $ResultsDirectory 'results.json')
Write-Output "$($passed.Count) synthetic snapshot scenarios passed. No processor was contacted."
