// Copyright (c) 2026 Neil Colvin. MIT licensed.
using System.Text.Json;
using CrestronHomeDevTools.Automation;
using CrestronHomeNUnit.Workflow;
using NUnit.Framework;
namespace CrestronHomeDevTools.Tests;
[TestFixture]
public sealed class AutomationMaintenanceLockTests
{
 private string root=null!,run=null!;private AutomationRequest request=null!;
 private readonly SubmissionWorkflowRelease release=new("fixture/driver",1,"v1",new('a',40),new('b',64),new('c',64),new('d',64));
 [SetUp]public void Setup() {
  root=Path.Combine(TestContext.CurrentContext.WorkDirectory,"maintenance-lock-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
  SubmissionWorkflow.Open(root,release);run=Path.Combine(root,SubmissionWorkflow.RunKey(release));
  var settings=new SubmissionAutomationSettings(1,root,release,root,new(Guid.NewGuid().ToString(),"1.0.0.0",PortalSubmissionKind.NewDriver,"Fixture"),"unused",new WorkflowPlan{Host="unused",CertificateSha256=new('a',64),SshFingerprint="unused",SourceRoots=[root],LocalTests=[],TestPackage=new("unused","unused","fixture",1),ProcessorSuites=[]});
  request=new(settings,new('e',64));
 }
 [TearDown]public void Cleanup()=>Directory.Delete(root,true);
 [Test]public void VerifiedMaintenanceOwnsGateAndReleasesItAfterCallback() {
  var original=File.ReadAllBytes(Path.Combine(run,"state.json"));
  int result=SubmissionWorkflow.WithVerifiedCheckpoint(root,release,c=>{
   Assert.That(c.RunDirectory,Is.EqualTo(run));
   Assert.Throws<IOException>(()=>SubmissionWorkflow.Read(root,release));return 7;
  });
  Assert.That(result,Is.EqualTo(7));Assert.DoesNotThrow(()=>SubmissionWorkflow.Read(root,release));
  Assert.That(File.ReadAllBytes(Path.Combine(run,"state.json")),Is.EqualTo(original));
 }
 [Test]public void CompetingOwnerPreventsMaintenanceCallback() {
  using var held=new FileStream(Path.Combine(run,"run.lock"),FileMode.Open,FileAccess.ReadWrite,FileShare.None);bool entered=false;
  Assert.Throws<IOException>(()=>SubmissionWorkflow.WithVerifiedCheckpoint(root,release,c=>{entered=true;return 0;}));Assert.That(entered,Is.False);
 }
 [Test]public void FailedMaintenanceReleasesGateWithoutChangingCheckpoint() {
  var original=File.ReadAllBytes(Path.Combine(run,"state.json"));
  Assert.Throws<InvalidDataException>(()=>SubmissionWorkflow.WithVerifiedCheckpoint<int>(root,release,c=>throw new InvalidDataException("fixture")));
  Assert.DoesNotThrow(()=>SubmissionWorkflow.Read(root,release));Assert.That(File.ReadAllBytes(Path.Combine(run,"state.json")),Is.EqualTo(original));
 }
 [Test]public void RemovalCommandReachesStateGuardWithoutReacquiringGate() {
  string path=Path.Combine(root,"request.json");AutomationFiles.Write(path,new AutomationRemovalReconciliation.Request(1,AutomationFiles.Hash(Path.Combine(run,"state.json")),new('a',64),root,new('b',64),"fixture",path,new('c',64)));
  var error=Assert.Throws<InvalidDataException>(()=>AutomationRemovalReconciliation.Command(request,path,AutomationFiles.Hash(path)));
  Assert.That(error!.Message,Does.Contain("stopped final tests"));Assert.DoesNotThrow(()=>SubmissionWorkflow.Read(root,release));
  Assert.That(Directory.Exists(Path.Combine(run,"removal")),Is.False);
 }
 [Test]public void DocumentBindingCommandReachesPinnedStateGuardWithoutReacquiringGate() {
  string path=Path.Combine(root,"tools.json");File.WriteAllText(path,"{}");
  var error=Assert.Throws<InvalidDataException>(()=>AutomationReviewTooling.Command(request,new('f',64),path,AutomationFiles.Hash(path)));
  Assert.That(error!.Message,Does.Contain("Inspected workflow state changed"));Assert.DoesNotThrow(()=>SubmissionWorkflow.Read(root,release));
  Assert.That(File.Exists(Path.Combine(run,AutomationReviewTooling.FileName)),Is.False);
 }
 [Test]public void ChangedReceiptPreventsMaintenanceCallback() {
  var state=SubmissionWorkflow.Read(root,release);File.WriteAllText(Path.Combine(run,"receipt.json"),"original");
  state.CompletedStages.Add(SubmissionWorkflowStage.ValidateCandidate,new("receipt.json",AutomationFiles.Hash(Path.Combine(run,"receipt.json"))));
  File.WriteAllText(Path.Combine(run,"state.json"),JsonSerializer.Serialize(state,new JsonSerializerOptions{PropertyNamingPolicy=JsonNamingPolicy.CamelCase,Converters={new System.Text.Json.Serialization.JsonStringEnumConverter()}}));
  File.AppendAllText(Path.Combine(run,"receipt.json"),"changed");bool entered=false;
  Assert.Throws<InvalidDataException>(()=>SubmissionWorkflow.WithVerifiedCheckpoint(root,release,c=>{entered=true;return 0;}));Assert.That(entered,Is.False);
 }
}
