#requires -Version 7.6
# Copyright (c) 2026 Neil Colvin. MIT licensed.
[CmdletBinding()]
param([Parameter(Mandatory)][string]$PackageDirectory,[Parameter(Mandatory)][string]$Version,[Parameter(Mandatory)][string]$ResultsDirectory)
$ErrorActionPreference='Stop'
$feed=(Resolve-Path -LiteralPath $PackageDirectory).Path
$results=[IO.Path]::GetFullPath($ResultsDirectory)
if(Test-Path -LiteralPath $results){throw 'Use a fresh package consumer directory'}
foreach($id in @('CrestronHomeDevTools','CrestronHomeDevTools.Automation','CrestronHomeDevTools.SubmissionTests')){if(!(Test-Path -LiteralPath (Join-Path $feed "$id.$Version.nupkg"))){throw "Missing staged package $id"}}
[IO.Directory]::CreateDirectory($results)|Out-Null
$xmlVersion=[Security.SecurityElement]::Escape($Version)
@"
<Project Sdk="Microsoft.NET.Sdk">
 <PropertyGroup><TargetFramework>net10.0</TargetFramework><IsTestProject>true</IsTestProject><IsPackable>false</IsPackable><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup>
 <ItemGroup>
  <PackageReference Include="CrestronHomeDevTools.SubmissionTests" Version="[$xmlVersion]" />
  <PackageReference Include="NUnit3TestAdapter" Version="6.3.0" />
  <PackageReference Include="Microsoft.NET.Test.Sdk" Version="18.9.0" />
 </ItemGroup>
</Project>
"@|Set-Content -LiteralPath "$results/Consumer.csproj" -Encoding utf8NoBOM
@'
using NUnit.Framework;
using CrestronHomeDevTools.SubmissionTests;
[TestFixture]
public sealed class PackagedSubmission : SubmissionFixture
{
 protected override string SettingsEnvironment => "CRESTRON_PACKAGE_DISCOVERY_ONLY";
}
'@|Set-Content -LiteralPath "$results/Consumer.cs" -Encoding utf8NoBOM
$xmlFeed=[Security.SecurityElement]::Escape($feed)
@"
<configuration><packageSources><clear/><add key="staged" value="$xmlFeed"/><add key="nuget.org" value="https://api.nuget.org/v3/index.json"/></packageSources><disabledPackageSources><clear/></disabledPackageSources><packageSourceMapping><clear/></packageSourceMapping></configuration>
"@|Set-Content -LiteralPath "$results/NuGet.Config" -Encoding utf8NoBOM
$project="$results/Consumer.csproj"
& dotnet restore $project --configfile "$results/NuGet.Config" --packages "$results/packages" --no-cache -p:ImportDirectoryBuildProps=false -p:ImportDirectoryBuildTargets=false *> "$results/restore.log"
if($LASTEXITCODE){throw 'Fresh-cache package consumer restore failed'}
$assets=Get-Content "$results/obj/project.assets.json" -Raw|ConvertFrom-Json
foreach($id in @('CrestronHomeDevTools','CrestronHomeDevTools.Automation','CrestronHomeDevTools.SubmissionTests')){if(!$assets.libraries.PSObject.Properties["$id/$Version"]){throw "Consumer resolved an unexpected version of $id"}}
# A fresh cache prevents reuse but does not prove which feed supplied identical package IDs/versions.
# Verify the exact staged bytes for every staged dependency selected by the consumer.
$verifiedPackages=@()
$stagedIds=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach($package in Get-ChildItem -LiteralPath $feed -Filter '*.nupkg' -File){
 $archive=[IO.Compression.ZipFile]::OpenRead($package.FullName)
 try {
  $specs=@($archive.Entries|Where-Object{$_.FullName -notmatch '/' -and $_.FullName.EndsWith('.nuspec',[StringComparison]::OrdinalIgnoreCase)})
  if($specs.Count -ne 1){throw 'Staged package must have one root NuGet specification'}
  $reader=[IO.StreamReader]::new($specs[0].Open())
  try{[xml]$spec=$reader.ReadToEnd()}finally{$reader.Dispose()}
  $id=[string]$spec.package.metadata.id;$packageVersion=[string]$spec.package.metadata.version
 }finally{$archive.Dispose()}
 if($id -notmatch '^[A-Za-z0-9_.-]+$' -or $packageVersion -notmatch '^[A-Za-z0-9_.+-]+$'){throw 'Invalid staged package identity'}
 [void]$stagedIds.Add($id)
 $resolved=@($assets.libraries.PSObject.Properties|Where-Object{$_.Name -ieq ($id+'/'+$packageVersion)})
 if(!$resolved.Count){continue}
 $restored=Join-Path "$results/packages" ($resolved[0].Value.path+'/'+$id.ToLowerInvariant()+'.'+$packageVersion.ToLowerInvariant()+'.nupkg')
 $cacheRoot=[IO.Path]::GetFullPath("$results/packages")+[IO.Path]::DirectorySeparatorChar
 if(![IO.Path]::GetFullPath($restored).StartsWith($cacheRoot,[StringComparison]::OrdinalIgnoreCase)){throw 'Resolved package escapes consumer cache'}
 if(!(Test-Path -LiteralPath $restored)){throw "Restored package archive missing: $id"}
 $expected=(Get-FileHash -LiteralPath $package.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
 $actual=(Get-FileHash -LiteralPath $restored -Algorithm SHA256).Hash.ToLowerInvariant()
 if($actual -ne $expected){throw "Consumer restored different bytes for staged package $id $packageVersion"}
 $verifiedPackages+=@{Id=$id;Version=$packageVersion;Sha256=$actual}
}
foreach($id in @('CrestronHomeDevTools','CrestronHomeDevTools.Automation','CrestronHomeDevTools.SubmissionTests')){
 if(@($verifiedPackages|Where-Object Id -eq $id).Count -ne 1){throw "Missing exact staged package proof: $id"}
}
foreach($id in $stagedIds){
 $used=@($assets.libraries.PSObject.Properties|Where-Object{$_.Name.StartsWith($id+'/',[StringComparison]::OrdinalIgnoreCase)})
 if($used.Count -and !@($verifiedPackages|Where-Object Id -eq $id).Count){throw "Consumer did not use the supplied staged dependency: $id"}
}
& dotnet test $project -c Release --no-restore --list-tests -p:ImportDirectoryBuildProps=false -p:ImportDirectoryBuildTargets=false *> "$results/discovery.log"
if($LASTEXITCODE){throw 'Packaged NUnit fixture could not build or discover'}
$log=Get-Content "$results/discovery.log" -Raw
$expected=@('CandidateValidation','LocalAndProcessorChecks','ProcessorEvidence','AppAndRecoveryChecks','Endurance','PostEnduranceAndRemovalChecks')
foreach($name in $expected){if([regex]::Matches($log,'(?m)^\s+'+[regex]::Escape($name)+'\s*$').Count -ne 1){throw "Expected exactly one discovered $name test"}}
@{Version=$Version;Discovered=$expected;TestsExecuted=0;EquipmentAccessed=$false;FreshPackageCache=$true;VerifiedStagedPackages=$verifiedPackages}|ConvertTo-Json -Depth 4|Set-Content -LiteralPath "$results/verified.json" -Encoding utf8NoBOM
Get-Content "$results/verified.json" -Raw
