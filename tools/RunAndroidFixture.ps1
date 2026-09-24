#requires -Version 7.6
#requires -PSEdition Core
# Copyright (c) 2026 Neil Colvin. MIT licensed.
[CmdletBinding()]
param(
 [Parameter(Mandatory)][string]$Emulator,
 [Parameter(Mandatory)][ValidatePattern('^[A-Za-z0-9_.-]{1,80}$')][string]$Avd,
 [Parameter(Mandatory)][string]$LogDirectory,
 [ValidateRange(5554,5682)][int]$Port=5554,
 [ValidateRange(1,8)][int]$Cores=2,
 [ValidateRange(1024,8192)][int]$MemoryMb=2048
)
$ErrorActionPreference='Stop'
if (-not [IO.Path]::IsPathFullyQualified($Emulator) -or -not (Test-Path -LiteralPath $Emulator -PathType Leaf) -or -not [IO.Path]::IsPathFullyQualified($LogDirectory) -or $Port % 2) { throw 'Use the installed emulator, absolute log directory and an even console port.' }
New-Item -ItemType Directory -Path $LogDirectory -Force | Out-Null
foreach ($name in @('emulator.stdout.log','emulator.stderr.log')) {
 $path=Join-Path $LogDirectory $name
 if(Test-Path -LiteralPath $path) { Move-Item -LiteralPath $path -Destination ($path+'.previous') -Force }
}
# No desktop window and no Windows logon password. The selected account owns the existing AVD.
$process=Start-Process -FilePath $Emulator -WindowStyle Hidden -ArgumentList '-avd',$Avd,'-port',$Port,'-no-window','-no-audio','-no-snapshot','-no-boot-anim','-gpu','swiftshader_indirect','-cores',$Cores,'-memory',$MemoryMb -PassThru -RedirectStandardOutput (Join-Path $LogDirectory 'emulator.stdout.log') -RedirectStandardError (Join-Path $LogDirectory 'emulator.stderr.log')
@{Pid=$process.Id;Avd=$Avd;StartedUtc=[DateTimeOffset]::UtcNow.ToString('o');Port=$Port;Headless=$true} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $LogDirectory 'process.json') -Encoding utf8NoBOM
try { $process.WaitForExit();exit $process.ExitCode }
finally { if(-not $process.HasExited) { Stop-Process -Id $process.Id };$process.Dispose() }
