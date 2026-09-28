#requires -Version 7.6
#requires -PSEdition Core
# Copyright (c) 2026 Neil Colvin. MIT licensed.
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Executable,
    [Parameter(Mandatory)][string]$Inbox,
    [Parameter(Mandatory)][ValidatePattern('^[a-f0-9]{64}$')][string]$RunKey,
    [Parameter(Mandatory)][ValidatePattern('^[A-Za-z0-9_-]{1,64}$')][string]$Name
)
$ErrorActionPreference = 'Stop'
foreach ($path in @($Executable, $Inbox)) {
    if (-not [IO.Path]::IsPathFullyQualified($path) -or $path.Contains('"') -or $path.Contains("`r") -or $path.Contains("`n")) {
        throw 'Use reviewed absolute paths without quotes or line breaks.'
    }
}
if (-not (Test-Path -LiteralPath $Executable -PathType Leaf) -or -not (Test-Path -LiteralPath $Inbox -PathType Container)) {
    throw 'Provision the trusted setup executable and private shared inbox first.'
}
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$taskName = "CrestronSubmission-$Name-operator"
if (Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue) { throw 'An action inbox task already exists. Inspect it before updating.' }
$inboxPath = [IO.Path]::TrimEndingDirectorySeparator($Inbox)
$watcher = Join-Path $PSScriptRoot 'WatchSubmissionOperatorInbox.ps1'
if (-not (Test-Path -LiteralPath $watcher -PathType Leaf)) { throw 'The operator watcher must accompany this installer.' }
$arguments = '-NoProfile -NonInteractive -WindowStyle Hidden -File "{0}" -Executable "{1}" -Inbox "{2}" -RunKey {3} -TaskName {4}' -f $watcher, $Executable, $inboxPath, $RunKey, $taskName
$action = New-ScheduledTaskAction -Execute (Join-Path $PSHOME 'pwsh.exe') -Argument $arguments -WorkingDirectory (Split-Path $Executable -Parent)
$trigger = New-ScheduledTaskTrigger -AtLogOn -User $identity.Name
$principal = New-ScheduledTaskPrincipal -UserId $identity.Name -LogonType Interactive -RunLevel Limited
# WinExe: no console appears. Only pending physical requests create a visible window.
# No service credentials, signature, upload or email authority are provisioned here.
$settings = New-ScheduledTaskSettingsSet -StartWhenAvailable -MultipleInstances IgnoreNew -ExecutionTimeLimit (New-TimeSpan -Hours 48) -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries
Register-ScheduledTask -TaskName $taskName -Action $action -Trigger $trigger -Principal $principal -Settings $settings -Description 'Present physical test actions for one retained submission run; does not approve signing or delivery.' | Out-Null
Start-ScheduledTask -TaskName $taskName
Get-ScheduledTaskInfo -TaskName $taskName | Select-Object TaskName, LastRunTime, LastTaskResult
