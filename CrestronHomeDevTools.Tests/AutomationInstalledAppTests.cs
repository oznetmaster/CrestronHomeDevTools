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
}
