#requires -Version 7.6
#requires -PSEdition Core
#requires -RunAsAdministrator
# Copyright (c) 2026 Neil Colvin. MIT licensed.
[CmdletBinding()]
param(
 [Parameter(Mandatory)][string]$Emulator,
 [Parameter(Mandatory)][string]$Launcher,
 [Parameter(Mandatory)][ValidatePattern('^[a-f0-9]{64}$')][string]$LauncherSha256,
 [Parameter(Mandatory)][ValidatePattern('^[A-Za-z0-9_.-]{1,80}$')][string]$Avd,
 [Parameter(Mandatory)][string]$LogDirectory,
 [Parameter(Mandatory)][ValidatePattern('^[A-Za-z0-9_-]{1,64}$')][string]$Name,
 [ValidateRange(5554,5682)][int]$Port=5554
)
$ErrorActionPreference='Stop'
foreach($path in @($Emulator,$Launcher,$LogDirectory)) {
 if(-not [IO.Path]::IsPathFullyQualified($path) -or $path.Contains('"') -or $path.Contains("`r") -or $path.Contains("`n")) { throw 'Use reviewed absolute paths.' }
}
if($Port % 2 -or -not (Test-Path -LiteralPath $Emulator -PathType Leaf) -or -not (Test-Path -LiteralPath $Launcher -PathType Leaf) -or (Get-FileHash -LiteralPath $Launcher).Hash.ToLowerInvariant() -ne $LauncherSha256) { throw 'The installed emulator or pinned launcher is invalid.' }
if(-not (Test-Path -LiteralPath (Join-Path $env:USERPROFILE ".android\avd\$Avd.ini") -PathType Leaf)) { throw 'Create the AVD under this Windows account before installing its startup task.' }
$taskName="CrestronSubmission-Android-$Name"
if(Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue) { throw 'An existing fixture task cannot be replaced implicitly.' }
New-Item -ItemType Directory -Path $LogDirectory -Force | Out-Null
$argsText='-NoProfile -NonInteractive -File "{0}" -Emulator "{1}" -Avd {2} -LogDirectory "{3}" -Port {4}' -f $Launcher,$Emulator,$Avd,[IO.Path]::TrimEndingDirectorySeparator($LogDirectory),$Port
$action=New-ScheduledTaskAction -Execute (Join-Path $PSHOME 'pwsh.exe') -Argument $argsText
# S4U runs as the selected AVD owner without a logged-in desktop or saved password.
# It does not provide Windows network credentials; app/processor authentication remains separate.
$principal=New-ScheduledTaskPrincipal -UserId ([Security.Principal.WindowsIdentity]::GetCurrent().Name) -LogonType S4U -RunLevel Limited
$settings=New-ScheduledTaskSettingsSet -StartWhenAvailable -RestartCount 3 -RestartInterval (New-TimeSpan -Minutes 1) -ExecutionTimeLimit ([TimeSpan]::Zero) -MultipleInstances IgnoreNew -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries
Register-ScheduledTask -TaskName $taskName -Action $action -Trigger (New-ScheduledTaskTrigger -AtStartup) -Principal $principal -Settings $settings -Description 'Headless Android test fixture startup; does not configure a Home or operate devices.' | Out-Null
Start-ScheduledTask -TaskName $taskName
Get-ScheduledTaskInfo -TaskName $taskName | Select-Object TaskName,LastTaskResult,LastRunTime
