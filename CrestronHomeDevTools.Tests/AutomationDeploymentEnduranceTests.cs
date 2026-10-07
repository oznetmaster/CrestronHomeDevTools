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

 private void SetProducerBindings(object identity,string installation="release-installation",string requirement="endurance",string host="fixture",string? baselineFile=null) {
  Write("endurance-producer-template/settings.generated.json",new{DeviceId="${deployedDeviceId}",CatalogueId="${deployedCatalogueId}",Identity=identity,
   InstallationIdentity=installation,RequirementId=requirement,ProcessorHost=host});
  if(baselineFile!=null){var input=System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(P("endurance-producer-template/settings.generated.json")))!.AsObject();input["BaselineFile"]=baselineFile;Write("endurance-producer-template/settings.generated.json",input);}
  var probe=settings.Endurance!.Probe with{Files=Directory.GetFiles(P("endurance-producer-template")).Select(p=>new SubmissionEvidenceFile(Path.GetFileName(p),AutomationFiles.Hash(p))).ToArray()};
  settings=settings with{Endurance=settings.Endurance with{Probe=probe,Plan=settings.Endurance.Plan with{ProducerId=SubmissionEnduranceProcessProbe.GetProducerId(probe)}}};
 }
 [TestCase("policy")][TestCase("package")][TestCase("source")][TestCase("template")]
 [TestCase("installation")][TestCase("requirement")][TestCase("processor")]
 public void StaleProducerBindingIsRejectedBeforePublication(string changed) {
  var id=settings.Endurance!.Plan.Identity;
  id=changed switch{"policy"=>id with{PolicySha256=new('a',64)},"package"=>id with{PackageSha256=new('a',64)},
   "source"=>id with{SourceCommit=new('b',40)},"template"=>id with{TemplateSha256=new('a',64)},_=>id};
  SetProducerBindings(id,changed=="installation"?"another":"release-installation",changed=="requirement"?"another":"endurance",changed=="processor"?"another":"fixture");
  var before=Directory.GetFiles(root,"*",SearchOption.AllDirectories).Order().Select(p=>p+":"+AutomationFiles.Hash(p)).ToArray();
  Assert.Throws<InvalidDataException>(()=>AutomationDeploymentEndurance.Resolve(context,settings));
  Assert.That(Directory.GetFiles(root,"*",SearchOption.AllDirectories).Order().Select(p=>p+":"+AutomationFiles.Hash(p)).ToArray(),Is.EqualTo(before));
 }
 [Test]public void MatchingProducerIdentitySurvivesDeploymentAndReadOnlyVerification() {
  SetProducerBindings(settings.Endurance!.Plan.Identity);
  var worker=AutomationDeploymentEndurance.Resolve(context,settings)!;
  Assert.That(AutomationDeploymentEndurance.Resolve(context,settings,true)!.Plan,Is.EqualTo(worker.Plan));
  using var document=JsonDocument.Parse(File.ReadAllBytes(worker.Probe.SettingsFile!));
  Assert.That(document.RootElement.GetProperty("Identity").Deserialize<SubmissionEvidenceIdentity>(AutomationFiles.Json),Is.EqualTo(worker.Plan.Identity));
 }
 [Test]public void ProducerIdentityConventionRejectsAmbiguousCaseVariants() {
  var node=System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(new{Identity=settings.Endurance!.Plan.Identity},AutomationFiles.Json))!.AsObject();
  node["identity"]=node["Identity"]!.DeepClone();
  Assert.Throws<InvalidDataException>(()=>AutomationDeploymentEndurance.ValidateSettingsBinding(node,settings.Endurance.Plan));
 }


 private sealed class OfflineLease(string owner):IProcessorOperationLease {
  public string Owner=>owner;
  public Task ReleaseAsync(CancellationToken token)=>Task.CompletedTask;
  public void Dispose(){}
 }
 private async Task<SubmissionEnduranceWorkerPlan> LegacyFirstSampleFailure(string phase="validate-plan",bool release=true,bool includeBaseline=false) {
  SetProducerBindings(settings.Endurance!.Plan.Identity,baselineFile:includeBaseline?P("prior-lifetime.json"):null);
  if(includeBaseline)Write("prior-lifetime.json",new{PlanSha256=new string('f',64),SettingsSha256=new string('f',64),Baseline=new{ObservedUtc=DateTimeOffset.UtcNow.AddDays(-1)}});
  SubmissionEnduranceWorkerPlan original;
  if(includeBaseline) {
   var source=settings.Endurance!.Probe;
   var input=System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(source.SettingsFile!))!.AsObject();
   var binary=source with{SettingsFile=null,Files=source.Files.Where(f=>f.RelativePath!="settings.generated.json").ToArray()};
   original=AutomationProbePreparation.Publish(settings.Endurance with{Probe=binary},P("endurance-producer"),AutomationDeploymentEndurance.RenderTemplate(input,5678,"catalogue-new"));
   Write("endurance-deployment-binding.json",new{context.Checkpoint.InputSha256,NUnitReceiptSha256=context.Checkpoint.CompletedStages[SubmissionWorkflowStage.WindowsTests].Sha256,Worker=original});
  } else original=AutomationDeploymentEndurance.Resolve(context,settings)!;
  // Reproduce an older preparation tool that migrated the plan's policy but
  // retained the previous producer template. No production files are modified.
  var changed=original.Plan.Identity with{PolicySha256=new('a',64)};
  original=original with{Plan=original.Plan with{Identity=changed}};
  settings=settings with{Endurance=settings.Endurance! with{Plan=settings.Endurance!.Plan with{Identity=changed}}};
  var node=System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(P("endurance-deployment-binding.json")))!.AsObject();
  node["Worker"]=JsonSerializer.SerializeToNode(original,AutomationFiles.Json);
  Write("endurance-deployment-binding.json",node);
  context=context with{Checkpoint=context.Checkpoint with{Status=SubmissionWorkflowStatus.Failed,ReasonCode="endurance-probe-failed"}};
  Write("state.json",context.Checkpoint);
  Task<IProcessorOperationLease> Lease(CancellationToken token)=>Task.FromResult<IProcessorOperationLease>(new OfflineLease(original.Plan.ReservationId));
  await SubmissionEnduranceMonitor.StartCoreAsync(P("endurance"),original.Plan,original.Processor,Lease);
  await SubmissionEnduranceMonitor.CollectCoreAsync(P("endurance"),original.Plan,original.Processor,Lease,_=>Task.FromResult(new SubmissionEnduranceProbeResult(
   original.Plan.Identity,original.Plan.ProcessorIdentity,original.Plan.InstallationIdentity,original.Plan.ReservationId,original.Plan.ProducerId,"unverified",SubmissionEvidenceOutcome.Failed,
   JsonSerializer.SerializeToUtf8Bytes(new{phase,errorType="InvalidDataException"}))),TimeProvider.System);
  if(release)await SubmissionEnduranceMonitor.FinishCoreAsync(P("endurance"),original.Plan,original.Processor,Lease);
  return original;
 }
 [Test]public async Task ReviewedIdentityCorrectionPreservesFailureScopeAndAllOriginalBytes() {
  var original=await LegacyFirstSampleFailure();
  var before=Directory.GetFiles(root,"*",SearchOption.AllDirectories).ToDictionary(p=>p,AutomationFiles.Hash);
  var request=new AutomationEnduranceIdentityRepair.Request(1,Guid.NewGuid().ToString("N"),AutomationEnduranceIdentityRepair.Inspect(context,original),"Reviewed stale policy metadata");
  AutomationEnduranceIdentityRepair.Bind(context,settings,request);
  var worker=AutomationEnduranceIdentityRepair.Resolve(context,settings)!;
  Assert.That(worker.Plan.Identity,Is.EqualTo(original.Plan.Identity));
  Assert.That(worker.Plan.Requirement,Is.EqualTo(original.Plan.Requirement));
  Assert.That(worker.Plan.SampleInterval,Is.EqualTo(original.Plan.SampleInterval));
  Assert.That(worker.Plan.ProbeTimeout,Is.EqualTo(original.Plan.ProbeTimeout));
  Assert.That(worker.Plan.ReservationId,Is.Not.EqualTo(original.Plan.ReservationId));
  Assert.That(AutomationEnduranceIdentityRepair.EvidenceDirectory(context),Is.EqualTo("endurance-recovery/"+request.AttemptId+"/collection/observations"));
  Assert.That(Directory.Exists(Path.Combine(root,AutomationEnduranceIdentityRepair.DirectoryName(context))),Is.False,"Binding never starts an interval");
  foreach(var pin in before)Assert.That(AutomationFiles.Hash(pin.Key),Is.EqualTo(pin.Value),pin.Key);
  Assert.That(SubmissionEnduranceMonitor.ReadStatus(P("endurance"),original.Plan,original.Processor).Checkpoint!.State,Is.EqualTo(SubmissionEnduranceState.Failed));
  Assert.Throws<InvalidDataException>(()=>AutomationEnduranceIdentityRepair.Bind(context,settings,request));
 }
 [TestCase("child-state",true)][TestCase("validate-plan",false)]
 public async Task IdentityCorrectionCannotReplaceFunctionalFailureOrUnreleasedOwnership(string phase,bool release) {
  var original=await LegacyFirstSampleFailure(phase,release);
  Assert.Throws<InvalidDataException>(()=>AutomationEnduranceIdentityRepair.Inspect(context,original));
  Assert.That(Directory.Exists(P("endurance-recovery")),Is.False);
 }
 [TestCase("original")][TestCase("settings")][TestCase("duration")][TestCase("directory")][TestCase("candidate")]
 public async Task IdentityCorrectionRejectsSubsequentTampering(string change) {
  var original=await LegacyFirstSampleFailure();
  var request=new AutomationEnduranceIdentityRepair.Request(1,Guid.NewGuid().ToString("N"),AutomationEnduranceIdentityRepair.Inspect(context,original),"Reviewed configuration failure");
  AutomationEnduranceIdentityRepair.Bind(context,settings,request);
  var worker=AutomationEnduranceIdentityRepair.Resolve(context,settings)!;
  if(change=="original")File.AppendAllText(P("endurance/observations/sample-000000.json")," ");
  else if(change=="settings")File.AppendAllText(worker.Probe.SettingsFile!," ");
  else {
   var node=System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(P(AutomationEnduranceIdentityRepair.FileName)))!.AsObject();
   if(change=="duration")node["Worker"]!["Plan"]!["Requirement"]!["MinimumDuration"]="00:01:00";
   else if(change=="candidate")node["Worker"]!["Plan"]!["Identity"]!["PackageSha256"]=new string('a',64);
   else node["Worker"]!["Probe"]!["Directory"]=P("another");
   Write(AutomationEnduranceIdentityRepair.FileName,node);
  }
  Assert.Catch(()=>AutomationEnduranceIdentityRepair.Resolve(context,settings));
 }


 private async Task<SubmissionEnduranceWorkerPlan> FailedBaselineAfterIdentityRepair(string phase="lifetime-baseline",bool release=true) {
  var first=await LegacyFirstSampleFailure(includeBaseline:true);
  var identityRequest=new AutomationEnduranceIdentityRepair.Request(1,Guid.NewGuid().ToString("N"),AutomationEnduranceIdentityRepair.Inspect(context,first),"Reviewed stale policy");
  AutomationEnduranceIdentityRepair.Bind(context,settings,identityRequest);
  var original=AutomationEnduranceIdentityRepair.Resolve(context,settings)!;
  string collection=P(AutomationEnduranceIdentityRepair.DirectoryName(context));
  Task<IProcessorOperationLease> Lease(CancellationToken token)=>Task.FromResult<IProcessorOperationLease>(new OfflineLease(original.Plan.ReservationId));
  await SubmissionEnduranceMonitor.StartCoreAsync(collection,original.Plan,original.Processor,Lease);
  await SubmissionEnduranceMonitor.CollectCoreAsync(collection,original.Plan,original.Processor,Lease,_=>Task.FromResult(new SubmissionEnduranceProbeResult(
   original.Plan.Identity,original.Plan.ProcessorIdentity,original.Plan.InstallationIdentity,original.Plan.ReservationId,original.Plan.ProducerId,"unverified",SubmissionEvidenceOutcome.Failed,
   JsonSerializer.SerializeToUtf8Bytes(new{phase,errorType="InvalidDataException"}))),TimeProvider.System);
  if(release)await SubmissionEnduranceMonitor.FinishCoreAsync(collection,original.Plan,original.Processor,Lease);
  return original;
 }
 private sealed class IntervalClock:TimeProvider {
  public DateTimeOffset Now=DateTimeOffset.UtcNow.AddDays(-1);
  public override DateTimeOffset GetUtcNow()=>Now;
 }
 [Test]public async Task FreshBaselineCorrectionCompletesAndExportsWithoutAlteringEitherFailedCollection() {
  var previous=await FailedBaselineAfterIdentityRepair();
  var before=Directory.GetFiles(root,"*",SearchOption.AllDirectories).ToDictionary(p=>p,AutomationFiles.Hash);
  var request=new AutomationEnduranceBaselineRepair.Request(1,Guid.NewGuid().ToString("N"),AutomationEnduranceBaselineRepair.Inspect(context,previous),"Reviewed baseline from previous plan");
  AutomationEnduranceBaselineRepair.Bind(context,settings,request);
  var worker=AutomationEnduranceSelection.Resolve(context,settings)!;
  string collection=P(AutomationEnduranceSelection.DirectoryName(context));
  Assert.That(Directory.Exists(collection),Is.False,"Binding must not start collection");
  using var generated=JsonDocument.Parse(File.ReadAllBytes(worker.Probe.SettingsFile!));
  string baseline=generated.RootElement.GetProperty("BaselineFile").GetString()!;
  Assert.That(baseline,Is.EqualTo(P("endurance-baseline-recovery/"+request.AttemptId+"/lifetime.json").Replace('/',Path.DirectorySeparatorChar)).IgnoreCase);
  Assert.That(File.Exists(baseline),Is.False,"The next producer observation owns first acquisition");
  Assert.That(worker.Plan.Requirement,Is.EqualTo(previous.Plan.Requirement));
  var clock=new IntervalClock();
  Task<IProcessorOperationLease> Lease(CancellationToken token)=>Task.FromResult<IProcessorOperationLease>(new OfflineLease(worker.Plan.ReservationId));
  await SubmissionEnduranceMonitor.StartCoreAsync(collection,worker.Plan,worker.Processor,Lease);
  SubmissionEnduranceCheckpoint checkpoint=null!;
  for(int i=0;i<=288;i++) {
   checkpoint=await SubmissionEnduranceMonitor.CollectCoreAsync(collection,worker.Plan,worker.Processor,Lease,_=>Task.FromResult(new SubmissionEnduranceProbeResult(
    worker.Plan.Identity,worker.Plan.ProcessorIdentity,worker.Plan.InstallationIdentity,worker.Plan.ReservationId,worker.Plan.ProducerId,"same-test-boot",SubmissionEvidenceOutcome.Passed,"synthetic offline evidence"u8.ToArray())),clock);
   if(i<288)clock.Now+=worker.Plan.SampleInterval;
  }
  Assert.That(checkpoint.State,Is.EqualTo(SubmissionEnduranceState.Passed));
  await SubmissionEnduranceMonitor.FinishCoreAsync(collection,worker.Plan,worker.Processor,Lease);
  var observation=SubmissionEndurance.Export(SubmissionEnduranceMonitor.GetEvidenceDirectory(collection),worker.Plan,DateTimeOffset.UtcNow);
  Write("endurance-evidence.json",new{EvidenceDirectory=AutomationEnduranceSelection.EvidenceDirectory(context),Observation=observation});
  context.Checkpoint.CompletedStages[SubmissionWorkflowStage.Endurance]=new("endurance-evidence.json",AutomationFiles.Hash(P("endurance-evidence.json")));
  AutomationEndurance.VerifyRetained(context,settings);
  foreach(var pin in before)Assert.That(AutomationFiles.Hash(pin.Key),Is.EqualTo(pin.Value),pin.Key);
  Assert.Throws<InvalidDataException>(()=>AutomationEnduranceBaselineRepair.Bind(context,settings,request));
 }
 [TestCase("child-state",true)][TestCase("lifetime-baseline",false)]
 public async Task BaselineCorrectionRejectsFunctionalFailureAndUnreleasedOwnership(string phase,bool release) {
  var worker=await FailedBaselineAfterIdentityRepair(phase,release);
  Assert.Throws<InvalidDataException>(()=>AutomationEnduranceBaselineRepair.Inspect(context,worker));
  Assert.That(Directory.Exists(P("endurance-baseline-recovery")),Is.False);
 }
 [TestCase("old-baseline")][TestCase("failed-sample")][TestCase("settings")][TestCase("duration")]
 public async Task BaselineCorrectionRejectsChangedEvidenceOrScope(string change) {
  var previous=await FailedBaselineAfterIdentityRepair();
  var request=new AutomationEnduranceBaselineRepair.Request(1,Guid.NewGuid().ToString("N"),AutomationEnduranceBaselineRepair.Inspect(context,previous),"Reviewed baseline mismatch");
  AutomationEnduranceBaselineRepair.Bind(context,settings,request);
  var worker=AutomationEnduranceSelection.Resolve(context,settings)!;
  if(change=="old-baseline")File.AppendAllText(P("prior-lifetime.json")," ");
  else if(change=="failed-sample")File.AppendAllText(P(AutomationEnduranceIdentityRepair.EvidenceDirectory(context)+"/sample-000000.json")," ");
  else if(change=="settings")File.AppendAllText(worker.Probe.SettingsFile!," ");
  else {
   var node=System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(P(AutomationEnduranceBaselineRepair.FileName)))!.AsObject();
   node["Worker"]!["Plan"]!["Requirement"]!["MinimumDuration"]="00:01:00";Write(AutomationEnduranceBaselineRepair.FileName,node);
  }
  Assert.Catch(()=>AutomationEnduranceSelection.Resolve(context,settings));
 }


 [Test]public void NewRunOwnsFreshBaselineWithoutReadingOrDeletingPreviousRunState() {
  Write("old-lifetime.json",new{PlanSha256=new string('a',64),Archived=true});
  string prior=AutomationFiles.Hash(P("old-lifetime.json"));
  SetProducerBindings(settings.Endurance!.Plan.Identity,baselineFile:P("old-lifetime.json"));
  var worker=AutomationDeploymentEndurance.Resolve(context,settings)!;
  using var input=JsonDocument.Parse(File.ReadAllBytes(worker.Probe.SettingsFile!));
  Assert.That(input.RootElement.GetProperty("BaselineFile").GetString(),Is.EqualTo(P("endurance-lifetime.json")));
  Assert.That(File.Exists(P("endurance-lifetime.json")),Is.False);
  Assert.That(AutomationFiles.Hash(P("old-lifetime.json")),Is.EqualTo(prior));
  Write("endurance-lifetime.json",new{AcquiredByProducer=true});
  Assert.That(AutomationDeploymentEndurance.Resolve(context,settings,true)!.Plan,Is.EqualTo(worker.Plan));
 }
 [Test]public void UnboundExistingRuntimeBaselineCannotBeAdoptedAsFresh() {
  SetProducerBindings(settings.Endurance!.Plan.Identity,baselineFile:P("old-lifetime.json"));
  Write("endurance-lifetime.json",new{Orphaned=true});string pin=AutomationFiles.Hash(P("endurance-lifetime.json"));
  Assert.Throws<InvalidDataException>(()=>AutomationDeploymentEndurance.Resolve(context,settings));
  Assert.That(AutomationFiles.Hash(P("endurance-lifetime.json")),Is.EqualTo(pin));
  Assert.That(Directory.Exists(P("endurance-producer")),Is.False);
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
 [Test]public void OnlyTheAppStageCanUsePreAppDeploymentEvidence() {
  context.Checkpoint.CompletedStages.Remove(SubmissionWorkflowStage.AppTests);
  Assert.Throws<InvalidDataException>(()=>AutomationDeploymentEvidence.Read(context,settings,true));
  var app=context with{Checkpoint=context.Checkpoint with{Stage=SubmissionWorkflowStage.AppTests}};
  Assert.That(AutomationDeploymentEvidence.Read(app,settings,true).Installed.DeviceId,Is.EqualTo(5678));
  Assert.Throws<InvalidDataException>(()=>AutomationDeploymentEvidence.Read(app,settings));
  app.Checkpoint.CompletedStages.Remove(SubmissionWorkflowStage.ProcessorTests);
  Assert.Throws<InvalidDataException>(()=>AutomationDeploymentEvidence.Read(app,settings,true));
 }

 [Test]public void ReadOnlyVerificationCannotPrepareMissingDeploymentBinding() {
  var before=Directory.GetFiles(root,"*",SearchOption.AllDirectories).Order().ToArray();
  Assert.Throws<InvalidDataException>(()=>AutomationDeploymentEndurance.Resolve(context,settings,verifyOnly:true));
  Assert.That(Directory.GetFiles(root,"*",SearchOption.AllDirectories).Order().ToArray(),Is.EqualTo(before));
  Assert.That(Directory.Exists(P("endurance-producer")),Is.False);
 }
 [Test]public void ReadOnlyVerificationConsumesExactExistingBindingWithoutWrites() {
  var original=AutomationDeploymentEndurance.Resolve(context,settings)!;
  var before=Directory.GetFiles(root,"*",SearchOption.AllDirectories).Order().Select(p=>p+":"+AutomationFiles.Hash(p)+":"+File.GetLastWriteTimeUtc(p).Ticks).ToArray();
  Assert.That(AutomationDeploymentEndurance.Resolve(context,settings,verifyOnly:true)!.Plan,Is.EqualTo(original.Plan));
  Assert.That(Directory.GetFiles(root,"*",SearchOption.AllDirectories).Order().Select(p=>p+":"+AutomationFiles.Hash(p)+":"+File.GetLastWriteTimeUtc(p).Ticks).ToArray(),Is.EqualTo(before));
 }
 [TestCase("endurance-deployment-binding.json")][TestCase("endurance-producer/settings.generated.json")]
 public void ReadOnlyVerificationNeverRecreatesMissingProducerFiles(string file) {
  _=AutomationDeploymentEndurance.Resolve(context,settings);File.Delete(P(file));
  Assert.Catch(()=>AutomationDeploymentEndurance.Resolve(context,settings,verifyOnly:true));
  Assert.That(File.Exists(P(file)),Is.False);
 }
 [Test]public void ReadOnlyVerificationRejectsChangedBindingWithoutRepair() {
  _=AutomationDeploymentEndurance.Resolve(context,settings);File.AppendAllText(P("endurance-deployment-binding.json")," ");
  string pin=AutomationFiles.Hash(P("endurance-deployment-binding.json"));
  Assert.Throws<InvalidDataException>(()=>AutomationDeploymentEndurance.Resolve(context,settings,verifyOnly:true));
  Assert.That(AutomationFiles.Hash(P("endurance-deployment-binding.json")),Is.EqualTo(pin));
 }
}
