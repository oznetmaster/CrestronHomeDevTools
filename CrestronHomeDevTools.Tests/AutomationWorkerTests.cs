// Copyright (c) 2026 Neil Colvin. MIT licensed.
using CrestronHomeDevTools.Automation;
using CrestronHomeNUnit.Workflow;
using NUnit.Framework;
namespace CrestronHomeDevTools.Tests;

[TestFixture]
public sealed class AutomationWorkerTests
{
 private string root=null!,registry=null!;
 private SubmissionAutomationSettings settings=null!;
 [SetUp]public void Setup() {
  root=Path.Combine(TestContext.CurrentContext.WorkDirectory,"automation-worker-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
  var release=new SubmissionWorkflowRelease("fixture/driver",3,"v1",new('a',40),new('b',64),new('c',64),new('d',64));
  settings=new(1,root,release,root,new(Guid.NewGuid().ToString(),"1.0.0.0",PortalSubmissionKind.NewDriver,"Fixture","fixture@example.org"),"unused",
   new WorkflowPlan{Host="unused",CertificateSha256=new('e',64),SshFingerprint="unused",SourceRoots=[root],LocalTests=[],TestPackage=new("unused.csproj","unused.pkg","fixture",1),ProcessorSuites=[]});
  string path=Path.Combine(root,"settings.json");AutomationFiles.Write(path,settings);
  registry=Path.Combine(root,"registry.json");AutomationFiles.Write(registry,new SubmissionAutomationRegistry(1,[new("fixture",3,SubmissionAutomationMode.Rehearsal,path,AutomationFiles.Hash(path))]));
  SubmissionWorkflow.Open(root,release);
 }
 [TearDown]public void Cleanup()=>Directory.Delete(root,true);
 [Test]public async Task ProtectedWorkerDoesNotRunEvidenceStages() {
  int calls=0;
  var states=await AutomationWorker.Tick(registry,SubmissionAutomationWorkerRole.Protected,default,(_,_)=>{calls++;throw new Exception("must not execute");});
  Assert.That(calls,Is.Zero);Assert.That(states.Single().Stage,Is.EqualTo("ValidateCandidate"));
 }
 [Test]public async Task EvidenceWorkerAdvancesItsWaitingRunWithoutAnAiPrompt() {
  int calls=0;
  var states=await AutomationWorker.Tick(registry,SubmissionAutomationWorkerRole.Evidence,default,(request,_)=> {
   calls++;Assert.That(request.Settings.Release,Is.EqualTo(settings.Release));
   return Task.FromResult(SubmissionWorkflow.Read(root,settings.Release) with{Stage=SubmissionWorkflowStage.Endurance,Status=SubmissionWorkflowStatus.Waiting,ReasonCode="endurance-collecting"});
  });
  Assert.That(calls,Is.EqualTo(1));Assert.That(states.Single().Reason,Is.EqualTo("endurance-collecting"));
 }
 private sealed class StopSteps:ISubmissionWorkflowSteps {
  public Task<SubmissionWorkflowStepResult> ExecuteAsync(SubmissionWorkflowStepContext c,CancellationToken t)=>Task.FromResult(new SubmissionWorkflowStepResult(SubmissionWorkflowStatus.NeedsInput,ReasonCode:"fixture-missing-input"));
  public Task<SubmissionWorkflowStepResult> RecoverAsync(SubmissionWorkflowStepContext c,CancellationToken t)=>throw new Exception("No implicit recovery");
 }
 private sealed class JsonFailureSteps:ISubmissionWorkflowSteps {
  public Task<SubmissionWorkflowStepResult> ExecuteAsync(SubmissionWorkflowStepContext c,CancellationToken t)=>throw new System.Text.Json.JsonException("Synthetic private diagnostic must not enter public status");
  public Task<SubmissionWorkflowStepResult> RecoverAsync(SubmissionWorkflowStepContext c,CancellationToken t)=>throw new InvalidOperationException("No recovery in this test");
 }
 [Test]public async Task AdapterExceptionReportsAttentionAndExactStageEvenWithRunningCheckpoint() {
  var states=await AutomationWorker.Tick(registry,SubmissionAutomationWorkerRole.Evidence,default,
   (request,t)=>SubmissionWorkflow.AdvanceAsync(root,settings.Release,new JsonFailureSteps(),t));
  Assert.That(SubmissionWorkflow.Read(root,settings.Release).Status,Is.EqualTo(SubmissionWorkflowStatus.Running));
  Assert.That(states.Single().State,Is.EqualTo("AttentionRequired"));
  Assert.That(states.Single().Stage,Is.EqualTo("ValidateCandidate"));
  Assert.That(states.Single().Reason,Is.EqualTo("JsonException"));
 }
 [Test]public async Task WorkerDoesNotResetAnAttentionState() {
  await SubmissionWorkflow.AdvanceAsync(root,settings.Release,new StopSteps());
  int calls=0;var states=await AutomationWorker.Tick(registry,SubmissionAutomationWorkerRole.Evidence,default,(_,_)=>{calls++;throw new Exception("must not execute");});
  Assert.That(calls,Is.Zero);Assert.That(states.Single().State,Is.EqualTo("NeedsInput"));
 }
 [Test]public void UnchangedPollingCreatesNoAdditionalHistoryOrStatusWrite() {
  var states=new[]{new AutomationWorker.Status("fixture",3,SubmissionAutomationMode.Rehearsal,"Waiting","Endurance","endurance-collecting")};
  Assert.That(AutomationWorker.SaveStatus(root,states),Is.True);
  string status=Path.Combine(root,"worker-status.json"),history=Path.Combine(root,"worker-history.jsonl");
  byte[] before=File.ReadAllBytes(history);DateTime stamp=File.GetLastWriteTimeUtc(status);
  for(int i=0;i<50;i++)Assert.That(AutomationWorker.SaveStatus(root,states),Is.False);
  Assert.That(File.ReadAllBytes(history),Is.EqualTo(before));Assert.That(File.GetLastWriteTimeUtc(status),Is.EqualTo(stamp));
 }
 [Test]public void ChangedStatusRotatesBoundedHistoryInsteadOfAddingThousandsOfFiles() {
  string history=Path.Combine(root,"worker-history.jsonl");File.WriteAllBytes(history,new byte[1024*1024]);
  AutomationWorker.SaveStatus(root,[new("fixture",3,SubmissionAutomationMode.Rehearsal,"NeedsInput","SignReview","rehearsal-ready-for-review")]);
  Assert.That(new FileInfo(history+".1").Length,Is.EqualTo(1024*1024));Assert.That(new FileInfo(history).Length,Is.LessThan(4096));
 }
 [Test]public void NoticesHaveUtcTimestampsAndIgnoreLockContentionIncludingRecovery() {
  var time=new DateTimeOffset(2026,9,27,8,0,0,TimeSpan.FromHours(1));
  var waiting=new AutomationWorker.Status("fixture",3,SubmissionAutomationMode.Submit,"Waiting","Endurance","endurance-collecting");
  var busy=waiting with{State="Busy",Stage=null,Reason="workflow-in-use"};
  Assert.That(AutomationWorker.Notification(root,[busy],time),Is.Null);
  Assert.That(AutomationWorker.Notification(root,[waiting],time),Does.StartWith("[27 Sep 2026, 07:00:00 UTC]"));
  for(int i=0;i<5;i++) {
   Assert.That(AutomationWorker.Notification(root,[busy],time.AddMinutes(i)),Is.Null);
   Assert.That(AutomationWorker.Notification(root,[waiting],time.AddMinutes(i)),Is.Null);
  }
  // A real failure must still surface even after an unreadable lock interval.
  var failure=waiting with{State="AttentionRequired",Reason="IOException"};
  Assert.That(AutomationWorker.Notification(root,[failure],time),Is.Not.Null);
  Assert.That(AutomationWorker.Notification(root,[busy],time),Is.Null);
  Assert.That(AutomationWorker.Notification(root,[failure],time),Is.Null);
  Assert.That(AutomationWorker.Notification(root,[waiting],time),Is.Not.Null);
  Assert.That(AutomationWorker.Notification(root,[waiting with{State="NeedsInput",Stage="SignReview",Reason="approval-required"}],time),Is.Not.Null);
  Assert.That(AutomationWorker.Notification(root,[waiting with{State="Completed",Stage="Complete",Reason=null}],time),Is.Not.Null);
 }
 [Test]public void BusyEntryDoesNotHideAnotherReleasesMeaningfulChange() {
  var time=DateTimeOffset.UtcNow;
  var a=new AutomationWorker.Status("one",1,SubmissionAutomationMode.Submit,"Waiting","Endurance","endurance-collecting");
  var b=a with{Profile="two",ReleaseId=2};
  Assert.That(AutomationWorker.Notification(root,[a,b],time),Is.Not.Null);
  var busy=a with{State="Busy",Stage=null,Reason="workflow-in-use"};
  var failed=b with{State="AttentionRequired",Reason="IOException"};
  Assert.That(AutomationWorker.Notification(root,[busy,failed],time),Is.Not.Null);
  Assert.That(AutomationWorker.Notification(root,[a,failed],time),Is.Null);
 }
 [TestCase("Waiting","Endurance","endurance-collecting",false)]
 [TestCase("Waiting","Endurance","endurance-collecting-with-issues",true)]
 [TestCase("Waiting","SignReview","worker-role-handoff",false)]
 [TestCase("Running","WindowsTests",null,false)]
 [TestCase("Completed","Complete",null,false)]
 [TestCase("Waiting","SignReview","signing-authorization-required",true)]
 [TestCase("Waiting","Upload","delivery-authorization-required",true)]
 [TestCase("Waiting","Endurance","unknown-wait",true)]
 [TestCase("NeedsInput","SignReview","review-required",true)]
 [TestCase("Failed","Endurance","probe-failed",true)]
 [TestCase("AttentionRequired",null,"IOException",true)]
 [TestCase("OutcomeUnknown","Upload",null,true)]
 public void NoticesDistinguishUnattendedWorkFromRequiredAction(string state,string? stage,string? reason,bool action) {
  var message=AutomationWorker.NoticeText([new("fixture",1,SubmissionAutomationMode.Submit,state,stage,reason)]);
  Assert.That(message.Contains("Inspect the private worker status",StringComparison.Ordinal),Is.EqualTo(action));
  Assert.That(message.Contains("No action required",StringComparison.Ordinal),Is.EqualTo(!action));
 }
 [Test]public void AnotherRunsApprovalCannotBeHiddenByHealthyEndurance() {
  var healthy=new AutomationWorker.Status("one",1,SubmissionAutomationMode.Submit,"Waiting","Endurance","endurance-collecting");
  var input=healthy with{Profile="two",ReleaseId=2,Stage="SignReview",Reason="signing-authorization-required"};
  Assert.That(AutomationWorker.NoticeText([healthy,input]),Does.Contain("requires input or approval"));
 }
 [TestCase(SubmissionAutomationMode.Submit,"Completed","Retain",null,true)]
 [TestCase(SubmissionAutomationMode.Submit,"Completed","AppTests",null,false)]
 [TestCase(SubmissionAutomationMode.Rehearsal,"NeedsInput","SignReview","rehearsal-ready-for-review",true)]
 [TestCase(SubmissionAutomationMode.Submit,"NeedsInput","SignReview","rehearsal-ready-for-review",false)]
 [TestCase(SubmissionAutomationMode.Rehearsal,"NeedsInput","AppTests","rehearsal-ready-for-review",false)]
 [TestCase(SubmissionAutomationMode.Submit,"Waiting","SignReview","worker-role-handoff",false)]
 [TestCase(SubmissionAutomationMode.Submit,"Waiting","SignReview","signing-authorization-required",false)]
 [TestCase(SubmissionAutomationMode.Rehearsal,"Failed","Endurance","probe-failed",true)]
 [TestCase(SubmissionAutomationMode.Rehearsal,"Failed",null,"probe-failed",false)]
 [TestCase(SubmissionAutomationMode.Rehearsal,"Failed","Endurance",null,false)]
 [TestCase(SubmissionAutomationMode.Submit,"OutcomeUnknown","Deliver",null,false)]
 [TestCase(SubmissionAutomationMode.Rehearsal,"Busy",null,"workflow-in-use",false)]
 public void FiniteWorkerOnlyExitsAfterItsConfiguredEndpoint(SubmissionAutomationMode mode,string state,string? stage,string? reason,bool finished) {
  Assert.That(AutomationWorker.Finished([new("fixture",3,mode,state,stage,reason)]),Is.EqualTo(finished));
 }
 [Test]public void FiniteWorkerDoesNotExitForEmptyOrPartlyFinishedRegistry() {
  Assert.That(AutomationWorker.Finished([]),Is.False);
  var done=new AutomationWorker.Status("one",1,SubmissionAutomationMode.Submit,"Completed","Retain",null);
  Assert.That(AutomationWorker.Finished([done,done with{Profile="two",ReleaseId=2,State="Running",Stage="AppTests"}]),Is.False);
 }
 [Test]public async Task FiniteWatchRetainsTerminalNoticeAndReleasesItsLockWithoutWaitingForNextPoll() {
  using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(5));
  int calls=0;
  int code=await AutomationWorker.Watch(registry,root,SubmissionAutomationWorkerRole.Evidence,TimeSpan.FromMinutes(15),timeout.Token,
   exitWhenFinished:true,advance:(_,_)=> {
    calls++;return Task.FromResult(SubmissionWorkflow.Read(root,settings.Release) with {
     Stage=SubmissionWorkflowStage.SignReview,Status=SubmissionWorkflowStatus.NeedsInput,ReasonCode="rehearsal-ready-for-review"});
   });
  Assert.That(code,Is.Zero);Assert.That(calls,Is.EqualTo(1));
  Assert.That(AutomationFiles.Read<AutomationWorker.Status[]>(Path.Combine(root,"worker-status.json")).Single().Reason,Is.EqualTo("rehearsal-ready-for-review"));
  Assert.That(File.Exists(Path.Combine(root,"notifications","worker-history.jsonl")),Is.True);
  using var released=new FileStream(Path.Combine(root,"worker.lock"),FileMode.Open,FileAccess.Write,FileShare.None);
  Assert.That(released.CanWrite,Is.True);
 }
 [Test]public async Task FiniteWatchRejectsFutureReleaseDiscoveryBeforeStarting() {
  await Assert.ThrowsAsync<InvalidDataException>(async()=>await AutomationWorker.Watch(registry,root,SubmissionAutomationWorkerRole.Evidence,
   TimeSpan.FromSeconds(30),default,profilesPath:Path.Combine(root,"profiles.json"),exitWhenFinished:true));
  Assert.That(File.Exists(Path.Combine(root,"worker.lock")),Is.False);
 }
 [Test]public async Task FiniteFailureRetainsFailureAndNoticeThenExitsNonzeroWithoutAnotherPoll() {
  using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(5));
  int calls=0;
  int code=await AutomationWorker.Watch(registry,root,SubmissionAutomationWorkerRole.Evidence,TimeSpan.FromMinutes(15),timeout.Token,
   exitWhenFinished:true,advance:(_,_)=> {
    calls++;return Task.FromResult(SubmissionWorkflow.Read(root,settings.Release) with {
     Stage=SubmissionWorkflowStage.AppTests,Status=SubmissionWorkflowStatus.Failed,ReasonCode="synthetic-app-failure"});
   });
  Assert.That(code,Is.EqualTo(2));Assert.That(calls,Is.EqualTo(1));
  var state=AutomationFiles.Read<AutomationWorker.Status[]>(Path.Combine(root,"worker-status.json")).Single();
  Assert.That(state.State,Is.EqualTo("Failed"));Assert.That(state.Reason,Is.EqualTo("synthetic-app-failure"));
  Assert.That(File.Exists(Path.Combine(root,"notifications","worker-history.jsonl")),Is.True);
  using var released=new FileStream(Path.Combine(root,"worker.lock"),FileMode.Open,FileAccess.Write,FileShare.None);
 }
}
