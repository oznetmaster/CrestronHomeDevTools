#requires -Version 7.6
#requires -PSEdition Core
# Copyright (c) 2026 Neil Colvin. MIT licensed.
[CmdletBinding()]
param(
 [Parameter(Mandatory)][string]$Executable,
 [Parameter(Mandatory)][string]$Inbox,
 [Parameter(Mandatory)][ValidatePattern('^[a-f0-9]{64}$')][string]$RunKey,
 [Parameter(Mandatory)][ValidatePattern('^CrestronSubmission-[A-Za-z0-9_-]{1,64}-operator$')][string]$TaskName
)
$ErrorActionPreference='Stop'
foreach($path in @($Executable,$Inbox)) {
 if(-not [IO.Path]::IsPathFullyQualified($path) -or $path.Contains('"') -or $path.Contains("`r") -or $path.Contains("`n")) {throw 'Invalid reviewed path.'}
}
$arguments='--operator-inbox "{0}" --run-key {1}' -f [IO.Path]::TrimEndingDirectorySeparator($Inbox),$RunKey
$process=Start-Process -FilePath $Executable -ArgumentList $arguments -WindowStyle Hidden -PassThru -Wait
if($process.ExitCode -ne 0) {exit $process.ExitCode}
# Exit 0 is emitted by the inbox only after it validates the matching completion record.
# Verify the task still names this exact watcher and run before unregistering it.
$task=Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
if($task) {
 if(@($task.Actions).Count -ne 1 -or -not $task.Actions[0].Arguments.Contains(('"{0}"' -f $PSCommandPath)) -or
  -not $task.Actions[0].Arguments.Contains($RunKey)) {throw 'Task changed; automatic cleanup refused.'}
 Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false
}
