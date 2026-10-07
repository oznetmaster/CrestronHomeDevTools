// Copyright (c) 2026 Neil Colvin. MIT licensed.
namespace CrestronHomeDevTools.Automation;

/// <summary>Phase-two execution used by native NUnit tests. It cannot prepare, sign or deliver a submission.</summary>
public interface ISubmissionTestSession
{
 string RunDirectory { get; }
 Task<SubmissionWorkflowCheckpoint> RunStageAsync(SubmissionWorkflowStage stage,CancellationToken cancellationToken);
}

/// <summary>A selected test has unfinished prerequisites. No prerequisite is automatically executed.</summary>
public sealed class SubmissionTestPrerequisiteException(SubmissionWorkflowStage required)
 :InvalidOperationException($"Complete {required} before running the selected test.")
{
 public SubmissionWorkflowStage RequiredStage { get; }=required;
}

/// <summary>Runs the existing, pinned stage adapters without a scheduled worker or alternate result format.
/// Waiting operations are recovered with the same durable identity; cancellation never records a pass.</summary>
public sealed class SubmissionTestSession:ISubmissionTestSession
{
 private readonly SubmissionAutomationSettings settings;
 private readonly ISubmissionWorkflowSteps steps;
 private readonly Action<SubmissionWorkflowStepContext> verify;
 private readonly Func<CancellationToken,Task> wait;
 public string RunDirectory=>Path.Combine(settings.PrivateRoot,SubmissionWorkflow.RunKey(settings.Release));

 /// <summary>Load the same private settings and digest used by CI. Does not execute tests or load credentials.</summary>
 public static SubmissionTestSession FromSettings(string path,string sha256) {
  var request=AutomationRequest.Load(["--settings",path,"--settings-sha256",sha256]);
  var stages=new SubmissionAutomationStages(request.Settings,request.Sha256);
  return new(request.Settings,stages,stages.VerifyCompletedEvidence,
   token=>Task.Delay(TimeSpan.FromSeconds(5),token));
 }
 internal SubmissionTestSession(SubmissionAutomationSettings settings,ISubmissionWorkflowSteps steps,
  Action<SubmissionWorkflowStepContext> verify,Func<CancellationToken,Task> wait) {
  this.settings=settings;this.steps=steps;this.verify=verify;this.wait=wait;
 }
 public async Task<SubmissionWorkflowCheckpoint> RunStageAsync(SubmissionWorkflowStage stage,CancellationToken cancellationToken) {
  if(stage is not (SubmissionWorkflowStage.ValidateCandidate or SubmissionWorkflowStage.WindowsTests or
   SubmissionWorkflowStage.ProcessorTests or SubmissionWorkflowStage.AppTests or SubmissionWorkflowStage.Endurance or SubmissionWorkflowStage.FinalizeTests))
   throw new ArgumentOutOfRangeException(nameof(stage),"Only phase-two stages can be selected by a test.");
  if(Environment.GetEnvironmentVariable("CRESTRON_HOME_WORKFLOW_ACTIVE")=="1")
   throw new InvalidOperationException("Do not include the submission orchestration fixture in its own local test selection.");
  cancellationToken.ThrowIfCancellationRequested();
  SubmissionWorkflow.Open(settings.PrivateRoot,settings.Release);
  while(true) {
   cancellationToken.ThrowIfCancellationRequested();
   var before=SubmissionWorkflow.Read(settings.PrivateRoot,settings.Release);
   if(!before.CompletedStages.ContainsKey(stage) && before.Stage!=stage)
    throw new SubmissionTestPrerequisiteException(before.Stage);
   var state=await SubmissionWorkflow.AdvanceStageAsync(settings.PrivateRoot,settings.Release,steps,stage,cancellationToken).ConfigureAwait(false);
   verify(new(RunDirectory,state));
   if(state.CompletedStages.ContainsKey(stage) || state.Status is not (SubmissionWorkflowStatus.Waiting or SubmissionWorkflowStatus.Running))return state;
   // Operator waits and endurance can outlive an IDE session. The retained operation remains authoritative.
   await wait(cancellationToken).ConfigureAwait(false);
  }
 }
}
