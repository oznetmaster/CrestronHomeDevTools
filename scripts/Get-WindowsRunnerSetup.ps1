#requires -Version 7.6
#requires -PSEdition Core
# Copyright (c) 2026 Neil Colvin. Licensed under the MIT License; see LICENSE.
$ErrorActionPreference = 'Stop'
$inputData = [Console]::In.ReadToEnd() | ConvertFrom-Json
$plan = $inputData.Plan
$serviceName = $inputData.ServiceName
$service = Get-CimInstance Win32_Service -Filter "Name='$serviceName'"
$registrationMatches = $false
$serviceMatches = $false
$root = [IO.Path]::GetFullPath($plan.InstallDirectory)
$registrationPath = Join-Path $root '.runner'
if (Test-Path -LiteralPath $registrationPath) {
    $registration = Get-Content -LiteralPath $registrationPath -Raw | ConvertFrom-Json
    $registrationMatches = $registration.AgentName -ceq $plan.RunnerName -and $registration.GitHubUrl.TrimEnd('/') -ieq $plan.GitHubUrl -and $registration.WorkFolder -eq '_work'
}
if ($service) {
    $expected = [IO.Path]::GetFullPath((Join-Path $root 'bin/RunnerService.exe'))
    $expectedSid = ([Security.Principal.NTAccount]::new($plan.ServiceAccount)).Translate([Security.Principal.SecurityIdentifier]).Value
    $actualSid = ([Security.Principal.NTAccount]::new($service.StartName)).Translate([Security.Principal.SecurityIdentifier]).Value
    $serviceMatches = $service.PathName.Trim('"') -ieq $expected -and $actualSid -eq $expectedSid
}
[ordered]@{
    DirectoryExists = [bool](Test-Path -LiteralPath $root)
    RegistrationMatches = [bool]$registrationMatches
    ServiceMatches = [bool]$serviceMatches
    ServiceRunning = [bool]($service -and $service.State -eq 'Running')
    ServiceAutomatic = [bool]($service -and $service.StartMode -eq 'Auto')
    ConflictingService = [bool]($service -and -not ($registrationMatches -and $serviceMatches))
} | ConvertTo-Json -Compress
