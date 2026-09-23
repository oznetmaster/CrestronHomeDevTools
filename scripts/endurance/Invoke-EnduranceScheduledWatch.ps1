#requires -Version 7.6
#requires -PSEdition Core
# Copyright (c) 2026 Neil Colvin.
# Licensed under the MIT License. See LICENSE in the repository root.
# PowerShell 7.6 or later; passive observation and approved notifications only.
param([Parameter(Mandatory)][string]$Configuration)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$guard = $null; $process = $null; $started = $false; $attempt = $null; $config = $null
$phase = 'configuration'
$utf8 = New-Object Text.UTF8Encoding($false)
function Write-Json([string]$Path, $Value) {
	$temp = $Path + '.' + [Guid]::NewGuid().ToString('N') + '.tmp'
	try {
		[IO.File]::WriteAllText($temp, ($Value | ConvertTo-Json -Depth 20), $utf8)
		if ([IO.File]::Exists($Path)) { [IO.File]::Replace($temp, $Path, [System.Management.Automation.Language.NullString]::Value) }
		else { [IO.File]::Move($temp, $Path) }
	} finally { if ([IO.File]::Exists($temp)) { [IO.File]::Delete($temp) } }
}
function Quote([string]$Value) {
	if ($Value.Contains('"') -or $Value.Contains([char]0) -or $Value.Contains("`r") -or $Value.Contains("`n")) { throw 'Invalid argument.' }
	return '"' + [regex]::Replace($Value, '(\\+)$', '$1$1') + '"'
}
# Successful attempts are temporary diagnostics. Keep a bounded compact history,
# then atomically retire the attempt before removing it. A crash during removal
# cannot make the next invocation replay a collector or misread an unfinished call.
function Clear-CompletedDiagnostics([string]$Parent) {
	if (-not (Test-Path -LiteralPath $Parent)) { return }
	$parentFull = [IO.Path]::GetFullPath($Parent).TrimEnd('\')
	$current = $parentFull
	while ($current) {
		if ((Get-Item -LiteralPath $current -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Linked diagnostic paths are not accepted.' }
		$current = [IO.Path]::GetDirectoryName($current)
	}
	foreach ($directory in @(Get-ChildItem -LiteralPath $parentFull -Force -Directory | Where-Object { $_.Name -cmatch '^completed-[0-9a-f]{32}$' })) {
		$target = [IO.Path]::GetFullPath($directory.FullName)
		if ([IO.Path]::GetDirectoryName($target) -cne $parentFull -or ($directory.Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Unexpected diagnostic cleanup path.' }
		# Wrapper diagnostics are flat; refuse unexpected content instead of following it.
		$items = @(Get-ChildItem -LiteralPath $target -Force)
		foreach ($item in $items) {
			if ($item.PSIsContainer -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Unexpected diagnostic cleanup content.' }
		}
		$resultPath = Join-Path $target 'result.json'
		if ($items.Count -gt 0) {
			if ((Test-Path -LiteralPath (Join-Path $target 'error.json')) -or -not (Test-Path -LiteralPath $resultPath -PathType Leaf)) { throw 'Unconfirmed diagnostics must be retained.' }
			$result = Get-Content -LiteralPath $resultPath -Raw | ConvertFrom-Json
			if ($result.ExitCode -ne 0) { throw 'Failed diagnostics must be retained.' }
			# Keep the successful completion marker until all other diagnostics are gone.
			foreach ($item in $items) { if ($item.Name -cne 'result.json') { [IO.File]::Delete($item.FullName) } }
			[IO.File]::Delete($resultPath)
		}
		[IO.Directory]::Delete($target, $false)
	}
}
function Retire-SuccessfulDiagnostics([string]$StateRoot, [string]$AttemptPath, $Summary) {
	$history = Join-Path $StateRoot 'history.jsonl'
	$previous = Join-Path $StateRoot 'history.previous.jsonl'
	foreach ($path in @($history, $previous)) {
		if ((Test-Path -LiteralPath $path) -and ((Get-Item -LiteralPath $path -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Linked diagnostic history is not accepted.' }
	}
	if ((Test-Path -LiteralPath $history) -and (Get-Item -LiteralPath $history).Length -ge 1MB) { [IO.File]::Move($history, $previous, $true) }
	$bytes = $utf8.GetBytes(($Summary | ConvertTo-Json -Compress -Depth 4) + [Environment]::NewLine)
	$stream = [IO.File]::Open($history, 'Append', 'Write', 'None')
	try { $stream.Write($bytes, 0, $bytes.Length); $stream.Flush($true) } finally { $stream.Dispose() }
	$parent = [IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($AttemptPath))
	$retired = Join-Path $parent ('completed-' + [Guid]::NewGuid().ToString('N'))
	# Windows scanners can briefly hold a newly written diagnostic directory.
	# Retry only the rename; never repeat the collector or notification operation.
	for ($renameAttempt=0; $renameAttempt -lt 3; $renameAttempt++) {
		try { [IO.Directory]::Move($AttemptPath, $retired); break }
		catch [IO.IOException], [UnauthorizedAccessException] {
			if ($renameAttempt -eq 2) { throw }
			Start-Sleep -Milliseconds (100 * ($renameAttempt + 1))
		}
	}
	$script:attempt = $null
	Clear-CompletedDiagnostics $parent
}
try {
	$config = Get-Content -LiteralPath $Configuration -Raw | ConvertFrom-Json
	if ($config.SchemaVersion -ne 1 -or $config.Kind -ne 'EnduranceWatch') { throw 'Invalid watch configuration.' }
	[void][IO.Directory]::CreateDirectory($config.StateDirectory)
	$guard = [IO.File]::Open((Join-Path $config.StateDirectory 'watch.lock'), 'OpenOrCreate', 'ReadWrite', 'None')
	Clear-CompletedDiagnostics $config.StateDirectory
	$attempt = Join-Path $config.StateDirectory ([Guid]::NewGuid().ToString('N'))
	[void][IO.Directory]::CreateDirectory($attempt)
	$phase = 'file-pins'
	foreach ($pin in $config.Inputs) {
		if ((Get-FileHash -LiteralPath $pin.Path -Algorithm SHA256).Hash -ne $pin.Sha256) { throw 'A reviewed watch input changed.' }
	}
	if ((Get-FileHash -LiteralPath $PSCommandPath -Algorithm SHA256).Hash -ne $config.ScriptSha256) { throw 'Watch script changed.' }
	$files = @(Get-ChildItem -LiteralPath $config.CliDirectory -Recurse -Force)
	if (@($files | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }).Count -ne 0) { throw 'Linked CLI files are not accepted.' }
	if (@($files | Where-Object { -not $_.PSIsContainer }).Count -ne @($config.CliFiles).Count) { throw 'CLI bundle changed.' }
	foreach ($pin in $config.CliFiles) {
		if ((Get-FileHash -LiteralPath (Join-Path $config.CliDirectory $pin.Path) -Algorithm SHA256).Hash -ne $pin.Sha256) { throw 'CLI bundle changed.' }
	}
	if (-not (Test-Path -LiteralPath $config.JournalDirectory -PathType Container)) { throw 'Create and protect the notification journal first.' }
	$phase = 'process-start'
	$start = New-Object Diagnostics.ProcessStartInfo
	$start.FileName = Join-Path $config.CliDirectory $config.CliExecutable
	$start.WorkingDirectory = $config.CliDirectory
	$start.Arguments = (@('endurance-watch', '--worker', $config.WorkerFile, '--observer', $config.ObserverFile,
		'--notifications', $config.NotificationsFile, '--journal', $config.JournalDirectory, '--send', 'true', '--credentials', $config.CredentialBindingsFile) | ForEach-Object { Quote $_ }) -join ' '
	$start.UseShellExecute = $false; $start.CreateNoWindow = $true
	$start.RedirectStandardInput = $true; $start.RedirectStandardOutput = $true; $start.RedirectStandardError = $true
	foreach ($name in @($start.EnvironmentVariables.Keys)) {
		if ($name.StartsWith('CRESTRON_HOME_', [StringComparison]::OrdinalIgnoreCase)) { $start.EnvironmentVariables.Remove($name) }
	}
	$process = New-Object Diagnostics.Process
	$process.StartInfo = $start
	if (-not $process.Start()) { throw 'Watcher did not start.' }
	$started = $true
	Write-Json (Join-Path $attempt 'process.json') @{ProcessId=$process.Id; StartedUtc=[DateTimeOffset]::UtcNow.ToString('O')}
	$out = $process.StandardOutput.ReadToEndAsync(); $err = $process.StandardError.ReadToEndAsync()
	# The CLI resolves named encrypted entries itself; the scheduler never reads a password.
	$process.StandardInput.Close()
	$phase = 'process-completion'
	if (-not $process.WaitForExit(100000)) { throw 'Watcher exceeded its deadline; inspect its notification journal before any retry.' }
	[IO.File]::WriteAllText((Join-Path $attempt 'stdout.json'), $out.GetAwaiter().GetResult(), $utf8)
	[IO.File]::WriteAllText((Join-Path $attempt 'stderr.txt'), $err.GetAwaiter().GetResult(), $utf8)
	$code = $process.ExitCode
	$result = @{ObservedUtc=[DateTimeOffset]::UtcNow.ToString('O'); ExitCode=$code; RequiresAttention=($code -ne 0); Attempt=$(if ($code -ne 0) { $attempt } else { $null })}
	Write-Json (Join-Path $attempt 'result.json') $result
	Write-Json (Join-Path $config.StateDirectory 'status.json') $result
	if ($code -eq 0) {
		Retire-SuccessfulDiagnostics $config.StateDirectory $attempt @{SchemaVersion=1; ObservedUtc=$result.ObservedUtc; ExitCode=0; RequiresAttention=$false}
	}
	exit $code
} catch {
	# Do not print exception text: credentials and private paths must not enter scheduler diagnostics.
	if ($null -ne $attempt) {
		$result = @{ObservedUtc=[DateTimeOffset]::UtcNow.ToString('O'); ExitCode=3; RequiresAttention=$true; Reason=('watch-' + $phase + '-failed'); Attempt=$attempt}
		try { Write-Json (Join-Path $attempt 'result.json') $result; Write-Json (Join-Path $config.StateDirectory 'status.json') $result } catch { }
	}
	[Console]::Error.WriteLine('Scheduled watch requires inspection. Preserve its state and notification journal; do not force another send.')
	exit 3
} finally {
	if ($null -ne $process) {
		try { if ($started -and -not $process.HasExited) { $process.Kill(); [void]$process.WaitForExit(10000) } } finally { $process.Dispose() }
	}
	if ($null -ne $guard) { $guard.Dispose() }
}
