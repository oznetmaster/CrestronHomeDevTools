#requires -Version 7.6
#requires -PSEdition Core
#requires -RunAsAdministrator
# Copyright (c) 2026 Neil Colvin. MIT licensed.
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Executable,
    [Parameter(Mandatory)][string]$Registry,
    [Parameter(Mandatory)][string]$StatusDirectory,
    [Parameter(Mandatory)][ValidatePattern('^[A-Za-z0-9_-]{1,64}$')][string]$Name,
    [ValidateSet('evidence','protected')][string]$Role = 'evidence',
    [ValidateSet('NT AUTHORITY\LOCAL SERVICE','NT AUTHORITY\NETWORK SERVICE')][string]$Account = 'NT AUTHORITY\LOCAL SERVICE',
    [ValidateRange(30,900)][int]$PollSeconds = 60,
    [string]$ReleaseProfiles,
    [string]$ProtectedWorker,
    [ValidatePattern('^[a-f0-9]{64}$')][string]$ProtectedWorkerSha256
)
$ErrorActionPreference = 'Stop'
foreach ($path in @($Executable, $Registry, $StatusDirectory)) {
    if (-not [IO.Path]::IsPathFullyQualified($path) -or $path.Contains('"') -or $path.Contains("`r") -or $path.Contains("`n")) { throw 'Use absolute reviewed paths.' }
}
if ($ReleaseProfiles) {
    if ($Role -ne 'evidence' -or -not [IO.Path]::IsPathFullyQualified($ReleaseProfiles) -or $ReleaseProfiles.Contains('"') -or $ReleaseProfiles.Contains("`r") -or $ReleaseProfiles.Contains("`n") -or -not (Test-Path -LiteralPath $ReleaseProfiles -PathType Leaf)) { throw 'Release profiles require an evidence worker and an absolute private file.' }
}
if ($Role -eq 'protected') {
    if (-not $ProtectedWorker -or -not $ProtectedWorkerSha256 -or -not [IO.Path]::IsPathFullyQualified($ProtectedWorker) -or $ProtectedWorker.Contains('"') -or $ProtectedWorker.Contains("`r") -or $ProtectedWorker.Contains("`n") -or -not (Test-Path -LiteralPath $ProtectedWorker -PathType Leaf) -or (Get-FileHash -LiteralPath $ProtectedWorker).Hash.ToLowerInvariant() -ne $ProtectedWorkerSha256) { throw 'Supply the independently reviewed protected-worker configuration and digest.' }
} elseif ($ProtectedWorker -or $ProtectedWorkerSha256) { throw 'Evidence workers cannot use protected-worker configuration.' }
if (-not (Test-Path -LiteralPath $Executable -PathType Leaf) -or -not (Test-Path -LiteralPath $Registry -PathType Leaf) -or -not (Test-Path -LiteralPath $StatusDirectory -PathType Container)) { throw 'Provision the trusted executable, private registry and service-writable status directory first.' }
$taskName = "CrestronSubmission-$Name-$Role"
if (Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue) { throw 'The task already exists. Inspect its recorded configuration before updating it.' }
# Credentials are provisioned separately for this service identity; no passwords are stored in task arguments.
# Trim only a trailing separator so CommandLineToArgvW cannot interpret a closing quote as escaped.
$statusPath = [IO.Path]::TrimEndingDirectorySeparator($StatusDirectory)
$arguments = '--watch-registry "{0}" --status-directory "{1}" --poll-seconds {2}' -f $Registry, $statusPath, $PollSeconds
if ($ReleaseProfiles) { $arguments += ' --release-profiles "{0}"' -f $ReleaseProfiles }
if ($ProtectedWorker) { $arguments += ' --protected-worker "{0}" --protected-worker-sha256 {1}' -f $ProtectedWorker, $ProtectedWorkerSha256 }
$arguments += ' --role {0}' -f $Role
$action = New-ScheduledTaskAction -Execute $Executable -Argument $arguments -WorkingDirectory (Split-Path $Executable -Parent)
$trigger = New-ScheduledTaskTrigger -AtStartup
$principal = New-ScheduledTaskPrincipal -UserId $Account -LogonType ServiceAccount -RunLevel Limited
$settings = New-ScheduledTaskSettingsSet -StartWhenAvailable -RestartCount 3 -RestartInterval (New-TimeSpan -Minutes 1) -ExecutionTimeLimit ([TimeSpan]::Zero) -MultipleInstances IgnoreNew -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries
Register-ScheduledTask -TaskName $taskName -Action $action -Trigger $trigger -Principal $principal -Settings $settings -Description 'Resume registered Crestron submission workflows using public tools; exact signing/delivery authority remains separate.' | Out-Null
Start-ScheduledTask -TaskName $taskName
Get-ScheduledTaskInfo -TaskName $taskName | Select-Object TaskName, LastRunTime, LastTaskResult
