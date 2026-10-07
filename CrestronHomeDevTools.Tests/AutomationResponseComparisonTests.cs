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
 private async Task ConfigureComparison(double afterMilliseconds=600, bool limits=true, bool nunitProducer=false, bool failedPost=false, bool qualitative=false, bool? unchanged=true, bool captureDiscovery=false, string captureRunState="Runnable") {
  ConfigurePostEndurance();
  string policy=Path.Combine(root,"response-policy.json");
  AutomationFiles.Write(policy,new SubmissionEvidencePolicy(1,[new("system.response",TimeSpan.Zero,false,
   new("selected.outlet","combined",SubmissionEvidenceOutcome.Passed,null,false))]));
  settings=settings with{Review=new(new(policy,AutomationFiles.Hash(policy)),new("unused-template",new('a',64)),null!,null!,null!,"Example","Example",[],
    PlannedGaps:limits||qualitative?null:[new("system.response","No reviewed response limits; retain comparison as partial.")]),
   ResponseComparison=new("system.response",[new("outlet",(nunitProducer?"nunit":"installed-app")+"/AndroidUI/response.json","post-endurance/installed-app/AndroidUI/response.json")],
    limits?new(1000,150,1.25,"Synthetic reviewed limits, not Crestron requirements."):null,
    Assessment:qualitative?"performance-assessment/assessment.json":null)};
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
   bool after=f.Contains("post-endurance",StringComparison.Ordinal);
   Measurement(f,after);
   if(captureDiscovery) File.WriteAllText(Path.Combine(f,"AndroidUI/discovery.dump"),"<test-run><test-case fullname='Example.Response' runstate='"+captureRunState+"'/></test-run>");
   if(qualitative) {
    string support=Path.Combine(f,"AndroidUI/support.json");
    AutomationFiles.Write(support,new{Synthetic=true,ObservedBehavior="Functional state and visible feedback from the synthetic producer"});

   }

   if(after && failedPost) {
    var result=new InstalledDriverTestResult(new WorkflowTestOutcome(0,1,0,false),true,true,true,true,"synthetic post failure");
    AutomationFiles.Write(Path.Combine(f,"InstalledDriverTests.json"),result);return Task.FromResult(result);
   }
   return Run(p,c,f,t);
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
  if(qualitative) {
   Directory.CreateDirectory(Path.Combine(context.RunDirectory,"performance-assessment"));
     string before="installed-app/AndroidUI/support.json",last="post-endurance/installed-app/AndroidUI/support.json";
     AutomationFiles.Write(Path.Combine(context.RunDirectory,"performance-assessment/assessment.json"),new SubmissionPerformanceAssessment(1,identity,
      "system.response",start,start.AddHours(1),"synthetic test reviewer",DateTimeOffset.UtcNow,
      [new("outlet","room:on",true,unchanged,true,true,"Synthetic reviewed before/after observations.",
       [new(before,AutomationFiles.Hash(Path.Combine(context.RunDirectory,before)))],
       [new(last,AutomationFiles.Hash(Path.Combine(context.RunDirectory,last)))])]));
    }
 }
 private SubmissionObservation CompareResponses() {
  var source=AutomationResponseComparison.Prepare(context,settings,default);
  return AutomationFiles.Read<SubmissionEvidenceDocument>(Path.Combine(context.RunDirectory,source.RelativePath)).Observations.Single();
 }
 [Test] public async Task ResponseComparisonUsesAcceptedUnsplitPostReplacementAndPreservesFailedMeasurements() {
  await ConfigureComparison(800,failedPost:true);
  string post=Path.Combine(context.RunDirectory,"post-endurance");
  var selected=AutomationPostEndurance.Resolve(context,settings);
  var plan=selected.InstalledAppTests!;
  var request=new AutomationAppStepRecovery.Request(1,"post-endurance",0,Guid.NewGuid().ToString("N"),new('f',64),
   AutomationAppStepRecovery.EvidenceHash(post),"installed-app/InstalledDriverTests.json",plan,
   await WorkflowEvidence.SourceDigestAsync(plan.SourceRoots,default),AutomationFiles.Hash(plan.AndroidTests.ProfilePath));
  string original=Path.Combine(post,"installed-app/AndroidUI/response.json");
  string hash=AutomationFiles.Hash(original);
  await AutomationAppStepRecovery.Execute(context with{RunDirectory=post},selected,request,(p,c,f,t)=>{
   Directory.CreateDirectory(Path.Combine(f,"AndroidUI"));
   // Change only the independently measured latency in this synthetic successful replacement.
   string text=File.ReadAllText(original).Replace("800","600",StringComparison.Ordinal);
   File.WriteAllText(Path.Combine(f,"AndroidUI/response.json"),text);
   return Run(p,c,f,t);
  },new(),default);
  Assert.That((await AutomationPostEndurance.Advance(context,settings,true,Run,_=>new(),default)).Status,Is.EqualTo(SubmissionWorkflowStatus.Completed));
  var report=CompareResponses();
  Assert.That(report.Outcome,Is.EqualTo(SubmissionEvidenceOutcome.Passed));
  Assert.That(report.Files.Any(f=>f.RelativePath.Contains(request.AttemptId,StringComparison.Ordinal)),Is.True);
  Assert.That(AutomationFiles.Hash(original),Is.EqualTo(hash));
  Assert.That(calls,Is.EqualTo(2));
 }
 [TestCase(false)][TestCase(true)]
 public async Task ResponseComparisonAcceptsCombinedInitialReceiptAndChecksBothInventories(bool additionalMeasurement) {
  await ConfigureComparison();
  context.Checkpoint.CompletedStages.Remove(SubmissionWorkflowStage.AppTests);
  settings=settings with{PreEnduranceTests=settings.InstalledAppTests};
  var additional=await AutomationInitialAdditionalTests.Advance(context,settings,(p,c,f,t)=>{
   string measurements=Path.Combine(f,"AndroidUI");Directory.CreateDirectory(measurements);
   File.Copy(Path.Combine(context.RunDirectory,"installed-app","AndroidUI","response.json"),Path.Combine(measurements,"response.json"));
   return Run(p,c,f,t);
  },_=>new(),default);
  context.Checkpoint.CompletedStages[SubmissionWorkflowStage.AppTests]=additional.Receipt!;
  if(additionalMeasurement) settings=settings with{ResponseComparison=settings.ResponseComparison! with{Pairs=[new("outlet",
   "pre-endurance/installed-app/AndroidUI/response.json","post-endurance/installed-app/AndroidUI/response.json")]}};
  Assert.That(CompareResponses().Outcome,Is.EqualTo(SubmissionEvidenceOutcome.Passed));
  File.AppendAllText(Path.Combine(context.RunDirectory,"pre-endurance","installed-app","raw.xml"),"changed");
  Assert.Throws<InvalidDataException>(()=>CompareResponses());
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

 [TestCase(true, SubmissionEvidenceOutcome.Passed)]
 [TestCase(false, SubmissionEvidenceOutcome.Failed)]
 [TestCase(null, SubmissionEvidenceOutcome.Inconclusive)]
 public async Task QualitativePerformanceResultUsesRetainedPhaseTwoEvidence(bool? unchanged,SubmissionEvidenceOutcome expected) {
  await ConfigureComparison(1800,limits:false,qualitative:true,unchanged:unchanged);
  int originalCalls=calls;
  var observation=CompareResponses();
  Assert.That(observation.Outcome,Is.EqualTo(expected));
  Assert.That(AutomationResponseComparison.AcceptableForFinalization(context,settings),Is.EqualTo(expected==SubmissionEvidenceOutcome.Passed));
  Assert.That(observation.Files.Any(f=>f.RelativePath=="performance-assessment/assessment.json"),Is.True);
  Assert.That(observation.Files.Count(f=>f.RelativePath.EndsWith("support.json")),Is.EqualTo(2));
  Assert.That(calls,Is.EqualTo(originalCalls));
  File.AppendAllText(Path.Combine(context.RunDirectory,"installed-app/AndroidUI/support.json"),"changed");
  Assert.Throws<InvalidDataException>(()=>CompareResponses());
 }
 [Test] public async Task QualitativeAssessmentDoesNotOverrideFrozenNumericalFailure() {
  await ConfigureComparison(1800);
  Assert.That(CompareResponses().Outcome,Is.EqualTo(SubmissionEvidenceOutcome.Failed));
  string receipt=Path.Combine(context.RunDirectory,"response-comparison/receipt.json");
  string hash=AutomationFiles.Hash(receipt);
  settings=settings with {ResponseComparison=settings.ResponseComparison! with {Assessment="post-endurance/installed-app/AndroidUI/performance-assessment.json"}};
  Assert.Throws<InvalidDataException>(()=>CompareResponses());
  Assert.That(AutomationFiles.Hash(receipt),Is.EqualTo(hash));
 }
}
