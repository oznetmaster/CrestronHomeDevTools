# Copyright (c) 2026 Neil Colvin.
# Licensed under the MIT License. See LICENSE in the repository root.
[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$WorkDirectory)
$ErrorActionPreference = 'Stop'
$WarningPreference = 'SilentlyContinue'
try {
    $folder = Get-Item -LiteralPath $WorkDirectory
    if (-not $folder.PSIsContainer -or $folder.PSProvider.Name -ne 'FileSystem') { throw 'Choose an existing local directory.' }
    $drive = New-Object System.IO.DriveInfo([System.IO.Path]::GetPathRoot($folder.FullName))
    $os = Get-CimInstance Win32_OperatingSystem -OperationTimeoutSec 10
    $computer = Get-CimInstance Win32_ComputerSystem -OperationTimeoutSec 10
    $cpus = @(Get-CimInstance Win32_Processor -OperationTimeoutSec 10)
    $sdkRoots = @((Join-Path $env:ProgramFiles 'dotnet\sdk'))
    if ($env:DOTNET_ROOT) { $sdkRoots += Join-Path $env:DOTNET_ROOT 'sdk' }
    $sdks = @($sdkRoots | Where-Object { Test-Path -LiteralPath $_ } | ForEach-Object {
        Get-ChildItem -LiteralPath $_ -Directory | Where-Object { $_.Name -match '^\d+\.\d+\.\d+(-[^\s]+)?$' } | Select-Object -ExpandProperty Name
    } | Sort-Object -Unique)
    $services = @(Get-Service | Where-Object { $_.Name -eq 'sshd' -or $_.Name -like 'actions.runner.*' } | ForEach-Object {
        [ordered]@{ Name=$_.Name; Status=$_.Status.ToString(); StartType=$_.StartType.ToString() }
    })
    $firmwareVirtualization = if ($computer.HypervisorPresent) { $true } elseif ($cpus.Count -gt 0 -and @($cpus | Where-Object { $null -eq $_.VirtualizationFirmwareEnabled }).Count -eq 0) {
        @($cpus | Where-Object { $_.VirtualizationFirmwareEnabled -ne $true }).Count -eq 0
    } else { $null }
    [ordered]@{
        MachineName=$env:COMPUTERNAME
        ObservedUtc=[DateTimeOffset]::UtcNow.ToString('o')
        OperatingSystem=$os.Caption
        ProcessorModels=@($cpus | Select-Object -ExpandProperty Name)
        LogicalProcessors=[int]$computer.NumberOfLogicalProcessors
        TotalMemoryMiB=[long][math]::Floor($computer.TotalPhysicalMemory / 1MB)
        AvailableMemoryMiB=[long][math]::Floor($os.FreePhysicalMemory / 1024)
        WorkDirectory=$folder.FullName
        AvailableDiskMiB=[long][math]::Floor($drive.AvailableFreeSpace / 1MB)
        VirtualizationAvailable=$firmwareVirtualization
        SessionId=[System.Diagnostics.Process]::GetCurrentProcess().SessionId
        UserInteractive=[Environment]::UserInteractive
        DesktopUsable=$null
        DotNetSdkVersions=$sdks
        Services=$services
    } | ConvertTo-Json -Depth 5 -Compress
} catch {
    [Console]::Error.WriteLine('Windows host inspection failed. Check CIM access and the local work directory.')
    exit 2
}
