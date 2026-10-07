// Copyright (c) 2026 Neil Colvin. MIT licensed.
using CrestronHomeDevTools.Automation;
using NUnit.Framework;
namespace CrestronHomeDevTools.Tests;
public sealed partial class AutomationReviewTests
{
 [Test]public void PhaseThreeCannotCreateItsOwnMissingTestAssessment() {
  var before=Directory.GetFiles(root,"*",SearchOption.AllDirectories).Order().ToArray();
  Assert.Catch<IOException>(()=>AutomationReview.PrepareInputs(context,settings,settings.Review!,default));
  Assert.That(Directory.GetFiles(root,"*",SearchOption.AllDirectories).Order().ToArray(),Is.EqualTo(before));
 }
 [TestCase(SubmissionEvidenceOutcome.Failed)][TestCase(SubmissionEvidenceOutcome.NotTested)][TestCase(SubmissionEvidenceOutcome.Inconclusive)]
 public void DeclaredNonpassingResultStillBlocksTheWholeTestSuite(SubmissionEvidenceOutcome outcome) {
  var original=AutomationFiles.Read<SubmissionEvidenceDocument>(P("nunit/observations.json"));
  File.Delete(P("nunit/observations.json"));Write("nunit/observations.json",original with{Observations=[original.Observations.Single() with{Outcome=outcome}]});
  File.Delete(P("windows-tests.json"));AutomationFiles.Write(P("windows-tests.json"),new{Files=new[]{new SubmissionWorkflowReceipt("nunit/observations.json",Hash("nunit/observations.json")),new("nunit/trace.txt",Hash("nunit/trace.txt"))}});
  settings=settings with{Review=settings.Review! with{PlannedGaps=[new("ui.navigation","Synthetic disclosure is not a pass.")]}};
  Assert.That(AutomationTestAssessment.Prepare(context,settings,default),Is.False);
  var failed=File.ReadAllBytes(P(AutomationTestAssessment.ReceiptName));
  Assert.Throws<InvalidDataException>(()=>AutomationReview.PrepareInputs(context,settings,settings.Review!,default));
  Assert.That(File.ReadAllBytes(P(AutomationTestAssessment.ReceiptName)),Is.EqualTo(failed));Assert.That(Directory.Exists(P("review-inputs")),Is.False);
 }
 [Test]public void DocumentPreparationConsumesIdenticalAssessedObservationsWithoutRecollecting() {
  Assert.That(AutomationTestAssessment.Prepare(context,settings,default),Is.True);
  var original=File.ReadAllBytes(P("test-assessment/observations.json"));
  // An unavailable producer index cannot trigger fresh collection by phase three.
  File.Move(P("windows-tests.json"),P("original-windows-tests.json"));
  AutomationReview.PrepareInputs(context,settings,settings.Review!,default);
  Assert.That(File.ReadAllBytes(P("review-inputs/observations.json")),Is.EqualTo(original));
 }
 [TestCase("test-assessment/observations.json")][TestCase("test-assessment/composition-report.json")][TestCase("nunit/trace.txt")]
 public void ChangedAssessmentOrUnderlyingEvidenceBlocksDocuments(string path) {
  Assert.That(AutomationTestAssessment.Prepare(context,settings,default),Is.True);File.AppendAllText(P(path),"changed");
  Assert.Throws<InvalidDataException>(()=>AutomationReview.PrepareInputs(context,settings,settings.Review!,default));
  Assert.That(Directory.Exists(P("review-inputs")),Is.False);
 }
 [Test]public void MissingFullPolicyRequirementFailsBeforeAnyDocuments() {
  var policy=new SubmissionEvidencePolicy(1,[new("ui.navigation",TimeSpan.Zero),new("required.outage",TimeSpan.Zero)]);
  Write("full-policy.json",policy);settings=settings with{Review=settings.Review! with{Policy=Input("full-policy.json")}};
  var old=AutomationFiles.Read<SubmissionEvidenceDocument>(P("nunit/observations.json"));
  File.Delete(P("nunit/observations.json"));Write("nunit/observations.json",old with{Observations=[old.Observations.Single() with{Identity=ReviewedIdentity()}]});
  File.Delete(P("windows-tests.json"));AutomationFiles.Write(P("windows-tests.json"),new{Files=new[]{new SubmissionWorkflowReceipt("nunit/observations.json",Hash("nunit/observations.json")),new("nunit/trace.txt",Hash("nunit/trace.txt"))}});
  Assert.That(AutomationTestAssessment.Prepare(context,settings,default),Is.False);
  var report=AutomationFiles.Read<SubmissionEvidenceCompositionReport>(P("test-assessment/composition-report.json"));
  Assert.That(report.Evidence.Issues.Any(i=>i.RequirementId=="required.outage" && i.Code=="missing-observation"),Is.True);
  Assert.That(Directory.Exists(P("review-inputs")),Is.False);
 }

 [TestCase(SubmissionAutomationMode.Rehearsal,60,true,true)]
 [TestCase(SubmissionAutomationMode.Submit,60,true,false)]
 [TestCase(SubmissionAutomationMode.Rehearsal,30,true,false)]
 [TestCase(SubmissionAutomationMode.Rehearsal,60,false,false)]
 public void ShortIntervalMustMeetTheExplicitRehearsalDurationAndCannotPassProduction(SubmissionAutomationMode mode,int minutes,bool declared,bool eligible) {
  var identity=ReviewedIdentity();var requirement=new SubmissionRequirement("endurance",TimeSpan.FromHours(1),Execution:new("candidate","endurance",SubmissionEvidenceOutcome.Passed,null,false,600));
  var plan=new SubmissionEndurancePlan(identity,requirement,"fixture","candidate",Guid.NewGuid().ToString("N"),"fixture",TimeSpan.FromMinutes(5),TimeSpan.FromSeconds(30));
  settings=settings with{Mode=mode,Endurance=new(plan,new("fixture","fixture"),null!),Review=settings.Review! with{PlannedGaps=declared?[new("endurance","One-hour rehearsal, not 24-hour production evidence.")]:null}};
  var start=DateTimeOffset.UtcNow.AddHours(-2);
  var report=new SubmissionEvidenceCompositionReport(new('a',64),new(1,[new("endurance",identity,SubmissionEvidenceOutcome.Passed,start,start.AddMinutes(minutes),[])]),
   new([new("endurance","insufficient-duration","Production requires 24 hours.")]));
  Assert.That(AutomationTestAssessment.Eligible(settings,report),Is.EqualTo(eligible));
 }
}
