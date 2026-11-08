# Copyright (c) 2026 Neil Colvin. MIT licensed.
# Focused local AST guard tests only: no publish, downloads or equipment calls.
[CmdletBinding()]
param([Parameter(Mandatory)][string]$ResultsDirectory)
$ErrorActionPreference='Stop'
$proposal=Join-Path $PSScriptRoot 'BuildSubmissionConsole.ps1'
$tokens=$null;$errors=$null
$ast=[Management.Automation.Language.Parser]::ParseFile($proposal,[ref]$tokens,[ref]$errors)
if($errors.Count){throw 'Proposed script parse failed'}
$functions=@($ast.FindAll({param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst]},$true))
foreach($fn in $functions){. ([ScriptBlock]::Create($fn.Extent.Text))}
$results=[IO.Path]::GetFullPath($ResultsDirectory)
Assert-OrdinaryRuntimeAncestors $results
if(Test-Path -LiteralPath $results){throw 'Use fresh private results'}
New-Item -ItemType Directory -Path $results | Out-Null
$passed=0
$manifest=[pscustomobject]@{files=@([pscustomobject]@{path='runtime/x.pyd'})}
$base=(Join-Path $results 'pathcases')+[IO.Path]::DirectorySeparatorChar
$relative='submission-tools\runtime\x.pyd'
$budget=259-$base.Length-1-$relative.Length
if($budget -le 0){throw 'Fixture prefix too long'}
$output259=$base+('a'*$budget)
$native=[IO.Path]::GetFullPath((Join-Path $output259 $relative))
if($native.Length -ne 259){throw "Fixture boundary length wrong: $($native.Length)"}
$script:published=0
& {Assert-SubmissionNativeOutputPaths $output259 $manifest;$script:published++}
if($script:published -ne 1){throw '259 denied'};$passed++
$script:published=0;$rejected=$false
try {& {Assert-SubmissionNativeOutputPaths ($output259+'a') $manifest;$script:published++}}catch{$rejected=$true}
if(-not $rejected -or $script:published -ne 0){throw '260 did not stop before publication'};$passed++
Assert-SubmissionNativeOutputPaths (Join-Path $results 'short') $manifest;$passed++
$script:published=0;$rejected=$false
$target=Join-Path $results 'ordinary-target'
$link=Join-Path $results 'owned-junction'
New-Item -ItemType Directory -Path $target | Out-Null
New-Item -ItemType Junction -Path $link -Target $target | Out-Null
try {& {Assert-SubmissionNativeOutputPaths (Join-Path $link 'future') $manifest;$script:published++}}catch{$rejected=$true}
if(-not $rejected -or $script:published){throw 'Actual junction ancestor not rejected'};$passed++
$rejected=$false
try {Assert-SubmissionNativeOutputPaths ($base+'short') ([pscustomobject]@{files=@([pscustomobject]@{path='../unsafe.dll'})})}catch{$rejected=$true}
if(-not $rejected){throw 'Unsafe native path not rejected'};$passed++
$text=Get-Content -LiteralPath $proposal -Raw
if($text.IndexOf('Assert-SubmissionNativeOutputPaths $output $bundleManifest') -gt $text.IndexOf('dotnet publish')){throw 'Admission is after publication'};$passed++
Write-Output "$passed production-AST path contract checks passed; no publication function invoked."
