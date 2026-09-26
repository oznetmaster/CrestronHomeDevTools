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
