#requires -Version 7.6
#requires -PSEdition Core
# Copyright (c) 2026 Neil Colvin. Licensed under the MIT License; see LICENSE.
param([Parameter(Mandatory)][string] $Version)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Push-Location $root
try {
    $release = Join-Path $root 'artifacts/release'
    if (Test-Path $release) { throw 'Use fresh release staging.' }
    & (Join-Path $PSHOME 'pwsh.exe') -NoProfile -File ./tools/endurance/Test-EnduranceDirectoryPermissions.ps1 -ResultsDirectory artifacts/directory-permissions-tests
    if ($LASTEXITCODE -ne 0) { throw 'Monitoring directory permission checks failed.' }
    & ./tools/endurance/Test-EnduranceScheduler.ps1 -ResultsDirectory artifacts/scheduler-tests
    & ./tools/endurance/Test-EnduranceWatchScheduler.ps1 -ResultsDirectory artifacts/watch-scheduler-tests
    & ./tools/endurance/Test-EnduranceSnapshot.ps1 -ResultsDirectory artifacts/endurance-snapshot-tests
    & (Join-Path $PSHOME 'pwsh.exe') -NoProfile -File ./tools/endurance/Test-EnduranceHealthSnapshot.ps1 -ResultsDirectory artifacts/endurance-health-tests
    if ($LASTEXITCODE -ne 0) { throw 'Passive health snapshot checks failed.' }
    & ./tools/Test-DiscoveredCoverageGuards.ps1
    & ./tools/Test-DiscoveredCoverage.ps1 -Configuration Release -ResultsDirectory artifacts/tests
    dotnet pack CrestronHomeDevTools/CrestronHomeDevTools.csproj -c Release "-p:Version=$Version" -o $release
    if ($LASTEXITCODE -ne 0) { throw 'Library pack failed.' }
    $console = Join-Path $root ('artifacts/console-' + [Guid]::NewGuid().ToString('N'))
    & ./tools/BuildSubmissionConsole.ps1 -OutputDirectory $console -Version $Version
    & ./tools/TestSubmissionConsole.ps1 -ConsoleDirectory $console
    & (Join-Path $console 'CrestronHomeDevTools.Console.exe') --help
    if ($LASTEXITCODE -ne 0) { throw 'Console smoke test failed.' }
    [IO.Compression.ZipFile]::CreateFromDirectory($console, (Join-Path $release 'CrestronHomeDevTools.Console-win-x64.zip'))
    foreach ($file in Get-ChildItem $release -File) {
        $zip = [IO.Compression.ZipFile]::OpenRead($file.FullName)
        try {
            if (@($zip.Entries | Where-Object FullName -Match '(?i)(\.profile$|\.local\.json$|LiveTestSettings\.json$|\.csproj\.user$|\.Local\.targets$|\.pfx$|(^|/)(TestResults|obj|bin)/)').Count) { throw "Private file in $($file.Name)." }
            foreach ($required in @('README.md','LICENSE','DEVELOPMENT-HISTORY.md','CHANGELOG.md','RELEASE-NOTES.md','THIRD-PARTY-NOTICES.md','docs/ProtocolReference.md','docs/DriverConfiguration.md','docs/RoomMoves.md','docs/ManagedChildValidation.md','docs/submission/EnduranceCollection.md','docs/submission/FormSigning.md','docs/submission/SigningStage.md','docs/submission/DeliveryPreparation.md')) {
                if (-not ($zip.Entries | Where-Object FullName -EQ $required)) { throw "Missing $required in $($file.Name)." }
            }
            foreach ($document in Get-ChildItem (Join-Path $root 'docs') -File -Recurse) {
                $relative = [IO.Path]::GetRelativePath($root, $document.FullName).Replace('\', '/')
                if (@($zip.Entries | Where-Object FullName -CEQ $relative).Count -ne 1) { throw "Missing or duplicated document $relative in $($file.Name)." }
            }
            if ($file.Extension -eq '.zip') {
                foreach ($name in @('setup/CrestronHomeDevTools.Setup.exe','automation/CrestronHomeDevTools.Automation.exe','scripts/automation/InstallSubmissionAutomationWorker.ps1','scripts/automation/InstallSubmissionAndroidFixture.ps1','scripts/automation/RunAndroidFixture.ps1')) {
                    if (@($zip.Entries | Where-Object FullName -CEQ $name).Count -ne 1) { throw "Missing automation component $name." }
                }
                foreach ($name in @('Set-EnduranceDirectoryPermissions.ps1','Invoke-EnduranceScheduledTick.ps1','New-EnduranceScheduleConfiguration.ps1','Register-EnduranceScheduledTask.ps1','Export-EnduranceScheduledRun.ps1','Get-EnduranceHealthSnapshot.ps1','New-EnduranceWatchConfiguration.ps1','Invoke-EnduranceScheduledWatch.ps1')) {
                    if (@($zip.Entries | Where-Object FullName -CEQ ('scripts/endurance/' + $name)).Count -ne 1) { throw "Missing scheduled-worker script $name." }
                }
            }
            if ($file.Extension -eq '.nupkg') {
                $entry = $zip.Entries | Where-Object FullName -Like '*.nuspec' | Select-Object -First 1
                $reader = [IO.StreamReader]::new($entry.Open())
                try { [xml]$spec = $reader.ReadToEnd() } finally { $reader.Dispose() }
                if ($spec.package.metadata.id -ne 'CrestronHomeDevTools' -or $spec.package.metadata.version -ne $Version) { throw 'Unexpected NuGet identity.' }
            }
        } finally { $zip.Dispose() }
    }
    $lines = @(Get-ChildItem $release -File | Sort-Object Name | ForEach-Object { (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + $_.Name })
    [IO.File]::WriteAllLines((Join-Path $release 'SHA256SUMS.txt'),$lines)
} finally { Pop-Location }
