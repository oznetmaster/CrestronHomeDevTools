// Copyright (c) 2026 Neil Colvin. MIT licensed.
using System.Net;
using System.Text.Json;
using CrestronHomeDevTools.Automation;
using CrestronHomeNUnit.Workflow;
using CrestronHomeNUnit.Client;
using NUnit.Framework;
namespace CrestronHomeDevTools.Tests;

public sealed partial class AutomationInstalledAppTests
{
 private async Task<AutomationAppStepRecovery.Request> SplitRequest() {
  string inbox=Path.Combine(root,"inbox");Directory.CreateDirectory(inbox);
  var fixture=JsonSerializer.SerializeToElement(new{EvidenceIdentity=new{PackageSha256=settings.Release.PackageSha256,PolicySha256=new string('a',64)}});
  settings=settings with{InstalledAppFixtureSettings=fixture,InstalledAppTests=settings.InstalledAppTests! with{
   AndroidTests=settings.InstalledAppTests.AndroidTests with{RequiredTests=["Synthetic.Outage"]},
   OperatorReadiness=new(inbox,new('a',64),"original.ready","Synthetic readiness")},
   Review=new(null!,null!,null!,null!,null!,"Synthetic","Synthetic",[
    "installed-app/steps/000/installed-app/AndroidUI/power.json",
    "installed-app/steps/000/installed-app/AndroidUI/network.json"])};
  var original=await FailedTest();
  var first=new AutomationAppScopeRevision.Invocation(original.Replacement,fixture,Path.Combine(root,"credentials.json"),original.SourceSha256,original.ProfileSha256);
  var second=first with{Tests=first.Tests with{Host="network.example",Target=first.Tests.Target with{DeviceId=3}}};
  return original with{SchemaVersion=2,ScopeRevision=new("Split distinct recovery clocks",[first,second],[new("power.json",0,"observation.json"),new("network.json",1,"observation.json")])};
 }
 private Task<SubmissionWorkflowStepResult> Split(AutomationAppStepRecovery.Request request,
  Func<InstalledDriverTestPlan,NetworkCredential,string,CancellationToken,Task<InstalledDriverTestResult>>? runner=null)=>
  AutomationAppStepRecovery.Execute(context,settings,request,runner??SplitRun,new(),default,_=>new());
 private async Task<InstalledDriverTestResult> SplitRun(InstalledDriverTestPlan plan,NetworkCredential credential,string output,CancellationToken token) {
  var result=await Run(plan,credential,output,token);
  Directory.CreateDirectory(Path.Combine(output,"AndroidUI"));
  File.WriteAllText(Path.Combine(output,"AndroidUI","observation.json"),"synthetic observation, not live evidence");
  return result;
 }
 private async Task<AutomationAppStepRecovery.Request> RetainedSuccessRequest() {
  var first=await SplitRequest();int count=0;
  var stopped=await Split(first,async(p,c,f,t)=>{
   if(++count==1)return await SplitRun(p,c,f,t);
   Directory.CreateDirectory(f);
   var failure=new InstalledDriverTestResult(new WorkflowTestOutcome(0,1,0,false),true,true,true,true,"synthetic second-scope failure");
   File.WriteAllText(Path.Combine(f,"InstalledDriverTests.json"),JsonSerializer.Serialize(failure));return failure;
  });
  Assert.That(stopped.Status,Is.EqualTo(SubmissionWorkflowStatus.NeedsInput));
  string prefix="installed-app/recovery-attempts/"+first.AttemptId+"/";
  var reused=first.ScopeRevision!.Invocations[0] with{Retained=new(first.AttemptId,0,
   AutomationFiles.Hash(Path.Combine(context.RunDirectory,prefix+"invocations/000/verified.json")),"Unaffected passed scope; exact bindings and probe retained")};
  return first with{AttemptId=Guid.NewGuid().ToString("N"),FailedOutcome=prefix+"installed-app/InstalledDriverTests.json",
   OriginalEvidenceSha256=AutomationAppStepRecovery.EvidenceHash(context.RunDirectory),
   ScopeRevision=first.ScopeRevision with{Invocations=[reused,first.ScopeRevision.Invocations[1]]}};
 }
 [Test] public async Task CorrectedAttemptReusesSuccessfulScopeWithoutNewPhysicalInvocation() {
  var next=await RetainedSuccessRequest();int invoked=0;
  var original=AutomationAppStepRecovery.Inventory(context.RunDirectory);
  var result=await Split(next,async(p,c,f,t)=>{invoked++;Assert.That(p.Host,Is.EqualTo("network.example"));return await SplitRun(p,c,f,t);});
  Assert.That(result.Status,Is.EqualTo(SubmissionWorkflowStatus.Completed));Assert.That(invoked,Is.EqualTo(1));
  var accepted=AutomationFiles.Read<AutomationAppStepRecovery.Completion>(Path.Combine(context.RunDirectory,"installed-app/replacement.json"));
  Assert.That(AutomationAppScopeRevision.Accepted(accepted)[0],Does.Contain(next.ScopeRevision!.Invocations[0].Retained!.AttemptId));
  Assert.That(accepted.RetainedProducerReceipts,Has.Count.EqualTo(1));
  foreach(var file in original)Assert.That(AutomationFiles.Hash(Path.Combine(context.RunDirectory,file.RelativePath)),Is.EqualTo(file.Sha256));
  AutomationInstalledApp.VerifyRetained(context.RunDirectory);
  Assert.That((await Split(next)).Status,Is.EqualTo(SubmissionWorkflowStatus.Completed));
 }
 [TestCase("binding")][TestCase("receipt")][TestCase("evidence")]
 public async Task InvalidRetainedScopeFailsBeforeAnyNewAction(string change) {
  var next=await RetainedSuccessRequest();var first=next.ScopeRevision!.Invocations[0];
  if(change=="binding")first=first with{Tests=first.Tests with{Host="wrong.example"}};
  if(change=="receipt")first=first with{Retained=first.Retained! with{VerifiedSha256=new('f',64)}};
  if(change=="evidence"){
   string old="installed-app/recovery-attempts/"+first.Retained!.AttemptId+"/invocations/000/installed-app/AndroidUI/observation.json";
   File.AppendAllText(Path.Combine(context.RunDirectory,old),"changed");
   next=next with{OriginalEvidenceSha256=AutomationAppStepRecovery.EvidenceHash(context.RunDirectory)};
  }
  next=next with{ScopeRevision=next.ScopeRevision with{Invocations=[first,next.ScopeRevision.Invocations[1]]}};
  int invoked=0;
  await Assert.ThrowsAsync<InvalidDataException>(async()=>await Split(next,(p,c,f,t)=>{invoked++;throw new InvalidOperationException("must not run");}));
  Assert.That(invoked,Is.Zero);
 }
 [TestCase("MeasurementInconclusive","not proof of a driver failure")]
 [TestCase("HarnessFailed","test tool failed")]
 public async Task FailedScopeExplainsWhetherMeasurementOrToolNeedsRepair(string disposition,string explanation) {
  var request=await SplitRequest();
  var result=await Split(request,(p,c,f,t)=>{
   Directory.CreateDirectory(Path.Combine(f,"AndroidUI/system-outage"));
   File.WriteAllText(Path.Combine(f,"AndroidUI/system-outage/recording-result.json"),JsonSerializer.Serialize(new{disposition}));
   var failed=new InstalledDriverTestResult(new WorkflowTestOutcome(0,1,0,false),true,true,true,true,"synthetic");
   File.WriteAllText(Path.Combine(f,"InstalledDriverTests.json"),JsonSerializer.Serialize(failed));return Task.FromResult(failed);
  });
  Assert.That(result.ReasonCode,Does.EndWith(disposition.ToLowerInvariant()));
  var aggregate=AutomationFiles.Read<InstalledDriverTestResult>(Path.Combine(context.RunDirectory,"installed-app/recovery-attempts",request.AttemptId,"installed-app/InstalledDriverTests.json"));
  Assert.That(aggregate.Detail,Does.Contain(explanation));Assert.That(aggregate.Detail,Does.Contain("No physical action is currently requested"));
 }
 [Test] public async Task ExplicitSplitRequiresBothScopesAndPreservesOriginalFailure() {
  var request=await SplitRequest();var failure=File.ReadAllText(Path.Combine(context.RunDirectory,request.FailedOutcome));
  Assert.That((await Split(request)).Status,Is.EqualTo(SubmissionWorkflowStatus.Completed));
  Assert.That(calls,Is.EqualTo(2));Assert.That((await Split(request)).Status,Is.EqualTo(SubmissionWorkflowStatus.Completed));
  Assert.That(calls,Is.EqualTo(2));Assert.That(File.ReadAllText(Path.Combine(context.RunDirectory,request.FailedOutcome)),Is.EqualTo(failure));
  var accepted=AutomationFiles.Read<AutomationAppStepRecovery.Completion>(Path.Combine(context.RunDirectory,"installed-app/replacement.json"));
  Assert.That(AutomationAppScopeRevision.Accepted(accepted),Has.Length.EqualTo(2));
  Assert.That(accepted.ObservationSources!.Keys,Is.EquivalentTo(new[]{"power.json","network.json"}));
 }
 [Test] public async Task InterruptedSecondScopeNeverReplaysEitherInvocation() {
  var request=await SplitRequest();int invoked=0;
  await Assert.ThrowsAsync<IOException>(async()=>await Split(request,async(p,c,f,t)=>{
   if(++invoked==2)throw new IOException("synthetic interruption");return await SplitRun(p,c,f,t);
  }));
  Assert.That((await Split(request)).Status,Is.EqualTo(SubmissionWorkflowStatus.OutcomeUnknown));
  Assert.That(calls,Is.EqualTo(1));Assert.That(invoked,Is.EqualTo(2));
  Assert.That(File.Exists(Path.Combine(context.RunDirectory,"installed-app-tests.json")),Is.False);
 }
 [Test] public async Task FailedFirstScopePreventsSecondAndCannotBeReplayed() {
  var request=await SplitRequest();int invoked=0;
  async Task<InstalledDriverTestResult> Fail(InstalledDriverTestPlan p,NetworkCredential c,string f,CancellationToken t) {
   invoked++;var result=(await SplitRun(p,c,f,t)) with{Tests=new(0,1,0,false)};
   File.WriteAllText(Path.Combine(f,"InstalledDriverTests.json"),JsonSerializer.Serialize(result));return result;
  }
  Assert.That((await Split(request,Fail)).Status,Is.EqualTo(SubmissionWorkflowStatus.NeedsInput));
  Assert.That((await Split(request,Fail)).Status,Is.EqualTo(SubmissionWorkflowStatus.NeedsInput));
  Assert.That(invoked,Is.EqualTo(1));
 }
 [TestCase("omitted")][TestCase("duplicate")][TestCase("escape")][TestCase("uncovered")]
 public async Task SplitRejectsMissingOrAmbiguousObservationCoverage(string change) {
  var request=await SplitRequest();var plan=request.ScopeRevision!;
  plan=change switch{
   "omitted"=>plan with{Observations=[plan.Observations[0]]},
   "duplicate"=>plan with{Observations=[plan.Observations[0],plan.Observations[0]]},
   "escape"=>plan with{Observations=[plan.Observations[0] with{RevisedPath="../bad.json"},plan.Observations[1]]},
   _=>plan with{Observations=[plan.Observations[0],plan.Observations[1] with{Invocation=0}]}};
  await Assert.ThrowsAsync<InvalidDataException>(async()=>await Split(request with{ScopeRevision=plan}));Assert.That(calls,Is.Zero);
 }
 [TestCase("candidate")][TestCase("policy")][TestCase("test")][TestCase("budget")][TestCase("readiness")]
 public async Task SplitCannotWeakenCandidateOrExecutionContract(string change) {
  var request=await SplitRequest();var plan=request.ScopeRevision!;var second=plan.Invocations[1];
  second=change switch{
   "candidate"=>second with{Tests=second.Tests with{PackageSha256=new('1',64)}},
   "policy"=>second with{Fixture=JsonSerializer.SerializeToElement(new{EvidenceIdentity=new{PolicySha256="other"}})},
   "test"=>second with{Tests=second.Tests with{AndroidTests=second.Tests.AndroidTests with{RequiredTests=["Other.Test"]}}},
   "budget"=>second with{Tests=second.Tests with{TimeoutSeconds=second.Tests.TimeoutSeconds+1}},
   _=>second with{Tests=second.Tests with{OperatorReadiness=null}}};
  await Assert.ThrowsAsync<InvalidDataException>(async()=>await Split(request with{ScopeRevision=plan with{Invocations=[plan.Invocations[0],second]}}));Assert.That(calls,Is.Zero);
 }
 [Test] public async Task SplitUsesFreshReadinessForEachProcessor() {
  var request=await SplitRequest();var seen=new List<string>();
  await Split(request,async(p,c,f,t)=>{seen.Add(p.OperatorReadiness!.Step);return await SplitRun(p,c,f,t);});
  Assert.That(seen.Distinct().Count(),Is.EqualTo(2));Assert.That(seen.All(s=>s.Contains(request.AttemptId)),Is.True);
 }
}
