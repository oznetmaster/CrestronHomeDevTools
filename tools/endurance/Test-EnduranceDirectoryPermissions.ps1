#requires -Version 7.6
#requires -PSEdition Core
# Copyright (c) 2026 Neil Colvin.
# Licensed under the MIT License. See LICENSE in the repository root.
param([Parameter(Mandatory)][string]$ResultsDirectory)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Import-Module Microsoft.PowerShell.Security -ErrorAction Stop
$helper = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../scripts/endurance/Set-EnduranceDirectoryPermissions.ps1'))
$ResultsDirectory = [IO.Path]::GetFullPath($ResultsDirectory)
if (Test-Path -LiteralPath $ResultsDirectory) { throw 'Choose a new test output directory.' }
[void][IO.Directory]::CreateDirectory($ResultsDirectory)
function Assert($Condition, [string]$Message) { if (-not $Condition) { throw $Message } }
function Prepare([string]$Name) {
    $root = Join-Path $ResultsDirectory $Name
    foreach ($child in @('cli','inputs','run','scheduler-state')) { [void][IO.Directory]::CreateDirectory((Join-Path $root $child)) }
    [IO.File]::WriteAllText((Join-Path $root 'inputs/worker.json'), '{"test":"synthetic; no credentials"}')
    return $root
}
function Service-Rights([string]$Path) {
    return @((Get-Acl -LiteralPath $Path).GetAccessRules($true,$true,[Security.Principal.SecurityIdentifier]) | Where-Object { $_.IdentityReference.Value -eq 'S-1-5-19' })
}
function Must-Refuse([scriptblock]$Operation, [string]$Expected) {
    $failure = $null
    try { & $Operation | Out-Null } catch { $failure = $_.Exception.Message }
    Assert ($failure -and $failure.Contains($Expected)) "Expected refusal containing '$Expected'; got '$failure'."
}
$passed = New-Object 'System.Collections.Generic.List[string]'
$root = Prepare 'existing-inputs'
$worker = Join-Path $root 'inputs/worker.json'
# Exercise explicit and inherited broad-user grants, including an existing file.
$users = New-Object Security.Principal.SecurityIdentifier('S-1-5-32-545')
$acl = Get-Acl -LiteralPath $root
$acl.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule($users,'Modify','ContainerInherit, ObjectInherit','None','Allow')))
Set-Acl -LiteralPath $root -AclObject $acl
$acl = Get-Acl -LiteralPath $worker
$acl.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule($users,'Modify','Allow')))
Set-Acl -LiteralPath $worker -AclObject $acl
$beforeRoot = (Get-Acl -LiteralPath $root).Sddl
$beforeFile = (Get-Acl -LiteralPath $worker).Sddl
$hash = (Get-FileHash -LiteralPath $worker).Hash
$preview = & $helper -RootDirectory $root
Assert (-not $preview.Applied -and -not $preview.Verified) 'Preview claimed mutation.'
Assert ((Get-Acl -LiteralPath $root).Sddl -eq $beforeRoot -and (Get-Acl -LiteralPath $worker).Sddl -eq $beforeFile) 'Preview changed permissions.'
$passed.Add('Preview leaves root and existing file permissions unchanged')
$applied = & $helper -RootDirectory $root -Apply
Assert ($applied.Applied -and $applied.Verified) 'Apply did not verify.'
foreach ($item in @((Get-Item -LiteralPath $root)) + @(Get-ChildItem -LiteralPath $root -Recurse -Force)) {
    $rules = @((Get-Acl -LiteralPath $item.FullName).GetAccessRules($true,$true,[Security.Principal.SecurityIdentifier]))
    Assert ($rules.Count -ge 3) 'An ACL was left empty.'
    Assert (@($rules | Where-Object { $_.IdentityReference.Value -in @('S-1-5-32-545','S-1-1-0','S-1-5-11') }).Count -eq 0) 'Broad-user access remained.'
}
Assert ((Get-FileHash -LiteralPath $worker).Hash -eq $hash) 'Input contents changed.'
$rights = @(Service-Rights $worker)
Assert ($rights.Count -eq 1 -and ($rights[0].FileSystemRights -band [Security.AccessControl.FileSystemRights]::Write) -eq 0) 'Existing input is service-writable or unreadable.'
$passed.Add('Existing files retain service read access without broad-user or service-write grants')
foreach ($case in @(@{Path='inputs/new.json';Write=$false},@{Path='run/new.json';Write=$true},@{Path='scheduler-state/new.json';Write=$true})) {
    $path = Join-Path $root $case.Path
    [IO.File]::WriteAllText($path,'synthetic')
    $rules = @(Service-Rights $path)
    Assert ($rules.Count -eq 1) 'New file did not inherit a service rule.'
    $canWrite = ($rules[0].FileSystemRights -band [Security.AccessControl.FileSystemRights]::Write) -ne 0
    Assert ($canWrite -eq $case.Write) 'New file inherited the wrong write permissions.'
}
$passed.Add('New input files inherit RX and new run/state files inherit Modify')
$protected = (Get-Acl -LiteralPath $root).Sddl
Must-Refuse { & $helper -RootDirectory $root -Apply } 'both output directories must be empty'
Assert ((Get-Acl -LiteralPath $root).Sddl -eq $protected) 'Nonempty-run refusal changed permissions.'
$passed.Add('Nonempty output prevents preparation of an active or used run')
$root2 = Prepare 'invalid-paths'
$initial = (Get-Acl -LiteralPath $root2).Sddl
Must-Refuse { & $helper -RootDirectory $root2 -RunDirectory '..' -Apply } 'strictly inside'
Must-Refuse { & $helper -RootDirectory $root2 -StateDirectory 'run' -Apply } 'must be separate'
Must-Refuse { & $helper -RootDirectory ([IO.Path]::GetPathRoot($root2)) -Apply } 'dedicated monitoring subdirectory'
Must-Refuse { & $helper -RootDirectory (Join-Path $env:SystemRoot 'System32') -Apply } 'inside Windows or Program Files'
Assert ((Get-Acl -LiteralPath $root2).Sddl -eq $initial) 'Invalid-path refusal changed permissions.'
$passed.Add('Escaped, overlapping and drive-root paths are rejected before mutation')
$outside = Join-Path $ResultsDirectory 'outside'
[void][IO.Directory]::CreateDirectory($outside)
$outsideAcl = (Get-Acl -LiteralPath $outside).Sddl
[void](New-Item -ItemType Junction -Path (Join-Path $root2 'linked') -Target $outside)
Must-Refuse { & $helper -RootDirectory $root2 -Apply } 'Linked files or directories'
Must-Refuse { & $helper -RootDirectory (Join-Path $root2 'linked') -Apply } 'Linked directories'
Assert ((Get-Acl -LiteralPath $outside).Sddl -eq $outsideAcl) 'Link rejection changed the external target.'
$passed.Add('Junction rejection preserves the external target')
[pscustomobject]@{Passed=$passed.Count; Checks=$passed.ToArray(); LiveServiceImpersonation=$false} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $ResultsDirectory 'results.json')
Write-Output "$($passed.Count) permission setup checks passed. No processor, task or credential store was accessed."
