#requires -Version 7.6
#requires -PSEdition Core
# Copyright (c) 2026 Neil Colvin.
# Licensed under the MIT License. See LICENSE in the repository root.
# Requires PowerShell 7.6 or later. Read-only; never opens a collector journal.
param(
	[Parameter(Mandatory)][ValidateNotNullOrEmpty()][string]$TaskName,
	[Parameter(Mandatory)][ValidateNotNullOrEmpty()][string]$StateDirectory,
	[string]$TaskPath = '\'
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
try {
	# Query current task state, not an earlier exported task registration.
	for ($attempt = 0; $attempt -lt 3; $attempt++) {
		$tasks = @(Get-ScheduledTask -TaskPath $TaskPath | Where-Object { $_.TaskName -ceq $TaskName })
		if ($tasks.Count -gt 1) { throw 'Ambiguous task.' }
		$task = if ($tasks.Count) { $tasks[0] } else { $null }
		$info = if ($null -ne $task) { $task | Get-ScheduledTaskInfo } else { $null }
		# These are separate OS queries. A task can start between them.
		# Retry only inconsistent reads; never invoke, stop or change a task.
		if ($null -eq $info -or $info.LastTaskResult -ne 267009 -or [string]$task.State -eq 'Running') { break }
		if ($attempt -lt 2) { Start-Sleep -Milliseconds 100 }
	}
	$receipt = $null
	$statusPath = Join-Path $StateDirectory 'status.json'
	if (Test-Path -LiteralPath $statusPath) {
		$stream = [IO.File]::OpenRead($statusPath)
		try {
			if ($stream.Length -gt 6MB) { throw 'Oversized receipt.' }
			$reader = New-Object IO.StreamReader($stream)
			try { $receipt = $reader.ReadToEnd() | ConvertFrom-Json } finally { $reader.Dispose() }
		} finally { $stream.Dispose() }
	}
	[ordered]@{
		CheckedUtc = [DateTimeOffset]::UtcNow.ToString('O')
		WorkerReachable = $true
		TaskPresent = ($null -ne $task)
		TaskEnabled = ($null -ne $task -and [string]$task.State -ne 'Disabled')
		TaskState = if ($null -ne $task) { [string]$task.State } else { 'Missing' }
		LastTaskResult = if ($null -ne $info) { [long]$info.LastTaskResult } else { -1 }
		AttentionPresent = [bool](Test-Path -LiteralPath (Join-Path $StateDirectory 'attention.json'))
		Scheduler = $receipt
	} | ConvertTo-Json -Depth 40 -Compress
} catch {
	# The independent caller must treat nonzero/no fresh snapshot as an observation failure.
	# Do not print a raw exception containing private paths or receipt contents.
	[Console]::Error.WriteLine('Health snapshot collection failed; inspect task access and completed scheduler files. No collector operation was attempted.')
	exit 3
}
