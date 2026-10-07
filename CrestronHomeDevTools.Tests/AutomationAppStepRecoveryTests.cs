// Copyright (c) 2026 Neil Colvin. MIT licensed.
using System.Net;
using System.Text.Json;
using CrestronHomeDevTools.Automation;
using CrestronHomeNUnit.Client;
using CrestronHomeNUnit.Workflow;
using NUnit.Framework;
namespace CrestronHomeDevTools.Tests;

public sealed partial class AutomationInstalledAppTests
{
 private async Task<AutomationAppStepRecovery.Request> FailedTest(bool restored=true) {
  await AutomationInstalledApp.Advance(context,settings,false,(p,c,f,t)=>{
   Directory.CreateDirectory(f);
   var result=new InstalledDriverTestResult(new WorkflowTestOutcome(0,1,0,false),restored,true,true,restored,"synthetic failure");
   File.WriteAllText(Path.Combine(f,"InstalledDriverTests.json"),JsonSerializer.Serialize(result));
   File.WriteAllText(Path.Combine(f,"failure.txt"),"original failure");return Task.FromResult(result);
  },_=>new(),default);
  var plan=settings.InstalledAppTests!;
  return new(1,"main",0,Guid.NewGuid().ToString("N"),new('f',64),AutomationAppStepRecovery.EvidenceHash(context.RunDirectory),
   "installed-app/InstalledDriverTests.json",plan,await WorkflowEvidence.SourceDigestAsync(plan.SourceRoots,default),AutomationFiles.Hash(plan.AndroidTests.ProfilePath));
 }
 private Task<SubmissionWorkflowStepResult> Replace(AutomationAppStepRecovery.Request request,
  Func<InstalledDriverTestPlan,NetworkCredential,string,CancellationToken,Task<InstalledDriverTestResult>>? runner=null)=>
  AutomationAppStepRecovery.Execute(context,settings,request,runner??Run,new(),default);
 [Test] public async Task FailedStepReplacementPreservesFailureAndDoesNotReplaySuccess() {
  var request=await FailedTest();string failed=File.ReadAllText(Path.Combine(context.RunDirectory,request.FailedOutcome));
  Assert.That((await Replace(request)).Status,Is.EqualTo(SubmissionWorkflowStatus.Completed));
  Assert.That((await Replace(request)).Status,Is.EqualTo(SubmissionWorkflowStatus.Completed));
  Assert.That(calls,Is.EqualTo(1));
  Assert.That(File.ReadAllText(Path.Combine(context.RunDirectory,request.FailedOutcome)),Is.EqualTo(failed));
  AutomationInstalledApp.VerifyRetained(context.RunDirectory);
 }
 [Test] public async Task FailedReplacementCannotReplay() {
  var request=await FailedTest();
  Task<InstalledDriverTestResult> Failed(InstalledDriverTestPlan p,NetworkCredential c,string f,CancellationToken t) {
   calls++;Directory.CreateDirectory(f);var result=new InstalledDriverTestResult(new WorkflowTestOutcome(0,1,0,false),true,true,true,true,"failed replacement");
   File.WriteAllText(Path.Combine(f,"InstalledDriverTests.json"),JsonSerializer.Serialize(result));return Task.FromResult(result);
  }
  Assert.That((await Replace(request,Failed)).Status,Is.EqualTo(SubmissionWorkflowStatus.NeedsInput));
  Assert.That((await Replace(request,Failed)).Status,Is.EqualTo(SubmissionWorkflowStatus.NeedsInput));Assert.That(calls,Is.EqualTo(1));
  Assert.That(File.Exists(Path.Combine(context.RunDirectory,"installed-app-tests.json")),Is.False);
 }
 [Test] public async Task UnsplitPhaseResumesAcceptedReplacementWithoutRepeatingOriginalFailure() {
  var request=await FailedTest();
  var replacement=await Replace(request);
  Assert.That((await Advance(true)).Receipt,Is.EqualTo(replacement.Receipt));
  Assert.That((await Advance()).Status,Is.EqualTo(SubmissionWorkflowStatus.Completed));
  Assert.That(calls,Is.EqualTo(1));
  Assert.That(AutomationAppStepRecovery.Failure(context.RunDirectory,request.FailedOutcome).Passed,Is.False);
  File.AppendAllText(Path.Combine(context.RunDirectory,"installed-app","failure.txt"),"tampered");
  await Assert.ThrowsAsync<InvalidDataException>(async()=>await Advance(true));
 }
 [Test] public async Task UnsplitPhaseSelectionUsesItsOriginalRootAndRejectsOtherIndices() {
  await FailedTest();
  var opened=SubmissionWorkflow.Open(settings.PrivateRoot,settings.Release);
  var state=context.Checkpoint with{Status=SubmissionWorkflowStatus.Failed,InputSha256=opened.InputSha256};
  string run=Path.Combine(settings.PrivateRoot,SubmissionWorkflow.RunKey(settings.Release));
  Directory.CreateDirectory(run);
  foreach(var stage in new[]{SubmissionWorkflowStage.ValidateCandidate,SubmissionWorkflowStage.WindowsTests,SubmissionWorkflowStage.ProcessorTests}) {
   string name=stage+".json";File.WriteAllText(Path.Combine(run,name),"synthetic retained stage");
   state.CompletedStages.Add(stage,new(name,AutomationFiles.Hash(Path.Combine(run,name))));
  }
  File.WriteAllText(Path.Combine(run,"state.json"),JsonSerializer.Serialize(state,new JsonSerializerOptions {
   PropertyNamingPolicy=JsonNamingPolicy.CamelCase,Converters={new System.Text.Json.Serialization.JsonStringEnumConverter()}}));
  AutomationFiles.Write(Path.Combine(run,"target-plan.json"),settings.InstalledAppTests);
  var selected=AutomationAppStepRecovery.Select(new(settings,new('a',64)),"main",0);
  Assert.That(selected.Step,Is.EqualTo(run));
  Assert.That(JsonSerializer.Serialize(selected.Settings.InstalledAppTests),Is.EqualTo(JsonSerializer.Serialize(settings.InstalledAppTests)));
  Assert.Throws<InvalidDataException>(()=>AutomationAppStepRecovery.Select(new(settings,new('a',64)),"main",1));
 }
 [Test] public async Task InterruptedReplacementCannotReplay() {
  var request=await FailedTest();
  await Assert.ThrowsAsync<IOException>(async()=>await Replace(request,(p,c,f,t)=>{calls++;throw new IOException("synthetic interruption");}));
  Assert.That((await Replace(request)).Status,Is.EqualTo(SubmissionWorkflowStatus.OutcomeUnknown));Assert.That(calls,Is.EqualTo(1));
 }
 [Test] public async Task UnconfirmedRestorationBlocksBeforeNewAttempt() {
  var request=await FailedTest(false);
  await Assert.ThrowsAsync<InvalidDataException>(async()=>await Replace(request));Assert.That(calls,Is.Zero);
  Assert.That(Directory.Exists(Path.Combine(context.RunDirectory,"installed-app","recovery-attempts")),Is.False);
 }
 [TestCase("host")][TestCase("device")][TestCase("package")][TestCase("tests")][TestCase("timing")][TestCase("source")][TestCase("evidence")]
 public async Task FailedStepReplacementRejectsChangedBindings(string change) {
  var request=await FailedTest();var plan=request.Replacement;
  request=change switch {
   "host"=>request with{Replacement=plan with{Host="other.example"}},
   "device"=>request with{Replacement=plan with{Target=plan.Target with{DeviceId=99}}},
   "package"=>request with{Replacement=plan with{PackageSha256=new('1',64)}},
   "tests"=>request with{Replacement=plan with{AndroidTests=plan.AndroidTests with{RequiredTests=["different"]}}},
   "timing"=>request with{Replacement=plan with{TimeoutSeconds=plan.TimeoutSeconds+1}},
   "source"=>request with{SourceSha256=new('2',64)},
   _=>request with{OriginalEvidenceSha256=new('3',64)}};
  await Assert.ThrowsAsync<InvalidDataException>(async()=>await Replace(request));Assert.That(calls,Is.Zero);
 }
 [Test] public async Task ReplacementCompletionDetectsTamperedOriginalFailure() {
  var request=await FailedTest();await Replace(request);
  File.AppendAllText(Path.Combine(context.RunDirectory,"installed-app","failure.txt"),"changed");
  await Assert.ThrowsAsync<InvalidDataException>(async()=>await Replace(request));Assert.That(calls,Is.EqualTo(1));
 }
 [Test] public async Task RestorationReconciliationMustBeBoundToOriginalEvidence() {
  var request=await FailedTest(false);
  string evidence=Path.Combine(context.RunDirectory,"reconciliation.json");File.WriteAllText(evidence,"synthetic independent verification");
  var proof=new AutomationAppStepRecovery.Reconciliation(request.OriginalEvidenceSha256,"synthetic operator",DateTimeOffset.UtcNow,true,true,true,
   [new("reconciliation.json",AutomationFiles.Hash(evidence))]);
  File.AppendAllText(evidence,"changed");
  await Assert.ThrowsAsync<InvalidDataException>(async()=>await Replace(request with{Restoration=proof}));Assert.That(calls,Is.Zero);
 }

