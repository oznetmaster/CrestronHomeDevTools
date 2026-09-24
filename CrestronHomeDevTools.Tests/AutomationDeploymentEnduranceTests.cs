// Copyright (c) 2026 Neil Colvin. MIT licensed.
using System.Text.Json;
using CrestronHomeDevTools.Automation;
using CrestronHomeNUnit.Workflow;
using NUnit.Framework;
namespace CrestronHomeDevTools.Tests;

[TestFixture]
public sealed class AutomationDeploymentEnduranceTests
{
 private string root=null!;
 private SubmissionAutomationSettings settings=null!;
 private SubmissionWorkflowStepContext context=null!;
 private string P(string name)=>Path.Combine(root,name);
 private void Write(string name,object value)=>File.WriteAllBytes(P(name),JsonSerializer.SerializeToUtf8Bytes(value,AutomationFiles.Json));
 [SetUp]public void Setup() {
  root=Path.Combine(TestContext.CurrentContext.WorkDirectory,"deployment-endurance-"+Guid.NewGuid().ToString("N"));
  Directory.CreateDirectory(P("nunit"));Directory.CreateDirectory(P("endurance-producer-template"));
  var release=new SubmissionWorkflowRelease("fixture/driver",1,"v1.2.3",new('a',40),new('b',64),new('c',64),new('d',64));
  var package=new DriverPackageInfo(Guid.NewGuid().ToString(),"Weather","Fixture","1.2.003.0000");
  Write("nunit/actual-import.json",new DriverDeploymentResult(package,release.PackageSha256,"catalogue-new","refreshed",true));
  Write("nunit/actual-activation.json",new DriverInstanceReady(5678,package.Model,package.Version,"Installed"));
  Write("nunit/ReleaseCandidate.json",new{Sha256=release.PackageSha256,release.SourceCommit,SourceInitiallyClean=true,Mode="PrebuiltRelease"});
  File.WriteAllText(P("endurance-producer-template/probe.exe"),"synthetic probe, never executed");
  Write("endurance-producer-template/settings.generated.json",new{DeviceId="${deployedDeviceId}",CatalogueId="${deployedCatalogueId}",Other="unchanged"});
  var probe=new SubmissionEnduranceProbeProgram(P("endurance-producer-template"),"probe.exe",
   Directory.GetFiles(P("endurance-producer-template")).Select(p=>new SubmissionEvidenceFile(Path.GetFileName(p),AutomationFiles.Hash(p))).ToArray(),P("endurance-producer-template/settings.generated.json"));
  var plan=new SubmissionEndurancePlan(new(release.PackageSha256,release.SourceCommit,new('e',64),new('f',64)),
   new("endurance",TimeSpan.FromHours(24),Execution:new("weather","endurance",SubmissionEvidenceOutcome.Passed,null,false,600)),
   "processor:fixture","release-installation",Guid.NewGuid().ToString("N"),SubmissionEnduranceProcessProbe.GetProducerId(probe),TimeSpan.FromMinutes(5),TimeSpan.FromMinutes(1));
  var nunit=new WorkflowPlan{Host="fixture",CertificateSha256=new('e',64),SshFingerprint="fixture",SourceRoots=[root],LocalTests=[],
   TestPackage=new(P("tests.csproj"),P("test.pkg"),"Tests",1),ProcessorSuites=[],ActualDriver=new(P("driver.csproj"),P("candidate.pkg"),"Station",1),
   ReleaseCandidate=new(release.PackageSha256,package.DriverId,package.Version,root,release.SourceCommit)};
  settings=new(1,root,release,root,new(package.DriverId,package.Version,PortalSubmissionKind.NewDriver,"Fixture","fixture@example.invalid"),"unused",nunit,
   Endurance:new(plan,new("fixture","fixture"),probe),EnduranceProbeSettingsTemplate:new(P("original-template.json"),new('a',64))) {EnduranceFromDeployment=true};
  context=new(root,new(1,new('a',64),release,SubmissionWorkflowStage.Endurance,SubmissionWorkflowStatus.Running,"endurance-operation",null,
   new(){[SubmissionWorkflowStage.ProcessorTests]=new("processor-tests.json",new('b',64)),[SubmissionWorkflowStage.AppTests]=new("app-tests.json",new('c',64))},DateTimeOffset.UtcNow));
  PinInventory();
 }
 private void PinInventory() {
  Write("windows-tests.json",new{context.Checkpoint.InputSha256,Files=Directory.GetFiles(P("nunit")).Select(p=>new SubmissionWorkflowReceipt(Path.GetRelativePath(root,p),AutomationFiles.Hash(p))).ToArray()});
  context.Checkpoint.CompletedStages[SubmissionWorkflowStage.WindowsTests]=new("windows-tests.json",AutomationFiles.Hash(P("windows-tests.json")));
 }
 [TearDown]public void Cleanup()=>Directory.Delete(root,true);
 [Test]public void NewDeploymentProducesPinnedNumericTargetAndRecoveryPreservesIt() {
  string original=JsonSerializer.Serialize(settings,AutomationFiles.Json);
  var worker=AutomationDeploymentEndurance.Resolve(context,settings)!;
  using var generated=JsonDocument.Parse(File.ReadAllBytes(worker.Probe.SettingsFile!));
  Assert.That(generated.RootElement.GetProperty("DeviceId").GetInt32(),Is.EqualTo(5678));
  Assert.That(generated.RootElement.GetProperty("CatalogueId").GetString(),Is.EqualTo("catalogue-new"));
  Assert.That(generated.RootElement.GetProperty("Other").GetString(),Is.EqualTo("unchanged"));
  Assert.That(worker.Plan.ProducerId,Is.Not.EqualTo(settings.Endurance!.Plan.ProducerId));
  Assert.That(worker.Plan with{ProducerId=settings.Endurance.Plan.ProducerId},Is.EqualTo(settings.Endurance.Plan));
  Assert.That(JsonSerializer.Serialize(settings,AutomationFiles.Json),Is.EqualTo(original));
  string pin=AutomationFiles.Hash(P("endurance-deployment-binding.json"));
  Directory.CreateDirectory(P("endurance"));
  var recovered=AutomationDeploymentEndurance.Resolve(context,settings)!;
  Assert.That(recovered.Plan,Is.EqualTo(worker.Plan));Assert.That(AutomationFiles.Hash(P("endurance-deployment-binding.json")),Is.EqualTo(pin));
 }
 [TestCase("nunit/actual-import.json")][TestCase("nunit/actual-activation.json")][TestCase("windows-tests.json")]
 public void ChangedRetainedReceiptsStopBeforeCreatingProducer(string file) {
  File.AppendAllText(P(file)," ");
  Assert.Throws<InvalidDataException>(()=>AutomationDeploymentEndurance.Resolve(context,settings));
  Assert.That(Directory.Exists(P("endurance-producer")),Is.False);
 }
 [TestCase("hash")][TestCase("version")][TestCase("available")][TestCase("id")][TestCase("source")]
 public void ReceiptIdentityMustMatchTheFrozenCandidate(string variant) {
  if(variant=="id")Write("nunit/actual-activation.json",new DriverInstanceReady(0,"Weather","1.2.003.0000","Installed"));
  else if(variant=="source")Write("nunit/ReleaseCandidate.json",new{Sha256=settings.Release.PackageSha256,SourceCommit=new string('f',40),SourceInitiallyClean=true,Mode="PrebuiltRelease"});
  else {
   var import=AutomationFiles.Read<DriverDeploymentResult>(P("nunit/actual-import.json"));
   Write("nunit/actual-import.json",variant switch{"hash"=>import with{Sha256=new('f',64)},"version"=>import with{Package=import.Package with{Version="1.2.004.0000"}},_=>import with{Available=false}});
  }
  PinInventory();Assert.Throws<InvalidDataException>(()=>AutomationDeploymentEndurance.Resolve(context,settings));
 }
 [Test]public void MissingFinalFilesAreNotRecreatedAfterBindingWasRetained() {
  var worker=AutomationDeploymentEndurance.Resolve(context,settings)!;File.Delete(worker.Probe.SettingsFile!);
  Assert.Catch(()=>AutomationDeploymentEndurance.Resolve(context,settings));
  Assert.That(File.Exists(worker.Probe.SettingsFile!),Is.False);
 }
 [Test]public void ExistingCollectionWithoutBindingCannotBeRetargeted() {
  Directory.CreateDirectory(P("endurance"));
  Assert.Throws<InvalidDataException>(()=>AutomationDeploymentEndurance.Resolve(context,settings));
  Assert.That(Directory.Exists(P("endurance-producer")),Is.False);
 }
 [Test]public void IncompleteStagesAndConfigurationCannotCreateABinding() {
  context.Checkpoint.CompletedStages.Remove(SubmissionWorkflowStage.AppTests);
  Assert.Throws<InvalidDataException>(()=>AutomationDeploymentEndurance.Resolve(context,settings));
  Assert.Throws<InvalidDataException>(()=>AutomationDeploymentEndurance.ValidateConfiguration(settings with{NUnit=settings.NUnit with{ActualDriver=null}}));
 }
 [Test]public void ExistingInstalledCandidateRouteRemainsUnchanged() {
  Assert.That(AutomationDeploymentEndurance.Resolve(context,settings with{EnduranceFromDeployment=false}),Is.SameAs(settings.Endurance));
  Assert.That(File.Exists(P("endurance-deployment-binding.json")),Is.False);
 }
}
