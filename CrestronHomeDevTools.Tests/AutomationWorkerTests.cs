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
 [Test]public void FinalTestsBelongToEvidenceWorkerEvenThoughEnumValueIsAppended() {
  Assert.That(AutomationWorker.Owns(SubmissionWorkflowStage.FinalizeTests,SubmissionAutomationWorkerRole.Evidence),Is.True);
  Assert.That(AutomationWorker.Owns(SubmissionWorkflowStage.FinalizeTests,SubmissionAutomationWorkerRole.Protected),Is.False);
 }
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
 private sealed class PausedRecoveryStages(Task release):ISubmissionWorkflowSteps {
  public Task<SubmissionWorkflowStepResult> ExecuteAsync(SubmissionWorkflowStepContext c,CancellationToken t)=>
   Task.FromResult(new SubmissionWorkflowStepResult(SubmissionWorkflowStatus.OutcomeUnknown,ReasonCode:"synthetic-original-failure"));
  public async Task<SubmissionWorkflowStepResult> RecoverAsync(SubmissionWorkflowStepContext c,CancellationToken t) {
   await release.WaitAsync(t);return new(SubmissionWorkflowStatus.NeedsInput,ReasonCode:"synthetic-next-check");
  }
 }
 [Test]public async Task ActiveRecoveryRetiresOldNoticeBeforeLongOperationCompletesAndPreservesOtherFailures() {
  var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
  var inner=new PausedRecoveryStages(release.Task);
  await SubmissionWorkflow.AdvanceAsync(root,settings.Release,inner);
  var entry=AutomationFiles.Read<SubmissionAutomationRegistry>(registry).Entries.Single();
  var old=new AutomationWorker.Status(entry.Profile,entry.ReleaseId,entry.Mode,"OutcomeUnknown","ValidateCandidate","synthetic-original-failure");
  var other=old with{Profile="other",ReleaseId=4};
  AutomationWorker.SaveStatus(root,[old,other]);AutomationWorker.Notification(root,[old,other],DateTimeOffset.UtcNow);
  string statePath=Path.Combine(root,SubmissionWorkflow.RunKey(settings.Release),"state.json");
  var original=File.ReadAllBytes(statePath);
  SubmissionWorkflow.RequestRecovery(root,settings.Release,AutomationFiles.Hash(statePath));
  var requested=File.ReadAllBytes(statePath);
  var announced=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
  var stages=new AutomationWorker.ReportingStages(inner,checkpoint=> {
   // The callback must run while Advance owns the exclusive workflow lock.
   Assert.Throws<IOException>(()=>{using var gate=new FileStream(Path.Combine(root,SubmissionWorkflow.RunKey(settings.Release),"run.lock"),FileMode.Open,FileAccess.Write,FileShare.None);});
   AutomationWorker.PublishStageDispatch(root,entry,checkpoint,DateTimeOffset.UtcNow);
   announced.SetResult();
  });
  using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(5));
  var operation=SubmissionWorkflow.AdvanceAsync(root,settings.Release,stages,deadline.Token);
  try {
   await announced.Task.WaitAsync(deadline.Token);
   Assert.That(operation.IsCompleted,Is.False);
   var notice=AutomationFiles.Read<AutomationWorker.Status[]>(Path.Combine(root,"notifications","worker-status.json"));
   Assert.That(notice[0].State,Is.EqualTo("Running"));Assert.That(notice[0].Reason,Is.EqualTo("recovery-in-progress"));
   Assert.That(notice[1],Is.EqualTo(other));
   Assert.That(File.ReadAllBytes(statePath),Is.EqualTo(requested),"Reporting must not change the durable checkpoint or mark the original failure passed.");
   Assert.That(requested,Is.Not.EqualTo(original));
  } finally {release.TrySetResult();await operation;}
 }
 [Test]public async Task NewFailureAfterRecoveryDispatchRestoresAttentionNotice() {
  var entry=AutomationFiles.Read<SubmissionAutomationRegistry>(registry).Entries.Single();
  var states=await AutomationWorker.Tick(registry,SubmissionAutomationWorkerRole.Evidence,default,
   (_,t)=>SubmissionWorkflow.AdvanceAsync(root,settings.Release,new AutomationWorker.ReportingStages(new JsonFailureSteps(),checkpoint=>
    AutomationWorker.PublishStageDispatch(root,entry,checkpoint,DateTimeOffset.UtcNow)),t));
  AutomationWorker.SaveStatus(root,states);AutomationWorker.Notification(root,states,DateTimeOffset.UtcNow);
  var notice=AutomationFiles.Read<AutomationWorker.Status[]>(Path.Combine(root,"notifications","worker-status.json")).Single();
  Assert.That(notice.State,Is.EqualTo("AttentionRequired"));Assert.That(notice.Reason,Is.EqualTo("JsonException"));
 }
 [Test]public void UnchangedEnduranceDispatchDoesNotCreateStatusOrNotificationChurn() {
  var entry=AutomationFiles.Read<SubmissionAutomationRegistry>(registry).Entries.Single();
  var checkpoint=SubmissionWorkflow.Read(root,settings.Release) with{Stage=SubmissionWorkflowStage.Endurance,Status=SubmissionWorkflowStatus.Waiting,ReasonCode="endurance-collecting"};
  var status=AutomationWorker.DispatchStatus(entry,checkpoint);
  AutomationWorker.SaveStatus(root,[status,status with{Profile="other",ReleaseId=4}]);
  AutomationWorker.PublishDispatch(root,status,DateTimeOffset.UtcNow);
  string history=Path.Combine(root,"worker-history.jsonl"),notice=Path.Combine(root,"notifications","worker-history.jsonl");
  var before=File.ReadAllBytes(history);var noticeBefore=File.ReadAllBytes(notice);
  for(int i=0;i<5;i++)AutomationWorker.PublishDispatch(root,status,DateTimeOffset.UtcNow);
  Assert.That(File.ReadAllBytes(history),Is.EqualTo(before));Assert.That(File.ReadAllBytes(notice),Is.EqualTo(noticeBefore));
 }
 [Test]public void SameRecoveryCheckpointCannotReopenAnAlertAcrossRepeatedPollsOrRestarts() {
  var entry=AutomationFiles.Read<SubmissionAutomationRegistry>(registry).Entries.Single();
  var checkpoint=SubmissionWorkflow.Read(root,settings.Release) with{Status=SubmissionWorkflowStatus.Waiting,ReasonCode="recovery-requested"};
  AutomationWorker.PublishStageDispatch(root,entry,checkpoint,DateTimeOffset.UtcNow);
  var failure=AutomationWorker.DispatchStatus(entry,checkpoint) with{State="AttentionRequired",Reason="IOException"};
  AutomationWorker.SaveStatus(root,[failure]);AutomationWorker.Notification(root,[failure],DateTimeOffset.UtcNow);
  var history=File.ReadAllBytes(Path.Combine(root,"notifications","worker-history.jsonl"));
  for(int i=0;i<5;i++)AutomationWorker.PublishStageDispatch(root,entry,checkpoint,DateTimeOffset.UtcNow);
  Assert.That(AutomationFiles.Read<AutomationWorker.Status[]>(Path.Combine(root,"notifications","worker-status.json")).Single(),Is.EqualTo(failure));
  Assert.That(File.ReadAllBytes(Path.Combine(root,"notifications","worker-history.jsonl")),Is.EqualTo(history));
  // A separately accepted request is a new checkpoint, not an elapsed-time rule.
  AutomationWorker.PublishStageDispatch(root,entry,checkpoint with{UpdatedUtc=checkpoint.UpdatedUtc.AddTicks(1)},DateTimeOffset.UtcNow);
  Assert.That(AutomationFiles.Read<AutomationWorker.Status[]>(Path.Combine(root,"notifications","worker-status.json")).Single().Reason,Is.EqualTo("recovery-in-progress"));
 }
 [Test]public async Task ImplicitRecoveryDoesNotAnnounceUnreviewedFailureResolution() {
  int reports=0;
  var inner=new PausedRecoveryStages(Task.CompletedTask);
  var stages=new AutomationWorker.ReportingStages(inner,_=>reports++);
  var checkpoint=SubmissionWorkflow.Read(root,settings.Release) with{Status=SubmissionWorkflowStatus.Running};
  var context=new SubmissionWorkflowStepContext(Path.Combine(root,SubmissionWorkflow.RunKey(settings.Release)),checkpoint);
  await stages.RecoverAsync(context,default);
  Assert.That(reports,Is.Zero);
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
 [TestCase(SubmissionAutomationMode.Rehearsal,"Completed","Retain",null,true)]
 [TestCase(SubmissionAutomationMode.Rehearsal,"NeedsInput","SignReview","rehearsal-ready-for-review",false)]
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
     Stage=SubmissionWorkflowStage.Retain,Status=SubmissionWorkflowStatus.Completed,ReasonCode=null});
   });
  Assert.That(code,Is.Zero);Assert.That(calls,Is.EqualTo(1));
  Assert.That(AutomationFiles.Read<AutomationWorker.Status[]>(Path.Combine(root,"worker-status.json")).Single().Stage,Is.EqualTo("Retain"));
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
 private sealed class SyntheticRecoveryStages:ISubmissionWorkflowSteps {
  public readonly List<SubmissionWorkflowStage> Started=[];
  public readonly List<string> EnduranceOperations=[];
  public Task<SubmissionWorkflowStepResult> ExecuteAsync(SubmissionWorkflowStepContext c,CancellationToken t) {Started.Add(c.Checkpoint.Stage);return Advance(c,false,t);}
  public Task<SubmissionWorkflowStepResult> RecoverAsync(SubmissionWorkflowStepContext c,CancellationToken t)=>Advance(c,true,t);
  private async Task<SubmissionWorkflowStepResult> Advance(SubmissionWorkflowStepContext c,bool resumed,CancellationToken token) {
   if(c.Checkpoint.Stage is SubmissionWorkflowStage.Deliver or SubmissionWorkflowStage.Retain)throw new InvalidOperationException("Evidence worker must hand off protected operations");
   if(c.Checkpoint.Stage==SubmissionWorkflowStage.SignReview)return new(SubmissionWorkflowStatus.Waiting,ReasonCode:"worker-role-handoff");
   if(c.Checkpoint.Stage==SubmissionWorkflowStage.Endurance) {
    EnduranceOperations.Add(c.Checkpoint.OperationId!);
    if(!resumed)return new(SubmissionWorkflowStatus.Waiting,ReasonCode:"endurance-collecting");
   }
   string name=c.Checkpoint.Stage+".json",path=Path.Combine(c.RunDirectory,name);
   if(c.Checkpoint.Stage==SubmissionWorkflowStage.AppTests) {
    var evidence="synthetic-observation.json";File.WriteAllText(Path.Combine(c.RunDirectory,evidence),"Synthetic worker integration; no hardware evidence.");
    var proof=new SubmissionEvidenceFile(evidence,AutomationFiles.Hash(Path.Combine(c.RunDirectory,evidence)));
    var origin=DateTimeOffset.UtcNow.AddMinutes(-10);
    SubmissionOutageCapture At(int sec)=>new(origin.AddSeconds(sec),origin.AddSeconds(sec),proof);
    var probed=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    await using var recovery=new SubmissionRecoverySession(async ct=>{await probed.Task.WaitAsync(ct);return At(0);},
     ct=>{probed.SetResult();return Task.FromResult(new[]{new SubmissionOutageFunction("control",SubmissionEvidenceOutcome.Passed,At(20))});},TimeSpan.FromSeconds(5),token);
    var observed=await recovery.Completion;
    var identity=new SubmissionEvidenceIdentity(new('a',64),new('b',40),new('c',64),new('d',64));
    var plan=new SubmissionOutageMeasurementPlan(identity,"synthetic.power",["processor"],["control"],TimeSpan.FromSeconds(60),TimeSpan.FromSeconds(60),SubmissionOutageRecoveryClock.ProgramLoaded,"processor");
    var record=new SubmissionOutageMeasurementRecord(3,identity,[new("processor",At(-120),At(-40))],observed.Clock,observed.Functions,At(-150),At(25),true){FunctionObservationsAreWindows=true};
    var report=SubmissionOutageMeasurements.Assess(plan,record,c.RunDirectory,DateTimeOffset.UtcNow);
    if(!report.MeasurementChecksPassed)throw new InvalidDataException("Synthetic recovery contract failed");
    AutomationFiles.Write(path,new{Synthetic=true,Record=record,Report=report,CleanupConfirmed=true});
   } else AutomationFiles.Write(path,new{Synthetic=true,Stage=c.Checkpoint.Stage.ToString(),Unsigned=true});
   return new(SubmissionWorkflowStatus.Completed,new(name,AutomationFiles.Hash(path)));
  }
 }
 [Test]public async Task RegisteredEvidenceWorkerAdvancesRecoveryEnduranceAndHandsOffWithoutClaimingCompletion() {
  var stages=new SyntheticRecoveryStages();using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(90));
  await Assert.ThrowsAsync<TaskCanceledException>(async()=>await AutomationWorker.Watch(registry,root,SubmissionAutomationWorkerRole.Evidence,TimeSpan.FromSeconds(30),timeout.Token,
   exitWhenFinished:true,advance:async(request,t)=> {
    var result=await SubmissionWorkflow.AdvanceAsync(root,request.Settings.Release,stages,t);
    if(result.Stage==SubmissionWorkflowStage.SignReview)timeout.Cancel();
    return result;
   }));
  var state=SubmissionWorkflow.Read(root,settings.Release);
  Assert.That(state.Stage,Is.EqualTo(SubmissionWorkflowStage.SignReview));Assert.That(state.ReasonCode,Is.EqualTo("worker-role-handoff"));
  Assert.That(AutomationWorker.Finished(AutomationFiles.Read<AutomationWorker.Status[]>(Path.Combine(root,"worker-status.json"))),Is.False);
  Assert.That(state.CompletedStages.Keys,Does.Contain(SubmissionWorkflowStage.PrepareReview));
  Assert.That(stages.Started.Distinct().Count(),Is.EqualTo(stages.Started.Count));
  Assert.That(stages.EnduranceOperations,Has.Count.EqualTo(2));Assert.That(stages.EnduranceOperations.Distinct().Count(),Is.EqualTo(1));
  Assert.That(stages.Started,Does.Not.Contain(SubmissionWorkflowStage.Deliver));
  Assert.That(File.Exists(Path.Combine(root,"notifications","worker-status.json")),Is.True);
  using var gate=new FileStream(Path.Combine(root,"worker.lock"),FileMode.Open,FileAccess.Write,FileShare.None);
 }

}
