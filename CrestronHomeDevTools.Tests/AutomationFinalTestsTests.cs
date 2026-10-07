// Copyright (c) 2026 Neil Colvin. MIT licensed.
using System.Net;
using CrestronHomeDevTools.Automation;
using CrestronHomeNUnit.Workflow;
using NUnit.Framework;
namespace CrestronHomeDevTools.Tests;

[TestFixture]
public sealed class AutomationFinalTestsTests
{
 private string root=null!;
 private SubmissionWorkflowStepContext context=null!;
 private SubmissionAutomationSettings settings=null!;
 private const string Pin="aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
 private int calls;
 [SetUp]public void Setup() {
  root=Path.Combine(TestContext.CurrentContext.WorkDirectory,"final-tests-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);calls=0;
  var release=new SubmissionWorkflowRelease("fixture/driver",1,"v1",new('a',40),new('b',64),new('c',64),new('d',64));
  settings=new(1,root,release,root,new(Guid.NewGuid().ToString(),"1.0.0.0",PortalSubmissionKind.NewDriver,"Fixture"),"unused",
   new WorkflowPlan{Host="unused",CertificateSha256=new('e',64),SshFingerprint="unused",SourceRoots=[root],LocalTests=[],TestPackage=new("unused","unused","fixture",1),ProcessorSuites=[]});
  var receipts=new Dictionary<SubmissionWorkflowStage,SubmissionWorkflowReceipt>();
  foreach(var stage in new[]{SubmissionWorkflowStage.ValidateCandidate,SubmissionWorkflowStage.WindowsTests,SubmissionWorkflowStage.ProcessorTests,SubmissionWorkflowStage.AppTests,SubmissionWorkflowStage.Endurance}) {
   string name=stage+".json";File.WriteAllText(Path.Combine(root,name),"synthetic "+stage);receipts.Add(stage,new(name,AutomationFiles.Hash(Path.Combine(root,name))));
  }
  File.WriteAllText(Path.Combine(root,"endurance-evidence.json"),"synthetic retained observation");
  context=new(root,new(2,new('f',64),release,SubmissionWorkflowStage.FinalizeTests,SubmissionWorkflowStatus.Running,"operation",null,receipts,DateTimeOffset.UtcNow));
  settings=AutomationAssessmentFixture.Configure(root,settings,context);
 }
 [TearDown]public void Cleanup()=>Directory.Delete(root,true);
 private NetworkCredential Credentials(string host){calls++;throw new InvalidOperationException("No equipment access expected");}
 private Task<InstalledDriverTestResult> Run(InstalledDriverTestPlan p,NetworkCredential c,string r,CancellationToken t){calls++;throw new InvalidOperationException("No replay expected");}
 private Task<SubmissionWorkflowStepResult> AdvanceFinalTests(bool recover=false)=>AutomationFinalTests.Advance(context,settings,Pin,recover,Run,Credentials,default);
 private async Task Complete() {
  var result=await AdvanceFinalTests();Assert.That(result.Status,Is.EqualTo(SubmissionWorkflowStatus.Completed));
  context.Checkpoint.CompletedStages.Add(SubmissionWorkflowStage.FinalizeTests,result.Receipt!);
 }
 [Test]public async Task CompletedBoundaryCanBeVerifiedAndRecoveredWithoutRepeatingTests() {
  await Complete();var bytes=File.ReadAllBytes(Path.Combine(root,AutomationFinalTests.ReceiptName));
  AutomationFinalTests.VerifyRetained(context,settings,Pin);
  Assert.That((await AdvanceFinalTests(true)).Status,Is.EqualTo(SubmissionWorkflowStatus.Completed));
  Assert.That(File.ReadAllBytes(Path.Combine(root,AutomationFinalTests.ReceiptName)),Is.EqualTo(bytes));Assert.That(calls,Is.Zero);
 }
 [Test]public async Task InterruptedInboxCloseoutRecoversWithoutRepeatingAnyTests() {
  string inbox=Path.Combine(root,"operator");settings=settings with{OperatorInbox=new(inbox,new('a',64))};
  await Assert.ThrowsAsync<DirectoryNotFoundException>(async()=>await AdvanceFinalTests());
  Assert.That(File.Exists(Path.Combine(root,AutomationFinalTests.ReceiptName)),Is.True);
  var retained=File.ReadAllBytes(Path.Combine(root,AutomationFinalTests.ReceiptName));Directory.CreateDirectory(inbox);
  Assert.That((await AdvanceFinalTests(true)).Status,Is.EqualTo(SubmissionWorkflowStatus.Completed));
  Assert.That(SubmissionOperatorInboxLifecycle.IsClosed(settings.OperatorInbox!),Is.True);
  Assert.That(File.ReadAllBytes(Path.Combine(root,AutomationFinalTests.ReceiptName)),Is.EqualTo(retained));Assert.That(calls,Is.Zero);
 }
 [TestCase(SubmissionWorkflowStage.AppTests)][TestCase(SubmissionWorkflowStage.Endurance)]
 public async Task MissingPrecedingStageCannotAdvanceFinalTests(SubmissionWorkflowStage stage) {
  context.Checkpoint.CompletedStages.Remove(stage);
  await Assert.ThrowsAsync<InvalidDataException>(async()=>await AdvanceFinalTests());
  Assert.That(File.Exists(Path.Combine(root,AutomationFinalTests.ReceiptName)),Is.False);Assert.That(calls,Is.Zero);
 }
 [TestCase("Endurance.json")][TestCase("endurance-evidence.json")]
 public async Task ChangedCompletedEvidenceBlocksDocumentPreparation(string name) {
  await Complete();File.AppendAllText(Path.Combine(root,name),"changed");
  Assert.Throws<InvalidDataException>(()=>AutomationFinalTests.VerifyRetained(context,settings,Pin));Assert.That(calls,Is.Zero);
 }
 [Test]public async Task DifferentSettingsCannotReuseFinalizedTests() {
  await Complete();Assert.Throws<InvalidDataException>(()=>AutomationFinalTests.VerifyRetained(context,settings,new('b',64)));
 }
 [Test]public async Task FinalizationRequiresPolicyButDoesNotOpenDocumentInputs() {
  var absent=new SubmissionAutomationInput(Path.Combine(root,"missing-policy.json"),Pin);
  settings=settings with{Review=settings.Review! with{Template=absent,Inventory=absent,Mapping=absent,Console=new(root,[])}};
  await Complete();AutomationFinalTests.VerifyRetained(context,settings,Pin);
  Assert.That((await AdvanceFinalTests(true)).Status,Is.EqualTo(SubmissionWorkflowStatus.Completed));
  Assert.That(Directory.Exists(Path.Combine(root,"review-inputs")),Is.False);
  Assert.That(Directory.Exists(Path.Combine(root,"review-snapshot")),Is.False);
  Assert.That(Directory.Exists(Path.Combine(root,"review")),Is.False);Assert.That(calls,Is.Zero);
 }
 [Test]public async Task ReviewStageNeverStartsMissingFinalTestsOrResolvesCredentials() {
  context=context with{Checkpoint=context.Checkpoint with{Stage=SubmissionWorkflowStage.PrepareReview,CompletedStages=[]}};
  var adapter=new SubmissionAutomationStages(settings,Pin,(_,_,_,_)=>throw new InvalidOperationException("No NUnit execution expected"),Credentials,installedApp:Run);
  await Assert.ThrowsAsync<InvalidDataException>(async()=>await adapter.ExecuteAsync(context,default));
  await Assert.ThrowsAsync<InvalidDataException>(async()=>await adapter.RecoverAsync(context,default));
  Assert.That(calls,Is.Zero);Assert.That(Directory.Exists(Path.Combine(root,"review")),Is.False);
 }
 [TestCase("review-inputs")][TestCase("review-intent.json")][TestCase("signed-review")]
 [TestCase("delivery-intent.json")][TestCase("retained.json")]
 public void LegacyBoundaryMigrationRejectsAnyDocumentOrDeliveryActivity(string entry) {
  File.WriteAllText(Path.Combine(root,entry),"synthetic original activity");
  Assert.Throws<InvalidDataException>(()=>AutomationBoundaryMigration.Verify(context,settings));
  Assert.That(File.ReadAllText(Path.Combine(root,entry)),Is.EqualTo("synthetic original activity"));Assert.That(calls,Is.Zero);
 }
 [Test]public void LegacyBoundaryMigrationDoesNotContactEquipmentOrPrepareDocuments() {
  var before=Directory.GetFileSystemEntries(root).Order().ToArray();
  AutomationBoundaryMigration.Verify(context,settings);
  Assert.That(Directory.GetFileSystemEntries(root).Order().ToArray(),Is.EqualTo(before));Assert.That(calls,Is.Zero);
 }
 [Test]public void LegacyBoundaryMigrationRequiresCompletedPostcheckEvidence() {
  var configured=settings with{PostEnduranceTests=new InstalledDriverTestPlan {Host="unused",CertificateSha256=Pin,SshFingerprint="unused",PackagePath="unused",PackageSha256=Pin,
   PackageSourceCommit=new('a',40),SourceRoots=[root],Target=new(2,-1,"Synthetic","Model",1,"1.0.0.0","catalogue","Synthetic","IP"),AndroidTests=new("unused","unused")}};
  Assert.Catch<IOException>(()=>AutomationBoundaryMigration.Verify(context,configured));Assert.That(calls,Is.Zero);
 }

}