 [Test] public async Task ReviewedRestorationEvidenceIsRetainedWithoutChangingOriginalFailure() {
  var request=await FailedTest(false);
  string proof=Path.Combine(context.RunDirectory,"reconciliation.json");File.WriteAllText(proof,"synthetic independent verification");
  request=request with{Restoration=new(request.OriginalEvidenceSha256,"synthetic verifier",DateTimeOffset.UtcNow,true,true,true,
   [new("reconciliation.json",AutomationFiles.Hash(proof))])};
  Assert.That((await Replace(request)).Status,Is.EqualTo(SubmissionWorkflowStatus.Completed));
  Assert.That(AutomationAppStepRecovery.Failure(context.RunDirectory,request.FailedOutcome).RestorationConfirmed,Is.False);
  Assert.That(File.ReadAllText(Path.Combine(context.RunDirectory,"installed-app","recovery-attempts",request.AttemptId,"reconciliation","000.evidence")),Is.EqualTo(File.ReadAllText(proof)));
 }
 [Test] public async Task NewAttemptCannotBypassInterruptedReplacement() {
  var request=await FailedTest();
  await Assert.ThrowsAsync<IOException>(async()=>await Replace(request,(p,c,f,t)=>{calls++;throw new IOException("interrupted");}));
  var another=request with{AttemptId=Guid.NewGuid().ToString("N"),OriginalEvidenceSha256=AutomationAppStepRecovery.EvidenceHash(context.RunDirectory)};
  await Assert.ThrowsAsync<InvalidDataException>(async()=>await Replace(another));Assert.That(calls,Is.EqualTo(1));
 }
 [Test] public async Task ReplacementRequiresItsOwnReadinessAcknowledgement() {
  string inbox=Path.Combine(root,"inbox");Directory.CreateDirectory(inbox);
  settings=settings with{InstalledAppTests=settings.InstalledAppTests! with{
   AndroidTests=settings.InstalledAppTests.AndroidTests with{RequiredTests=["Synthetic.Test"]},
   OperatorReadiness=new(inbox,new('a',64),"old.ready","Synthetic only")}};
  var request=await FailedTest();
  await Replace(request,(p,c,f,t)=>{
   Assert.That(p.OperatorReadiness!.Step,Is.EqualTo("app-replacement-"+request.AttemptId+".ready"));
   Assert.That(p.OperatorReadiness.Directory,Is.EqualTo(inbox));return Run(p,c,f,t);
  });
 }
}
