#requires -Version 7.6
#requires -PSEdition Core
# Copyright (c) 2026 Neil Colvin. MIT licensed.
param([string]$Configuration, [string]$ConfigurationSha256)
$ErrorActionPreference = 'Stop'

function Get-SubmissionWorkerPrincipalSid([string]$Account) {
    ([Security.Principal.NTAccount]::new($Account)).Translate([Security.Principal.SecurityIdentifier]).Value
}

function Complete-SubmissionAutomationTask($Launch, [string]$ExpectedArguments, [string]$ExpectedPowerShell) {
    if ((Get-FileHash -LiteralPath $Launch.Registry).Hash.ToLowerInvariant() -ne $Launch.RegistrySha256) {
        throw 'Registry changed; automatic task retirement refused.'
    }
    $registry = Get-Content -LiteralPath $Launch.Registry -Raw | ConvertFrom-Json
    $statusFile = Join-Path $Launch.StatusDirectory 'worker-status.json'
    $states = @(Get-Content -LiteralPath $statusFile -Raw | ConvertFrom-Json)
    $entries = @($registry.Entries)
    if (!$entries.Count -or $states.Count -ne $entries.Count) { throw 'Incomplete terminal status; task retained.' }
    foreach ($entry in $entries) {
        $matches = @($states | Where-Object { $_.Profile -ceq $entry.Profile -and $_.ReleaseId -eq $entry.ReleaseId -and $_.Mode -ceq $entry.Mode })
        if ($matches.Count -ne 1) { throw 'Terminal status identity differs; task retained.' }
        $state = $matches[0]
        $finished = ($state.State -ceq 'Completed' -and $state.Stage -ceq 'Retain') -or
            ($state.Mode -ceq 'Rehearsal' -and $state.State -ceq 'NeedsInput' -and $state.Stage -ceq 'SignReview' -and $state.Reason -ceq 'rehearsal-ready-for-review')
        if (!$finished) { throw 'Run is not finished; task retained.' }
    }
    $task = Get-ScheduledTask -TaskName $Launch.TaskName -ErrorAction SilentlyContinue
    if (!$task) { throw 'Scheduled task is missing; inspect closeout before retrying.' }
    if (@($task.Actions).Count -ne 1 -or $task.Actions[0].Execute -ine $ExpectedPowerShell -or
        $task.Actions[0].Arguments -cne $ExpectedArguments -or (Get-SubmissionWorkerPrincipalSid $task.Principal.UserId) -ne $Launch.UserSid -or
        $task.Principal.RunLevel.ToString() -ne 'Limited') { throw 'Task changed; automatic retirement refused.' }
    # The child has exited. Exclude another worker while preserving its terminal evidence.
    $gate = [IO.FileStream]::new((Join-Path $Launch.StatusDirectory 'worker.lock'), [IO.FileMode]::Open, [IO.FileAccess]::Write, [IO.FileShare]::None)
    try {
        $archive = Join-Path $Launch.StatusDirectory 'worker-task-closeout.xml'
        Export-ScheduledTask -TaskName $Launch.TaskName | Set-Content -LiteralPath $archive
        Unregister-ScheduledTask -TaskName $Launch.TaskName -Confirm:$false
        if (Get-ScheduledTask -TaskName $Launch.TaskName -ErrorAction SilentlyContinue) { throw 'Task retirement was not confirmed.' }
        [ordered]@{ CompletedUtc=[DateTimeOffset]::UtcNow; TaskName=$Launch.TaskName; RegistrySha256=$Launch.RegistrySha256;
            StatusSha256=(Get-FileHash -LiteralPath $statusFile).Hash.ToLowerInvariant(); TaskRemoved=$true; EvidencePreserved=$true } |
            ConvertTo-Json | Set-Content -LiteralPath (Join-Path $Launch.StatusDirectory 'worker-task-closeout.json')
    } finally { $gate.Dispose() }
}

if ($MyInvocation.InvocationName -eq '.') { return }
if (![IO.Path]::IsPathFullyQualified($Configuration) -or $Configuration.Contains('"') -or
    $Configuration.Contains("`r") -or $Configuration.Contains("`n") -or $ConfigurationSha256 -cnotmatch '^[a-f0-9]{64}$' -or
    (Get-FileHash -LiteralPath $Configuration).Hash.ToLowerInvariant() -ne $ConfigurationSha256) { throw 'Invalid or changed worker launch configuration.' }
$launch = Get-Content -LiteralPath $Configuration -Raw | ConvertFrom-Json
if ($launch.SchemaVersion -ne 1 -or $launch.TaskName -cnotmatch '^CrestronSubmission-[A-Za-z0-9_-]{1,64}-(evidence|protected)$' -or
    $launch.UserId -ine [Security.Principal.WindowsIdentity]::GetCurrent().Name -or
    !$launch.Arguments.Contains(' --exit-when-finished') -or $launch.Arguments.Contains('--release-profiles')) { throw 'Invalid finite owner-worker configuration.' }
$arguments = '-NoProfile -NonInteractive -WindowStyle Hidden -File "{0}" -Configuration "{1}" -ConfigurationSha256 {2}' -f $PSCommandPath, $Configuration, $ConfigurationSha256
try {
    $process = Start-Process -FilePath $launch.Executable -ArgumentList $launch.Arguments -WorkingDirectory (Split-Path $launch.Executable -Parent) -WindowStyle Hidden -Wait -PassThru
    if ($process.ExitCode -ne 0) { throw "Worker exited with code $($process.ExitCode); no automatic retirement." }
    Complete-SubmissionAutomationTask $launch $arguments (Join-Path $PSHOME 'pwsh.exe')
} catch {
    [ordered]@{ObservedUtc=[DateTimeOffset]::UtcNow;TaskName=$launch.TaskName;Error=$_.Exception.Message;EvidencePreserved=$true} |
        ConvertTo-Json | Set-Content -LiteralPath (Join-Path $launch.StatusDirectory 'worker-task-closeout-error.json')
    throw
}
