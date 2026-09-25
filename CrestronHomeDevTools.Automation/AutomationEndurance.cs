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

internal sealed class AutomationEndurance(string directory, SubmissionEnduranceWorkerPlan worker, NetworkCredential credential) : IAutomationEndurance
{
 internal static void ValidateReservation(SubmissionEnduranceWorkerPlan? worker) {
  if(worker!=null && !Guid.TryParseExact(worker.Plan.ReservationId,"N",out _))
   throw new InvalidDataException("Endurance.Plan.ReservationId must be a unique GUID in N format; release templates can use ${reservationId}.");
 }
 public Task Start(CancellationToken t)=>SubmissionEnduranceMonitor.StartAsync(directory,worker.Plan,worker.Processor,credential,t);
 public SubmissionEnduranceMonitorStatus Read()=>SubmissionEnduranceMonitor.ReadStatus(directory,worker.Plan,worker.Processor);
 public Task<SubmissionEnduranceCheckpoint> Collect(CancellationToken t)=>SubmissionEnduranceMonitor.CollectAsync(directory,worker.Plan,worker.Processor,credential,
  ct=>SubmissionEnduranceProcessProbe.RunAsync(worker.Probe,worker.Plan,ct),t);
 public Task Finish(CancellationToken t)=>SubmissionEnduranceMonitor.FinishAsync(directory,worker.Plan,worker.Processor,credential,t);
 public SubmissionObservation Export()=>SubmissionEndurance.Export(SubmissionEnduranceMonitor.GetEvidenceDirectory(directory),worker.Plan,DateTimeOffset.UtcNow);

 internal static async Task<SubmissionWorkflowStepResult> Advance(SubmissionWorkflowStepContext c, bool recover, IAutomationEndurance monitor,CancellationToken token)
 {
  string directory=Path.Combine(c.RunDirectory,"endurance");
  if(!Directory.Exists(directory)) {
   if(recover)return new(SubmissionWorkflowStatus.OutcomeUnknown,ReasonCode:"inspect-endurance-start");
   await monitor.Start(token);
  }
  var status=monitor.Read();
  if(status.ReservationState is not ("Held" or "Released") || status.Checkpoint?.State is SubmissionEnduranceState.ProbePending or SubmissionEnduranceState.Interrupted)
   return new(SubmissionWorkflowStatus.OutcomeUnknown,ReasonCode:"inspect-endurance-operation");
  var checkpoint=status.Checkpoint;
  if(checkpoint?.State is not (SubmissionEnduranceState.Passed or SubmissionEnduranceState.Failed)) {
   checkpoint=await monitor.Collect(token);
   if(checkpoint.State==SubmissionEnduranceState.Collecting)
    return new(SubmissionWorkflowStatus.Waiting,ReasonCode:"endurance-collecting");
   if(checkpoint.State!=SubmissionEnduranceState.Passed && checkpoint.State!=SubmissionEnduranceState.Failed)
    return new(SubmissionWorkflowStatus.OutcomeUnknown,ReasonCode:"inspect-endurance-operation");
  }
  // Both known terminal outcomes release the owned read-only reservation. Failed evidence stays failed.
  if(status.ReservationState!="Released")await monitor.Finish(token);
  if(monitor.Read().ReservationState!="Released")throw new InvalidDataException("Endurance release was not confirmed.");
  if(checkpoint.State==SubmissionEnduranceState.Failed)
   return new(SubmissionWorkflowStatus.Failed,ReasonCode:"endurance-probe-failed");
  return AutomationFiles.Complete(c,"endurance-evidence.json",new { EvidenceDirectory="endurance/observations",Observation=monitor.Export() });
 }
}
