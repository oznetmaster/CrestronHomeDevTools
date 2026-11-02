[xml]$properties = Get-Content Directory.Build.props -Raw
$version = "$($properties.Project.PropertyGroup.Version)-ci.$env:GITHUB_RUN_ID.$env:GITHUB_RUN_ATTEMPT"
foreach ($id in @('CrestronHomeDevTools','CrestronHomeDevTools.Automation','CrestronHomeDevTools.SubmissionTests')) {
  dotnet pack "$id/$id.csproj" -c Release "-p:Version=$version" -o artifacts/packages
  if ($LASTEXITCODE -ne 0) { throw "$id pack failed." }
}
./tools/Test-SubmissionNuGetConsumer.ps1 -PackageDirectory artifacts/packages -Version $version -ResultsDirectory artifacts/package-consumer
