// Copyright (c) 2026 Neil Colvin. MIT licensed.
using CrestronHomeDevTools.Automation;
using CrestronHomeNUnit.Workflow;
using NUnit.Framework;
namespace CrestronHomeDevTools.Tests;

[TestFixture]
public sealed class AutomationProtectedWorkerTests
{
 private string root=null!,path=null!,runs=null!;
 private SubmissionAutomationProtectedWorker installed=null!;
 private SubmissionAutomationSettings supplied=null!;
 [SetUp]public void Setup() {
  root=Path.Combine(TestContext.CurrentContext.WorkDirectory,"protected-worker-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
  runs=Path.Combine(root,"runs");Directory.CreateDirectory(runs);path=Path.Combine(root,"installed.json");
  var tools=new SubmissionAutomationConsole(Path.Combine(root,"trusted-tools"),[]);
  var plan=new SubmissionAutomationProtectedPlan(Path.Combine(root,"secret-bindings.json"),
   new(Path.Combine(root,"approvals","${runKey}","sign.json"),Path.Combine(root,"approvals","${runKey}","sign.sha256")),
   new(Path.Combine(root,"approvals","${runKey}","send.json"),Path.Combine(root,"approvals","${runKey}","send.sha256")));
  installed=new(1,[runs],["fixture/driver"],tools,plan);AutomationFiles.Write(path,installed);
  var sourceInput=new SubmissionAutomationInput("unused",new('d',64));
  var review=new SubmissionAutomationReviewPlan(sourceInput,sourceInput,sourceInput,sourceInput,new(Path.Combine(runs,"attacker-tools"),[]),"fixture","fixture",[]);
  supplied=new(1,runs,new("fixture/driver",3,"v1.0.0",new('a',40),new('b',64),new('c',64),new('d',64)),runs,
   new(Guid.NewGuid().ToString(),"1.0.0.0",PortalSubmissionKind.NewDriver,"Fixture"),"unused",
   new WorkflowPlan{Host="unused",CertificateSha256=new('e',64),SshFingerprint="unused",SourceRoots=[runs],LocalTests=[],TestPackage=new("unused.csproj","unused.pkg","fixture",1),ProcessorSuites=[]},
   Mode:SubmissionAutomationMode.Submit,Review:review,Protected:plan with{CredentialBindings=Path.Combine(runs,"attacker-bindings.json")});
 }
 [TearDown]public void Cleanup()=>Directory.Delete(root,true);
 [Test]public void RunCannotSelectProtectedExecutablesCredentialsOrApprovalLocations() {
  var trusted=AutomationProtectedWorker.Load(path,AutomationFiles.Hash(path));var bound=trusted.Bind(supplied);
  Assert.That(bound.Review!.Console.Directory,Is.EqualTo(installed.Console.Directory));Assert.That(bound.Review.Console.Files,Is.EqualTo(installed.Console.Files));
  Assert.That(bound.Protected!.CredentialBindings,Is.EqualTo(installed.Plan.CredentialBindings));
  Assert.That(bound.Protected.SigningApproval.DocumentPath,Is.EqualTo(installed.Plan.SigningApproval.DocumentPath.Replace("${runKey}",SubmissionWorkflow.RunKey(supplied.Release))));
  Assert.That(bound.Release,Is.EqualTo(supplied.Release));
 }
 [Test]public void ChangedInstalledConfigurationFailsItsIndependentPin() {
  string pin=AutomationFiles.Hash(path);File.AppendAllText(path," ");
  Assert.Throws<InvalidDataException>(()=>AutomationProtectedWorker.Load(path,pin));
 }
 [Test]public void BuildWritableProtectedToolsAreRejectedEvenIfConfigurationHashMatches() {
  File.Delete(path);AutomationFiles.Write(path,installed with{Console=installed.Console with{Directory=Path.Combine(runs,"tools")}});
  Assert.Throws<InvalidDataException>(()=>AutomationProtectedWorker.Load(path,AutomationFiles.Hash(path)));
 }
 [Test]public void OtherRepositoryOrRootCannotUseThisProtectedWorker() {
  var trusted=AutomationProtectedWorker.Load(path,AutomationFiles.Hash(path));
  Assert.Throws<InvalidDataException>(()=>trusted.Bind(supplied with{PrivateRoot=root}));
  Assert.Throws<InvalidDataException>(()=>trusted.Bind(supplied with{Release=supplied.Release with{Repository="other/driver"}}));
 }
 [Test]public void PublicProtectedAdapterRequiresTheIndependentConfiguration() {
  var adapter=new SubmissionAutomationStages(supplied,new('a',64),SubmissionAutomationWorkerRole.Protected);
  var checkpoint=SubmissionWorkflow.Open(runs,supplied.Release) with{Stage=SubmissionWorkflowStage.SignReview};
  var context=new SubmissionWorkflowStepContext(Path.Combine(runs,SubmissionWorkflow.RunKey(supplied.Release)),checkpoint);
  Assert.ThrowsAsync<InvalidDataException>(async()=>await adapter.ExecuteAsync(context,default));
  Assert.That(Directory.GetFiles(context.RunDirectory,"*",SearchOption.AllDirectories).Any(f=>f.Contains("signing-request")),Is.False);
 }
}
