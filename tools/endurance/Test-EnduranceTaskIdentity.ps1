#requires -Version 7.6
#requires -PSEdition Core
# In-memory task cmdlets: never registers a Windows task or accesses credentials.
param([Parameter(Mandatory)][string]$ResultsDirectory)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
if (Test-Path $ResultsDirectory) { throw 'Choose a fresh output directory.' }
[void][IO.Directory]::CreateDirectory($ResultsDirectory)
$scripts=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../scripts/endurance'))
$tick=Join-Path $scripts 'Invoke-EnduranceScheduledTick.ps1'
$config=Join-Path $ResultsDirectory 'schedule.json'
@{SchemaVersion=1;ScriptSha256=(Get-FileHash $tick -Algorithm SHA256).Hash} | ConvertTo-Json | Set-Content $config
function Get-ScheduledTask { param($TaskName,$ErrorAction) return $null }
function New-ScheduledTaskPrincipal { param($UserId,$LogonType,$RunLevel) return [pscustomobject]@{UserId=$UserId;LogonType=$LogonType;RunLevel=$RunLevel} }
function New-ScheduledTaskAction { param($Execute,$Argument) return [pscustomobject]@{Execute=$Execute;Argument=$Argument} }
function New-ScheduledTaskTrigger { param([switch]$AtStartup,[switch]$AtLogOn,$User,[switch]$Once,$At,$RepetitionInterval) return [pscustomobject]@{AtStartup=[bool]$AtStartup;AtLogOn=[bool]$AtLogOn;User=$User} }
function New-ScheduledTaskSettingsSet { param($MultipleInstances,[switch]$StartWhenAvailable,$ExecutionTimeLimit,[switch]$AllowStartIfOnBatteries,[switch]$DontStopIfGoingOnBatteries) return @{} }
function Register-ScheduledTask { param($TaskName,$Action,$Trigger,$Settings,$Principal,$Description) $global:EnduranceIdentityTestRegistration=[pscustomobject]@{TaskName=$TaskName;State='PreparedOnly';Action=$Action;Trigger=$Trigger;Principal=$Principal}; return $global:EnduranceIdentityTestRegistration }
foreach ($owner in @($true,$false)) {
	& (Join-Path $scripts 'Register-EnduranceScheduledTask.ps1') -TaskName 'Crestron-Endurance-IdentityTest' -Configuration $config -TickScript $tick -CurrentUser:$owner | Out-Null
	$p=$global:EnduranceIdentityTestRegistration.Principal
	$expected=if($owner){[Security.Principal.WindowsIdentity]::GetCurrent().User.Value}else{'S-1-5-19'}
	if ($p.UserId -ne $expected -or $p.RunLevel -ne 'Limited' -or $p.LogonType -ne $(if($owner){'Interactive'}else{'ServiceAccount'})) { throw 'Incorrect task identity.' }
	if ($global:EnduranceIdentityTestRegistration.Action.Argument -notmatch '-WindowStyle Hidden' -or $global:EnduranceIdentityTestRegistration.Action.Argument -match '--credentials|--password') { throw 'Task visibility or credential argument regression.' }
	if ($owner -and (-not $global:EnduranceIdentityTestRegistration.Trigger[0].AtLogOn -or $global:EnduranceIdentityTestRegistration.Trigger[0].User -ne $expected)) { throw 'Owner task must wait for the selected user login.' }
	if (-not $owner -and -not $global:EnduranceIdentityTestRegistration.Trigger[0].AtStartup) { throw 'Legacy service task changed.' }
}
@{Passed=$true;Cases=2;ActualTasksRegistered=0} | ConvertTo-Json | Tee-Object -FilePath (Join-Path $ResultsDirectory 'result.json')
