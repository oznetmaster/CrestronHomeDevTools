// Copyright (c) 2026 Neil Colvin. MIT licensed.
using CrestronHomeDevTools.Automation;
using NUnit.Framework;

namespace CrestronHomeDevTools.SubmissionTests;

/// <summary>Inherit in a dedicated NUnit test project for both IDE and CI execution.
/// Original NUnit/TRX/Workflow.json and evidence remain authoritative; these tests expose the durable stages.
/// Discovery never reads private settings, resolves credentials or accesses equipment.</summary>
[NonParallelizable]
[Category("Submission")]
public abstract class SubmissionFixture
{
 /// <summary>Environment variable containing the private settings path; its _SHA256 companion pins those bytes.</summary>
 protected abstract string SettingsEnvironment { get; }
 protected virtual ISubmissionTestSession CreateSession() {
  string? path=Environment.GetEnvironmentVariable(SettingsEnvironment);
  string? pin=Environment.GetEnvironmentVariable(SettingsEnvironment+"_SHA256");
  if(string.IsNullOrWhiteSpace(path)||string.IsNullOrWhiteSpace(pin))
   Assert.Ignore("Select the private submission settings and their _SHA256 digest before running this suite.");
  return SubmissionTestSession.FromSettings(path!,pin!);
 }
 [Test] public Task CandidateValidation()=>Run(SubmissionWorkflowStage.ValidateCandidate);
 [Test,DependsOnTest(nameof(CandidateValidation))] public Task LocalAndProcessorChecks()=>Run(SubmissionWorkflowStage.WindowsTests);
 [Test,DependsOnTest(nameof(LocalAndProcessorChecks))] public Task ProcessorEvidence()=>Run(SubmissionWorkflowStage.ProcessorTests);
 [Test,DependsOnTest(nameof(ProcessorEvidence))] public Task AppAndRecoveryChecks()=>Run(SubmissionWorkflowStage.AppTests);
 [Test,Explicit("Select endurance deliberately after the initial tests pass.")]
 public Task Endurance()=>Run(SubmissionWorkflowStage.Endurance);
 [Test] public Task PostEnduranceAndRemovalChecks()=>Run(SubmissionWorkflowStage.FinalizeTests);

 private async Task Run(SubmissionWorkflowStage stage) {
  var session=CreateSession();
  SubmissionWorkflowCheckpoint state;
  try {state=await session.RunStageAsync(stage,TestContext.CurrentContext.CancellationToken).ConfigureAwait(false);}
  catch(SubmissionTestPrerequisiteException e) {Assert.Inconclusive(e.Message);return;}
  catch(OperationCanceledException) {throw;}
  catch(Exception error) when(error is not OutOfMemoryException) {
   // Private settings and transport exception text can include credentials. Keep raw evidence private.
   Assert.Fail($"Submission test could not complete ({error.GetType().Name}). Inspect its retained run: {session.RunDirectory}");return;
  }
  if(!state.CompletedStages.TryGetValue(stage,out var receipt)) {
   Assert.Fail($"Submission test is {state.Status}: {state.ReasonCode}. Retained run: {session.RunDirectory}");return;
  }
  TestContext.AddTestAttachment(Path.Combine(session.RunDirectory,receipt.RelativePath),$"Verified {stage} receipt; original test results remain in this run.");
 }
}
