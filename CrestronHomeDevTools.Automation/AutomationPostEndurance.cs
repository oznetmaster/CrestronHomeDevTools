// Copyright (c) 2026 Neil Colvin. MIT licensed.
using System.Net;
using CrestronHomeNUnit.Workflow;

namespace CrestronHomeDevTools.Automation;

internal static class AutomationPostEndurance
{
 internal const string DirectoryName="post-endurance";
 private sealed record Binding(string InputSha256,SubmissionWorkflowReceipt Endurance);
 internal static SubmissionAutomationSettings Settings(SubmissionAutomationSettings settings) {
  if(settings.PostEnduranceTests==null)throw new InvalidDataException("Post-endurance tests are not configured.");
  return settings with{InstalledAppTests=settings.PostEnduranceTests,NUnit=settings.NUnit with{AndroidTests=null},
   InstalledAppFixtureSettings=settings.PostEnduranceFixtureSettings??settings.InstalledAppFixtureSettings};
 }
 internal static void Validate(SubmissionAutomationSettings settings) {
  if(settings.PostEnduranceFromDeployment && (settings.PostEnduranceTests==null || settings.NUnit.ActualDriver==null || settings.NUnit.ReleaseCandidate==null))
   throw new InvalidDataException("Deployment-bound post-endurance checks require a test plan and an actual candidate deployment.");
  if(settings.PostEnduranceTests==null) {
   if(settings.PostEnduranceFixtureSettings!=null)throw new InvalidDataException("Post-endurance fixture inputs require a test plan.");
   return;
  }
  if(settings.Endurance==null)throw new InvalidDataException("Post-endurance tests require an endurance plan.");
  AutomationInstalledApp.ValidateTemplate(Settings(settings),settings.PostEnduranceFromDeployment);
 }
 internal static SubmissionAutomationSettings Resolve(SubmissionWorkflowStepContext context,SubmissionAutomationSettings settings) {
  var resolved=Settings(settings);
  if(settings.ManagedDevices!=null && resolved.InstalledAppFixtureSettings is {} fixture)
   resolved=resolved with{InstalledAppFixtureSettings=AutomationManagedDevices.RenderInputs(fixture,AutomationManagedDevices.VerifyRetained(context))};
  if(!settings.PostEnduranceFromDeployment)return resolved;
  var deployment=AutomationDeploymentEvidence.Read(context,settings);
  var plan=resolved.InstalledAppTests!;
  var target=plan.Target;
  var actual=settings.NUnit.ActualDriver!;
  if(target.Name!=actual.InstanceName || target.LocationId!=actual.LocationId ||
   target.Model!=deployment.Installed.Model || Version.Parse(target.Version)!=Version.Parse(deployment.Installed.Version) ||
   (actual.ExpectedDeviceId is {} expected && expected!=deployment.Installed.DeviceId))
   throw new InvalidDataException("Post-endurance expectations differ from the actual deployment.");
  return resolved with{InstalledAppTests=plan with{Target=target with{
   DeviceId=deployment.Installed.DeviceId,CatalogueId=deployment.Imported.CatalogueId}}};
 }
 private static Binding Expected(SubmissionWorkflowStepContext context) {
  if(!context.Checkpoint.CompletedStages.TryGetValue(SubmissionWorkflowStage.Endurance,out var receipt) ||
   !SubmissionEvidence.SafeEvidencePath(context.RunDirectory,receipt.RelativePath.Replace('\\','/'),out var path) ||
   AutomationFiles.Hash(path)!=receipt.Sha256)
   throw new InvalidDataException("Post-endurance tests require an intact completed endurance receipt.");
  return new(context.Checkpoint.InputSha256,receipt);
 }
 internal static async Task<SubmissionWorkflowStepResult> Advance(SubmissionWorkflowStepContext context,
  SubmissionAutomationSettings settings,bool recover,
  Func<InstalledDriverTestPlan,NetworkCredential,string,CancellationToken,Task<InstalledDriverTestResult>> run,
  Func<string,NetworkCredential> credentials,CancellationToken token) {
  Validate(settings);
  var binding=Expected(context);
  string folder=Path.Combine(context.RunDirectory,DirectoryName);
  Directory.CreateDirectory(folder);
  AutomationFiles.Write(Path.Combine(folder,"endurance-binding.json"),binding);
  var resolved=Resolve(context,settings);
  if(File.Exists(Path.Combine(folder,"installed-app-intent.json")) &&
   !SubmissionEvidence.SafeEvidencePath(folder,"target-plan.json",out _))
   throw new InvalidDataException("The retained post-endurance target plan is missing or unsafe.");
  AutomationFiles.Write(Path.Combine(folder,"target-plan.json"),resolved.InstalledAppTests);
  var result=await AutomationInstalledApp.Advance(context with{RunDirectory=folder},resolved,recover,run,credentials,token);
  if(result.Status==SubmissionWorkflowStatus.Completed)VerifyRetained(context);
  return result;
 }
 internal static void VerifyRetained(SubmissionWorkflowStepContext context) {
  string folder=Path.Combine(context.RunDirectory,DirectoryName);
  if(AutomationFiles.Read<Binding>(Path.Combine(folder,"endurance-binding.json"))!=Expected(context))
   throw new InvalidDataException("Post-endurance tests belong to another completed segment.");
  AutomationInstalledApp.VerifyRetained(folder);
 }
 internal static IEnumerable<SubmissionWorkflowReceipt> RetainedFiles(SubmissionWorkflowStepContext context) {
  VerifyRetained(context);
  string folder=Path.Combine(context.RunDirectory,DirectoryName);
  // The installed-app verifier checks its exact bounded inventory before it is read here.
  using var document=System.Text.Json.JsonDocument.Parse(File.ReadAllBytes(Path.Combine(folder,"installed-app-tests.json")));
  return document.RootElement.GetProperty("Files").EnumerateArray().Select(f=>new SubmissionWorkflowReceipt(
   DirectoryName+"/"+f.GetProperty("RelativePath").GetString()!.Replace('\\','/'),f.GetProperty("Sha256").GetString()!)).ToArray();
 }
}
