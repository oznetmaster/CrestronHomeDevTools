# Copyright (c) 2026 Neil Colvin.
# Licensed under the MIT License. See LICENSE in the repository root.
param(
	[Parameter(Mandatory)][string]$TaskName,
	[Parameter(Mandatory)][string]$Configuration,
	[Parameter(Mandatory)][string]$TickScript
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if ($TaskName -notmatch '^Crestron-Endurance-[A-Za-z0-9_-]{1,80}$') { throw 'Use a unique Crestron-Endurance- task name.' }
if (Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue) { throw 'The task already exists; inspect it instead of replacing it.' }
$Configuration = (Resolve-Path -LiteralPath $Configuration).Path
$TickScript = (Resolve-Path -LiteralPath $TickScript).Path
foreach ($path in @($Configuration, $TickScript)) {
	if ($path.Contains('"') -or $path.Contains("`r") -or $path.Contains("`n") -or $path.StartsWith('\\')) { throw 'Use absolute local file paths.' }
}
$config = Get-Content -LiteralPath $Configuration -Raw | ConvertFrom-Json
if ($config.SchemaVersion -ne 1 -or (Get-FileHash -LiteralPath $TickScript -Algorithm SHA256).Hash -ne $config.ScriptSha256) { throw 'Tick script does not match the reviewed configuration.' }
# ACL provisioning is separate: LocalService reads protected inputs and writes only private state/run directories.
$principal = New-ScheduledTaskPrincipal -UserId 'S-1-5-19' -LogonType ServiceAccount -RunLevel Limited
$action = New-ScheduledTaskAction -Execute "$env:SystemRoot\System32\WindowsPowerShell\v1.0\powershell.exe" -Argument (
	'-NoProfile -NonInteractive -ExecutionPolicy Bypass -File "' + $TickScript + '" -Configuration "' + $Configuration + '"')
$triggers = @(
	New-ScheduledTaskTrigger -AtStartup
	New-ScheduledTaskTrigger -Once -At (Get-Date).AddMinutes(1) -RepetitionInterval (New-TimeSpan -Minutes 1)
)
$settings = New-ScheduledTaskSettingsSet -MultipleInstances IgnoreNew -StartWhenAvailable -ExecutionTimeLimit ([TimeSpan]::Zero) -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries
Register-ScheduledTask -TaskName $TaskName -Action $action -Trigger $triggers -Settings $settings -Principal $principal -Description 'Read-only endurance observations for an explicitly started, pinned run. Never starts a new endurance period.' | Select-Object TaskName, State