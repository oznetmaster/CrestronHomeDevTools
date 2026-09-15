# Copyright (c) 2026 Neil Colvin.
# Licensed under the MIT License. See LICENSE in the repository root.
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/Test-DiscoveredCoverage.ps1"
Assert-SameTests @('A','B') @('B','A')
foreach ($invalid in @(@(), @('A'), @('A','A'), @('A','C'))) {
    $rejected = $false
    try { Assert-SameTests @('A','B') $invalid } catch { $rejected = $true }
    if (!$rejected) { throw 'Invalid test coverage was accepted.' }
}
Write-Host 'Coverage guard checks passed.'
