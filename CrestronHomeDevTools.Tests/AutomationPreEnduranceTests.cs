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
 [TestCase(SubmissionEvidenceOutcome.Passed,true)]
 [TestCase(SubmissionEvidenceOutcome.Partial,false)]
 public void AdditionalInitialEvidenceFeedsGateOnlyFromVerifiedInventory(SubmissionEvidenceOutcome outcome,bool passed) {
  string requirement=initial[0];
  Observations(initial.Where(id=>id!=requirement).Select(Observation).ToArray());
  Directory.CreateDirectory(P("installed-app"));
  Write("installed-app-tests.json",new{context.Checkpoint.InputSha256,Files=Array.Empty<SubmissionWorkflowReceipt>()});
  var app=new SubmissionWorkflowReceipt("installed-app-tests.json",Hash("installed-app-tests.json"));
  context.Checkpoint.CompletedStages.Remove(SubmissionWorkflowStage.AppTests);
  Directory.CreateDirectory(P("pre-endurance/installed-app"));
  Write("pre-endurance/initial-app-binding.json",new{context.Checkpoint.InputSha256,AppTests=app});
  Write("pre-endurance/installed-app-intent.json",new{Synthetic=true});
  File.WriteAllText(P("pre-endurance/installed-app/trace.txt"),"Synthetic additional initial evidence");
  Write("pre-endurance/installed-app/observations.json",new SubmissionEvidenceDocument(1,
   [Observation(requirement) with{Outcome=outcome,Files=[new("installed-app/trace.txt",Hash("pre-endurance/installed-app/trace.txt"))]}]));
  Write("pre-endurance/installed-app-tests.json",new{context.Checkpoint.InputSha256,Files=new[]{
   new SubmissionWorkflowReceipt(Path.Combine("installed-app","observations.json"),Hash("pre-endurance/installed-app/observations.json")),
   new(Path.Combine("installed-app","trace.txt"),Hash("pre-endurance/installed-app/trace.txt"))}});
  context.Checkpoint.CompletedStages[SubmissionWorkflowStage.AppTests]=AutomationInitialAdditionalTests.Complete(context).Receipt!;
  // This gate test starts after a synthetic completed producer; it does not execute this plan.
  settings=settings with{PreEnduranceTests=new(){Host=settings.NUnit.Host,CertificateSha256=settings.NUnit.CertificateSha256,
   SshFingerprint=settings.NUnit.SshFingerprint,PackagePath="unused.pkg",PackageSha256=settings.Release.PackageSha256,
   SourceRoots=[root],Target=new(2,-1,"Example","Model",1,"1.0.0.0","catalogue","Example","IP"),
   AndroidTests=new("unused.csproj","unused.json")},Review=settings.Review! with{
    ObservationSources=["nunit/observations.json","pre-endurance/installed-app/observations.json"]}};
  Assert.That(AutomationPreEndurance.Check(context,settings,default).EvidenceChecksPassed,Is.EqualTo(passed));
  File.AppendAllText(P("pre-endurance/installed-app/trace.txt"),"changed");
  Assert.Throws<InvalidDataException>(()=>AutomationPreEndurance.Check(context,settings,default));
 }
 [Test]public void CompletedInitialCoverageAllowsEnduranceWithoutEnduranceEvidence() {
  Assert.That(AutomationPreEndurance.Check(context,settings,default).EvidenceChecksPassed,Is.True);
 }
 [TestCase("installed-app/steps/000")]
 [TestCase("pre-endurance/installed-app/steps/000")]
 [TestCase("pre-endurance/installed-app/steps/000/installed-app/preparation-recovery")]
 public void SteppedEvidenceUsesItsActualProducerBase(string producer) {
  Observations(initial.Where(id=>id!="system.power").Select(Observation).ToArray());
  string observation=AddProducer(producer,"system.power");
  PinAppFiles([producer+"/installed-app/trace.txt",observation]);
  settings=settings with{Review=settings.Review! with{ObservationSources=["nunit/observations.json",observation]}};
  Assert.That(AutomationPreEndurance.Check(context,settings,default).EvidenceChecksPassed,Is.True);
  File.AppendAllText(P(producer+"/installed-app/trace.txt"),"changed");
  Assert.Throws<InvalidDataException>(()=>AutomationPreEndurance.Check(context,settings,default));
 }
 [TestCase(false)][TestCase(true)]
 public void AcceptedSplitReplacementFeedsTheSameEvidenceToGateAndReview(bool tamper) {
  Observations(initial.Where(id=>id is not ("system.power" or "system.network")).Select(Observation).ToArray());
  string step="pre-endurance/installed-app/steps/000",id=new('a',32),attempt="installed-app/recovery-attempts/"+id+"/";
  string[] producers=Enumerable.Range(0,2).Select(i=>attempt+"invocations/"+i.ToString("D3")+"/installed-app/AndroidUI/").ToArray();
  var files=new List<string>();var mappings=new Dictionary<string,string>();
  string[] requirements=["system.power","system.network"];
  for(int i=0;i<2;i++) {
   string producer=step+"/"+attempt+"invocations/"+i.ToString("D3");
   files.Add(AddProducer(producer,requirements[i]));files.Add(producer+"/installed-app/trace.txt");
   mappings.Add(requirements[i]+".json",producers[i]+"observations.json");
  }
  Directory.CreateDirectory(P(step+"/installed-app/AndroidUI"));
  foreach(string requirement in requirements) {
   // Failed originals remain retained, but cannot replace the accepted observation.
   string old=step+"/installed-app/AndroidUI/"+requirement+".json";
   Write(old,new SubmissionEvidenceDocument(1,[Observation(requirement) with{Outcome=SubmissionEvidenceOutcome.Failed}]));files.Add(old);
  }
  string pointer=step+"/installed-app/replacement.json";
  Write(pointer,new AutomationAppStepRecovery.Completion(id,producers[0],new('b',64),producers,mappings));files.Add(pointer);
  foreach(string name in new[]{"attempt.json","original-evidence.json"}) {
   string path=step+"/"+attempt+name;Write(path,new{Synthetic=true});files.Add(path);
  }
  PinAppFiles(files);
  settings=settings with{Review=settings.Review! with{ObservationSources=["nunit/observations.json",..requirements.Select(r=>step+"/installed-app/AndroidUI/"+r+".json")]}};
  if(tamper) {
   File.AppendAllText(P(pointer)," ");
   Assert.Throws<InvalidDataException>(()=>AutomationPreEndurance.Check(context,settings,default));
  } else Assert.That(AutomationPreEndurance.Check(context,settings,default).EvidenceChecksPassed,Is.True);
 }

 [TestCase("original")][TestCase("accepted")][TestCase("missing")]
 [TestCase("tampered")][TestCase("unretained")][TestCase("provenance-missing")]
 public void ResponseBaselineUsesAcceptedProducerLineage(string mode) {
  var policy=AutomationFiles.Read<SubmissionEvidencePolicy>(P("policy.json"));
  Write("policy.json",policy with{Requirements=[..policy.Requirements,
   new("performance",TimeSpan.Zero,Execution:new("candidate","combined",SubmissionEvidenceOutcome.Passed,null,false,null))]});
  identity=identity with{PolicySha256=Hash("policy.json")};
  settings=settings with{Review=settings.Review! with{Policy=new(P("policy.json"),identity.PolicySha256)},
   Endurance=settings.Endurance! with{Plan=settings.Endurance.Plan with{Identity=identity}},
   PostEnduranceTests=new(){Host="fixture",CertificateSha256=new('e',64),SshFingerprint="fixture",
    PackagePath="unused.pkg",PackageSha256=settings.Release.PackageSha256,SourceRoots=[root],
    Target=new(2,-1,"Example","Model",1,"1.0.0.0","catalogue","Example","IP"),AndroidTests=new("unused.csproj","unused.json")}};
  Observations(initial.Select(Observation).ToArray());
  string step="installed-app/steps/000",id=new('a',32),attempt="installed-app/recovery-attempts/"+id+"/";
  string suffix="outlet/response-measurements.json",requested=step+"/installed-app/AndroidUI/"+suffix;
  string prefix=attempt+"installed-app/AndroidUI/",replacement=step+"/"+prefix+suffix;
  settings=settings with{ResponseComparison=new("performance",[new("outlet",requested,"post-endurance/installed-app/AndroidUI/"+suffix)],Assessment:"performance-assessment/assessment.json")};
  var files=new List<string>();
  void Add(string path,object value){Directory.CreateDirectory(Path.GetDirectoryName(P(path))!);Write(path,value);files.Add(path);}
  if(mode=="original")Add(requested,new{Synthetic=true});
  else if(mode!="missing") {
   Add(replacement,new{Synthetic=true});
   Add(step+"/installed-app/replacement.json",new AutomationAppStepRecovery.Completion(id,prefix,new('b',64)));
   Add(step+"/"+attempt+"attempt.json",new{Synthetic=true});
   if(mode!="provenance-missing")Add(step+"/"+attempt+"original-evidence.json",new{Synthetic=true});
   if(mode=="unretained")files.Remove(replacement);
  }
  PinAppFiles(files);
  if(mode=="tampered")File.AppendAllText(P(replacement),"changed");
  if(mode is "tampered" or "unretained" or "provenance-missing")
   Assert.Throws<InvalidDataException>(()=>AutomationPreEndurance.Check(context,settings,default));
  else {
   var report=AutomationPreEndurance.Check(context,settings,default);
   Assert.That(report.EvidenceChecksPassed,Is.EqualTo(mode!="missing"));
   if(mode=="missing")Assert.That(report.Issues.Single().Code,Is.EqualTo("response-baseline-missing"));
  }
 }
 private string AddProducer(string producer,string requirement) {
  Directory.CreateDirectory(P(producer+"/installed-app/AndroidUI"));
  string trace=producer+"/installed-app/trace.txt",observation=producer+"/installed-app/AndroidUI/observations.json";
  File.WriteAllText(P(trace),"synthetic producer evidence");
  Write(observation,new SubmissionEvidenceDocument(1,[Observation(requirement) with{Files=[new("installed-app/trace.txt",Hash(trace))]}]));
  return observation;
 }
 private void PinAppFiles(IEnumerable<string> paths) {
  Write("app-tests.json",new{context.Checkpoint.InputSha256,Files=paths.Select(p=>new SubmissionWorkflowReceipt(p,Hash(p))).ToArray()});
  context.Checkpoint.CompletedStages[SubmissionWorkflowStage.AppTests]=new("app-tests.json",Hash("app-tests.json"));
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
 [TestCase(SubmissionAutomationMode.Rehearsal)]
 [TestCase(SubmissionAutomationMode.Submit)]
 public void ConfigurationReportsKnownInitialGapsBeforeReadingEvidence(SubmissionAutomationMode mode) {
  // Readiness inspection must work without opening the future worker's paths.
  File.Delete(P("policy.json"));
  var draft=settings with{Mode=mode,Review=settings.Review! with{PlannedGaps=[
   new("system.power","Missing timing"),new("system.network","Missing timing"),new("button.double","Not recorded")]}};
  var report=SubmissionAutomationConfiguration.Check(draft,releaseTemplate:true);
  Assert.That(report.AllStageBindingsPresent,Is.False);
  foreach(string id in new[]{"system.power","system.network","button.double"})
   Assert.That(report.MissingBindings,Does.Contain("Resolve declared pre-endurance gap: "+id));
 }
 [Test]public void ConfigurationDefersOnlyExactlyBoundLaterOperationsNotPlacement() {
  settings=settings with{Removal=new(null!,"remove","ui.placement"),ResponseComparison=new("performance",[]),
   Review=settings.Review! with{PlannedGaps=[new("endurance","One-hour rehearsal"),new("remove","After interval"),
    new("performance","After interval"),new("ui.placement","Missing initial placement"),new("other.endurance","Not bound")]}};
  var report=SubmissionAutomationConfiguration.Check(settings,true);
  Assert.That(report.MissingBindings.Where(x=>x.StartsWith("Resolve declared pre-endurance gap:",StringComparison.Ordinal)),
   Is.EquivalentTo(new[]{"Resolve declared pre-endurance gap: ui.placement","Resolve declared pre-endurance gap: other.endurance"}));
 }
 [Test]public void RemovingADeclaredGapDoesNotReplaceTheEvidenceGate() {
  settings=settings with{Review=settings.Review! with{PlannedGaps=[]}};
  Assert.That(SubmissionAutomationConfiguration.Check(settings,true).MissingBindings,
   Has.None.StartsWith("Resolve declared pre-endurance gap:"));
  Observations(initial.Where(id=>id!="system.network").Select(Observation).ToArray());
  Assert.That(AutomationPreEndurance.Check(context,settings,default).Issues.Any(i=>i.RequirementId=="system.network" && i.Code=="missing-observation"),Is.True);
 }
 [Test]public void MalformedAndRepeatedGapDeclarationsDoNotHideReadinessProblems() {
  settings=settings with{Review=settings.Review! with{PlannedGaps=[null!,new("","Invalid"),new("system.power","First"),new("system.power","Second")]}};
  var report=SubmissionAutomationConfiguration.Check(settings,true);
  Assert.That(report.MissingBindings.Count(x=>x=="Review.PlannedGaps requires an explicit requirement ID"),Is.EqualTo(1));
  Assert.That(report.MissingBindings.Count(x=>x=="Resolve declared pre-endurance gap: system.power"),Is.EqualTo(1));
 }
 [Test]public void MissingInitialStageBlocksEvenWhenObservationsPass() {
  context.Checkpoint.CompletedStages.Remove(SubmissionWorkflowStage.ProcessorTests);
  Assert.That(AutomationPreEndurance.Check(context,settings,default).Issues.Single().Code,Is.EqualTo("initial-stage-incomplete"));
 }
}
