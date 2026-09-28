// Copyright (c) 2026 Neil Colvin. MIT licensed.
using CrestronHomeDevTools.Automation;
using CrestronHomeNUnit.Workflow;
using NUnit.Framework;

namespace CrestronHomeDevTools.Tests;

[TestFixture]
public sealed class AutomationPreEnduranceTests
{
 private string root=null!;
 private SubmissionAutomationSettings settings=null!;
 private SubmissionWorkflowStepContext context=null!;
 private SubmissionEvidenceIdentity identity=null!;
 private readonly string[] initial=["button.double","motion.recovery","system.power","system.network","ui.placement"];
 private string P(string name)=>Path.Combine(root,name);
 private string Hash(string name)=>AutomationFiles.Hash(P(name));
 private void Write(string name,object value)=>File.WriteAllBytes(P(name),System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(value,AutomationFiles.Json));
 [SetUp]public void Setup() {
  root=Path.Combine(TestContext.CurrentContext.WorkDirectory,"pre-endurance-"+Guid.NewGuid().ToString("N"));
  Directory.CreateDirectory(P("nunit"));
  File.WriteAllText(P("template.pdf"),"synthetic template");
  File.WriteAllText(P("nunit/trace.txt"),"synthetic test evidence, not a live test");
  var duration=new SubmissionRequirement("endurance",TimeSpan.FromHours(24),Execution:new("candidate","endurance",SubmissionEvidenceOutcome.Passed,null,false,600));
  Write("policy.json",new SubmissionEvidencePolicy(1,initial.Select(id=>new SubmissionRequirement(id,TimeSpan.Zero)).Append(duration).ToArray()));
  var release=new SubmissionWorkflowRelease("fixture/driver",1,"v1",new('a',40),new('b',64),new('c',64),new('d',64));
  identity=new(release.PackageSha256,release.SourceCommit,Hash("policy.json"),Hash("template.pdf"));
  var review=new SubmissionAutomationReviewPlan(new(P("policy.json"),identity.PolicySha256),new(P("template.pdf"),identity.TemplateSha256),
   new("unused",new('e',64)),new("unused",new('e',64)),new(root,[]),"Synthetic","Tests",["nunit/observations.json"]);
  var nunit=new WorkflowPlan{Host="fixture",CertificateSha256=new('e',64),SshFingerprint="fixture",SourceRoots=[root],LocalTests=[],
   TestPackage=new("unused.csproj","unused.pkg","fixture",1),ProcessorSuites=[]};
  var plan=new SubmissionEndurancePlan(identity,duration,"fixture","candidate",Guid.NewGuid().ToString("N"),"fixture",TimeSpan.FromMinutes(5),TimeSpan.FromSeconds(30));
  settings=new(1,root,release,root,new(Guid.NewGuid().ToString(),"1.0.0.0",PortalSubmissionKind.NewDriver,"Fixture","fixture@example.invalid"),
   "unused",nunit,Endurance:new(plan,new("fixture","fixture"),null!),Review:review);
  context=new(root,new(1,new('f',64),release,SubmissionWorkflowStage.Endurance,SubmissionWorkflowStatus.Running,"operation",null,[],DateTimeOffset.UtcNow));
  Observations(initial.Select(Observation).ToArray());
 }
 private SubmissionObservation Observation(string id)=>new(id,identity,SubmissionEvidenceOutcome.Passed,
  DateTimeOffset.UtcNow.AddMinutes(-2),DateTimeOffset.UtcNow.AddMinutes(-1),[new("nunit/trace.txt",Hash("nunit/trace.txt"))]);
 private void Observations(SubmissionObservation[] observations) {
  Write("nunit/observations.json",new SubmissionEvidenceDocument(1,observations));
  var files=new[]{new SubmissionWorkflowReceipt("nunit/observations.json",Hash("nunit/observations.json")),new("nunit/trace.txt",Hash("nunit/trace.txt"))};
  foreach(var stage in new[]{SubmissionWorkflowStage.WindowsTests,SubmissionWorkflowStage.ProcessorTests,SubmissionWorkflowStage.AppTests}) {
   string name=stage switch{SubmissionWorkflowStage.WindowsTests=>"windows-tests.json",SubmissionWorkflowStage.ProcessorTests=>"processor-tests.json",_=>"app-tests.json"};
   Write(name,new{context.Checkpoint.InputSha256,Stage=stage.ToString(),Files=files});
   context.Checkpoint.CompletedStages[stage]=new(name,Hash(name));
  }
 }
 [TearDown]public void Cleanup()=>Directory.Delete(root,true);
 [Test]public void CompletedInitialCoverageAllowsEnduranceWithoutEnduranceEvidence() {
  Assert.That(AutomationPreEndurance.Check(context,settings,default).EvidenceChecksPassed,Is.True);
 }
 [Test]public void OneHourRehearsalKeepsFullInitialCoverageAndDoesNotShortenSubmission() {
  settings=settings with{Mode=SubmissionAutomationMode.Rehearsal,Endurance=settings.Endurance! with{
   Plan=settings.Endurance.Plan with{Requirement=settings.Endurance.Plan.Requirement with{MinimumDuration=TimeSpan.FromHours(1)}}}};
  Assert.That(AutomationPreEndurance.Check(context,settings,default).EvidenceChecksPassed,Is.True);
  Assert.Throws<InvalidDataException>(()=>AutomationPreEndurance.Check(context,settings with{Mode=SubmissionAutomationMode.Submit},default));
 }
 [TestCase(SubmissionAutomationMode.Rehearsal)][TestCase(SubmissionAutomationMode.Submit)]
 public async Task DeclaredPhysicalOrOutageGapStopsBeforeCredentialsOrMonitor(SubmissionAutomationMode mode) {
  Observations([Observation("ui.placement")]);
  settings=settings with{Mode=mode,Review=settings.Review! with{PlannedGaps=initial.Where(id=>id!="ui.placement").Select(id=>new SubmissionGapDeclaration(id,"Do it after endurance")).ToArray()}};
  var adapter=new SubmissionAutomationStages(settings,new('a',64),(_,_,_,_)=>throw new AssertionException("NUnit should not restart"),
   _=>throw new AssertionException("Processor credentials must not be opened"));
  var result=await adapter.ExecuteAsync(context,default);
  Assert.That(result.Status,Is.EqualTo(SubmissionWorkflowStatus.NeedsInput));
  Assert.That(result.ReasonCode,Is.EqualTo("pre-endurance-evidence-required"));
  Assert.That(Directory.Exists(P("endurance")),Is.False);
  var report=AutomationPreEndurance.Check(context,settings,default);
  Assert.That(report.Issues.Select(i=>i.RequirementId),Is.EquivalentTo(initial.Where(id=>id!="ui.placement")));
 }
 [TestCase(SubmissionEvidenceOutcome.Failed)][TestCase(SubmissionEvidenceOutcome.Partial)]
 [TestCase(SubmissionEvidenceOutcome.Inconclusive)][TestCase(SubmissionEvidenceOutcome.NotTested)]
 public void NonPassingPhysicalEvidenceBlocks(SubmissionEvidenceOutcome outcome) {
  Observations(initial.Select(id=>id=="button.double"?Observation(id) with{Outcome=outcome}:Observation(id)).ToArray());
  Assert.That(AutomationPreEndurance.Check(context,settings,default).Issues.Any(i=>i.RequirementId=="button.double" && i.Code=="not-passed"),Is.True);
 }
 [Test]public void CompletedTestCountsDoNotSupplyMissingChecklistObservations() {
  settings=settings with{Review=settings.Review! with{ObservationSources=[]}};
  Assert.That(AutomationPreEndurance.Check(context,settings,default).Issues.Count,Is.EqualTo(initial.Length));
 }
 [Test]public void PostEnduranceSourcesCannotPostponeInitialCoverage() {
  settings=settings with{Review=settings.Review! with{ObservationSources=["post-endurance/installed-app/observations.json"]}};
  Assert.That(AutomationPreEndurance.Check(context,settings,default).EvidenceChecksPassed,Is.False);
  settings=settings with{Review=settings.Review! with{PreEnduranceObservationSources=["nunit/observations.json"]}};
  Assert.That(AutomationPreEndurance.Check(context,settings,default).EvidenceChecksPassed,Is.True);
 }
 [Test]public void ChangedEvidenceAndForeignReceiptsAreRejected() {
  File.AppendAllText(P("nunit/trace.txt")," changed");
  Assert.Throws<InvalidDataException>(()=>AutomationPreEndurance.Check(context,settings,default));
  Observations(initial.Select(Observation).ToArray());
  context=context with{Checkpoint=context.Checkpoint with{InputSha256=new('a',64)}};
  Assert.Throws<InvalidDataException>(()=>AutomationPreEndurance.Check(context,settings,default));
 }
 [Test]public void UnretainedAndDuplicateObservationsAreRejected() {
  File.Copy(P("nunit/observations.json"),P("loose.json"));
  Assert.Throws<InvalidDataException>(()=>AutomationPreEndurance.Check(context,settings with{Review=settings.Review! with{ObservationSources=["loose.json"]}},default));
  Observations(initial.Select(Observation).Append(Observation("button.double")).ToArray());
  Assert.That(AutomationPreEndurance.Check(context,settings,default).Issues.Any(i=>i.Code=="duplicate-observation"),Is.True);
 }
 [Test]public void NotApplicableMustBePermittedByFullPolicy() {
  Observations(initial.Select(id=>Observation(id) with{Outcome=SubmissionEvidenceOutcome.NotApplicable,Rationale="Not available"}).ToArray());
  Assert.That(AutomationPreEndurance.Check(context,settings,default).Issues.Any(i=>i.Code=="invalid-not-applicable"),Is.True);
 }
 [Test]public void PolicyPermittedNotApplicableDoesNotRequireAnUnusedFeatureTest() {
  var policy=AutomationFiles.Read<SubmissionEvidencePolicy>(P("policy.json"));
  Write("policy.json",policy with{Requirements=policy.Requirements.Select(r=>r.Id=="button.double"?r with{AllowNotApplicable=true}:r).ToArray()});
  identity=identity with{PolicySha256=Hash("policy.json")};
  settings=settings with{Review=settings.Review! with{Policy=new(P("policy.json"),identity.PolicySha256)},
   Endurance=settings.Endurance! with{Plan=settings.Endurance.Plan with{Identity=identity}}};
  Observations(initial.Select(id=>id=="button.double"?Observation(id) with{Outcome=SubmissionEvidenceOutcome.NotApplicable,Rationale="No button in this candidate's reviewed scope"}:Observation(id)).ToArray());
  Assert.That(AutomationPreEndurance.Check(context,settings,default).EvidenceChecksPassed,Is.True);
 }
 [Test]public void MissingInitialStageBlocksEvenWhenObservationsPass() {
  context.Checkpoint.CompletedStages.Remove(SubmissionWorkflowStage.ProcessorTests);
  Assert.That(AutomationPreEndurance.Check(context,settings,default).Issues.Single().Code,Is.EqualTo("initial-stage-incomplete"));
 }
}
