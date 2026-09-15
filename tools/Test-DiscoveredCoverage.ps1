# Copyright (c) 2026 Neil Colvin.
# Licensed under the MIT License. See LICENSE in the repository root.
param([string]$Configuration = 'Release', [string]$ResultsDirectory = 'artifacts/tests')
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Assert-SameTests($Expected, $Actual) {
    $left = @($Expected | Sort-Object -CaseSensitive)
    $right = @($Actual | Sort-Object -CaseSensitive)
    if (!$left.Count -or $left.Count -ne $right.Count) { throw 'Empty or incomplete test execution.' }
    for ($index = 0; $index -lt $left.Count; $index++) {
        if ($left[$index] -cne $right[$index]) { throw 'Executed test identities differ from discovery.' }
    }
}
if ($MyInvocation.InvocationName -eq '.') { return }
$project = Join-Path $PSScriptRoot '../CrestronHomeDevTools.Tests/CrestronHomeDevTools.Tests.csproj'
$results = [IO.Path]::GetFullPath($ResultsDirectory)
if (Test-Path $results) { throw 'Use a fresh results directory.' }
[IO.Directory]::CreateDirectory($results) | Out-Null
$output = (& dotnet msbuild $project -nologo "-p:Configuration=$Configuration" '-getProperty:TargetDir') -join "`n"
if ($LASTEXITCODE) { throw 'Cannot resolve test output directory.' }
$started = [DateTime]::UtcNow
& dotnet test $project -c $Configuration --list-tests -- NUnit.DumpXmlTestDiscovery=true
if ($LASTEXITCODE) { throw 'Test discovery failed.' }
$dump = Get-Item (Join-Path $output.Trim() 'Dump/D_CrestronHomeDevTools.Tests.dll.dump')
if ($dump.LastWriteTimeUtc -lt $started) { throw 'Discovery output is stale.' }
Copy-Item -LiteralPath $dump.FullName -Destination (Join-Path $results 'discovery.dump')
$matches = [regex]::Matches([IO.File]::ReadAllText($dump.FullName), '(?s)<test-run\b.*?</test-run>')
if ($matches.Count -ne 1) { throw 'Expected exactly one NUnit discovery tree.' }
$tree = [Xml.XmlDocument]::new()
$tree.XmlResolver = $null
$tree.LoadXml($matches[0].Value)
if ($tree.SelectNodes("//*[@runstate='NotRunnable']").Count) { throw 'Invalid test discovery.' }
$expected = @($tree.SelectNodes('//test-case') | ForEach-Object { $_.GetAttribute('fullname') })
& dotnet test $project -c $Configuration --no-build --logger 'trx;LogFileName=release.trx' --results-directory $results
if ($LASTEXITCODE) { throw 'Tests failed.' }
[xml]$trx = Get-Content (Join-Path $results 'release.trx') -Raw
$definitions = @{}
foreach ($definition in $trx.TestRun.TestDefinitions.UnitTest) { $definitions[$definition.GetAttribute('id')] = $definition }
$actual = @()
foreach ($result in $trx.TestRun.Results.UnitTestResult) {
    if ($result.GetAttribute('outcome') -ne 'Passed') { throw 'A discovered test did not pass.' }
    $definition = $definitions[$result.GetAttribute('testId')]
    if ($null -eq $definition) { throw 'Test result has no matching definition.' }
    $actual += $definition.TestMethod.GetAttribute('className') + '.' + $definition.GetAttribute('name')
}
Assert-SameTests $expected $actual
$counts = $trx.TestRun.ResultSummary.Counters
if ([int]$counts.passed -ne $expected.Count -or [int]$counts.total -ne $expected.Count) { throw 'Result summary differs from discovery.' }
Write-Host "Verified all $($expected.Count) discovered tests passed."
