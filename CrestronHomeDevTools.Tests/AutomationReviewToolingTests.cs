// Copyright (c) 2026 Neil Colvin. MIT licensed.
using System.Text.Json;
using CrestronHomeDevTools.Automation;
using CrestronHomeNUnit.Workflow;
using NUnit.Framework;
namespace CrestronHomeDevTools.Tests;
[TestFixture]
public sealed class AutomationReviewToolingTests
{
 private string root=null!;private SubmissionWorkflowStepContext context=null!;private SubmissionAutomationSettings settings=null!;
 private AutomationReviewTooling.Request request=null!;private int verified;
 private const string Pin="aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
 [SetUp]public async Task Setup() {
  root=Path.Combine(TestContext.CurrentContext.WorkDirectory,"document-tooling-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);verified=0;
  var release=new SubmissionWorkflowRelease("fixture/driver",1,"v1",new('a',40),new('b',64),new('c',64),new('d',64));
  settings=new(1,root,release,root,new(Guid.NewGuid().ToString(),"1.0.0.0",PortalSubmissionKind.NewDriver,"Fixture"),"unused",new WorkflowPlan{Host="unused",CertificateSha256=Pin,SshFingerprint="unused",SourceRoots=[root],LocalTests=[],TestPackage=new("unused","unused","fixture",1),ProcessorSuites=[]});
  var receipts=new Dictionary<SubmissionWorkflowStage,SubmissionWorkflowReceipt>();
  foreach(var stage in new[]{SubmissionWorkflowStage.ValidateCandidate,SubmissionWorkflowStage.WindowsTests,SubmissionWorkflowStage.ProcessorTests,SubmissionWorkflowStage.AppTests,SubmissionWorkflowStage.Endurance}) {
   string name=stage+".json";File.WriteAllText(Path.Combine(root,name),"synthetic "+stage);receipts.Add(stage,new(name,AutomationFiles.Hash(Path.Combine(root,name))));
  }
  File.WriteAllText(Path.Combine(root,"endurance-evidence.json"),"synthetic retained sample");
  context=new(root,new(2,new('f',64),release,SubmissionWorkflowStage.FinalizeTests,SubmissionWorkflowStatus.Running,"final-operation",null,receipts,DateTimeOffset.UtcNow));
  settings=AutomationAssessmentFixture.Configure(root,settings,context);
  var result=await AutomationFinalTests.Advance(context,settings,Pin,false,(_,_,_,_)=>throw new InvalidOperationException("No test execution"),_=>throw new InvalidOperationException("No credentials"),default);
  receipts.Add(SubmissionWorkflowStage.FinalizeTests,result.Receipt!);
  context=context with{Checkpoint=context.Checkpoint with{Stage=SubmissionWorkflowStage.PrepareReview,Status=SubmissionWorkflowStatus.Ready,OperationId="review-operation"}};
  var input=new SubmissionAutomationInput(Path.Combine(root,"not-used"),Pin);

  request=new(1,new(Path.Combine(root,"new-console"),[new("new.exe",new('b',64))]),"Use validated portable phase-three tooling; preserve completed tests.");
 }
 [TearDown]public void Cleanup()=>Directory.Delete(root,true);
 private void Verify(SubmissionAutomationConsole console){Assert.That(AutomationReviewTooling.Digest(console),Is.EqualTo(AutomationReviewTooling.Digest(request.Console)));verified++;}
 private void Bind()=>AutomationReviewTooling.Bind(context,settings,Pin,request,Verify);
 [Test]public void BindingChangesOnlyDocumentConsoleAndDoesNotRewriteTestSettingsOrReceipts() {
  var original=JsonSerializer.Serialize(settings);var final=File.ReadAllBytes(Path.Combine(root,"tests-finalized.json"));Bind();
  var selected=AutomationReviewTooling.Resolve(context,settings,Pin,Verify);
  Assert.That(AutomationReviewTooling.Digest(selected.Review!.Console),Is.EqualTo(AutomationReviewTooling.Digest(request.Console)));Assert.That(selected with{Review=selected.Review with{Console=settings.Review!.Console}},Is.EqualTo(settings));
  Assert.That(JsonSerializer.Serialize(settings),Is.EqualTo(original));Assert.That(File.ReadAllBytes(Path.Combine(root,"tests-finalized.json")),Is.EqualTo(final));
  Assert.That(Directory.Exists(Path.Combine(root,"review")),Is.False);Assert.That(verified,Is.EqualTo(2));
 }
 [Test]public void SameBindingIsIdempotentButDifferentToolingCannotReplaceIt() {
  Bind();var saved=File.ReadAllBytes(Path.Combine(root,AutomationReviewTooling.FileName));Bind();
  Assert.That(File.ReadAllBytes(Path.Combine(root,AutomationReviewTooling.FileName)),Is.EqualTo(saved));
  request=request with{Reason="Different request"};Assert.Throws<InvalidDataException>(Bind);
 }
 [TestCase(SubmissionWorkflowStage.FinalizeTests,SubmissionWorkflowStatus.Ready)]
 [TestCase(SubmissionWorkflowStage.PrepareReview,SubmissionWorkflowStatus.Running)]
 [TestCase(SubmissionWorkflowStage.SignReview,SubmissionWorkflowStatus.Ready)]
 public void OnlyUnstartedPhaseThreeCanBind(SubmissionWorkflowStage stage,SubmissionWorkflowStatus status) {
  context=context with{Checkpoint=context.Checkpoint with{Stage=stage,Status=status}};Assert.Throws<InvalidDataException>(Bind);Assert.That(verified,Is.Zero);
 }
 [TestCase("review-intent.json")][TestCase("review-inputs")][TestCase("signed-review")][TestCase("delivery-intent.json")]
 public void ExistingDocumentActivityBlocksNewBinding(string path) {File.WriteAllText(Path.Combine(root,path),"retained");Assert.Throws<InvalidDataException>(Bind);Assert.That(verified,Is.Zero);}
 [Test]public void MissingCompletedFinalTestsBlocksBinding() {context.Checkpoint.CompletedStages.Remove(SubmissionWorkflowStage.FinalizeTests);Assert.Throws<InvalidDataException>(Bind);}
 [Test]public void ChangedFinalizedEvidenceBlocksBindingAndLaterReuse() {Bind();File.AppendAllText(Path.Combine(root,"tests-finalized.json")," changed");Assert.Throws<InvalidDataException>(()=>AutomationReviewTooling.Resolve(context,settings,Pin,Verify));}
 [Test]public void DifferentFrozenSettingsOrOriginalToolsCannotReuseBinding() {
  Bind();Assert.Throws<InvalidDataException>(()=>AutomationReviewTooling.Resolve(context,settings,new('c',64),Verify));
  Assert.Throws<InvalidDataException>(()=>AutomationReviewTooling.Resolve(context,settings with{Review=settings.Review! with{Console=request.Console}},Pin,Verify));
 }
 [Test]public void FailedToolInventoryVerificationLeavesNoBinding() {
  Assert.Throws<InvalidDataException>(()=>AutomationReviewTooling.Bind(context,settings,Pin,request,_=>throw new InvalidDataException("Changed tool")));
  Assert.That(File.Exists(Path.Combine(root,AutomationReviewTooling.FileName)),Is.False);
 }
 [Test]public void OriginalConfigurationRemainsDefaultWithoutBinding() {Assert.That(AutomationReviewTooling.Resolve(context,settings,Pin,_=>throw new InvalidOperationException()),Is.SameAs(settings));}
}
