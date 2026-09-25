// Copyright (c) 2026 Neil Colvin. MIT licensed.
using System.Text.Json;
using CrestronHomeDevTools.Automation;
using CrestronHomeNUnit.Workflow;
using NUnit.Framework;

namespace CrestronHomeDevTools.Tests;
[TestFixture]
public sealed class AutomationReviewTests
{
 private string root=null!;
 private SubmissionAutomationSettings settings=null!;
 private SubmissionWorkflowStepContext context=null!;
 private int executions;
 private string P(string name)=>Path.Combine(root,name);
 private void Write<T>(string name,T value)=>AutomationReview.WriteDocument(P(name),value);
 private string Hash(string name)=>AutomationFiles.Hash(P(name));
 private SubmissionAutomationInput Input(string name)=>new(P(name),Hash(name));
 [SetUp]public void Setup() {
  root=Path.Combine(TestContext.CurrentContext.WorkDirectory,"automation-review-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);Directory.CreateDirectory(P("nunit"));
  using var package=SubmissionPackageTests.Package();File.WriteAllBytes(P("candidate.pkg"),package.ToArray());
  File.WriteAllText(P("template.pdf"),"%PDF-1.7\nSynthetic template identity");
  File.WriteAllText(P("inventory.json"),"{}");File.WriteAllText(P("mapping.json"),"{}");
  Write("policy.json",new SubmissionEvidencePolicy(1,[new("ui.navigation",TimeSpan.Zero)]));
  var release=new SubmissionWorkflowRelease("example/driver",2,"v1",new('a',40),Hash("candidate.pkg"),new('b',64),new('c',64));
  var identity=new SubmissionEvidenceIdentity(release.PackageSha256,release.SourceCommit,Hash("policy.json"),Hash("template.pdf"));
  File.WriteAllText(P("nunit/trace.txt"),"Synthetic UI observation; not hardware evidence");
  var now=DateTimeOffset.UtcNow.AddMinutes(-1);
  Write("nunit/observations.json",new SubmissionEvidenceDocument(1,[new("ui.navigation",identity,SubmissionEvidenceOutcome.Passed,now,now,[new("nunit/trace.txt",Hash("nunit/trace.txt"))])]));
  AutomationFiles.Write(P("release.json"),new{PackageName="ExampleDeveloper_Test_Example_IP.pkg"});
  AutomationFiles.Write(P("windows-tests.json"),new{Files=new[]{new SubmissionWorkflowReceipt("nunit/observations.json",Hash("nunit/observations.json")),new("nunit/trace.txt",Hash("nunit/trace.txt"))}});
  var review=new SubmissionAutomationReviewPlan(Input("policy.json"),Input("template.pdf"),Input("inventory.json"),Input("mapping.json"),new(root,[]),"Synthetic driver","Test author",["nunit/observations.json"]);
  settings=new(1,root,release,root,new("1286a404-144e-4d77-b96b-1d1272f21c64","1.2.003.0000",PortalSubmissionKind.NewDriver,"ExampleDeveloper","support@example.com"),"unused",new WorkflowPlan{Host="synthetic",CertificateSha256=new('e',64),SshFingerprint="synthetic",SourceRoots=[root],LocalTests=[],TestPackage=new("unused.csproj","unused.pkg","synthetic",1),ProcessorSuites=[]},Review:review);
  context=new(root,new(1,new('d',64),release,SubmissionWorkflowStage.PrepareReview,SubmissionWorkflowStatus.Running,"operation",null,[],DateTimeOffset.UtcNow));executions=0;
 }
 [TearDown]public void Cleanup()=>Directory.Delete(root,true);
 private Task<int> Prepare(SubmissionAutomationConsole console,string[] args,string logs,CancellationToken token) {
  executions++;Assert.That(args.Take(2),Is.EqualTo(new[]{"submission","prepare-review"}));
  using var config=JsonDocument.Parse(File.ReadAllBytes(args[3]));var s=config.RootElement;
  string Field(string key)=>s.GetProperty(key).GetString()!;
  string Arg(string key)=>args[Array.IndexOf(args,key)+1];
  string output=Field("output");Directory.CreateDirectory(output);
  var bundle=SubmissionBundle.Create(Path.Combine(output,"evidence.zip"),Field("candidate"),Arg("--candidate-sha256"),Field("package"),Field("policy"),Field("template"),Field("observations"),Field("evidence"),DateTimeOffset.UtcNow,token);
  Assert.That(bundle.ValidationChecksPassed,Is.True);
  File.WriteAllText(Path.Combine(output,"self-test.review.pdf"),"Synthetic form bytes: adapter test only");File.WriteAllText(Path.Combine(output,"form-report.json"),"{}");
  Write("review/review-receipt.json",new {schemaVersion=1,state="UnsignedReviewPrepared",sourceCommit=settings.Release.SourceCommit,
   candidateSha256=Arg("--candidate-sha256"),inventorySha256=Arg("--inventory-sha256"),mappingSha256=Arg("--mapping-sha256"),
   formSha256=Hash("review/self-test.review.pdf"),formReportSha256=Hash("review/form-report.json"),bundleSha256=bundle.BundleSha256,signingCopy=true,submissionReady=false,deliveryAttempted=false});
  File.WriteAllText(P("review/COMPLETE"),Hash("review/review-receipt.json")+"\n");return Task.FromResult(0);
 }
 [Test]public async Task PreparesFromRetainedObservationsAndRecoveryDoesNotRegenerate() {
  var result=await AutomationReview.Advance(context,settings,false,default,Prepare);
  Assert.That(result.Status,Is.EqualTo(SubmissionWorkflowStatus.Completed));
  var recovered=await AutomationReview.Advance(context,settings,true,default,Prepare);
  Assert.That(recovered.Receipt,Is.EqualTo(result.Receipt));Assert.That(executions,Is.EqualTo(1));
  var sources=AutomationFiles.Read<SubmissionEvidenceDocument>(P("review-inputs/observations.json"));
  Assert.That(sources.Observations.Single().Files.Any(f=>f.RelativePath=="nunit/observations.json"),Is.True);
  AutomationReview.VerifyRetained(root);
 }
 [Test]public void UnretainedObservationCannotBecomeChecklistEvidence() {
  File.Copy(P("nunit/observations.json"),P("untrusted.json"));
  var plan=settings.Review! with{ObservationSources=["untrusted.json"]};
  Assert.Throws<InvalidDataException>(()=>AutomationReview.PrepareInputs(context,settings,plan,default));
 }
 private SubmissionAutomationReviewPlan InstalledObservations(bool completed=true,string? inputSha256=null) {
  Directory.CreateDirectory(P("installed-app"));
  File.Copy(P("nunit/trace.txt"),P("installed-app/trace.txt"));
  var observations=AutomationFiles.Read<SubmissionEvidenceDocument>(P("nunit/observations.json"));
  Write("installed-app/observations.json",observations with {Observations=observations.Observations.Select(o=>o with {
   Files=[new("installed-app/trace.txt",Hash("installed-app/trace.txt"))]}).ToArray()});
  AutomationFiles.Write(P("installed-app-tests.json"),new{InputSha256=inputSha256??context.Checkpoint.InputSha256,Files=new[]{
   new SubmissionWorkflowReceipt(Path.Combine("installed-app","observations.json"),Hash("installed-app/observations.json")),
   new SubmissionWorkflowReceipt(Path.Combine("installed-app","trace.txt"),Hash("installed-app/trace.txt"))}});
  if(completed)context.Checkpoint.CompletedStages.Add(SubmissionWorkflowStage.AppTests,new("installed-app-tests.json",Hash("installed-app-tests.json")));
  return settings.Review! with{ObservationSources=["installed-app/observations.json"]};
 }
 [Test]public async Task CompletedSeparateAppObservationsReachReviewAndBundle() {
  settings=settings with{Review=InstalledObservations()};
  var result=await AutomationReview.Advance(context,settings,false,default,Prepare);
  Assert.That(result.Status,Is.EqualTo(SubmissionWorkflowStatus.Completed));
  var combined=AutomationFiles.Read<SubmissionEvidenceDocument>(P("review-inputs/observations.json"));
  Assert.That(combined.Observations.Single().Files.Any(f=>f.RelativePath=="installed-app/trace.txt"),Is.True);
 }
 [Test]public void UncompletedSeparateAppReceiptDoesNotAuthorizeObservations() {
  var plan=InstalledObservations(completed:false);
  Assert.Throws<InvalidDataException>(()=>AutomationReview.PrepareInputs(context,settings,plan,default));
 }
 [Test]public void ChangedSeparateAppEvidenceBlocksReview() {
  var plan=InstalledObservations();File.AppendAllText(P("installed-app/trace.txt"),"changed");
  Assert.Throws<InvalidDataException>(()=>AutomationReview.PrepareInputs(context,settings,plan,default));
 }
 [Test]public void SeparateAppReceiptFromAnotherWorkflowBlocksReview() {
  var plan=InstalledObservations(inputSha256:new('f',64));
  Assert.Throws<InvalidDataException>(()=>AutomationReview.PrepareInputs(context,settings,plan,default));
 }
 [Test]public void ChangedProducerOutputIsNotRehashedIntoTrustedEvidence() {
  File.AppendAllText(P("nunit/observations.json")," ");
  Assert.Throws<InvalidDataException>(()=>AutomationReview.PrepareInputs(context,settings,settings.Review!,default));
 }
 [Test]public void MissingRequirementIsNotFilledFromTheNUnitPassReceipt() {
  Write("extended-policy.json",new SubmissionEvidencePolicy(1,[new("ui.navigation",TimeSpan.Zero),new("power.interruption",TimeSpan.Zero)]));
  settings=settings with{Review=settings.Review! with{Policy=Input("extended-policy.json")}};
  // Original observations still name the old policy. Retain the original mismatch, do not rebind it.
  var prepared=AutomationReview.PrepareInputs(context,settings,settings.Review!,default);
  using var report=JsonDocument.Parse(File.ReadAllBytes(P("review-inputs/composition-report.json")));
  Assert.That(report.RootElement.GetProperty("compositionChecksPassed").GetBoolean(),Is.False);
  Assert.That(AutomationFiles.Read<SubmissionEvidenceDocument>(P("review-inputs/observations.json")).Observations,Has.Count.EqualTo(1));
 }
 [Test]public async Task InterruptedDocumentOperationStopsRatherThanRepeating() {
  await AutomationReview.Advance(context,settings,false,default,(_,_,_,_)=>throw new IOException("synthetic interrupted process"))
   .ContinueWith(t=>Assert.That(t.IsFaulted,Is.True));
  var result=await AutomationReview.Advance(context,settings,true,default,Prepare);
  Assert.That(result.Status,Is.EqualTo(SubmissionWorkflowStatus.OutcomeUnknown));Assert.That(executions,Is.Zero);
 }
 [Test]public async Task ChangedPreparedPacketBlocksRecoveryAndContinuation() {
  await AutomationReview.Advance(context,settings,false,default,Prepare);
  File.AppendAllText(P("review/self-test.review.pdf"),"changed");
  Assert.Throws<InvalidDataException>(()=>AutomationReview.VerifyRetained(root));
  Assert.ThrowsAsync<InvalidDataException>(async()=>await AutomationReview.Advance(context,settings,true,default,Prepare));
 }
 [Test]public async Task ProcessFailureIsNotACompletedReview() {
  var result=await AutomationReview.Advance(context,settings,false,default,(_,_,_,_)=>Task.FromResult(2));
  Assert.That(result.Status,Is.EqualTo(SubmissionWorkflowStatus.Failed));Assert.That(File.Exists(P("review-evidence.json")),Is.False);
 }
 [Test]public async Task NoReviewPlanIsAnExplicitMissingInput() {
  var result=await AutomationReview.Advance(context,settings with{Review=null},false,default,Prepare);
  Assert.That(result.ReasonCode,Is.EqualTo("review-plan-required"));Assert.That(executions,Is.Zero);
 }
 private void ConfigureApplicability(SubmissionEvidenceOutcome outcome=SubmissionEvidenceOutcome.NotApplicable,
  bool allowed=true,bool correctCandidate=true,string rationale="Source has no slider") {
  Directory.CreateDirectory(P("applicability-source"));
  File.WriteAllText(P("applicability-source/ui.xml"),"<page><button /></page>");
  Write("applicability-policy.json",new SubmissionEvidencePolicy(1,[new("ui.slider",TimeSpan.Zero,allowed)]));
  settings=settings with{Review=settings.Review! with{Policy=Input("applicability-policy.json"),ObservationSources=[]}};
  var identity=ReviewedIdentity();if(!correctCandidate)identity=identity with{PackageSha256=new('f',64)};
  var now=DateTimeOffset.UtcNow.AddMinutes(-1);
  Write("applicability-source/observations.json",new SubmissionEvidenceDocument(1,[new("ui.slider",identity,outcome,now,now,
   [new("applicability/ui.xml",Hash("applicability-source/ui.xml"))],rationale)]));
  settings=settings with{Review=settings.Review! with{Applicability=new(P("applicability-source"),[
   new("ui.xml",Hash("applicability-source/ui.xml")),new("observations.json",Hash("applicability-source/observations.json"))],"observations.json")}};
 }
 [Test]public async Task ReviewedApplicabilityTravelsWithPortableBundleAndNeedsNoSourceOnRecovery() {
  ConfigureApplicability();AutomationApplicability.Prepare(root,ReviewedIdentity(),settings.Review!,default);
  Directory.Delete(P("applicability-source"),true);
  var result=await AutomationReview.Advance(context,settings,false,default,Prepare);
  Assert.That(result.Status,Is.EqualTo(SubmissionWorkflowStatus.Completed));
  var combined=AutomationFiles.Read<SubmissionEvidenceDocument>(P("review-inputs/observations.json"));
  Assert.That(combined.Observations.Single().Outcome,Is.EqualTo(SubmissionEvidenceOutcome.NotApplicable));
  Assert.That(combined.Observations.Single().Files.Any(f=>f.RelativePath=="applicability/ui.xml"),Is.True);
 }
 [TestCase(SubmissionEvidenceOutcome.Passed)]
 [TestCase(SubmissionEvidenceOutcome.Failed)]
 [TestCase(SubmissionEvidenceOutcome.ReviewedPriorPass)]
 public void ApplicabilityCannotImportTestResults(SubmissionEvidenceOutcome outcome) {
  ConfigureApplicability(outcome);
  Assert.Throws<InvalidDataException>(()=>AutomationReview.PrepareInputs(context,settings,settings.Review!,default));
 }
 [TestCase(false,true,"Has no slider")]
 [TestCase(true,false,"Has no slider")]
 [TestCase(true,true,"")]
 public void ApplicabilityRequiresPermissionCandidateAndReason(bool allowed,bool correctCandidate,string rationale) {
  ConfigureApplicability(allowed:allowed,correctCandidate:correctCandidate,rationale:rationale);
  Assert.Throws<InvalidDataException>(()=>AutomationReview.PrepareInputs(context,settings,settings.Review!,default));
 }
 [Test]public void ApplicabilitySourceChangesCannotBeRehashedIntoEvidence() {
  ConfigureApplicability();File.AppendAllText(P("applicability-source/ui.xml"),"<slider />");
  Assert.Throws<InvalidDataException>(()=>AutomationReview.PrepareInputs(context,settings,settings.Review!,default));
 }
 [Test]public void MissingRetainedApplicabilityIsNotSilentlyRestored() {
  ConfigureApplicability();AutomationApplicability.Prepare(root,ReviewedIdentity(),settings.Review!,default);
  File.Delete(P("applicability/ui.xml"));
  Assert.Throws<InvalidDataException>(()=>AutomationReview.PrepareInputs(context,settings,settings.Review!,default));
 }
 private void ConfigureQualification(SubmissionEvidenceOutcome outcome) {
  Directory.CreateDirectory(P("qualification-source"));
  File.WriteAllText(P("qualification-source/outage.txt"),"Synthetic retained outage: restoration exceeded policy deadline");
  Write("qualification-policy.json",new SubmissionEvidencePolicy(1,[new("power",TimeSpan.Zero)]));
  settings=settings with{Review=settings.Review! with{Policy=Input("qualification-policy.json"),ObservationSources=[]}};
  var now=DateTimeOffset.UtcNow.AddMinutes(-1);
  Write("qualification-source/observations.json",new SubmissionEvidenceDocument(1,[new("power",ReviewedIdentity(),outcome,now,now,
   [new("qualifications/outage.txt",Hash("qualification-source/outage.txt"))],"Original limitation remains disclosed.")]));
  settings=settings with{Review=settings.Review! with{Qualifications=new(P("qualification-source"),[
   new("outage.txt",Hash("qualification-source/outage.txt")),new("observations.json",Hash("qualification-source/observations.json"))],"observations.json")}};
 }
 [TestCase(SubmissionEvidenceOutcome.Partial)]
 [TestCase(SubmissionEvidenceOutcome.Failed)]
 public void QualificationRetainsOutcomeAndStillRequiresExplicitReview(SubmissionEvidenceOutcome outcome) {
  ConfigureQualification(outcome);AutomationQualifications.Prepare(root,ReviewedIdentity(),settings.Review!,default);
  Directory.Delete(P("qualification-source"),true);
  AutomationReview.PrepareInputs(context,settings,settings.Review!,default);
  var report=AutomationFiles.Read<SubmissionEvidenceCompositionReport>(P("review-inputs/composition-report.json"));
  Assert.That(report.CompositionChecksPassed,Is.False);
  Assert.That(report.Observations.Observations.Single().Outcome,Is.EqualTo(outcome));
  var rules=AutomationFiles.Read<SubmissionEvidencePolicy>(settings.Review!.Policy.Path).Requirements;
  var unapproved=SubmissionReviewAssessment.Assess(ReviewedIdentity(),rules,report.Observations.Observations,root,
   SubmissionReviewMode.DeclaredGaps,[],DateTimeOffset.UtcNow);
  Assert.That(unapproved.ReadyForReview,Is.False);
 }
 [TestCase(SubmissionEvidenceOutcome.Passed)]
 [TestCase(SubmissionEvidenceOutcome.NotApplicable)]
 [TestCase(SubmissionEvidenceOutcome.ReviewedPriorPass)]
 public void QualificationCannotImportSatisfactoryOutcomes(SubmissionEvidenceOutcome outcome) {
  ConfigureQualification(outcome);
  Assert.Throws<InvalidDataException>(()=>AutomationReview.PrepareInputs(context,settings,settings.Review!,default));
 }
 [Test]public void QualificationRejectsChangedOriginalEvidence() {
  ConfigureQualification(SubmissionEvidenceOutcome.Partial);File.AppendAllText(P("qualification-source/outage.txt"),"changed");
  Assert.Throws<InvalidDataException>(()=>AutomationReview.PrepareInputs(context,settings,settings.Review!,default));
 }
 private void ConfigurePrior(SubmissionEvidenceOutcome originalOutcome=SubmissionEvidenceOutcome.Passed) {
  Directory.CreateDirectory(P("originals/source"));
  File.WriteAllText(P("originals/source/raw.txt"),"Synthetic original observation; not hardware evidence");
  var rule=new SubmissionRequirement("ui.navigation",TimeSpan.Zero);
  Write("originals/source/policy.json",new SubmissionEvidencePolicy(1,[rule,new("other",TimeSpan.Zero)]));
  var originalIdentity=new SubmissionEvidenceIdentity(new('f',64),new('b',40),Hash("originals/source/policy.json"),Hash("template.pdf"));
  var observed=DateTimeOffset.UtcNow.AddHours(-2);
  var files=new[]{new SubmissionEvidenceFile("raw.txt",Hash("originals/source/raw.txt"))};
  Write("originals/source/observations.json",new SubmissionEvidenceDocument(1,[
   new("ui.navigation",originalIdentity,originalOutcome,observed,observed,files),
   new("other",originalIdentity,SubmissionEvidenceOutcome.Failed,observed,observed,files,"Original unrelated failure") ]));
  Write("originals/analysis.json",new{Rationale="Synthetic reviewed code comparison"});
  Write("originals/change-review.json",new SubmissionChangeImpactReview(1,originalIdentity,settings.Release.PackageSha256,
   settings.Release.SourceCommit,DateTimeOffset.UtcNow.AddMinutes(-1),"Test reviewer",[
    new("ui.navigation","Synthetic unchanged navigation decision",[new("prior-evidence/analysis.json",Hash("originals/analysis.json"))]) ]));
  var prior=new SubmissionPriorEvidenceRequirements(originalIdentity,
   new("prior-evidence/source/policy.json",Hash("originals/source/policy.json")),
   new("prior-evidence/source/observations.json",Hash("originals/source/observations.json")),"prior-evidence/source",
   new("prior-evidence/change-review.json",Hash("originals/change-review.json")));
  Write("prior-policy.json",new SubmissionEvidencePolicy(1,[rule with{PriorEvidence=prior}]));
  var inventory=Directory.GetFiles(P("originals"),"*",SearchOption.AllDirectories)
   .Select(f=>new SubmissionEvidenceFile(Path.GetRelativePath(P("originals"),f).Replace('\\','/'),AutomationFiles.Hash(f))).ToArray();
  settings=settings with{Review=settings.Review! with{Policy=Input("prior-policy.json"),ObservationSources=[],PriorEvidence=new(P("originals"),inventory)}};
 }
 private SubmissionEvidenceIdentity ReviewedIdentity()=>new(settings.Release.PackageSha256,settings.Release.SourceCommit,
  settings.Review!.Policy.Sha256,settings.Review.Template.Sha256);
 [Test]public async Task PriorHandoffProducesPortableReviewWithoutRelabellingOriginalTests() {
  ConfigurePrior();byte[] original=File.ReadAllBytes(P("originals/source/observations.json"));
  // Candidate preparation retains inputs before a potentially long hardware run.
  AutomationPriorEvidence.Prepare(root,ReviewedIdentity(),settings.Review!,default);
  Directory.Delete(P("originals"),true);
  var result=await AutomationReview.Advance(context,settings,false,default,Prepare);
  Assert.That(result.Status,Is.EqualTo(SubmissionWorkflowStatus.Completed));
  var combined=AutomationFiles.Read<SubmissionEvidenceDocument>(P("review-inputs/observations.json"));
  Assert.Multiple(()=>{
   Assert.That(combined.Observations.Single().Outcome,Is.EqualTo(SubmissionEvidenceOutcome.ReviewedPriorPass));
   Assert.That(combined.Observations.Single().Execution,Is.Null);
   Assert.That(File.ReadAllBytes(P("prior-evidence/source/observations.json")),Is.EqualTo(original));
   Assert.That(AutomationFiles.Read<SubmissionEvidenceDocument>(P("prior-evidence/source/observations.json")).Observations[1].Outcome,
    Is.EqualTo(SubmissionEvidenceOutcome.Failed));
  });
  var recovered=await AutomationReview.Advance(context,settings,true,default,Prepare);
  Assert.That(recovered.Receipt,Is.EqualTo(result.Receipt));Assert.That(executions,Is.EqualTo(1));
 }
 [Test]public void ChangedOriginalCannotBeRepinnedDuringAutomaticHandoff() {
  ConfigurePrior();File.AppendAllText(P("originals/source/raw.txt"),"changed");
  Assert.Throws<InvalidDataException>(()=>AutomationReview.PrepareInputs(context,settings,settings.Review!,default));
 }
 [Test]public void DeletedRetainedEvidenceIsNotSilentlyRestoredFromSource() {
  ConfigurePrior();AutomationPriorEvidence.Prepare(root,ReviewedIdentity(),settings.Review!,default);
  File.Delete(P("prior-evidence/source/raw.txt"));
  Assert.Throws<InvalidDataException>(()=>AutomationReview.PrepareInputs(context,settings,settings.Review!,default));
  Assert.That(File.Exists(P("prior-evidence/source/raw.txt")),Is.False);
 }
 [Test]public void FailedOriginalCannotBecomeReviewedPassThroughController() {
  ConfigurePrior(SubmissionEvidenceOutcome.Failed);
  Assert.Throws<InvalidDataException>(()=>AutomationReview.PrepareInputs(context,settings,settings.Review!,default));
  Assert.That(File.Exists(P("prior-evidence-receipt.json")),Is.False);
 }
 [Test]public void PriorInventoryCannotWriteOutsideItsDedicatedFolder() {
  ConfigurePrior();settings=settings with{Review=settings.Review! with{PriorEvidence=new(P("originals"),[
   new("../candidate.pkg",Hash("candidate.pkg"))])}};
  Assert.Throws<InvalidDataException>(()=>AutomationReview.PrepareInputs(context,settings,settings.Review!,default));
  Assert.That(File.Exists(P("prior-evidence-receipt.json")),Is.False);
 }
 [Test]public void FreshFailureAndPriorPassRemainAConflictInsteadOfChoosingThePass() {
  ConfigurePrior();
  var now=DateTimeOffset.UtcNow.AddSeconds(-1);
  Write("nunit/conflict.json",new SubmissionEvidenceDocument(1,[new("ui.navigation",ReviewedIdentity(),SubmissionEvidenceOutcome.Failed,
   now,now,[new("nunit/trace.txt",Hash("nunit/trace.txt"))],"Synthetic new failure must remain visible")]));
  File.WriteAllBytes(P("windows-tests.json"),JsonSerializer.SerializeToUtf8Bytes(new{Files=new[]{
   new SubmissionWorkflowReceipt("nunit/conflict.json",Hash("nunit/conflict.json")),new("nunit/trace.txt",Hash("nunit/trace.txt"))}},AutomationFiles.Json));
  settings=settings with{Review=settings.Review! with{ObservationSources=["nunit/conflict.json"]}};
  AutomationReview.PrepareInputs(context,settings,settings.Review!,default);
  using var report=JsonDocument.Parse(File.ReadAllBytes(P("review-inputs/composition-report.json")));
  Assert.That(report.RootElement.GetProperty("compositionChecksPassed").GetBoolean(),Is.False);
  var combined=AutomationFiles.Read<SubmissionEvidenceDocument>(P("review-inputs/observations.json"));
  Assert.That(combined.Observations.Select(o=>o.Outcome),Is.EquivalentTo(new[]{SubmissionEvidenceOutcome.Failed,SubmissionEvidenceOutcome.ReviewedPriorPass}));
 }
}
