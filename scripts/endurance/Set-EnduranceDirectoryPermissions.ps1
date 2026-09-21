# Copyright (c) 2026 Neil Colvin.
# Licensed under the MIT License. See LICENSE in the repository root.
# Preview by default. Apply only to a dedicated, unused monitoring directory.
param(
    [Parameter(Mandatory)][string]$RootDirectory,
    [string]$RunDirectory = 'run',
    [string]$StateDirectory = 'scheduler-state',
    [switch]$Apply
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) { throw 'This setup helper requires Windows.' }
Import-Module Microsoft.PowerShell.Security -ErrorAction Stop

function Local-Path([string]$Path) {
    if ($Path -notmatch '^[A-Za-z]:[\\/]' -or $Path.Contains('*') -or $Path.Contains('?')) { throw 'Use an absolute local drive path without wildcards.' }
    return [IO.Path]::GetFullPath($Path).TrimEnd('\')
}
function Is-Child([string]$Path, [string]$Parent) {
    return $Path.StartsWith($Parent + '\', [StringComparison]::OrdinalIgnoreCase)
}
$root = Local-Path $RootDirectory
$excluded = @([IO.Path]::GetPathRoot($root).TrimEnd('\'), $env:SystemRoot, $env:ProgramData, $env:ProgramFiles, ${env:ProgramFiles(x86)}, $env:USERPROFILE)
if ($env:USERPROFILE) { $excluded += Split-Path $env:USERPROFILE -Parent }
foreach ($path in $excluded) {
    if ($path -and $root.Equals($path.TrimEnd('\'), [StringComparison]::OrdinalIgnoreCase)) { throw 'Choose a dedicated monitoring subdirectory, not a system or profile root.' }
}
foreach ($path in @($env:SystemRoot, $env:ProgramFiles, ${env:ProgramFiles(x86)})) {
    if ($path -and (Is-Child $root $path.TrimEnd('\'))) { throw 'Do not prepare monitoring directories inside Windows or Program Files.' }
}
if (-not (Test-Path -LiteralPath $root -PathType Container)) { throw 'Create and stage the dedicated monitoring directory first.' }
# Check ancestors as well as descendants; a junction above the chosen root can redirect changes.
$ancestor = Get-Item -LiteralPath $root -Force
while ($null -ne $ancestor) {
    if ($ancestor.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Linked directories are not accepted.' }
    $ancestor = $ancestor.Parent
}
$mutable = @($RunDirectory, $StateDirectory | ForEach-Object {
    $path = if ([IO.Path]::IsPathRooted($_)) { Local-Path $_ } else { Local-Path (Join-Path $root $_) }
    if (-not (Is-Child $path $root)) { throw 'Both output directories must be strictly inside the monitoring root.' }
    if (-not (Test-Path -LiteralPath $path -PathType Container)) { throw 'Create both empty output directories before preparing permissions.' }
    $path
})
if ($mutable[0] -eq $mutable[1] -or (Is-Child $mutable[0] $mutable[1]) -or (Is-Child $mutable[1] $mutable[0])) { throw 'Run and scheduler-state directories must be separate, not nested.' }

# Walk one level at a time so links are rejected before any descent or mutation.
$items = New-Object 'System.Collections.Generic.List[System.IO.FileSystemInfo]'
$pending = New-Object 'System.Collections.Generic.Stack[System.IO.DirectoryInfo]'
$pending.Push((Get-Item -LiteralPath $root -Force))
while ($pending.Count -gt 0) {
    $directory = $pending.Pop()
    $items.Add($directory)
    foreach ($item in @(Get-ChildItem -LiteralPath $directory.FullName -Force)) {
        if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Linked files or directories are not accepted.' }
        if ($item -is [IO.DirectoryInfo]) { $pending.Push($item) } else { $items.Add($item) }
    }
}
foreach ($directory in $mutable) {
    if (@(Get-ChildItem -LiteralPath $directory -Force).Count -ne 0) { throw 'Use this helper before starting a run: both output directories must be empty.' }
}
$owner = (Get-Acl -LiteralPath $root).GetOwner([Security.Principal.SecurityIdentifier])
$service = New-Object Security.Principal.SecurityIdentifier('S-1-5-19')
$full = @($owner.Value, 'S-1-5-32-544', 'S-1-5-18' | Select-Object -Unique)
if ($owner.Value -in @('S-1-5-19','S-1-1-0','S-1-5-11','S-1-5-32-545')) { throw 'The monitoring directory must have an individual or administrator owner.' }
$plan = foreach ($item in $items) {
    $write = $item.FullName -in $mutable
    [pscustomobject]@{ Path=$item.FullName; Directory=($item -is [IO.DirectoryInfo]); ServiceAccess=$(if ($write) {'Modify'} else {'ReadAndExecute'}); Acl=(Get-Acl -LiteralPath $item.FullName) }
}
$summary = [ordered]@{ SchemaVersion=1; RootDirectory=$root; OwnerSid=$owner.Value; FullAccessSids=$full; ServiceSid=$service.Value; ReadOnlyFiles=@($plan | Where-Object { -not $_.Directory }).Count; Directories=@($plan | Where-Object Directory).Count; WritableDirectories=$mutable; Applied=$false; Verified=$false }
if (-not $Apply) { [pscustomobject]$summary; return }

foreach ($entry in $plan) {
    $acl = $entry.Acl
    # Build a complete DACL before writing it. Never clear a file's inherited ACL in a separate operation.
    $acl.SetAccessRuleProtection($true, $false)
    foreach ($rule in @($acl.GetAccessRules($true, $false, [Security.Principal.SecurityIdentifier]))) { [void]$acl.RemoveAccessRuleSpecific($rule) }
    $inheritance = if ($entry.Directory) { [Security.AccessControl.InheritanceFlags]'ContainerInherit, ObjectInherit' } else { [Security.AccessControl.InheritanceFlags]::None }
    foreach ($sid in $full) {
        $identity = New-Object Security.Principal.SecurityIdentifier($sid)
        $rule = New-Object Security.AccessControl.FileSystemAccessRule($identity, 'FullControl', $inheritance, 'None', 'Allow')
        $acl.AddAccessRule($rule)
    }
    $acl.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule($service, $entry.ServiceAccess, $inheritance, 'None', 'Allow')))
    Set-Acl -LiteralPath $entry.Path -AclObject $acl
}
foreach ($entry in $plan) {
    $acl = Get-Acl -LiteralPath $entry.Path
    $rules = @($acl.GetAccessRules($true, $true, [Security.Principal.SecurityIdentifier]))
    if (-not $acl.AreAccessRulesProtected -or $rules.Count -ne ($full.Count + 1)) { throw "Permission verification failed for $($entry.Path)." }
    foreach ($rule in $rules) {
        $sid = $rule.IdentityReference.Value
        $expected = if ($sid -eq $service.Value) { [Security.AccessControl.FileSystemRights]$entry.ServiceAccess } elseif ($sid -in $full) { [Security.AccessControl.FileSystemRights]::FullControl } else { throw "Unexpected principal on $($entry.Path)." }
        # Windows adds Synchronize to allow rules.
        $actual = $rule.FileSystemRights -band (-bnot [Security.AccessControl.FileSystemRights]::Synchronize)
        $expected = $expected -band (-bnot [Security.AccessControl.FileSystemRights]::Synchronize)
        if ($rule.AccessControlType -ne 'Allow' -or $actual -ne $expected -or $rule.IsInherited -or $rule.PropagationFlags -ne 'None') { throw "Unexpected access on $($entry.Path)." }
        $inheritance = if ($entry.Directory) { [Security.AccessControl.InheritanceFlags]'ContainerInherit, ObjectInherit' } else { [Security.AccessControl.InheritanceFlags]::None }
        if ($rule.InheritanceFlags -ne $inheritance) { throw "Unexpected inheritance on $($entry.Path)." }
    }
}
$summary.Applied = $true
$summary.Verified = $true
[pscustomobject]$summary
