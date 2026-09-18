# Copyright (c) 2026 Neil Colvin. Licensed under the MIT License; see LICENSE.
[CmdletBinding()]
param([Parameter(Mandatory)][string] $OutputDirectory)
$ErrorActionPreference = 'Stop'
$source = Join-Path $PSScriptRoot 'submission'
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $output) { throw 'Use a fresh submission runtime output directory.' }
$lock = Get-Content -LiteralPath (Join-Path $source 'runtime-lock.json') -Raw | ConvertFrom-Json
if ($lock.schemaVersion -ne 1 -or $lock.platform -ne 'win-x64') { throw 'Unsupported runtime lock.' }
New-Item -ItemType Directory -Path $output | Out-Null
$download = Join-Path $output '.downloads'
New-Item -ItemType Directory -Path $download | Out-Null
# No pip, PATH edits or machine installation. This directory is a disposable build artifact.
foreach ($artifact in $lock.artifacts) {
    if ($artifact.sha256 -cnotmatch '^[a-f0-9]{64}$' -or $artifact.target -notin @('runtime','runtime/packages') -or
        $artifact.file -match '[/\\:]' -or ([Uri]$artifact.url).Scheme -ne 'https') { throw 'Invalid runtime artifact lock.' }
    $archive = Join-Path $download $artifact.file
    Invoke-WebRequest -Uri $artifact.url -OutFile $archive
    if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant() -cne $artifact.sha256) {
        throw "Pinned runtime digest mismatch: $($artifact.name)."
    }
    $target = [IO.Path]::GetFullPath((Join-Path $output $artifact.target))
    $zip = [IO.Compression.ZipFile]::OpenRead($archive)
    try {
        foreach ($entry in $zip.Entries) {
            $name = $entry.FullName
            if ($name.Contains('\') -or $name.Contains(':') -or $name.StartsWith('/') -or
                @($name.Split('/') | Where-Object { $_ -in @('.','..') -or $_ -match '[. ]$' }).Count -gt 0 -or
                (($entry.ExternalAttributes -shr 16) -band 0xF000) -eq 0xA000) { throw 'Unsafe runtime archive entry.' }
            $destination = [IO.Path]::GetFullPath((Join-Path $target $name))
            if (-not $destination.StartsWith($target + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
                throw 'Runtime archive path escapes its destination.'
            }
            if ($name.EndsWith('/')) { New-Item -ItemType Directory -Path $destination -Force | Out-Null; continue }
            New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($destination)) -Force | Out-Null
            [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $destination, $false)
        }
    } finally { $zip.Dispose() }
    Remove-Item -LiteralPath $archive
}
Remove-Item -LiteralPath $download
$scripts = Join-Path $output 'scripts'
New-Item -ItemType Directory -Path $scripts | Out-Null
$modules = @('audit_android','build_help','coverage_plan','dependency_notices','normalize_package','package_help',
             'prepare_delivery','prepare_review','prepare_signed_review','render_help','revalidate_delivery',
             'review_android','self_test_form','sign_self_test_form','bundled_entry','validator_runtime')
foreach ($module in $modules) { Copy-Item -LiteralPath (Join-Path $source ($module + '.py')) -Destination $scripts }
Copy-Item -LiteralPath (Join-Path $source 'commands.json') -Destination $scripts
Copy-Item -LiteralPath (Join-Path $source 'runtime-lock.json') -Destination $output
# _pth excludes the current directory, user site and all interpreter environment variables.
$paths = @($lock.pythonStandardLibrary, '.', 'packages', '../scripts') -join "`n"
[IO.File]::WriteAllText((Join-Path $output ('runtime/' + $lock.pythonPathFile)), $paths + "`n", [Text.UTF8Encoding]::new($false))
$files = @(Get-ChildItem -LiteralPath $output -File -Recurse | Sort-Object FullName | ForEach-Object {
    [ordered]@{ path = [IO.Path]::GetRelativePath($output, $_.FullName).Replace('\','/'); sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
})
$manifest = [ordered]@{ schemaVersion = 1; platform = 'win-x64'; files = $files }
[IO.File]::WriteAllText((Join-Path $output 'manifest.json'), ($manifest | ConvertTo-Json -Depth 5), [Text.UTF8Encoding]::new($false))
Write-Host "Submission runtime prepared with $($files.Count) pinned files."
