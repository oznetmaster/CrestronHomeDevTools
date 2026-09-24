#requires -Version 7.6
#requires -PSEdition Core
# Copyright (c) 2026 Neil Colvin. MIT licensed.
[CmdletBinding()]
param(
 [Parameter(Mandatory)][string]$Emulator,
 [Parameter(Mandatory)][ValidatePattern('^[A-Za-z0-9_.-]{1,80}$')][string]$Avd,
 [Parameter(Mandatory)][string]$LogDirectory,
 [string]$Adb,
 [ValidatePattern('^[A-Za-z0-9_.]+/[A-Za-z0-9_.]+$')][string]$ApplicationActivity,
 [ValidateRange(5554,5682)][int]$Port=5554,
 [ValidateRange(1,8)][int]$Cores=2,
 [ValidateRange(1024,8192)][int]$MemoryMb=2048
)
$ErrorActionPreference='Stop'
if (-not [IO.Path]::IsPathFullyQualified($Emulator) -or -not (Test-Path -LiteralPath $Emulator -PathType Leaf) -or -not [IO.Path]::IsPathFullyQualified($LogDirectory) -or $Port % 2) { throw 'Use the installed emulator, absolute log directory and an even console port.' }
if(-not $Adb) { $Adb=Join-Path (Split-Path (Split-Path $Emulator -Parent) -Parent) 'platform-tools/adb.exe' }
if(-not [IO.Path]::IsPathFullyQualified($Adb) -or -not (Test-Path -LiteralPath $Adb -PathType Leaf)) { throw 'Provide the installed Android SDK ADB executable.' }
New-Item -ItemType Directory -Path $LogDirectory -Force | Out-Null
# Start the shared ADB server before redirecting emulator logs. Otherwise the
# emulator can start ADB with inherited log handles that survive emulator exit
# and prevent the next launch from rotating its own files. Never kill a shared
# server automatically; an old inherited handle requires maintenance first.
$adbStart=Start-Process -FilePath $Adb -WindowStyle Hidden -ArgumentList 'start-server' -PassThru
try {
 if(-not $adbStart.WaitForExit(20000)) { $adbStart.Kill();throw 'ADB startup timed out; inspect the server before retrying.' }
 if($adbStart.ExitCode -ne 0) { throw 'ADB startup failed.' }
} finally { $adbStart.Dispose() }
$stage='RotateLogs'
$rotationRetries=0
try {
foreach ($name in @('emulator.stdout.log','emulator.stderr.log')) {
 $path=Join-Path $LogDirectory $name
 if(Test-Path -LiteralPath $path) {
  for($attempt=0;;$attempt++) {
   try {Move-Item -LiteralPath $path -Destination ($path+'.previous') -Force;break}
   catch [IO.IOException] {
    # The emulator's final child handles can close just after its main process.
    # Retry only this local rename; no ADB command or emulator launch is repeated.
    if($attempt -ge 40){throw};$rotationRetries++;Start-Sleep -Milliseconds 250
   }
  }
 }
}
$stage='StartEmulator'
# No desktop window and no Windows logon password. The selected account owns the existing AVD.
$process=Start-Process -FilePath $Emulator -WindowStyle Hidden -ArgumentList '-avd',$Avd,'-port',$Port,'-no-window','-no-audio','-no-snapshot','-no-boot-anim','-gpu','swiftshader_indirect','-cores',$Cores,'-memory',$MemoryMb -PassThru -RedirectStandardOutput (Join-Path $LogDirectory 'emulator.stdout.log') -RedirectStandardError (Join-Path $LogDirectory 'emulator.stderr.log')
@{Pid=$process.Id;Avd=$Avd;StartedUtc=[DateTimeOffset]::UtcNow.ToString('o');Port=$Port;Headless=$true;Cores=$Cores;MemoryMb=$MemoryMb;LogRotationRetries=$rotationRetries} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $LogDirectory 'process.json') -Encoding utf8NoBOM
try {
 if($ApplicationActivity) {
  $stage='WaitForAndroidBoot'
  function Invoke-AdbStartup([string[]]$Arguments) {
   $info=[Diagnostics.ProcessStartInfo]::new($Adb)
   $info.UseShellExecute=$false;$info.CreateNoWindow=$true;$info.RedirectStandardOutput=$true;$info.RedirectStandardError=$true
   foreach($argument in @('-s',"emulator-$Port")+$Arguments){$info.ArgumentList.Add($argument)}
   $command=[Diagnostics.Process]::Start($info)
   try {
    $out=$command.StandardOutput.ReadToEndAsync();$err=$command.StandardError.ReadToEndAsync()
    if(-not $command.WaitForExit(15000)){ $command.Kill();throw 'ADB boot check timed out.' }
    if(-not $out.Wait(3000) -or -not $err.Wait(3000)){throw 'ADB startup output did not finish.'}
    return @{ExitCode=$command.ExitCode;Output=$out.Result.Trim();ErrorOutput=$err.Result.Trim();ReportedError=($err.Result -match '(?m)^Error')}
   } finally {$command.Dispose()}
  }
  $deadline=[DateTime]::UtcNow.AddMinutes(3);$booted=$false
  while(-not $process.HasExited -and [DateTime]::UtcNow -lt $deadline) {
   $boot=Invoke-AdbStartup @('shell','getprop','sys.boot_completed')
   if($boot.ExitCode -eq 0 -and $boot.Output -eq '1'){$booted=$true;break}
   Start-Sleep -Seconds 2
  }
  if(-not $booted){throw 'Android did not finish booting within three minutes.'}
  $stage='OpenConfiguredApplication'
  $opened=Invoke-AdbStartup @('shell','am','start','-n',$ApplicationActivity)
  if($opened.ExitCode -ne 0 -or $opened.ReportedError -or $opened.Output -match '(?m)^Error') {
   $opened | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $LogDirectory 'application-start-failure.json') -Encoding utf8NoBOM
   throw 'Configured application did not start; inspect the private startup diagnostic.'
  }
  @{Activity=$ApplicationActivity;OpenedUtc=[DateTimeOffset]::UtcNow.ToString('o');HomeReadinessVerified=$false} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $LogDirectory 'application-start.json') -Encoding utf8NoBOM
 }
 $stage='RunningEmulator'
 $process.WaitForExit();exit $process.ExitCode
}
finally {
 if(-not $process.HasExited) {
  # The Windows emulator is a launcher with a QEMU child. Stopping only the
  # parent leaves the owned fixture alive while the task reports failure.
  $process.Kill($true);[void]$process.WaitForExit(10000)
 }
 $process.Dispose()
}
} catch {
 @{Stage=$stage;ErrorType=$_.Exception.GetType().FullName;Line=$_.InvocationInfo.ScriptLineNumber;ObservedUtc=[DateTimeOffset]::UtcNow.ToString('o')} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $LogDirectory 'launcher-error.json') -Encoding utf8NoBOM
 throw
}
