// Copyright (c) 2026 Neil Colvin. MIT licensed.
using System.Text.Json;
using CrestronHomeDevTools.Automation;
using CrestronHomeNUnit.Workflow;
using CrestronHomeNUnit.Client;
using NUnit.Framework;

namespace CrestronHomeDevTools.Tests;

public sealed partial class AutomationInstalledAppTests
{
 private void WithManualSteps() {
  settings=settings with {OperatorInbox=new(Path.Combine(context.RunDirectory,"operator-inbox"),new('a',64)),
   InstalledAppTests=settings.InstalledAppTests! with {AndroidTests=settings.InstalledAppTests!.AndroidTests with {
    RequiredTests=["Example.Tests.Single","Example.Tests.Double"]}},
   InstalledAppSteps=[new(["Example.Tests.Single"],"Prepare for a single press of Demo Button."),
    new(["Example.Tests.Double"],"Prepare for a double press of Demo Button.")]};
 }
 private SubmissionOperatorHandle Readiness()=>SubmissionOperatorStep.Pending(settings.OperatorInbox!.Directory,settings.OperatorInbox.RunKey).Single();
 [Test] public async Task PreparedModeStartsTheFixtureWithoutPublishingAnEarlyReadyPrompt() {
  WithManualSteps();settings=settings with {InstalledAppSteps=settings.InstalledAppSteps!.Select(s=>s with{PrepareBeforeReadiness=true}).ToArray()};
  Task<InstalledDriverTestResult> PreparedRun(InstalledDriverTestPlan p,System.Net.NetworkCredential c,string f,CancellationToken t) {
   Assert.That(p.OperatorReadiness,Is.Not.Null);
   Assert.That(p.OperatorReadiness!.ReadStatus(),Is.Null);
   Assert.That(p.OperatorReadiness.Step,Does.EndWith(".prepared-ready"));
   return Run(p,c,f,t);
  }
  Assert.That((await AutomationInstalledApp.Advance(context,settings,false,PreparedRun,_=>new(),default)).Status,Is.EqualTo(SubmissionWorkflowStatus.Completed));
  Assert.That(calls,Is.EqualTo(2));
 }
 [Test] public void PreparedModeCannotSilentlyRunAnUnspecifiedPhysicalAction() {
  WithManualSteps();settings=settings with{InstalledAppSteps=[new(["Example.Tests.Single"]){PrepareBeforeReadiness=true},settings.InstalledAppSteps![1]]};
  Assert.Throws<InvalidDataException>(()=>AutomationAppSteps.Validate(settings));
 }
 [Test] public async Task ManualStepsWaitAcrossRestartAndRunOnlyAcknowledgedCase() {
  WithManualSteps();
  Assert.That((await Advance()).Status,Is.EqualTo(SubmissionWorkflowStatus.Waiting));
  Assert.That(calls,Is.Zero);var first=Readiness();
  Assert.That((await Advance(true)).Status,Is.EqualTo(SubmissionWorkflowStatus.Waiting));
  Assert.That(Readiness(),Is.EqualTo(first));Assert.That(calls,Is.Zero);
  SubmissionOperatorStep.Respond(first,SubmissionOperatorOutcome.Done);
  Assert.That((await Advance()).Status,Is.EqualTo(SubmissionWorkflowStatus.Waiting));
  Assert.That(calls,Is.EqualTo(1));var second=Readiness();Assert.That(second,Is.Not.EqualTo(first));
  Assert.That((await Advance(true)).Status,Is.EqualTo(SubmissionWorkflowStatus.Waiting));Assert.That(calls,Is.EqualTo(1));
  SubmissionOperatorStep.Respond(second,SubmissionOperatorOutcome.Done);
  Assert.That((await Advance()).Status,Is.EqualTo(SubmissionWorkflowStatus.Completed));Assert.That(calls,Is.EqualTo(2));
  AutomationInstalledApp.VerifyRetained(context.RunDirectory);
  Assert.That((await Advance(true)).Status,Is.EqualTo(SubmissionWorkflowStatus.Completed));Assert.That(calls,Is.EqualTo(2));
 }
 [Test] public async Task CannotPerformPausesWithoutStartingTestsAndRetainsReason() {
  WithManualSteps();await Advance();
  SubmissionOperatorStep.Respond(Readiness(),SubmissionOperatorOutcome.Unable,"The button is unavailable.");
  var result=await Advance();Assert.That(result.Status,Is.EqualTo(SubmissionWorkflowStatus.NeedsInput));
  Assert.That(calls,Is.Zero);Assert.That(SubmissionOperatorStep.Pending(settings.OperatorInbox!.Directory,settings.OperatorInbox.RunKey),Is.Empty);
  Assert.That(File.ReadAllText(Path.Combine(context.RunDirectory,"installed-app","steps","000","readiness-response.json")),Does.Contain("The button is unavailable."));
 }
 [Test] public async Task FailedActionDoesNotIssueNextPromptOrReplayOnRecovery() {
  WithManualSteps();await Advance();SubmissionOperatorStep.Respond(Readiness(),SubmissionOperatorOutcome.Done);
  Task<InstalledDriverTestResult> Fail(InstalledDriverTestPlan p,System.Net.NetworkCredential c,string f,CancellationToken t) {
   calls++;Directory.CreateDirectory(f);
   var result=new InstalledDriverTestResult(new WorkflowTestOutcome(0,1,0,true),true,true,true,true,"Synthetic event mismatch");
   File.WriteAllText(Path.Combine(f,"InstalledDriverTests.json"),JsonSerializer.Serialize(result));return Task.FromResult(result);
  }
  var result=await AutomationInstalledApp.Advance(context,settings,false,Fail,_=>new(),default);
  Assert.That(result.Status,Is.EqualTo(SubmissionWorkflowStatus.NeedsInput));Assert.That(calls,Is.EqualTo(1));
  Assert.That(SubmissionOperatorStep.Pending(settings.OperatorInbox!.Directory,settings.OperatorInbox.RunKey),Is.Empty);
  Assert.That((await Advance(true)).Status,Is.EqualTo(SubmissionWorkflowStatus.NeedsInput));Assert.That(calls,Is.EqualTo(1));
 }
 [Test] public void StepCoverageCannotOmitOrRepeatAnySelectedTest() {
  WithManualSteps();settings=settings with {InstalledAppSteps=[settings.InstalledAppSteps![0]]};
  Assert.Throws<InvalidDataException>(()=>AutomationAppSteps.Validate(settings));
 }
 [Test] public async Task SeparateOutagePhaseHasItsOwnDurablePromptAndDoesNotReplayMainTests() {
  WithManualSteps();ConfigureSeparateInitial();
  settings=settings with {PreEnduranceAppSteps=[new(["Example.Tests.Single"],"Prepare the outage device."),
   new(["Example.Tests.Double"],"Prepare the second outage check.")]};
  await Advance();SubmissionOperatorStep.Respond(Readiness(),SubmissionOperatorOutcome.Done);
  await Advance();SubmissionOperatorStep.Respond(Readiness(),SubmissionOperatorOutcome.Done);
  await Advance();Assert.That(calls,Is.EqualTo(2));
  var first=await AutomationInitialAdditionalTests.Advance(context,settings,Run,_=>new(),default);
  Assert.That(first.Status,Is.EqualTo(SubmissionWorkflowStatus.Waiting));Assert.That(calls,Is.EqualTo(2));
  var outage=Readiness();Assert.That(SubmissionOperatorStep.Read(outage).Request.Target,Does.StartWith("outage.example:"));
  var resumed=await AutomationInitialAdditionalTests.Advance(context,settings,Run,_=>new(),default);
  Assert.That(resumed.Status,Is.EqualTo(SubmissionWorkflowStatus.Waiting));Assert.That(Readiness(),Is.EqualTo(outage));
  SubmissionOperatorStep.Respond(outage,SubmissionOperatorOutcome.Done);
  await AutomationInitialAdditionalTests.Advance(context,settings,Run,_=>new(),default);
  Assert.That(calls,Is.EqualTo(3));SubmissionOperatorStep.Respond(Readiness(),SubmissionOperatorOutcome.Done);
  Assert.That((await AutomationInitialAdditionalTests.Advance(context,settings,Run,_=>new(),default)).Status,Is.EqualTo(SubmissionWorkflowStatus.Completed));
  Assert.That(calls,Is.EqualTo(4));AutomationInitialAdditionalTests.VerifyRetained(context);
 }
 [Test] public async Task SourceChangeWhileWaitingCannotStartTheRecorder() {
  WithManualSteps();await Advance();SubmissionOperatorStep.Respond(Readiness(),SubmissionOperatorOutcome.Done);
  File.AppendAllText(settings.InstalledAppTests!.AndroidTests.Project,"changed source");
  await Assert.ThrowsAsync<InvalidDataException>(async()=>await Advance());Assert.That(calls,Is.Zero);
 }
 [Test] public async Task CompletedStepsWithRetainedRetryHistoryAggregateAndResumeWithoutReplay() {
  WithManualSteps();
  settings=settings with{InstalledAppSteps=settings.InstalledAppSteps!.Select(s=>s with{OperatorInstructions=null}).ToArray()};
  Task<InstalledDriverTestResult> WithHistory(InstalledDriverTestPlan p,System.Net.NetworkCredential c,string f,CancellationToken t) {
   if(calls==0) {
    for(int attempt=0;attempt<2;attempt++) {
     string history=Directory.CreateDirectory(Path.Combine(f,"recovery-attempts",Guid.NewGuid().ToString("N"))).FullName;
     for(int file=0;file<2100;file++)File.WriteAllText(Path.Combine(history,file+".json"),"original retained evidence");
    }
   }
   return Run(p,c,f,t);
  }
  var first=await AutomationInstalledApp.Advance(context,settings,false,WithHistory,_=>new(),default);
  Assert.That(first.Status,Is.EqualTo(SubmissionWorkflowStatus.Completed));
  Assert.That(calls,Is.EqualTo(2));
  AutomationInstalledApp.VerifyRetained(context.RunDirectory);
  var resumed=await AutomationInstalledApp.Advance(context,settings,true,WithHistory,_=>new(),default);
  Assert.That(resumed,Is.EqualTo(first));
  Assert.That(calls,Is.EqualTo(2),"Completed actions must not be replayed when aggregating history.");
 }
}
