#requires -Version 7.6
# Copyright (c) 2026 Neil Colvin. MIT licensed.
param([Parameter(Mandatory)][string]$ResultsDirectory)
$ErrorActionPreference='Stop'
if(Test-Path -LiteralPath $ResultsDirectory){throw 'Use fresh closeout-test results.'}
$root=[IO.Path]::GetFullPath($ResultsDirectory)
[IO.Directory]::CreateDirectory($root)|Out-Null
. "$PSScriptRoot/WatchSubmissionAutomationWorker.ps1"
$script:removed=$false
function Get-ScheduledTask { param($TaskName,$ErrorAction) if(!$script:removed){$script:task} }
function Export-ScheduledTask { param($TaskName) '<Task>synthetic task definition</Task>' }
function Unregister-ScheduledTask { param($TaskName,$Confirm) $script:removed=$true }
function Get-SubmissionWorkerPrincipalSid([string]$Account) { if($Account -in @('HOST\fixture','fixture')){'S-1-5-21-1-2-3-1001'}else{'S-1-5-21-1-2-3-1002'} }
$results=@()
foreach($scenario in @('complete','review','short-account','failed','terminal-failure','mixed-terminal-failure','failure-plus-active','unknown-outcome','wrong-failure-exit','approval','handoff','empty','mixed','wrong-id','wrong-mode','changed-task','changed-user','changed-runlevel','changed-registry','worker-locked')) {
    $folder=Join-Path $root $scenario;[IO.Directory]::CreateDirectory($folder)|Out-Null
    $registry=Join-Path $folder 'registry.json'
    $entries=@([ordered]@{Profile='fixture';ReleaseId=7;Mode='Rehearsal'})
    $states=@([ordered]@{Profile='fixture';ReleaseId=7;Mode='Rehearsal';State='Completed';Stage='Retain';Reason=$null})
    $workerExit=0
    switch($scenario) {
        review {$states[0].State='NeedsInput';$states[0].Stage='SignReview';$states[0].Reason='rehearsal-ready-for-review'}
        failed {$states[0].State='Failed';$states[0].Reason='synthetic-failure'}
        terminal-failure {$states[0].State='Failed';$states[0].Reason='synthetic-failure';$workerExit=2}
        mixed-terminal-failure {$entries+=@{Profile='other';ReleaseId=8;Mode='Rehearsal'};$states+=@{Profile='other';ReleaseId=8;Mode='Rehearsal';State='Failed';Stage='AppTests';Reason='synthetic-failure'};$workerExit=2}
        failure-plus-active {$states[0].State='Failed';$states[0].Reason='synthetic-failure';$entries+=@{Profile='other';ReleaseId=8;Mode='Rehearsal'};$states+=@{Profile='other';ReleaseId=8;Mode='Rehearsal';State='Running';Stage='AppTests';Reason=$null};$workerExit=2}
        unknown-outcome {$states[0].State='OutcomeUnknown';$states[0].Reason='synthetic-unknown';$workerExit=2}
        wrong-failure-exit {$workerExit=2}
        approval {$states[0].State='NeedsInput';$states[0].Stage='SignReview';$states[0].Reason='exact-approval-required'}
        handoff {$states[0].State='Waiting';$states[0].Stage='SignReview';$states[0].Reason='worker-role-handoff'}
        empty {$states=@();$entries=@()}
        mixed {$entries+=@{Profile='other';ReleaseId=8;Mode='Rehearsal'};$states+=@{Profile='other';ReleaseId=8;Mode='Rehearsal';State='Running';Stage='AppTests';Reason=$null}}
        wrong-id {$states[0].ReleaseId=8}
        wrong-mode {$states[0].Mode='Submit'}
    }
    @{SchemaVersion=1;Entries=$entries}|ConvertTo-Json -Depth 5|Set-Content -LiteralPath $registry
    ConvertTo-Json -InputObject $states -Depth 5|Set-Content -LiteralPath (Join-Path $folder 'worker-status.json')
    [IO.File]::WriteAllText((Join-Path $folder 'worker.lock'),'')
    $launch=[pscustomobject]@{Registry=$registry;RegistrySha256=(Get-FileHash $registry).Hash.ToLowerInvariant();StatusDirectory=$folder;TaskName='CrestronSubmission-Fixture-evidence';UserId='HOST\fixture';UserSid='S-1-5-21-1-2-3-1001'}
    $script:task=[pscustomobject]@{Actions=@([pscustomobject]@{Execute='pwsh.exe';Arguments='exact fixture arguments'});Principal=[pscustomobject]@{UserId='HOST\fixture';RunLevel='Limited'}}
    switch($scenario) {
        short-account {$script:task.Principal.UserId='fixture'}
        changed-task {$script:task.Actions[0].Arguments='another run'}
        changed-user {$script:task.Principal.UserId='HOST\other'}
        changed-runlevel {$script:task.Principal.RunLevel='Highest'}
        changed-registry {Add-Content -LiteralPath $registry -Value ' '}
    }
    $script:removed=$false;$errorText=$null;$held=$null
    try {
        if($scenario -eq 'worker-locked'){$held=[IO.FileStream]::new((Join-Path $folder 'worker.lock'),[IO.FileMode]::Open,[IO.FileAccess]::Write,[IO.FileShare]::None)}
        try {Complete-SubmissionAutomationTask $launch 'exact fixture arguments' 'pwsh.exe' $workerExit} catch {$errorText=$_.Exception.Message}
    } finally {if($held){$held.Dispose()}}
    $expected=$scenario -in @('complete','review','short-account','terminal-failure','mixed-terminal-failure')
    if($script:removed -ne $expected -or ($expected -and $errorText) -or (!$expected -and !$errorText)){throw "Unexpected closeout result: $scenario ($errorText)"}
    $receipt=Join-Path $folder 'worker-task-closeout.json'
    if((Test-Path $receipt) -ne $expected){throw "Incorrect closeout receipt: $scenario"}
    if($expected -and ((Get-Content $receipt -Raw|ConvertFrom-Json).Outcome -ne $(if($workerExit -eq 2){'Failed'}else{'Completed'}))){throw 'Retirement misclassified the retained outcome.'}
    if(!(Test-Path $registry) -or !(Test-Path (Join-Path $folder 'worker-status.json'))){throw 'Evidence was removed.'}
    $results += [ordered]@{Case=$scenario;Passed=$true;TaskRemoved=$script:removed;RetainedReason=$errorText}
}
$results|ConvertTo-Json -Depth 4|Set-Content (Join-Path $root 'results.json')
Write-Host "Verified $($results.Count) worker closeout cases. No real scheduled task was changed."
