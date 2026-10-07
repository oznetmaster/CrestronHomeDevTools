// Copyright (c) 2026 Neil Colvin. MIT licensed.
using System.Net;

namespace CrestronHomeDevTools.Automation;

internal interface IAutomationEndurance
{
 Task Start(CancellationToken token);
 SubmissionEnduranceMonitorStatus Read();
 Task<SubmissionEnduranceCheckpoint> Collect(CancellationToken token);
 Task Finish(CancellationToken token);
 SubmissionObservation Export();
}

internal sealed class AutomationEndurance(string directory, SubmissionEnduranceWorkerPlan worker, NetworkCredential credential,
 Func<CancellationToken,Task>? preflight=null) : IAutomationEndurance
{
 internal static void ValidateReservation(SubmissionEnduranceWorkerPlan? worker) {
  if(worker!=null && !Guid.TryParseExact(worker.Plan.ReservationId,"N",out _))
   throw new InvalidDataException("Endurance.Plan.ReservationId must be a unique GUID in N format; release templates can use ${reservationId}.");
 }
 public async Task Start(CancellationToken t) {
  if(preflight!=null)await preflight(t);
  await SubmissionEnduranceMonitor.StartAsync(directory,worker.Plan,worker.Processor,credential,t);
 }
 public SubmissionEnduranceMonitorStatus Read()=>SubmissionEnduranceMonitor.ReadStatus(directory,worker.Plan,worker.Processor);
 public Task<SubmissionEnduranceCheckpoint> Collect(CancellationToken t)=>SubmissionEnduranceMonitor.CollectAsync(directory,worker.Plan,worker.Processor,credential,
  ct=>SubmissionEnduranceProcessProbe.RunAsync(worker.Probe,worker.Plan,ct),t);
 public Task Finish(CancellationToken t)=>SubmissionEnduranceMonitor.FinishAsync(directory,worker.Plan,worker.Processor,credential,t);
 public SubmissionObservation Export()=>SubmissionEndurance.Export(SubmissionEnduranceMonitor.GetEvidenceDirectory(directory),worker.Plan,DateTimeOffset.UtcNow);

 internal static void VerifyRetained(SubmissionWorkflowStepContext c,SubmissionAutomationSettings settings) {
  const string name="endurance-evidence.json";
  if(!c.Checkpoint.CompletedStages.TryGetValue(SubmissionWorkflowStage.Endurance,out var receipt) || receipt.RelativePath!=name ||
   !SubmissionEvidence.SafeEvidencePath(c.RunDirectory,name,out var path) || AutomationFiles.Hash(path)!=receipt.Sha256)
   throw new InvalidDataException("Completed endurance receipt is missing or changed; verification cannot recreate it.");
  var plan=AutomationEnduranceSelection.Resolve(c,settings,verifyOnly:true)?.Plan
   ??throw new InvalidDataException("Completed endurance plan is missing.");
  // Revalidate original samples and the same frozen plan without a producer, export write or checkpoint repair.
  var observation=SubmissionEndurance.Export(Path.Combine(c.RunDirectory,AutomationEnduranceSelection.EvidenceDirectory(c)),plan,DateTimeOffset.UtcNow);
  var expected=System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new{EvidenceDirectory=AutomationEnduranceSelection.EvidenceDirectory(c),Observation=observation},AutomationFiles.Json);
  if(!File.ReadAllBytes(path).AsSpan().SequenceEqual(expected))
   throw new InvalidDataException("Completed endurance receipt differs from its original collection.");
 }

 internal static async Task<SubmissionWorkflowStepResult> Advance(SubmissionWorkflowStepContext c, bool recover, IAutomationEndurance monitor,CancellationToken token,bool initialGateCompletedNow=false,string relativeDirectory="endurance")
 {
  string directory=Path.Combine(c.RunDirectory,relativeDirectory);
  if(!Directory.Exists(directory)) {
   // A gate completed for the first time in this invocation proves collection
   // could not previously start. Missing journals after an older gate remain uncertain.
   if(recover && !initialGateCompletedNow)return new(SubmissionWorkflowStatus.OutcomeUnknown,ReasonCode:"inspect-endurance-start");
   await monitor.Start(token);
  }
  var status=monitor.Read();
  if(status.ReservationState is not ("Held" or "Released") || status.Checkpoint?.State is SubmissionEnduranceState.ProbePending or SubmissionEnduranceState.Interrupted)
   return new(SubmissionWorkflowStatus.OutcomeUnknown,ReasonCode:"inspect-endurance-operation");
  var checkpoint=status.Checkpoint;
  if(checkpoint?.State is not (SubmissionEnduranceState.Passed or SubmissionEnduranceState.Failed)) {
   checkpoint=await monitor.Collect(token);
   if(checkpoint.State==SubmissionEnduranceState.Collecting)
    return new(SubmissionWorkflowStatus.Waiting,ReasonCode:checkpoint.Reason=="inconclusive-observation-retained"
     ?"endurance-collecting-with-issues":"endurance-collecting");
   if(checkpoint.State!=SubmissionEnduranceState.Passed && checkpoint.State!=SubmissionEnduranceState.Failed)
    return new(SubmissionWorkflowStatus.OutcomeUnknown,ReasonCode:"inspect-endurance-operation");
  }
  // Both known terminal outcomes release the owned read-only reservation. Failed evidence stays failed.
  if(status.ReservationState!="Released")await monitor.Finish(token);
  if(monitor.Read().ReservationState!="Released")throw new InvalidDataException("Endurance release was not confirmed.");
  if(checkpoint.State==SubmissionEnduranceState.Failed)
   return new(SubmissionWorkflowStatus.Failed,ReasonCode:"endurance-probe-failed");
  return AutomationFiles.Complete(c,"endurance-evidence.json",new { EvidenceDirectory=relativeDirectory+"/observations",Observation=monitor.Export() });
 }
}
