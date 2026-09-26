// Copyright (c) 2026 Neil Colvin. MIT licensed.
using System.Net;
using System.Text.Json;
using CrestronHomeDevTools.Automation;
using CrestronHomeNUnit.Android;
using CrestronHomeNUnit.Client;
using CrestronHomeNUnit.Workflow;
using NUnit.Framework;

namespace CrestronHomeDevTools.Tests;
[TestFixture]
public sealed class AutomationInstalledAppTests
{
 private string root=null!;
 private SubmissionAutomationSettings settings=null!;
 private SubmissionWorkflowStepContext context=null!;
 private int calls;
 [SetUp] public void Setup() {
  root=Path.Combine(TestContext.CurrentContext.WorkDirectory,"installed-app-"+Guid.NewGuid().ToString("N"));
  Directory.CreateDirectory(root);string source=Path.Combine(root,"source");Directory.CreateDirectory(source);
  string project=Path.Combine(source,"Tests.csproj"),adb=Path.Combine(root,"adb.exe"),profile=Path.Combine(root,"profile.json"),package=Path.Combine(root,"candidate.pkg");
  File.WriteAllText(project,"<Project />");File.WriteAllText(adb,"");File.WriteAllText(package,"synthetic package; fake runner only");
  File.WriteAllText(profile,JsonSerializer.Serialize(new AndroidSessionProfile(adb,"emulator-5554","com.crestron.phoenix.app","Example",Path.Combine(root,"android.lock"))));
  var release=new SubmissionWorkflowRelease("example/driver",1,"v1",new('a',40),AutomationFiles.Hash(package),new('b',64),new('c',64));
  var nunit=new WorkflowPlan {Host="processor.example",CertificateSha256=new('d',64),SshFingerprint="pin",SourceRoots=[source],
   LocalTests=[new(project,1)],TestPackage=new(project,package,"Tests",1),ProcessorSuites=[new("unit",1,[])]};
  var app=new InstalledDriverTestPlan {Host=nunit.Host,CertificateSha256=nunit.CertificateSha256,SshFingerprint=nunit.SshFingerprint,
   PackagePath=package,PackageSha256=release.PackageSha256,PackageSourceCommit=release.SourceCommit,SourceRoots=[source],
   Target=new(2,-1,"Example","Model",1,"1.0.0.0","catalogue","Example","IP"),AndroidTests=new(project,profile)};
  settings=new(1,root,release,source,new(Guid.NewGuid().ToString(),"1.0.0.0",PortalSubmissionKind.NewDriver,"Example","example@example.invalid"),"unused",nunit,InstalledAppTests:app);
  string run=Path.Combine(root,"run");Directory.CreateDirectory(run);
  context=new(run,new(1,new('e',64),release,SubmissionWorkflowStage.AppTests,SubmissionWorkflowStatus.Running,Guid.NewGuid().ToString("N"),null,[],DateTimeOffset.UtcNow));
  calls=0;
 }
 [TearDown] public void Cleanup()=>Directory.Delete(root,true);
 private Task<InstalledDriverTestResult> Run(InstalledDriverTestPlan plan,NetworkCredential credential,string folder,CancellationToken token) {
  calls++;Directory.CreateDirectory(folder);
  var result=new InstalledDriverTestResult(new WorkflowTestOutcome(3,0,0,true),true,true,true,true,"synthetic offline result");
  File.WriteAllText(Path.Combine(folder,"InstalledDriverTests.json"),JsonSerializer.Serialize(result));
  File.WriteAllText(Path.Combine(folder,"raw.xml"),"retained raw evidence");return Task.FromResult(result);
 }
 private Task<SubmissionWorkflowStepResult> Advance(bool recover=false)=>AutomationInstalledApp.Advance(context,settings,recover,Run,_=>new NetworkCredential("synthetic","synthetic"),default);
 [Test] public async Task RestoredPublicResultIsRetainedAndRecoveryDoesNotRepeatTests() {
  var first=await Advance();Assert.That(first.Status,Is.EqualTo(SubmissionWorkflowStatus.Completed));
  Assert.That((await Advance(true)).Receipt,Is.EqualTo(first.Receipt));
  Assert.That((await Advance()).Receipt,Is.EqualTo(first.Receipt));Assert.That(calls,Is.EqualTo(1));
  AutomationInstalledApp.VerifyRetained(context.RunDirectory);
  File.AppendAllText(Path.Combine(context.RunDirectory,"installed-app","raw.xml"),"changed");
  Assert.Throws<InvalidDataException>(()=>AutomationInstalledApp.VerifyRetained(context.RunDirectory));
 }
 [TestCase("host")][TestCase("package")][TestCase("commit")][TestCase("combined")]
 public void WrongCandidateOrAmbiguousModeStopsBeforeAnyTests(string difference) {
  settings=difference switch {
   "host"=>settings with{InstalledAppTests=settings.InstalledAppTests! with{Host="another.example"}},
   "package"=>settings with{InstalledAppTests=settings.InstalledAppTests! with{PackageSha256=new('f',64)}},
   "commit"=>settings with{InstalledAppTests=settings.InstalledAppTests! with{PackageSourceCommit=new('f',40)}},
   _=>settings with{NUnit=settings.NUnit with{AndroidTests=settings.InstalledAppTests!.AndroidTests}}};
  Assert.ThrowsAsync<InvalidDataException>(async()=>await Advance());Assert.That(calls,Is.Zero);
 }
 [Test] public async Task InterruptedInvocationRetainsUncertaintyWithoutReplaying() {
  async Task<InstalledDriverTestResult> Interrupted(InstalledDriverTestPlan p,NetworkCredential c,string f,CancellationToken t) {
   calls++;await Task.Yield();throw new IOException("synthetic interruption");
  }
  Assert.ThrowsAsync<IOException>(async()=>await AutomationInstalledApp.Advance(context,settings,false,Interrupted,_=>new(),default));
  var recovered=await Advance(true);Assert.That(recovered.Status,Is.EqualTo(SubmissionWorkflowStatus.OutcomeUnknown));Assert.That(calls,Is.EqualTo(1));
 }
 [TestCase(false,true,true,true)][TestCase(true,false,true,true)][TestCase(true,true,false,true)][TestCase(true,true,true,false)]
 public async Task EveryRestorationAndIdentityGateIsRequired(bool restoration,bool cleanup,bool verified,bool released) {
  Task<InstalledDriverTestResult> Incomplete(InstalledDriverTestPlan p,NetworkCredential c,string f,CancellationToken t) {
   Directory.CreateDirectory(f);var result=new InstalledDriverTestResult(new WorkflowTestOutcome(3,0,0,true),restoration,cleanup,verified,released,"synthetic incomplete result");
   File.WriteAllText(Path.Combine(f,"InstalledDriverTests.json"),JsonSerializer.Serialize(result));return Task.FromResult(result);
  }
  var outcome=await AutomationInstalledApp.Advance(context,settings,false,Incomplete,_=>new(),default);
  Assert.That(outcome.Status,Is.EqualTo(SubmissionWorkflowStatus.Failed));Assert.That(File.Exists(Path.Combine(context.RunDirectory,"installed-app-tests.json")),Is.False);
 }
 [Test] public async Task ControllerRoutesSeparateAppTestsWithoutRerunningNUnit() {
  var stages=new SubmissionAutomationStages(settings,new('f',64),(_,_,_,_)=>throw new AssertionException("NUnit must not run here"),_=>new(),installedApp:Run);
  Assert.That((await stages.ExecuteAsync(context,default)).Status,Is.EqualTo(SubmissionWorkflowStatus.Completed));Assert.That(calls,Is.EqualTo(1));
 }
 [Test] public async Task FixtureSettingsAreAvailableBeforeExecutionAndIncludedInReceipt() {
  settings=settings with{InstalledAppFixtureSettings=JsonSerializer.SerializeToElement(new{TileName="Station",DeviceId=2})};
  Task<InstalledDriverTestResult> Inspect(InstalledDriverTestPlan p,NetworkCredential c,string f,CancellationToken t) {
   using var fixture=JsonDocument.Parse(File.ReadAllText(Path.Combine(context.RunDirectory,"app-fixture-settings.json")));
   Assert.That(fixture.RootElement.GetProperty("TileName").GetString(),Is.EqualTo("Station"));
   return Run(p,c,f,t);
  }
  await AutomationInstalledApp.Advance(context,settings,false,Inspect,_=>new(),default);
  await Advance(true);Assert.That(calls,Is.EqualTo(1));
  AutomationInstalledApp.VerifyRetained(context.RunDirectory);
  File.Delete(Path.Combine(context.RunDirectory,"app-fixture-settings.json"));
  Assert.Throws<InvalidDataException>(()=>AutomationInstalledApp.VerifyRetained(context.RunDirectory));
  Assert.ThrowsAsync<InvalidDataException>(async()=>await Advance(true));Assert.That(calls,Is.EqualTo(1));
 }
 [Test] public async Task ChangedFixtureDataIsNeverUsedToRecoverOrReplayAnAttempt() {
  settings=settings with{InstalledAppFixtureSettings=JsonSerializer.SerializeToElement(new{DeviceId=2})};
  await Advance();settings=settings with{InstalledAppFixtureSettings=JsonSerializer.SerializeToElement(new{DeviceId=3})};
  Assert.ThrowsAsync<InvalidDataException>(async()=>await Advance(true));Assert.That(calls,Is.EqualTo(1));
 }
 [Test] public void FixtureMutationDuringExecutionCannotProduceCompletionReceipt() {
  settings=settings with{InstalledAppFixtureSettings=JsonSerializer.SerializeToElement(new{DeviceId=2})};
  Task<InstalledDriverTestResult> Mutate(InstalledDriverTestPlan p,NetworkCredential c,string f,CancellationToken t) {
   File.WriteAllText(Path.Combine(context.RunDirectory,"app-fixture-settings.json"),"{}");return Run(p,c,f,t);
  }
  Assert.ThrowsAsync<InvalidDataException>(async()=>await AutomationInstalledApp.Advance(context,settings,false,Mutate,_=>new(),default));
  Assert.That(File.Exists(Path.Combine(context.RunDirectory,"installed-app-tests.json")),Is.False);
 }
 [Test] public void NonObjectFixtureSettingsAreRejectedBeforeTests() {
  settings=settings with{InstalledAppFixtureSettings=JsonSerializer.SerializeToElement("wrong shape")};
  Assert.ThrowsAsync<InvalidDataException>(async()=>await Advance());Assert.That(calls,Is.Zero);
 }
 [Test] public void ConfigurationCheckListsLaterGapsWithoutCallingTestsOrReadingCredentials() {
  var report=SubmissionAutomationConfiguration.Check(settings with{CredentialBindings="private-store-not-opened"});
  Assert.That(report.AllStageBindingsPresent,Is.False);
  Assert.That(report.MissingBindings,Is.EquivalentTo(new[]{"Endurance","Review"}));
  Assert.That(calls,Is.Zero);
  Assert.That(JsonSerializer.Serialize(report),Does.Not.Contain("private-store-not-opened").And.Not.Contain(settings.NUnit.Host));
 }
 [Test] public void SubmitConfigurationRequiresProtectedBindingsThatRehearsalDoesNot() {
  var report=SubmissionAutomationConfiguration.Check(settings with{Mode=SubmissionAutomationMode.Submit});
  Assert.That(report.MissingBindings,Does.Contain("Protected"));
  Assert.That(SubmissionAutomationConfiguration.Check(settings).MissingBindings,Does.Not.Contain("Protected"));
 }
 private void ConfigurePostEndurance() {
  // The fake app runner never reads the monitor plan. The completed-stage
  // receipt models the workflow engine's already verified endurance gate.
  settings=settings with{PostEnduranceTests=settings.InstalledAppTests,Endurance=new(null!,null!,null!)};
  context=context with{Checkpoint=context.Checkpoint with{Stage=SubmissionWorkflowStage.PrepareReview}};
  string receipt=Path.Combine(context.RunDirectory,"endurance-result.json");
  File.WriteAllText(receipt,"synthetic completed endurance receipt");
  context.Checkpoint.CompletedStages[SubmissionWorkflowStage.Endurance]=new("endurance-result.json",AutomationFiles.Hash(receipt));
 }
 private Task<SubmissionWorkflowStepResult> Post(bool recover=false)=>AutomationPostEndurance.Advance(context,settings,recover,Run,_=>new("synthetic","synthetic"),default);
 private void ConfigurePostDeployment() {
  ConfigurePostEndurance();
  var plan=settings.PostEnduranceTests!;string guid=Guid.NewGuid().ToString();
  settings=settings with{PostEnduranceFromDeployment=true,NUnit=settings.NUnit with{
   ActualDriver=new(plan.AndroidTests.Project,plan.PackagePath,plan.Target.Name,plan.Target.LocationId),
   ReleaseCandidate=new(settings.Release.PackageSha256,guid,plan.Target.Version,settings.SourceRepository,settings.Release.SourceCommit)}};
  string folder=Path.Combine(context.RunDirectory,"nunit");Directory.CreateDirectory(folder);
  void Write(string name,object value)=>File.WriteAllBytes(Path.Combine(folder,name),JsonSerializer.SerializeToUtf8Bytes(value,AutomationFiles.Json));
  Write("actual-import.json",new DriverDeploymentResult(new(guid,plan.Target.Model,"Example","1.0.000.0000"),settings.Release.PackageSha256,"observed.catalogue.1.0.000.0000","refreshed",true));
  Write("actual-activation.json",new DriverInstanceReady(167,plan.Target.Model,"1.0.000.0000","Installed"));
  Write("ReleaseCandidate.json",new{Sha256=settings.Release.PackageSha256,settings.Release.SourceCommit,SourceInitiallyClean=true,Mode="PrebuiltRelease"});
  string receipt=Path.Combine(context.RunDirectory,"windows-tests.json");
  File.WriteAllBytes(receipt,JsonSerializer.SerializeToUtf8Bytes(new{context.Checkpoint.InputSha256,
   Files=Directory.GetFiles(folder).Select(p=>new SubmissionWorkflowReceipt(Path.GetRelativePath(context.RunDirectory,p),AutomationFiles.Hash(p))).ToArray()},AutomationFiles.Json));
  context.Checkpoint.CompletedStages[SubmissionWorkflowStage.WindowsTests]=new("windows-tests.json",AutomationFiles.Hash(receipt));
  context.Checkpoint.CompletedStages[SubmissionWorkflowStage.ProcessorTests]=new("processor-tests.json",new('a',64));
  context.Checkpoint.CompletedStages[SubmissionWorkflowStage.AppTests]=new("app-tests.json",new('b',64));
 }
 private async Task ConfigureRemoval() {
  ConfigurePostDeployment();await Post();calls=0;
  string policy=Path.Combine(root,"removal-policy.json");
  File.WriteAllBytes(policy,JsonSerializer.SerializeToUtf8Bytes(new SubmissionEvidencePolicy(1,[new("system.removal",TimeSpan.Zero,false,
   new("selected.driver","combined",SubmissionEvidenceOutcome.Passed,null,false))]),AutomationFiles.Json));
  var profile=AndroidWorkflowSession.Read<AndroidSessionProfile>(settings.PostEnduranceTests!.AndroidTests.ProfilePath);
  settings=settings with{Review=new(new(policy,AutomationFiles.Hash(policy)),new("unused-template",new('a',64)),null!,null!,null!,"Example","Example",[]),
   Removal=new(new(profile,[new(167,"Example",1,"Room",false)],[]),"system.removal")};
 }
 private Task<SubmissionWorkflowStepResult> Remove(bool pass=true,bool interrupt=false,bool baseline=false,string placementBaseline="passed")=>AutomationRemoval.Advance(context,settings,_=>new("synthetic","synthetic"),default,
  (plan,credential,folder,token)=>{
   calls++;Assert.That(plan.Target.DeviceId,Is.EqualTo(167));Assert.That(plan.Target.CatalogueId,Is.EqualTo("observed.catalogue.1.0.000.0000"));
   if(interrupt)throw new IOException("Synthetic interrupted removal");
   Directory.CreateDirectory(folder);
   var result=new DriverRemovalWorkflowResult(!baseline,true,true,new(true,true,true,true,true,new(true,pass,"synthetic log interval",[],pass?[]:["synthetic error"])),null);
   File.WriteAllBytes(Path.Combine(folder,"result.json"),JsonSerializer.SerializeToUtf8Bytes(result,AutomationFiles.Json));
   File.WriteAllText(Path.Combine(folder,"raw.xml"),"synthetic retained UI/log evidence");
   if(settings.Removal!.PlacementRequirementId!=null && placementBaseline!="missing") {
    string before=Path.Combine(folder,"removal","ui-before");Directory.CreateDirectory(before);
    AutomationFiles.Write(Path.Combine(before,"outcome.json"),new DriverRemovalUiOutcome(placementBaseline!="failed",placementBaseline!="unrestored"));
    AutomationFiles.Write(Path.Combine(before,"plan.json"),placementBaseline=="changed"?plan.App with{NonvisualDeviceIds=[999]}:plan.App);
   }
   return Task.FromResult(result);
  });
 private async Task ConfigurePlacement() {
  await ConfigureRemoval();var review=settings.Review!;
  var policy=AutomationFiles.Read<SubmissionEvidencePolicy>(review.Policy.Path);
  var placement=new SubmissionRequirement("ui.placement",TimeSpan.Zero,false,new("selected.membership","combined",SubmissionEvidenceOutcome.Passed,null,false));
  File.WriteAllBytes(review.Policy.Path,JsonSerializer.SerializeToUtf8Bytes(policy with{Requirements=[..policy.Requirements,placement]},AutomationFiles.Json));
  settings=settings with{Review=review with{Policy=review.Policy with{Sha256=AutomationFiles.Hash(review.Policy.Path)}},
   Removal=settings.Removal! with{PlacementRequirementId=placement.Id}};
 }
 [Test] public async Task PlacementReusesMatchingBeforeRemovalBaselineWithoutAnotherOperation() {
  await ConfigurePlacement();Assert.That((await Remove()).Status,Is.EqualTo(SubmissionWorkflowStatus.Completed));
  var source=AutomationRemoval.VerifyRetained(context);
  var observations=AutomationFiles.Read<SubmissionEvidenceDocument>(Path.Combine(context.RunDirectory,source.RelativePath));
  Assert.That(observations.Observations.Select(o=>o.RequirementId),Is.EquivalentTo(new[]{"system.removal","ui.placement"}));
  var placement=observations.Observations.Single(o=>o.RequirementId=="ui.placement");
  Assert.That(placement.Execution!.Target,Is.EqualTo("selected.membership"));
  Assert.That(placement.Files.Any(f=>f.RelativePath.EndsWith("ui-before/plan.json",StringComparison.Ordinal)),Is.True);
  await Remove();Assert.That(calls,Is.EqualTo(1));
 }
 [TestCase("failed")][TestCase("unrestored")][TestCase("changed")][TestCase("missing")]
 public async Task PlacementRejectsInvalidOrMissingBaselineWithoutReplayingRemoval(string condition) {
  await ConfigurePlacement();var error=Assert.CatchAsync(async()=>await Remove(placementBaseline:condition));
  Assert.That(error,Is.InstanceOf<InvalidDataException>().Or.InstanceOf<IOException>());
  Assert.That(File.Exists(Path.Combine(context.RunDirectory,"removal-evidence.json")),Is.False);
  Assert.That((await Remove()).Status,Is.EqualTo(SubmissionWorkflowStatus.OutcomeUnknown));Assert.That(calls,Is.EqualTo(1));
 }
 [Test] public async Task PlacementCannotReuseTheRemovalRequirement() {
  await ConfigureRemoval();settings=settings with{Removal=settings.Removal! with{PlacementRequirementId="system.removal"}};
  Assert.ThrowsAsync<InvalidDataException>(async()=>await Remove());Assert.That(calls,Is.Zero);
 }
 [Test] public async Task FinalRemovalUsesDeploymentAndCannotRunTwice() {
  await ConfigureRemoval();var first=await Remove();Assert.That(first.Status,Is.EqualTo(SubmissionWorkflowStatus.Completed));
  Assert.That((await Remove()).Receipt,Is.EqualTo(first.Receipt));Assert.That(calls,Is.EqualTo(1));
  var source=AutomationRemoval.VerifyRetained(context);
  var observations=AutomationFiles.Read<SubmissionEvidenceDocument>(Path.Combine(context.RunDirectory,source.RelativePath));
  Assert.That(observations.Observations.Single().RequirementId,Is.EqualTo("system.removal"));
  Assert.That(observations.Observations.Single().Outcome,Is.EqualTo(SubmissionEvidenceOutcome.Passed));
  File.AppendAllText(Path.Combine(context.RunDirectory,"removal","operation","raw.xml"),"changed");
  Assert.Throws<InvalidDataException>(()=>AutomationRemoval.VerifyRetained(context));
 }
 [Test] public async Task InterruptedRemovalCannotBeReissued() {
  await ConfigureRemoval();Assert.ThrowsAsync<IOException>(async()=>await Remove(interrupt:true));
  Assert.That((await Remove()).Status,Is.EqualTo(SubmissionWorkflowStatus.OutcomeUnknown));Assert.That(calls,Is.EqualTo(1));
 }
 [Test] public async Task RemovalFailureRemainsFailedDuringRecovery() {
  await ConfigureRemoval();Assert.That((await Remove(pass:false)).Status,Is.EqualTo(SubmissionWorkflowStatus.Failed));
  Assert.That((await Remove()).Status,Is.EqualTo(SubmissionWorkflowStatus.Failed));Assert.That(calls,Is.EqualTo(1));
  Assert.Throws<InvalidDataException>(()=>AutomationRemoval.VerifyRetained(context));
 }
 [Test] public async Task NonRemovingBaselineCannotBecomeRemovalEvidence() {
  await ConfigureRemoval();Assert.That((await Remove(baseline:true)).Status,Is.EqualTo(SubmissionWorkflowStatus.OutcomeUnknown));
  Assert.That(File.Exists(Path.Combine(context.RunDirectory,"removal-evidence.json")),Is.False);
 }
 [Test] public async Task MissingOrChangedPrecedingEvidenceStopsRemoval() {
  await ConfigureRemoval();File.AppendAllText(Path.Combine(context.RunDirectory,"post-endurance","installed-app","raw.xml"),"changed");
  Assert.ThrowsAsync<InvalidDataException>(async()=>await Remove());Assert.That(calls,Is.Zero);
 }
 [Test] public async Task PostEnduranceUsesVerifiedDeploymentIdsAndRetainsTheResolvedPlan() {
  ConfigurePostDeployment();
  Task<InstalledDriverTestResult> Inspect(InstalledDriverTestPlan p,NetworkCredential c,string f,CancellationToken t) {
   Assert.That(p.Target.DeviceId,Is.EqualTo(167));
   Assert.That(p.Target.CatalogueId,Is.EqualTo("observed.catalogue.1.0.000.0000"));
   Assert.That(p.Target.Name,Is.EqualTo(settings.PostEnduranceTests!.Target.Name));
   return Run(p,c,f,t);
  }
  var result=await AutomationPostEndurance.Advance(context,settings,false,Inspect,_=>new(),default);
  Assert.That(result.Status,Is.EqualTo(SubmissionWorkflowStatus.Completed));
  Assert.That(settings.PostEnduranceTests!.Target.DeviceId,Is.EqualTo(2),"Input plan must remain immutable");
  Assert.That(AutomationPostEndurance.RetainedFiles(context).Any(f=>f.RelativePath=="post-endurance/target-plan.json"),Is.True);
  await Post(true);Assert.That(calls,Is.EqualTo(1));
  File.AppendAllText(Path.Combine(context.RunDirectory,"post-endurance","target-plan.json")," ");
  Assert.Throws<InvalidDataException>(()=>AutomationPostEndurance.VerifyRetained(context));
 }
 [TestCase("receipt")][TestCase("expected-device")][TestCase("room")][TestCase("name")]
 public void PostDeploymentMismatchStopsBeforeAnyControls(string difference) {
  ConfigurePostDeployment();
  if(difference=="receipt")File.AppendAllText(Path.Combine(context.RunDirectory,"nunit","actual-import.json")," ");
  else if(difference=="expected-device")settings=settings with{NUnit=settings.NUnit with{ActualDriver=settings.NUnit.ActualDriver! with{ExpectedDeviceId=99}}};
  else settings=settings with{PostEnduranceTests=settings.PostEnduranceTests! with{Target=difference=="room"
   ?settings.PostEnduranceTests.Target with{LocationId=9}:settings.PostEnduranceTests.Target with{Name="Another instance"}}};
  Assert.ThrowsAsync<InvalidDataException>(async()=>await Post());Assert.That(calls,Is.Zero);
 }
 [Test] public async Task MissingRetainedPostTargetIsNotReconstructedOnRecovery() {
  ConfigurePostDeployment();await Post();
  string path=Path.Combine(context.RunDirectory,"post-endurance","target-plan.json");File.Delete(path);
  Assert.ThrowsAsync<InvalidDataException>(async()=>await Post(true));
  Assert.That(File.Exists(path),Is.False);Assert.That(calls,Is.EqualTo(1));
 }
 [Test] public async Task PostEnduranceUsesSeparateEvidenceAndRecoveryDoesNotRepeatControls() {
  await Advance();Assert.That(calls,Is.EqualTo(1));
  string initial=File.ReadAllText(Path.Combine(context.RunDirectory,"installed-app-tests.json"));
  ConfigurePostEndurance();
  Assert.That((await Post()).Status,Is.EqualTo(SubmissionWorkflowStatus.Completed));
  Assert.That((await Post(true)).Status,Is.EqualTo(SubmissionWorkflowStatus.Completed));
  Assert.That(calls,Is.EqualTo(2));
  Assert.That(File.ReadAllText(Path.Combine(context.RunDirectory,"installed-app-tests.json")),Is.EqualTo(initial));
  Assert.That(AutomationPostEndurance.RetainedFiles(context).All(f=>f.RelativePath.StartsWith("post-endurance/")),Is.True);
  File.AppendAllText(Path.Combine(context.RunDirectory,"post-endurance","installed-app","raw.xml"),"changed");
  Assert.Throws<InvalidDataException>(()=>AutomationPostEndurance.VerifyRetained(context));
 }
 [TestCase(false)][TestCase(true)] public void PostEnduranceRequiresCompletedUnchangedSegment(bool changed) {
  ConfigurePostEndurance();
  if(changed)File.AppendAllText(Path.Combine(context.RunDirectory,"endurance-result.json"),"changed");
  else context.Checkpoint.CompletedStages.Clear();
  Assert.ThrowsAsync<InvalidDataException>(async()=>await Post());Assert.That(calls,Is.Zero);
 }
 [Test] public void PostEnduranceRejectsAnotherCandidateBeforeCallingRunner() {
  ConfigurePostEndurance();settings=settings with{PostEnduranceTests=settings.PostEnduranceTests! with{PackageSourceCommit=new('f',40)}};
  Assert.ThrowsAsync<InvalidDataException>(async()=>await Post());Assert.That(calls,Is.Zero);
 }
 [Test] public async Task PostEnduranceDoesNotReplayAnInterruptedInvocation() {
  ConfigurePostEndurance();
  Task<InstalledDriverTestResult> Interrupted(InstalledDriverTestPlan p,NetworkCredential c,string f,CancellationToken t) {
   calls++;throw new IOException("synthetic lost runner connection");
  }
  Assert.ThrowsAsync<IOException>(async()=>await AutomationPostEndurance.Advance(context,settings,false,Interrupted,_=>new(),default));
  Assert.That((await Post(true)).Status,Is.EqualTo(SubmissionWorkflowStatus.OutcomeUnknown));
  Assert.That(calls,Is.EqualTo(1));
 }
}
