// Copyright (c) 2026 Neil Colvin. MIT licensed.
using CrestronHomeDevTools.Automation;
using CrestronHomeNUnit.Workflow;
using NUnit.Framework;
namespace CrestronHomeDevTools.Tests;

[TestFixture]
public sealed class SubmissionTestSessionTests
{
 private string root=null!;
 private SubmissionAutomationSettings settings=null!;
 private Adapter adapter=null!;
 private int verified;
 [SetUp]public void Setup() {
  root=Path.Combine(TestContext.CurrentContext.WorkDirectory,"native-session-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);verified=0;adapter=new();
  var release=new SubmissionWorkflowRelease("fixture/driver",1,"v1",new('a',40),new('b',64),new('c',64),new('d',64));
  settings=new(1,root,release,root,new(Guid.NewGuid().ToString(),"1.0.0.0",PortalSubmissionKind.NewDriver,"Fixture"),"unused",
   new WorkflowPlan{Host="unused",CertificateSha256=new('e',64),SshFingerprint="unused",SourceRoots=[root],LocalTests=[],TestPackage=new("unused","unused","fixture",1),ProcessorSuites=[]});
 }
 [TearDown]public void Cleanup()=>Directory.Delete(root,true);
 private SubmissionTestSession Session(Func<CancellationToken,Task>? wait=null)=>new(settings,adapter,_=>verified++,wait??(_=>Task.CompletedTask));
 private sealed class Adapter:ISubmissionWorkflowSteps {
  internal int Started,Recovered;
  internal string? StartId,RecoveryId;
  internal SubmissionWorkflowStatus Initial=SubmissionWorkflowStatus.Completed;
  public Task<SubmissionWorkflowStepResult> ExecuteAsync(SubmissionWorkflowStepContext c,CancellationToken t) {
   Started++;StartId=c.Checkpoint.OperationId;return Task.FromResult(Initial==SubmissionWorkflowStatus.Completed?Complete(c):new(Initial,ReasonCode:"test-attention"));
  }
  public Task<SubmissionWorkflowStepResult> RecoverAsync(SubmissionWorkflowStepContext c,CancellationToken t) {
   Recovered++;RecoveryId=c.Checkpoint.OperationId;return Task.FromResult(Complete(c));
  }
  private static SubmissionWorkflowStepResult Complete(SubmissionWorkflowStepContext c) {
   string name=c.Checkpoint.Stage+".json",file=Path.Combine(c.RunDirectory,name);File.WriteAllText(file,"synthetic");
   return new(SubmissionWorkflowStatus.Completed,new(name,AutomationFiles.Hash(file)));
  }
 }
 [Test]public async Task WaitingTestRecoversWithoutRepeatingStartOrFollowingStages() {
  adapter.Initial=SubmissionWorkflowStatus.Waiting;int waits=0;
  var result=await Session(_=>{waits++;return Task.CompletedTask;}).RunStageAsync(SubmissionWorkflowStage.ValidateCandidate,default);
  Assert.That(result.Stage,Is.EqualTo(SubmissionWorkflowStage.WindowsTests));Assert.That(waits,Is.EqualTo(1));
  Assert.That(adapter.Started,Is.EqualTo(1));Assert.That(adapter.Recovered,Is.EqualTo(1));Assert.That(adapter.RecoveryId,Is.EqualTo(adapter.StartId));
 }
 [TestCase(SubmissionWorkflowStatus.Failed)][TestCase(SubmissionWorkflowStatus.NeedsInput)][TestCase(SubmissionWorkflowStatus.OutcomeUnknown)]
 public async Task AttentionNeverAutomaticallyRequestsRecoveryOrMarksAPass(SubmissionWorkflowStatus status) {
  adapter.Initial=status;var session=Session();var result=await session.RunStageAsync(SubmissionWorkflowStage.ValidateCandidate,default);
  Assert.That(result.Status,Is.EqualTo(status));Assert.That(result.CompletedStages,Is.Empty);
  await session.RunStageAsync(SubmissionWorkflowStage.ValidateCandidate,default);Assert.That(adapter.Started,Is.EqualTo(1));Assert.That(adapter.Recovered,Is.Zero);
 }
 [Test]public async Task CompletedSelectionReverifiesEvidenceWithoutExecutingTests() {
  var session=Session();await session.RunStageAsync(SubmissionWorkflowStage.ValidateCandidate,default);await session.RunStageAsync(SubmissionWorkflowStage.ValidateCandidate,default);
  Assert.That(verified,Is.EqualTo(2));Assert.That(adapter.Started,Is.EqualTo(1));
 }
 [Test]public async Task MissingPrerequisiteDoesNotStartEquipmentWork() {
  await Assert.ThrowsAsync<SubmissionTestPrerequisiteException>(async()=>await Session().RunStageAsync(SubmissionWorkflowStage.Endurance,default));
  Assert.That(adapter.Started,Is.Zero);
 }
 [TestCase(SubmissionWorkflowStage.PrepareReview)][TestCase(SubmissionWorkflowStage.SignReview)][TestCase(SubmissionWorkflowStage.Deliver)][TestCase(SubmissionWorkflowStage.Retain)]
 public async Task TestsCannotExecutePhaseThree(SubmissionWorkflowStage stage) {
  await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async()=>await Session().RunStageAsync(stage,default));Assert.That(adapter.Started,Is.Zero);
 }
 [Test]public async Task CancelDuringWaitRetainsOriginalOperationForLaterRecovery() {
  adapter.Initial=SubmissionWorkflowStatus.Waiting;using var stop=new CancellationTokenSource();
  var session=Session(t=>{stop.Cancel();return Task.Delay(Timeout.Infinite,t);});
  await Assert.ThrowsAsync<TaskCanceledException>(async()=>await session.RunStageAsync(SubmissionWorkflowStage.ValidateCandidate,stop.Token));
  Assert.That(SubmissionWorkflow.Read(root,settings.Release).Status,Is.EqualTo(SubmissionWorkflowStatus.Waiting));
  var resumed=await Session().RunStageAsync(SubmissionWorkflowStage.ValidateCandidate,default);
  Assert.That(resumed.CompletedStages.ContainsKey(SubmissionWorkflowStage.ValidateCandidate),Is.True);
  Assert.That(adapter.Started,Is.EqualTo(1));Assert.That(adapter.RecoveryId,Is.EqualTo(adapter.StartId));
 }
}
