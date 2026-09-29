#requires -Version 7.6
#requires -PSEdition Core
# Copyright (c) 2026 Neil Colvin. MIT licensed.
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Executable,
    [Parameter(Mandatory)][string]$Registry,
    [Parameter(Mandatory)][string[]]$Profiles,
    [Parameter(Mandatory)][ValidatePattern('^[A-Za-z0-9_-]{1,64}$')][string]$Name,
    [string]$WorkerStatusDirectory
)
$ErrorActionPreference = 'Stop'
foreach ($path in @($Executable, $Registry)) {
    if (-not [IO.Path]::IsPathFullyQualified($path) -or $path.Contains('"') -or $path.Contains("`r") -or $path.Contains("`n")) {
        throw 'Use reviewed absolute paths without quotes or line breaks.'
    }
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw 'Provision the trusted setup executable and private registry first.' }
}
if ($Profiles.Count -lt 1 -or $Profiles.Count -gt 100 -or @($Profiles | Where-Object { $_ -cnotmatch '^[A-Za-z0-9_-]{1,64}$' }).Count -or @($Profiles | Sort-Object -Unique -CaseSensitive).Count -ne $Profiles.Count) {
    throw 'Select distinct trusted profile names.'
}
$taskName = "CrestronSubmission-$Name-operator-listener"
if (Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue) { throw 'An operator listener task already exists. Inspect it before updating.' }
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$arguments = '--operator-registry "{0}" --profiles {1}' -f $Registry, ($Profiles -join ',')
if($WorkerStatusDirectory) {
    if(![IO.Path]::IsPathFullyQualified($WorkerStatusDirectory) -or $WorkerStatusDirectory.Contains('"') -or $WorkerStatusDirectory.Contains("`r") -or $WorkerStatusDirectory.Contains("`n") -or !(Test-Path -LiteralPath $WorkerStatusDirectory -PathType Container)){throw 'Provision the private worker status directory and use an absolute path.'}
    $arguments += ' --worker-status "{0}"' -f [IO.Path]::TrimEndingDirectorySeparator($WorkerStatusDirectory)
}
$action = New-ScheduledTaskAction -Execute $Executable -Argument $arguments -WorkingDirectory (Split-Path $Executable -Parent)
$trigger = New-ScheduledTaskTrigger -AtLogOn -User $identity.Name
$principal = New-ScheduledTaskPrincipal -UserId $identity.Name -LogonType Interactive -RunLevel Limited
# A persistent WinExe, not a per-run task. It stays idle between releases and creates no console.
# It reads only the selected trusted profiles; physical responses confer no signing/delivery authority.
$settings = New-ScheduledTaskSettingsSet -StartWhenAvailable -MultipleInstances IgnoreNew -ExecutionTimeLimit ([TimeSpan]::Zero) -RestartCount 3 -RestartInterval (New-TimeSpan -Minutes 1) -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries
Register-ScheduledTask -TaskName $taskName -Action $action -Trigger $trigger -Principal $principal -Settings $settings -Description 'Present physical test actions for future releases of selected trusted profiles. Does not approve signing or delivery.' | Out-Null
Start-ScheduledTask -TaskName $taskName
Get-ScheduledTaskInfo -TaskName $taskName | Select-Object TaskName, LastRunTime, LastTaskResult
