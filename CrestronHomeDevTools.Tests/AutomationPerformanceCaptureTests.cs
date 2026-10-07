// Copyright (c) 2026 Neil Colvin. MIT licensed.
using System.Net;
using CrestronHomeNUnit.Client;
using CrestronHomeDevTools.Automation;
using CrestronHomeNUnit.Workflow;
using NUnit.Framework;
namespace CrestronHomeDevTools.Tests;
public sealed partial class AutomationInstalledAppTests
{
 private async Task<AutomationPerformanceCapture.Request> CaptureRequest(string runState="Runnable") {
  settings=settings with{InstalledAppTests=settings.InstalledAppTests! with{AndroidTests=settings.InstalledAppTests!.AndroidTests with{RequiredTests=["Example.Response"]}}};
  await ConfigureComparison(limits:false,qualitative:true,captureDiscovery:true,captureRunState:runState);
  context=context with{Checkpoint=context.Checkpoint with{SchemaVersion=2,Stage=SubmissionWorkflowStage.FinalizeTests,Status=SubmissionWorkflowStatus.Waiting,ReasonCode="performance-assessment-required"}};
  File.Move(Path.Combine(context.RunDirectory,"performance-assessment/assessment.json"),Path.Combine(root,"assessment.pending"));
  var plan=settings.PostEnduranceTests!;
  return new(1,Guid.NewGuid().ToString("N"),0,"Original video omitted visible feedback; add observations without discarding it.",new('a',64),
   context.Checkpoint.CompletedStages[SubmissionWorkflowStage.Endurance],new("post-endurance/installed-app-tests.json",AutomationFiles.Hash(Path.Combine(context.RunDirectory,"post-endurance/installed-app-tests.json"))),
   plan,await WorkflowEvidence.SourceDigestAsync(plan.SourceRoots,default),AutomationFiles.Hash(plan.AndroidTests.ProfilePath),[new("outlet","Example.Response","response.json")]);
 }
 private Task<InstalledDriverTestResult> CaptureProducer(InstalledDriverTestPlan p,NetworkCredential c,string folder,CancellationToken token) {
  calls++;Directory.CreateDirectory(Path.Combine(folder,"AndroidUI"));
  File.Copy(Path.Combine(context.RunDirectory,"post-endurance/installed-app/AndroidUI/response.json"),Path.Combine(folder,"AndroidUI/response.json"));
  File.WriteAllText(Path.Combine(folder,"AndroidUI/support.json"),"synthetic supplemental visual observation");
  var result=new InstalledDriverTestResult(new WorkflowTestOutcome(1,0,0,true),true,true,true,true,"synthetic capture restored");
  AutomationFiles.Write(Path.Combine(folder,"InstalledDriverTests.json"),result);return Task.FromResult(result);
 }
 private Task<SubmissionWorkflowStepResult> Capture(AutomationPerformanceCapture.Request r)=>AutomationPerformanceCapture.Collect(context,settings,r,CaptureProducer,_=>new(),default);
 [Test] public async Task AdditionalPerformanceCapturePreservesPassesAndIsNeverReplayed() {
  var r=await CaptureRequest();int prior=calls;string post=AutomationFiles.Hash(Path.Combine(context.RunDirectory,r.PostTests.RelativePath)),end=AutomationFiles.Hash(Path.Combine(context.RunDirectory,r.Endurance.RelativePath));
  var first=await Capture(r);Assert.That(first.Status,Is.EqualTo(SubmissionWorkflowStatus.Completed));
  Assert.That((await Capture(r)).Receipt,Is.EqualTo(first.Receipt));Assert.That(calls,Is.EqualTo(prior+1));
  Assert.That(AutomationFiles.Hash(Path.Combine(context.RunDirectory,r.PostTests.RelativePath)),Is.EqualTo(post));Assert.That(AutomationFiles.Hash(Path.Combine(context.RunDirectory,r.Endurance.RelativePath)),Is.EqualTo(end));
  string file="performance-captures/"+r.AttemptId+"/installed-app/AndroidUI/support.json";
  var support=AutomationPerformanceCapture.SupportingFile(context,settings,"outlet",new(file,AutomationFiles.Hash(Path.Combine(context.RunDirectory,file))));
  Assert.That(support.Files.Any(f=>f.RelativePath.EndsWith("/installed-app-tests.json")),Is.True);
  File.AppendAllText(Path.Combine(context.RunDirectory,file),"changed");
  Assert.Throws<InvalidDataException>(()=>AutomationPerformanceCapture.SupportingFile(context,settings,"outlet",new(file,AutomationFiles.Hash(Path.Combine(context.RunDirectory,file)))));
 }
 [TestCase("host")][TestCase("profile")][TestCase("selection")][TestCase("pair")][TestCase("timing")][TestCase("endurance")]
 public async Task AdditionalPerformanceCaptureRejectsChangedScopeBeforeInput(string change) {
  var r=await CaptureRequest();int prior=calls;
  r=change switch{
   "host"=>r with{Plan=r.Plan with{Host="wrong.example"}},
   "profile"=>r with{ProfileSha256=new('f',64)},
   "selection"=>r with{Plan=r.Plan with{AndroidTests=r.Plan.AndroidTests with{RequiredTests=["Unrelated.Test"]}}},
   "pair"=>r with{Pairs=[new("other","Example.Response","response.json")]},
   "timing"=>r with{Plan=r.Plan with{TimeoutSeconds=r.Plan.TimeoutSeconds+1}},
   _=>r with{Endurance=r.Endurance with{Sha256=new('f',64)}}};
  await Assert.ThrowsAsync<InvalidDataException>(async()=>await Capture(r));Assert.That(calls,Is.EqualTo(prior));
 }
 [Test] public async Task AdditionalPerformanceCaptureRejectsExplicitPhysicalCases() {
  var r=await CaptureRequest("Explicit");int prior=calls;await Assert.ThrowsAsync<InvalidDataException>(async()=>await Capture(r));Assert.That(calls,Is.EqualTo(prior));
 }
 [Test] public async Task InterruptedAdditionalCaptureRemainsUncertainWithoutReplay() {
  var r=await CaptureRequest();int prior=calls;
  await Assert.ThrowsAsync<IOException>(async()=>await AutomationPerformanceCapture.Collect(context,settings,r,(_,_,_,_)=>{calls++;throw new IOException("synthetic interruption");},_=>new(),default));
  Assert.That((await Capture(r)).Status,Is.EqualTo(SubmissionWorkflowStatus.OutcomeUnknown));Assert.That(calls,Is.EqualTo(prior+1));
  await Assert.ThrowsAsync<InvalidDataException>(async()=>await Capture(r with{AttemptId=Guid.NewGuid().ToString("N")}));
  Assert.Throws<InvalidDataException>(()=>AutomationPerformanceCapture.VerifyOtherCaptures(context));
 }
 [Test] public async Task UnstartedCaptureCanBeExplicitlyClosedWithoutReplayingOrDiscardingIntent() {
  var r=await CaptureRequest();int prior=calls;
  await Assert.ThrowsAsync<IOException>(async()=>await AutomationPerformanceCapture.Collect(context,settings,r,(_,_,_,_)=>{throw new IOException("before readiness and producer");},_=>new(),default));
  string intent=Path.Combine(context.RunDirectory,"performance-captures/"+r.AttemptId+"/installed-app-intent.json");string hash=AutomationFiles.Hash(intent);
  AutomationPerformanceCapture.CloseUnstarted(context,settings,r,"Original producer never created its required pre-input output directory; inspect retained launch failure.");
  AutomationPerformanceCapture.VerifyOtherCaptures(context);Assert.That(AutomationFiles.Hash(intent),Is.EqualTo(hash));
  await Assert.ThrowsAsync<InvalidDataException>(async()=>await Capture(r));Assert.That(calls,Is.EqualTo(prior));
  Assert.That((await Capture(r with{AttemptId=Guid.NewGuid().ToString("N")})).Status,Is.EqualTo(SubmissionWorkflowStatus.Completed));
 }
 [Test] public async Task StartedCaptureCannotBeClosedAsUnstartedEvenWithoutFinalOutcome() {
  var r=await CaptureRequest();
  await Assert.ThrowsAsync<IOException>(async()=>await AutomationPerformanceCapture.Collect(context,settings,r,(_,_,folder,_)=>{Directory.CreateDirectory(folder);throw new IOException("interrupted after producer entry");},_=>new(),default));
  Assert.Throws<InvalidDataException>(()=>AutomationPerformanceCapture.CloseUnstarted(context,settings,r,"Must reject"));
 }
 [TestCase(false)][TestCase(true)] public async Task PerformanceAssessmentCanUseVerifiedAdditionalEvidenceButRetainsOriginalMeasurements(bool tamperedSeries) {
  var r=await CaptureRequest();await Capture(r);
  string path="performance-captures/"+r.AttemptId+"/installed-app/AndroidUI/support.json";
  var assessment=AutomationFiles.Read<SubmissionPerformanceAssessment>(Path.Combine(root,"assessment.pending"));
  var finding=assessment.Findings[0] with{AfterEvidence=[new(path,AutomationFiles.Hash(Path.Combine(context.RunDirectory,path)))]};
  AutomationFiles.Write(Path.Combine(context.RunDirectory,"performance-assessment/assessment.json"),assessment with{ReviewedUtc=DateTimeOffset.UtcNow,Findings=[finding]});
  if(tamperedSeries){
   string series=Path.Combine(context.RunDirectory,"performance-captures/"+r.AttemptId+"/installed-app/AndroidUI/response.json");File.AppendAllText(series,"changed");
   Assert.Throws<InvalidDataException>(()=>CompareResponses());
  }else{
   var observation=CompareResponses();Assert.That(observation.Outcome,Is.EqualTo(SubmissionEvidenceOutcome.Passed));
   Assert.That(observation.Files.Any(f=>f.RelativePath==settings.ResponseComparison!.Pairs[0].After),Is.True);
   Assert.That(observation.Files.Any(f=>f.RelativePath==path),Is.True);
  }
 }
 private async Task<SubmissionWorkflowStepResult> FailedCapture(AutomationPerformanceCapture.Request r,string unresolved="") =>
  await AutomationPerformanceCapture.Collect(context,settings,r,async(p,c,folder,t)=> {
   calls++;Directory.CreateDirectory(Path.Combine(folder,"AndroidUI"));
   File.Copy(Path.Combine(context.RunDirectory,"post-endurance/installed-app/AndroidUI/response.json"),Path.Combine(folder,"AndroidUI/response.json"));
   await File.WriteAllTextAsync(Path.Combine(folder,"AndroidUI/support.json"),"synthetic failed observation",t);
   var good=new InstalledDriverTestResult(new WorkflowTestOutcome(1,0,0,true),true,true,true,true,"synthetic capture");
   var failed=good with{Tests=new WorkflowTestOutcome(0,1,0,false),RestorationConfirmed=unresolved!="restoration",CleanupConfirmed=unresolved!="cleanup",CandidateVerified=unresolved!="candidate",ReservationsReleased=unresolved!="reservation",Detail="Synthetic observer failure; physical state restored."};
   AutomationFiles.Write(Path.Combine(folder,"InstalledDriverTests.json"),failed);return failed;
  },_=>new(),default);
 [Test] public async Task FailedAdditionalCaptureCanBeClosedButNeverBecomesPassedOrReplayed() {
  var r=await CaptureRequest();Assert.That((await FailedCapture(r)).Status,Is.EqualTo(SubmissionWorkflowStatus.Failed));int prior=calls;
  string result=Path.Combine(context.RunDirectory,"performance-captures/"+r.AttemptId+"/installed-app/InstalledDriverTests.json");string hash=AutomationFiles.Hash(result);
  AutomationPerformanceCapture.CloseFailed(context,settings,r,"Inspected observer timeout; restoration and cleanup confirmed.");AutomationPerformanceCapture.VerifyOtherCaptures(context);
  Assert.That(AutomationFiles.Hash(result),Is.EqualTo(hash));Assert.That(AutomationFiles.Read<InstalledDriverTestResult>(result).Passed,Is.False);
  await Assert.ThrowsAsync<InvalidDataException>(async()=>await Capture(r));Assert.That(calls,Is.EqualTo(prior));
  Assert.That((await Capture(r with{AttemptId=Guid.NewGuid().ToString("N")})).Status,Is.EqualTo(SubmissionWorkflowStatus.Completed));
  Assert.That(AutomationPerformanceCapture.FailedRetention(context).Any(f=>f.RelativePath.EndsWith("/InstalledDriverTests.json")),Is.True);
 }
 [TestCase("restoration")][TestCase("cleanup")][TestCase("candidate")][TestCase("reservation")]
 public async Task FailedAdditionalCaptureCannotCloseWithoutResolvedSafety(string unresolved) {
  var r=await CaptureRequest();await FailedCapture(r,unresolved);
  Assert.Throws<InvalidDataException>(()=>AutomationPerformanceCapture.CloseFailed(context,settings,r,"Must reject unresolved outcome"));
 }
 [Test] public async Task PassedAdditionalCaptureCannotBeClosedAsFailed() {
  var r=await CaptureRequest();await Capture(r);
  Assert.Throws<InvalidDataException>(()=>AutomationPerformanceCapture.CloseFailed(context,settings,r,"Must reject"));
 }
 [Test] public async Task ClosedFailedCaptureRejectsChangedEvidenceBeforeAnotherAttempt() {
  var r=await CaptureRequest();await FailedCapture(r);AutomationPerformanceCapture.CloseFailed(context,settings,r,"Inspected failure");
  File.AppendAllText(Path.Combine(context.RunDirectory,"performance-captures/"+r.AttemptId+"/installed-app/AndroidUI/support.json"),"changed");int prior=calls;
  await Assert.ThrowsAsync<InvalidDataException>(async()=>await Capture(r with{AttemptId=Guid.NewGuid().ToString("N")}));Assert.That(calls,Is.EqualTo(prior));
 }
 [Test] public async Task PerformanceComparisonRetainsClosedFailuresAlongsideItsPassedObservations() {
  var r=await CaptureRequest();await FailedCapture(r);AutomationPerformanceCapture.CloseFailed(context,settings,r,"Inspected failure");
  var assessment=AutomationFiles.Read<SubmissionPerformanceAssessment>(Path.Combine(root,"assessment.pending"));
  AutomationFiles.Write(Path.Combine(context.RunDirectory,"performance-assessment/assessment.json"),assessment with{ReviewedUtc=DateTimeOffset.UtcNow});
  var observation=CompareResponses();Assert.That(observation.Files.Any(f=>f.RelativePath=="performance-captures/"+r.AttemptId+"/closed-failed.json"),Is.True);
  Assert.That(observation.Files.Any(f=>f.RelativePath=="performance-captures/"+r.AttemptId+"/installed-app/AndroidUI/support.json"),Is.True);
 }
}
