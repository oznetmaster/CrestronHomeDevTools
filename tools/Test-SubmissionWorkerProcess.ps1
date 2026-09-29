#requires -Version 7.6
# Copyright (c) 2026 Neil Colvin. MIT licensed.
param([Parameter(Mandatory)][string]$ResultsDirectory,[switch]$IncludeScheduledTask,[switch]$ScheduledTaskOnly)
$ErrorActionPreference='Stop'
if(!$IsWindows){throw 'Worker lifetime tests require Windows.'}
if(Test-Path -LiteralPath $ResultsDirectory){throw 'Use a fresh results directory.'}
$root=[IO.Path]::GetFullPath($ResultsDirectory)
[IO.Directory]::CreateDirectory($root)|Out-Null
$helper=Join-Path $PSScriptRoot 'SubmissionWorkerProcess.cs'
Add-Type -Path $helper
$pwsh=Join-Path $PSHOME 'pwsh.exe'
$results=@()
function Encode([string]$Text){[Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($Text))}
function Wait-For([scriptblock]$Condition,[string]$Failure){
    $until=[DateTimeOffset]::UtcNow.AddSeconds(60)
    do{if(& $Condition){return};Start-Sleep -Milliseconds 100}while([DateTimeOffset]::UtcNow -lt $until)
    throw $Failure
}
$modes=@('dispose','forced-parent-stop')
if($IncludeScheduledTask){$modes+='scheduled-task-stop'}
if($ScheduledTaskOnly){$modes=@('scheduled-task-stop')}
foreach($mode in $modes){
    $folder=Join-Path $root $mode;[IO.Directory]::CreateDirectory($folder)|Out-Null
    $pidFile=Join-Path $folder 'children.json'
    $grandchild=Encode 'Start-Sleep -Seconds 120'
    $childBody=@"
`$ErrorActionPreference='Stop'
`$PID|Set-Content -LiteralPath '$($folder.Replace("'","''"))/child-started.txt'
try {
`$p=Start-Process -FilePath '$($pwsh.Replace("'","''"))' -ArgumentList '-NoProfile -NonInteractive -EncodedCommand $grandchild' -WindowStyle Hidden -PassThru
@{Child=`$PID;Grandchild=`$p.Id}|ConvertTo-Json|Set-Content -LiteralPath '$($pidFile.Replace("'","''"))'
Start-Sleep -Seconds 120
} catch {`$_|Out-String|Set-Content -LiteralPath '$($folder.Replace("'","''"))/child-error.txt';exit 1}
"@
    $childBody|Set-Content -LiteralPath (Join-Path $folder 'child.ps1')
    $childArguments='-NoProfile -NonInteractive -File "'+(Join-Path $folder 'child.ps1')+'"'
    $launcherBody=@"
`$ErrorActionPreference='Stop'
`$PID|Set-Content -LiteralPath '$($folder.Replace("'","''"))/launcher.pid'
Add-Type -Path '$($helper.Replace("'","''"))'
`$worker=[CrestronHomeDevTools.WorkerHosting.SubmissionWorkerProcess]::new('$($pwsh.Replace("'","''"))','$($childArguments.Replace("'","''"))','$($folder.Replace("'","''"))')
`$worker.Process.Id|Set-Content -LiteralPath '$($folder.Replace("'","''"))/launched-child.txt'
try {
    if('$mode' -eq 'dispose'){
        while(!(Test-Path -LiteralPath '$($pidFile.Replace("'","''"))')){Start-Sleep -Milliseconds 100}
        Start-Sleep -Seconds 2
    }else{`$worker.Process.WaitForExit();`$worker.Process.ExitCode|Set-Content -LiteralPath '$($folder.Replace("'","''"))/child-exit-code.txt'}
} finally {`$worker.Dispose()}
"@
    $launcherBody|Set-Content -LiteralPath (Join-Path $folder 'launcher.ps1')
    $launcher=Encode $launcherBody
    $parent=$null;$taskName=$null
    $launcherArguments="-NoProfile -NonInteractive -WindowStyle Hidden -EncodedCommand $launcher"
    $children=@()
    try {
        if($mode -eq 'scheduled-task-stop'){
            $taskName='CrestronSubmission-LifetimeProbe-'+[Guid]::NewGuid().ToString('N')
            $action=New-ScheduledTaskAction -Execute $pwsh -Argument $launcherArguments
            $principal=New-ScheduledTaskPrincipal -UserId ([Security.Principal.WindowsIdentity]::GetCurrent().Name) -LogonType Interactive -RunLevel Limited
            Register-ScheduledTask -TaskName $taskName -Action $action -Principal $principal -Settings (New-ScheduledTaskSettingsSet -ExecutionTimeLimit (New-TimeSpan -Minutes 3))|Out-Null
            Export-ScheduledTask -TaskName $taskName|Set-Content (Join-Path $folder 'task.xml')
            Start-ScheduledTask -TaskName $taskName
            Wait-For {Test-Path -LiteralPath (Join-Path $folder 'launcher.pid')} 'Scheduled launcher did not start.'
            $parent=Get-Process -Id ([int](Get-Content (Join-Path $folder 'launcher.pid')))
        }else{
            $parent=Start-Process -FilePath $pwsh -ArgumentList $launcherArguments -WindowStyle Hidden -PassThru -RedirectStandardError (Join-Path $folder 'launcher.stderr.txt')
        }
        Wait-For {Test-Path -LiteralPath $pidFile} 'Fixture did not start both children.'
        $ids=Get-Content -LiteralPath $pidFile -Raw|ConvertFrom-Json
        $children=@(Get-Process -Id $ids.Child,$ids.Grandchild -ErrorAction Stop)
        if($mode -eq 'forced-parent-stop'){Stop-Process -InputObject $parent -Force}
        if($mode -eq 'scheduled-task-stop'){Stop-ScheduledTask -TaskName $taskName}
        Wait-For {$parent.HasExited} 'Launcher remained running.'
        Wait-For {@($children|Where-Object {!$_.HasExited}).Count -eq 0} 'Worker or descendant survived its launcher.'
        $results+=@{Case=$mode;Passed=$true;Parent=$parent.Id;Child=$ids.Child;Grandchild=$ids.Grandchild}
    } finally {
        # Exact process handles from this fixture only; retained results are never deleted.
        foreach($p in @($parent)+$children){if($p){if(!$p.HasExited){Stop-Process -InputObject $p -Force};$p.Dispose()}}
        if($taskName){
            $registered=Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
            if($registered){
                if(@($registered.Actions).Count -ne 1 -or $registered.Actions[0].Arguments -cne $launcherArguments){throw 'Synthetic task changed; inspect it before cleanup.'}
                Stop-ScheduledTask -TaskName $taskName
                Unregister-ScheduledTask -TaskName $taskName -Confirm:$false
                if(Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue){throw 'Synthetic task was not removed.'}
            }
        }
    }
}
$hosted=[CrestronHomeDevTools.WorkerHosting.SubmissionWorkerProcess]::new($pwsh,'-NoProfile -NonInteractive -Command "exit 37"',$root)
try {$hosted.Process.WaitForExit();if($hosted.Process.ExitCode -ne 37){throw 'Worker exit code lost.'}}finally{$hosted.Dispose()}
$results+=@{Case='exit-code';Passed=$true}
$rejected=$false
try {$invalid=[CrestronHomeDevTools.WorkerHosting.SubmissionWorkerProcess]::new((Join-Path $root 'missing.exe'),'',$root);$invalid.Dispose()}catch{$rejected=$true}
if(!$rejected){throw 'Invalid worker launch was accepted.'}
$results+=@{Case='invalid-launch';Passed=$true}
$results|ConvertTo-Json -Depth 4|Set-Content -LiteralPath (Join-Path $root 'results.json')
Write-Host "Verified $($results.Count) real worker process lifetime cases. No devices used; any opted-in synthetic task was archived and removed."
