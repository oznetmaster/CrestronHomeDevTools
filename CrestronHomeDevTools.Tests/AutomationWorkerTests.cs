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
}
