// Copyright (c) 2026 Neil Colvin. MIT licensed.
using System.Text.Json;
using CrestronHomeDevTools.Automation;
using CrestronHomeNUnit.Workflow;
using NUnit.Framework;
namespace CrestronHomeDevTools.Tests;

[TestFixture]
public sealed class AutomationRetainedEnduranceTests
{
 private string root=null!;
 private SubmissionAutomationSettings settings=null!;
 private SubmissionWorkflowStepContext context=null!;
 private string P(string name)=>Path.Combine(root,name);
 private sealed class Clock:TimeProvider {
  internal DateTimeOffset Now=DateTimeOffset.UtcNow.AddMinutes(-5);
  public override DateTimeOffset GetUtcNow()=>Now;
 }
 [SetUp]public async Task Setup() {
  root=Path.Combine(TestContext.CurrentContext.WorkDirectory,"retained-endurance-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
  var release=new SubmissionWorkflowRelease("fixture/driver",1,"v1",new('a',40),new('b',64),new('c',64),new('d',64));
  var identity=new SubmissionEvidenceIdentity(release.PackageSha256,release.SourceCommit,new('e',64),new('f',64));
  var plan=new SubmissionEndurancePlan(identity,new("endurance",TimeSpan.FromMinutes(1),Execution:new("gateway","endurance",SubmissionEvidenceOutcome.Passed,null,false,40)),
   "processor","installation","reservation","producer",TimeSpan.FromSeconds(30),TimeSpan.FromSeconds(5));
  var clock=new Clock();
  for(int i=0;i<3;i++) {
   await SubmissionEndurance.CollectAsync(P("endurance/observations"),plan,_=>Task.FromResult(new SubmissionEnduranceProbeResult(identity,plan.ProcessorIdentity,
    plan.InstallationIdentity,plan.ReservationId,plan.ProducerId,"boot",SubmissionEvidenceOutcome.Passed,"Synthetic evidence; no hardware."u8.ToArray())),clock);
   clock.Now=clock.Now.AddSeconds(30);
  }
  var observation=SubmissionEndurance.Export(P("endurance/observations"),plan,DateTimeOffset.UtcNow);
  AutomationFiles.Write(P("endurance-evidence.json"),new{EvidenceDirectory="endurance/observations",Observation=observation});
  settings=new(1,root,release,root,new(Guid.NewGuid().ToString(),"1.0.0.0",PortalSubmissionKind.NewDriver,"Fixture"),"unused",
   new WorkflowPlan{Host="unused",CertificateSha256=new('e',64),SshFingerprint="unused",SourceRoots=[root],LocalTests=[],TestPackage=new("unused","unused","Fixture",1),ProcessorSuites=[]},
   Endurance:new(plan,new("unused","unused"),new("unused","unused",[])));
  context=new(root,new(2,new('a',64),release,SubmissionWorkflowStage.PrepareReview,SubmissionWorkflowStatus.Running,"operation",null,
   new(){[SubmissionWorkflowStage.Endurance]=new("endurance-evidence.json",AutomationFiles.Hash(P("endurance-evidence.json")))},DateTimeOffset.UtcNow));
  AutomationFiles.Write(P("automation-binding.json"),new{SettingsSha256=new string('b',64),context.Checkpoint.InputSha256});
 }
 [TearDown]public void Cleanup()=>Directory.Delete(root,true);
 private SubmissionAutomationStages Adapter()=>new(settings,new('b',64),(_,_,_,_)=>throw new AssertionException("Must not execute tests"),_=>throw new AssertionException("Must not resolve credentials"));
 private string[] Snapshot()=>Directory.GetFiles(root,"*",SearchOption.AllDirectories).Order(StringComparer.Ordinal)
  .Select(p=>Path.GetRelativePath(root,p)+":"+AutomationFiles.Hash(p)+":"+File.GetLastWriteTimeUtc(p).Ticks).ToArray();
 [TestCase(SubmissionWorkflowStage.FinalizeTests)][TestCase(SubmissionWorkflowStage.PrepareReview)]
 [TestCase(SubmissionWorkflowStage.SignReview)][TestCase(SubmissionWorkflowStage.Deliver)][TestCase(SubmissionWorkflowStage.Retain)]
 public void LaterStageVerificationDoesNotRewriteEvidenceOrContactHardware(SubmissionWorkflowStage stage) {
  context=context with{Checkpoint=context.Checkpoint with{Stage=stage}};var before=Snapshot();
  Adapter().VerifyCompletedEvidence(context);Adapter().VerifyCompletedEvidence(context);
  Assert.That(Snapshot(),Is.EqualTo(before));
 }
 [TestCase("endurance-evidence.json")][TestCase("endurance/observations/checkpoint.json")]
 [TestCase("endurance/observations/collector.lock")][TestCase("endurance/observations/sample-000001.json")]
 public void MissingCompletedEvidenceIsRejectedWithoutRecreatingIt(string file) {
  File.Delete(P(file));var before=Snapshot();Assert.Catch(()=>Adapter().VerifyCompletedEvidence(context));
  Assert.That(File.Exists(P(file)),Is.False);Assert.That(Snapshot(),Is.EqualTo(before));
 }
 [TestCase("endurance-evidence.json")][TestCase("endurance/observations/sample-000001.json")]
 public void ChangedEvidenceIsRejectedAndNeverRepaired(string file) {
  File.AppendAllText(P(file),"changed");var before=Snapshot();Assert.Catch(()=>Adapter().VerifyCompletedEvidence(context));
  Assert.That(Snapshot(),Is.EqualTo(before));
 }
 [Test]public void MissingWholeCollectionIsNotRecreated() {
  Directory.Delete(P("endurance"),true);var before=Snapshot();Assert.Catch(()=>Adapter().VerifyCompletedEvidence(context));
  Assert.That(Directory.Exists(P("endurance")),Is.False);Assert.That(Snapshot(),Is.EqualTo(before));
 }
 [Test]public void WrongCompletedReceiptCannotAuthorizeAnotherExport() {
  context.Checkpoint.CompletedStages[SubmissionWorkflowStage.Endurance]=new("different.json",new('b',64));var before=Snapshot();
  Assert.Throws<InvalidDataException>(()=>Adapter().VerifyCompletedEvidence(context));Assert.That(Snapshot(),Is.EqualTo(before));
 }
 [Test]public void DifferentFrozenPlanCannotReuseCompletedEvidence() {
  settings=settings with{Endurance=settings.Endurance! with{Plan=settings.Endurance!.Plan with{ProducerId="other"}}};var before=Snapshot();
  Assert.Throws<InvalidDataException>(()=>Adapter().VerifyCompletedEvidence(context));Assert.That(Snapshot(),Is.EqualTo(before));
 }
 [Test]public void ReadOnlyCollectorLockRemainsReadableWithoutWriteAccess() {
  string path=P("endurance/observations/collector.lock");File.SetAttributes(path,FileAttributes.ReadOnly);
  try {var before=Snapshot();Adapter().VerifyCompletedEvidence(context);Assert.That(Snapshot(),Is.EqualTo(before));}
  finally {File.SetAttributes(path,FileAttributes.Normal);}
 }
}
