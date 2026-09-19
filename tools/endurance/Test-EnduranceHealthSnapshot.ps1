# Copyright (c) 2026 Neil Colvin.
# Licensed under the MIT License. See LICENSE in the repository root.
# Synthetic OS queries only; no real task or processor is accessed.
param([Parameter(Mandatory)][string]$ResultsDirectory)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$ResultsDirectory = [IO.Path]::GetFullPath($ResultsDirectory)
if (Test-Path -LiteralPath $ResultsDirectory) { throw 'Choose a new results directory.' }
[void][IO.Directory]::CreateDirectory($ResultsDirectory)
$source = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../scripts/endurance/Get-EnduranceHealthSnapshot.ps1'))
$healthTestState = [pscustomobject]@{ Reads=0; Scenario='' }
function Get-ScheduledTask {
	param($TaskPath)
	$healthTestState.Reads++
	if ($healthTestState.Scenario -eq 'missing') { return }
	$state = if ($healthTestState.Scenario -eq 'running' -or ($healthTestState.Scenario -eq 'race' -and $healthTestState.Reads -gt 1)) { 'Running' } else { 'Ready' }
	[pscustomobject]@{ TaskName='CandidateA'; State=$state }
}
function Get-ScheduledTaskInfo {
	param([Parameter(ValueFromPipeline)]$InputObject)
	process { [pscustomobject]@{ LastTaskResult = if ($healthTestState.Scenario -in @('race','running','inconsistent')) { 267009 } else { 0 } } }
}
$passed = @()
foreach ($case in @('ready','running','race','inconsistent','missing','attention')) {
	$healthTestState.Reads = 0
	$healthTestState.Scenario = $case
	$state = Join-Path $ResultsDirectory $case
	[void][IO.Directory]::CreateDirectory($state)
	$status = Join-Path $state 'status.json'
	[IO.File]::WriteAllText($status, '{"SchemaVersion":1,"ObservedUtc":"2026-09-19T06:00:00Z","State":"Collecting","ExitCode":0,"Collector":null}')
	if ($case -eq 'attention') { [IO.File]::WriteAllText((Join-Path $state 'attention.json'), '{}') }
	$before = (Get-FileHash -LiteralPath $status).Hash
	$json = & $source -TaskName 'CandidateA' -StateDirectory $state
	$parsed = $json | ConvertFrom-Json
	if ($parsed.WorkerReachable -ne $true -or $parsed.Scheduler.State -ne 'Collecting') { throw "Invalid snapshot: $case" }
	if ($parsed.TaskPresent -ne ($case -ne 'missing')) { throw "Invalid presence: $case" }
	if ($parsed.AttentionPresent -ne ($case -eq 'attention')) { throw "Invalid attention: $case" }
	if ($case -eq 'race' -and ($healthTestState.Reads -ne 2 -or $parsed.TaskState -ne 'Running')) { throw 'Task transition was not reread.' }
	if ($case -eq 'inconsistent' -and ($healthTestState.Reads -ne 3 -or $parsed.TaskState -ne 'Ready' -or $parsed.LastTaskResult -ne 267009)) { throw 'Persistent inconsistency was hidden or retried without a bound.' }
	if ((Get-FileHash -LiteralPath $status).Hash -ne $before) { throw 'Source receipt changed.' }
	[IO.File]::WriteAllText((Join-Path $state 'observation.json'), $json)
	$passed += $case
}
@{ Passed=$passed; Count=$passed.Count; HardwareAccess=$false } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $ResultsDirectory 'result.json') -Encoding UTF8
Write-Output "Passed $($passed.Count) passive snapshot cases."
