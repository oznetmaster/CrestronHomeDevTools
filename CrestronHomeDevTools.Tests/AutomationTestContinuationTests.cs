// Copyright (c) 2026 Neil Colvin. MIT licensed.
using CrestronHomeDevTools.Automation;
using NUnit.Framework;
namespace CrestronHomeDevTools.Tests;
public sealed partial class AutomationReviewTests
{
 private SubmissionEvidenceCompositionReport ContinuationReport(params SubmissionObservation[] observations)=>new(new('a',64),new(1,observations),new([]));
 private SubmissionObservation ContinuationObservation(string id,SubmissionEvidenceOutcome outcome=SubmissionEvidenceOutcome.Passed)=>new(id,ReviewedIdentity(),outcome,DateTimeOffset.UtcNow.AddMinutes(-2),DateTimeOffset.UtcNow.AddMinutes(-1),[]);
 private AutomationTestContinuation.Inspection Continuation(SubmissionEvidencePolicy policy,SubmissionEvidenceCompositionReport report,params string[] changed)=>AutomationTestContinuation.Analyze(settings,policy,report,changed);
 [Test] public void ContinuationPassingEvidenceIsOnlyACandidateForScopedReview() {
  var result=Continuation(new(1,[new("ui.navigation",TimeSpan.Zero)]),ContinuationReport(ContinuationObservation("ui.navigation")));
  Assert.That(result.Requirements.Single().Disposition,Is.EqualTo(AutomationTestContinuation.Disposition.PriorPassReviewRequired));
  Assert.That(result.PlanningOnly,Is.True);Assert.That(result.ExecutionAuthorized,Is.False);Assert.That(result.PhaseThreeAuthorized,Is.False);Assert.That(result.ProducerAuthenticationRequired,Is.True);
 }
 [TestCase(SubmissionEvidenceOutcome.Failed)][TestCase(SubmissionEvidenceOutcome.Partial)][TestCase(SubmissionEvidenceOutcome.NotTested)][TestCase(SubmissionEvidenceOutcome.Inconclusive)]
 public void ContinuationNeverCarriesForwardANonpassingResult(SubmissionEvidenceOutcome outcome) {
  var result=Continuation(new(1,[new("ui.navigation",TimeSpan.Zero)]),ContinuationReport(ContinuationObservation("ui.navigation",outcome)));
  Assert.That(result.Requirements.Single().Disposition,Is.EqualTo(AutomationTestContinuation.Disposition.NewExecutionRequired));
  Assert.That(result.Requirements.Single().OriginalOutcome,Is.EqualTo(outcome));
 }
 [TestCase(SubmissionEvidenceOutcome.ReviewedPriorPass)][TestCase(SubmissionEvidenceOutcome.NotApplicable)]
 public void ContinuationRequiresOriginalSourceOrApplicabilityReviewInsteadOfBlindReuse(SubmissionEvidenceOutcome outcome) {
  var result=Continuation(new(1,[new("ui.navigation",TimeSpan.Zero)]),ContinuationReport(ContinuationObservation("ui.navigation",outcome)));
  Assert.That(result.Requirements.Single().Disposition,Is.EqualTo(AutomationTestContinuation.Disposition.OriginalSourceReviewRequired));
 }
 [Test] public void ContinuationMissingAndChangedRequirementsNeedFreshExecution() {
  var result=Continuation(new(1,[new("ui.navigation",TimeSpan.Zero),new("missing",TimeSpan.Zero)]),ContinuationReport(ContinuationObservation("ui.navigation")),"ui.navigation");
  Assert.That(result.Requirements.All(r=>r.Disposition==AutomationTestContinuation.Disposition.NewExecutionRequired),Is.True);
 }
 [TestCase("response")][TestCase("interval")]
 public void ContinuationCouplesResponseMeasurementsAndIntervalWithoutRepeatingOutage(string changed) {
  var interval=new SubmissionRequirement("interval",TimeSpan.FromHours(1));
  settings=settings with{Endurance=new(new(ReviewedIdentity(),interval,"synthetic","synthetic","synthetic","synthetic",TimeSpan.FromMinutes(5),TimeSpan.FromSeconds(5)),new("synthetic","synthetic"),new(root,"unused",[])),
   ResponseComparison=new("response",[])};
  var policy=new SubmissionEvidencePolicy(1,[interval,new("response",TimeSpan.Zero),new("outage",TimeSpan.Zero)]);
  var result=Continuation(policy,ContinuationReport(ContinuationObservation("interval"),ContinuationObservation("response"),ContinuationObservation("outage")),changed);
  Assert.That(result.Requirements.Where(r=>r.Id!="outage").All(r=>r.Disposition==AutomationTestContinuation.Disposition.NewExecutionRequired),Is.True);
  Assert.That(result.Requirements.Single(r=>r.Id=="outage").Disposition,Is.EqualTo(AutomationTestContinuation.Disposition.PriorPassReviewRequired));
 }
 [Test] public void ContinuationCannotReuseOldCleanupForUnreviewedNewInstallation() {
  settings=settings with{Removal=new(null!,"removal","placement")};
  var result=Continuation(new(1,[new("test",TimeSpan.Zero),new("removal",TimeSpan.Zero),new("placement",TimeSpan.Zero)]),ContinuationReport(ContinuationObservation("test",SubmissionEvidenceOutcome.Failed),ContinuationObservation("removal"),ContinuationObservation("placement")));
  Assert.That(result.Requirements.Where(r=>r.Id!="test").All(r=>r.Disposition==AutomationTestContinuation.Disposition.InstallationReviewRequired),Is.True);
 }
 [TestCase("unavailable-evidence")][TestCase("evidence-digest")]
 public void ContinuationRejectsCorruptedOriginalsInsteadOfPickingGoodParts(string issue) {
  var report=ContinuationReport(ContinuationObservation("ui.navigation")) with{Evidence=new([new("ui.navigation",issue,"Synthetic corruption")])};
  Assert.Throws<InvalidDataException>(()=>Continuation(new(1,[new("ui.navigation",TimeSpan.Zero)]),report));
 }
 [Test] public void ContinuationRejectsUnknownOrDuplicateChangedScopesAndConflictingOriginals() {
  var policy=new SubmissionEvidencePolicy(1,[new("ui.navigation",TimeSpan.Zero)]);var report=ContinuationReport(ContinuationObservation("ui.navigation"));
  Assert.Throws<InvalidDataException>(()=>Continuation(policy,report,"other"));
  Assert.Throws<InvalidDataException>(()=>Continuation(policy,report,"ui.navigation","ui.navigation"));
  Assert.Throws<InvalidDataException>(()=>Continuation(policy,ContinuationReport(ContinuationObservation("ui.navigation"),ContinuationObservation("ui.navigation",SubmissionEvidenceOutcome.Failed))));
  Assert.Throws<InvalidDataException>(()=>Continuation(policy,ContinuationReport(ContinuationObservation("ui.navigation") with{Identity=ReviewedIdentity() with{PackageSha256=new('a',64)}})));
 }
 [Test] public void ContinuationCannotReusePassedLabelWithInsufficientDuration() {
  var report=ContinuationReport(ContinuationObservation("ui.navigation")) with{Evidence=new([new("ui.navigation","insufficient-duration","Too short")])};
  Assert.That(Continuation(new(1,[new("ui.navigation",TimeSpan.Zero)]),report).Requirements.Single().Disposition,Is.EqualTo(AutomationTestContinuation.Disposition.NewExecutionRequired));
 }
 [TestCase(SubmissionAutomationMode.Rehearsal,60,false,true)]
 [TestCase(SubmissionAutomationMode.Rehearsal,30,false,false)]
 [TestCase(SubmissionAutomationMode.Submit,60,false,false)]
 [TestCase(SubmissionAutomationMode.Rehearsal,60,true,false)]
 public void ContinuationRespectsExplicitShortRehearsalWithoutTreatingItAsProductionPass(SubmissionAutomationMode mode,int minutes,bool changed,bool qualified) {
  var interval=new SubmissionRequirement("interval",TimeSpan.FromHours(1));
  settings=settings with{Mode=mode,Endurance=new(new(ReviewedIdentity(),interval,"synthetic","synthetic","synthetic","synthetic",TimeSpan.FromMinutes(5),TimeSpan.FromSeconds(5)),new("synthetic","synthetic"),new(root,"unused",[])),
   Review=settings.Review! with{PlannedGaps=[new("interval","Deliberate one-hour rehearsal, not the production duration.")]}};
  var start=DateTimeOffset.UtcNow.AddHours(-2);
  var observation=ContinuationObservation("interval") with{StartedUtc=start,FinishedUtc=start.AddMinutes(minutes)};
  var report=ContinuationReport(observation) with{Evidence=new([new("interval","insufficient-duration","Production requires 24h")])};
  var result=Continuation(new(1,[interval with{MinimumDuration=TimeSpan.FromHours(24)}]),report,changed?["interval"]:[]);
  Assert.That(result.Requirements.Single().Disposition,Is.EqualTo(qualified?AutomationTestContinuation.Disposition.QualifiedEvidenceReviewRequired:AutomationTestContinuation.Disposition.NewExecutionRequired));
  Assert.That(result.PhaseThreeAuthorized,Is.False);
 }
 [Test] public void ContinuationInspectionDoesNotWriteAssessmentCheckpointOrReview() {
  var state=SubmissionWorkflow.Open(root,settings.Release);string run=Path.Combine(root,SubmissionWorkflow.RunKey(settings.Release));
  Directory.CreateDirectory(Path.Combine(run,"nunit"));
  foreach(string file in new[]{"policy.json","nunit/observations.json","nunit/trace.txt"})File.Copy(P(file),Path.Combine(run,file));
  AutomationReview.WriteDocument(Path.Combine(run,"composition.json"),new SubmissionEvidenceCompositionPlan(1,ReviewedIdentity(),[new("nunit/observations.json",Hash("nunit/observations.json"))]));
  string statePin=AutomationFiles.Hash(Path.Combine(run,"state.json")),compositionPin=AutomationFiles.Hash(Path.Combine(run,"composition.json"));
  var before=Directory.GetFiles(root,"*",SearchOption.AllDirectories).ToDictionary(p=>p,p=>File.ReadAllBytes(p));
  _=AutomationTestContinuation.Inspect(new(settings,new('f',64)),statePin,"composition.json",compositionPin,[],default);
  Assert.That(Directory.GetFiles(root,"*",SearchOption.AllDirectories).Order(),Is.EqualTo(before.Keys.Order()));
  foreach(var file in before)Assert.That(File.ReadAllBytes(file.Key),Is.EqualTo(file.Value));
  Assert.Throws<InvalidDataException>(()=>AutomationTestContinuation.Inspect(new(settings,new('f',64)),new('0',64),"composition.json",compositionPin,[],default));
  Assert.Throws<InvalidDataException>(()=>AutomationTestContinuation.Inspect(new(settings,new('f',64)),statePin,"composition.json",new('0',64),[],default));
 }
}
