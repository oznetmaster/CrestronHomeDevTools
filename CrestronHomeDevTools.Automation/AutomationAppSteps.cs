// Copyright (c) 2026 Neil Colvin. MIT licensed.
using System.Net;
using CrestronHomeNUnit.Workflow;

namespace CrestronHomeDevTools.Automation;

/// <summary>Persisted test boundaries, so unattended human waits never occupy a test host or lease.</summary>
internal static class AutomationAppSteps
{
 internal static void Validate(SubmissionAutomationSettings settings) {
  if(settings.InstalledAppSteps is not {} steps)return;
  var selected=settings.InstalledAppTests?.AndroidTests.RequiredTests?.ToArray();
  if(steps.Length is <1 or >128 || selected==null || steps.Any(s=>s.Tests==null || s.Tests.Length==0 ||
   s.Tests.Any(string.IsNullOrWhiteSpace) || (s.OperatorInstructions!=null &&
   (s.Tests.Length!=1 || string.IsNullOrWhiteSpace(s.OperatorInstructions) || s.OperatorInstructions.Length>3000))) ||
   steps.Any(s=>s.OperatorInstructions!=null) && settings.OperatorInbox==null)
   throw new InvalidDataException("App steps require exact selected tests and a declared operator inbox for each single-test manual step.");
  var actual=steps.SelectMany(s=>s.Tests).ToArray();
  if(actual.Distinct(StringComparer.Ordinal).Count()!=actual.Length ||
   !actual.Order(StringComparer.Ordinal).SequenceEqual(selected.Order(StringComparer.Ordinal)))
   throw new InvalidDataException("App steps must cover every selected test exactly once.");
 }

 internal static async Task<SubmissionWorkflowStepResult> Advance(SubmissionWorkflowStepContext context,
  SubmissionAutomationSettings settings,
  Func<InstalledDriverTestPlan,NetworkCredential,string,CancellationToken,Task<InstalledDriverTestResult>> run,
  Func<string,NetworkCredential> credentials,CancellationToken token) {
  Validate(settings);
  var plan=settings.InstalledAppTests!;
  string appRoot=Path.Combine(context.RunDirectory,"installed-app");Directory.CreateDirectory(appRoot);
  string phaseKey=Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(
   System.Text.Encoding.UTF8.GetBytes(Path.GetFullPath(context.RunDirectory))))[..16];
  AutomationFiles.Write(Path.Combine(appRoot,"steps-binding.json"),new {
   context.Checkpoint.InputSha256,SourceDigest=await WorkflowEvidence.SourceDigestAsync(plan.SourceRoots,token),
   ProfileSha256=AutomationFiles.Hash(plan.AndroidTests.ProfilePath),Plan=plan});
  AutomationAppFixture.Check(context.RunDirectory,settings,true);
  AutomationFiles.Write(Path.Combine(appRoot,"steps-plan.json"),settings.InstalledAppSteps!);
  for(int index=0;index<settings.InstalledAppSteps!.Length;index++) {
   token.ThrowIfCancellationRequested();
   var step=settings.InstalledAppSteps[index];
   string stepRoot=Path.Combine(appRoot,"steps",index.ToString("D3",System.Globalization.CultureInfo.InvariantCulture));
   Directory.CreateDirectory(stepRoot);
   var child=settings with {InstalledAppSteps=null,InstalledAppTests=plan with {
    AndroidTests=plan.AndroidTests with {RequiredTests=step.Tests}}};
   var childContext=context with {RunDirectory=stepRoot};
   string receipt=Path.Combine(stepRoot,"installed-app-tests.json");
   if(File.Exists(receipt)) {AutomationInstalledApp.VerifyRetained(stepRoot);continue;}
   if(step.OperatorInstructions is {} instructions) {
    var inbox=settings.OperatorInbox!;
    var handle=SubmissionOperatorStep.GetOrCreateReadiness(inbox.Directory,inbox.RunKey,$"app-{phaseKey}-step-{index:D3}.ready",
     $"{plan.Host}: {plan.Target.Name}",instructions+"\nDo not operate the device yet. Choose I'm ready, then wait for the recording prompt.");
    // Persist the binding before yielding. A restart reopens this exact request, never a replacement.
    AutomationFiles.Write(Path.Combine(stepRoot,"readiness-handle.json"),handle);
    var status=SubmissionOperatorStep.Read(handle);
    if(status.Waiting)return new(SubmissionWorkflowStatus.Waiting,ReasonCode:"operator-readiness-pending");
    AutomationFiles.Write(Path.Combine(stepRoot,"readiness-response.json"),status);
    if(status.Response!.Outcome!=SubmissionOperatorOutcome.Done)
     return new(SubmissionWorkflowStatus.NeedsInput,ReasonCode:"operator-cannot-perform-action");
   }
   bool recover=File.Exists(Path.Combine(stepRoot,"installed-app-intent.json"));
   var outcome=await AutomationInstalledApp.Advance(childContext,child,recover,run,credentials,token);
   if(outcome.Status!=SubmissionWorkflowStatus.Completed) {
    // No following manual request is published. Failed/uncertain attempts remain immutable.
    AutomationFiles.Write(Path.Combine(stepRoot,"attention.json"),outcome);
    return outcome.Status==SubmissionWorkflowStatus.Failed
     ?new(SubmissionWorkflowStatus.NeedsInput,ReasonCode:"app-step-failed-inspect-retained-evidence")
     :outcome;
   }
  }
  return AutomationFiles.Complete(context,"installed-app-tests.json",new {
   context.Checkpoint.InputSha256,Files=AutomationInstalledApp.Inventory(context.RunDirectory)});
 }
}
