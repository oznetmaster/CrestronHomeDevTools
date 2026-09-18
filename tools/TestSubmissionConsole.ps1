# Copyright (c) 2026 Neil Colvin. Licensed under the MIT License; see LICENSE.
[CmdletBinding()]
param([Parameter(Mandatory)][string] $ConsoleDirectory)
$ErrorActionPreference = 'Stop'
$directory = [IO.Path]::GetFullPath($ConsoleDirectory)
$console = Join-Path $directory 'CrestronHomeDevTools.Console.exe'
& $console submission runtime-check
if ($LASTEXITCODE -ne 0) { throw 'Submission console integrity check failed.' }
dotnet build (Join-Path $PSScriptRoot '../CrestronHomeDevTools.Tests.Probe/CrestronHomeDevTools.Tests.Probe.csproj') -c Release --verbosity quiet
if ($LASTEXITCODE -ne 0) { throw 'Synthetic delivery probe build failed.' }
$names = @('SUBMISSION_TEST_BUNDLE', 'SUBMISSION_TEST_DOTNET', 'SUBMISSION_TEST_VALIDATOR', 'SUBMISSION_TEST_PROBE')
$previous = @{}
foreach ($name in $names) { $previous[$name] = [Environment]::GetEnvironmentVariable($name) }
try {
    $env:SUBMISSION_TEST_BUNDLE = $console
    $env:SUBMISSION_TEST_DOTNET = (Get-Command dotnet -CommandType Application).Source
    $env:SUBMISSION_TEST_VALIDATOR = Join-Path $directory 'CrestronHomeDevTools.Console.dll'
    $env:SUBMISSION_TEST_PROBE = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../CrestronHomeDevTools.Tests.Probe/bin/Release/net10.0/CrestronHomeDevTools.Tests.Probe.dll'))
    & (Join-Path $directory 'submission-tools/runtime/python.exe') -I -B -X utf8 (Join-Path $PSScriptRoot 'submission/run_bundle_tests.py')
    if ($LASTEXITCODE -ne 0) { throw 'Packaged submission console acceptance failed.' }
} finally {
    foreach ($name in $names) { [Environment]::SetEnvironmentVariable($name, $previous[$name]) }
}
