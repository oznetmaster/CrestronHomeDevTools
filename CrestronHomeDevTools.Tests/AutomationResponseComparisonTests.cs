// Copyright (c) 2026 Neil Colvin. MIT licensed.
using System.Net;
using System.Text.Json;
using CrestronHomeDevTools.Automation;
using CrestronHomeNUnit.Workflow;
using NUnit.Framework;

namespace CrestronHomeDevTools.Tests;

public sealed partial class AutomationInstalledAppTests
{
 private async Task ConfigureComparison(double afterMilliseconds=600, bool limits=true, bool nunitProducer=false) {
  ConfigurePostEndurance();
  string policy=Path.Combine(root,"response-policy.json");
  AutomationFiles.Write(policy,new SubmissionEvidencePolicy(1,[new("system.response",TimeSpan.Zero,false,
   new("selected.outlet","combined",SubmissionEvidenceOutcome.Passed,null,false))]));
  settings=settings with{Review=new(new(policy,AutomationFiles.Hash(policy)),new("unused-template",new('a',64)),null!,null!,null!,"Example","Example",[],
    PlannedGaps:limits?null:[new("system.response","No reviewed response limits; retain comparison as partial.")]),
   ResponseComparison=new("system.response",[new("outlet",(nunitProducer?"nunit":"installed-app")+"/AndroidUI/response.json","post-endurance/installed-app/AndroidUI/response.json")],
    limits?new(1000,150,1.25,"Synthetic reviewed limits, not Crestron requirements."):null)};
  var identity=new SubmissionEvidenceIdentity(settings.Release.PackageSha256,settings.Release.SourceCommit,settings.Review.Policy.Sha256,settings.Review.Template.Sha256);
  var start=DateTimeOffset.UtcNow.AddHours(-2);
  string interval=Path.Combine(context.RunDirectory,"endurance-evidence.json");
  AutomationFiles.Write(interval,new{EvidenceDirectory="endurance/observations",Observation=new SubmissionObservation("system.endurance",identity,
   SubmissionEvidenceOutcome.Passed,start,start.AddHours(1),[])});
  context.Checkpoint.CompletedStages[SubmissionWorkflowStage.Endurance]=new("endurance-evidence.json",AutomationFiles.Hash(interval));
  void Measurement(string folder,bool after) {
   string path=Path.Combine(folder,"AndroidUI");Directory.CreateDirectory(path);
   var input=start.AddMinutes(after?61:-1);
   AutomationFiles.Write(Path.Combine(path,"response.json"),new SubmissionResponseSeries(1,identity,"synthetic-device","synthetic-observer",
    [new("room:on",input,input.AddSeconds(1),after?afterMilliseconds:500)]));
  }
  Task<InstalledDriverTestResult> Producer(InstalledDriverTestPlan p,NetworkCredential c,string f,CancellationToken t) {
   Measurement(f,f.Contains("post-endurance",StringComparison.Ordinal));return Run(p,c,f,t);
  }
  if(nunitProducer) {
   string folder=Path.Combine(context.RunDirectory,"nunit");Measurement(folder,false);
   var receipt=AutomationFiles.Complete(context,"windows-tests.json",new{context.Checkpoint.InputSha256,Stage="Windows",
    Files=Directory.GetFiles(folder,"*",SearchOption.AllDirectories).Select(p=>new SubmissionWorkflowReceipt(Path.GetRelativePath(context.RunDirectory,p),AutomationFiles.Hash(p))).ToArray()});
   context.Checkpoint.CompletedStages[SubmissionWorkflowStage.WindowsTests]=receipt.Receipt!;
   settings=settings with{InstalledAppTests=null};
  } else {
   var initial=await AutomationInstalledApp.Advance(context,settings,false,Producer,_=>new(),default);
   context.Checkpoint.CompletedStages[SubmissionWorkflowStage.AppTests]=initial.Receipt!;
  }
  await AutomationPostEndurance.Advance(context,settings,false,Producer,_=>new(),default);
 }
 private SubmissionObservation CompareResponses() {
  var source=AutomationResponseComparison.Prepare(context,settings,default);
  return AutomationFiles.Read<SubmissionEvidenceDocument>(Path.Combine(context.RunDirectory,source.RelativePath)).Observations.Single();
 }
 [TestCase(false)][TestCase(true)]
 public async Task ResponseComparisonUsesRetainedInitialAndPostEvidenceWithoutReplaying(bool nunitProducer) {
  await ConfigureComparison(nunitProducer:nunitProducer);
  int initialCalls=calls;
  var observation=CompareResponses();
  Assert.That(observation.Outcome,Is.EqualTo(SubmissionEvidenceOutcome.Passed));
  Assert.That(observation.Files.Any(f=>f.RelativePath==settings.ResponseComparison!.Pairs[0].Before),Is.True);
  Assert.That(observation.Files.Any(f=>f.RelativePath==settings.ResponseComparison!.Pairs[0].After),Is.True);
  CompareResponses();Assert.That(calls,Is.EqualTo(initialCalls));
  File.AppendAllText(Path.Combine(context.RunDirectory,settings.ResponseComparison!.Pairs[0].Before)," ");
  Assert.Throws<InvalidDataException>(()=>CompareResponses());Assert.That(calls,Is.EqualTo(initialCalls));
 }
 [Test] public async Task SlowerResponseFailsReviewedLimitsRatherThanBecomingAPass() {
  await ConfigureComparison(800);Assert.That(CompareResponses().Outcome,Is.EqualTo(SubmissionEvidenceOutcome.Failed));
 }
 [Test] public async Task MissingLimitsProduceAnExplicitPartialComparison() {
  await ConfigureComparison(limits:false);var result=CompareResponses();
  Assert.That(result.Outcome,Is.EqualTo(SubmissionEvidenceOutcome.Partial));
  Assert.That(result.Rationale,Does.Contain("not a no-degradation pass"));
 }
 [Test] public async Task ComparisonRequiresTheActualCompletedIntervalReceipt() {
  await ConfigureComparison();File.AppendAllText(Path.Combine(context.RunDirectory,"endurance-evidence.json")," ");
  Assert.Throws<InvalidDataException>(()=>CompareResponses());
 }
 [Test] public async Task ChangedLimitsCannotRewriteRetainedComparison() {
  await ConfigureComparison();CompareResponses();
  settings=settings with{ResponseComparison=settings.ResponseComparison! with{Limits=new(2000,500,2,"Changed criteria")}};
  Assert.Throws<InvalidDataException>(()=>CompareResponses());
 }
 [Test] public async Task UnretainedMeasurementsAndNullPathsAreRejected() {
  await ConfigureComparison();
  settings=settings with{ResponseComparison=settings.ResponseComparison! with{Pairs=[new("other","installed-app/unretained.json","post-endurance/installed-app/AndroidUI/response.json")]}};
  Assert.Throws<InvalidDataException>(()=>CompareResponses());
  settings=settings with{ResponseComparison=settings.ResponseComparison! with{Pairs=[new("other","installed-app/AndroidUI/response.json",null!)]}};
  Assert.Throws<InvalidDataException>(()=>AutomationResponseComparison.Validate(settings));
 }
}
