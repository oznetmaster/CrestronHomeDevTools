// Copyright (c) 2026 Neil Colvin. MIT licensed.
using System.Diagnostics;
using System.Net;
using System.Text.Json;
using CrestronHomeDevTools.Automation;
using CrestronHomeNUnit.Client;
using CrestronHomeNUnit.Workflow;
using NUnit.Framework;

namespace CrestronHomeDevTools.Tests;
[TestFixture]
public sealed class SubmissionAutomationTests
{
 private string root=null!,source=null!;
 private SubmissionAutomationSettings settings=null!;
 private SubmissionWorkflowStepContext context=null!;
 private int executions;
 private string Git(params string[] args) {
  var p=new ProcessStartInfo("git"){WorkingDirectory=source,UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
  foreach(var arg in args)p.ArgumentList.Add(arg);using var run=Process.Start(p)!;string output=run.StandardOutput.ReadToEnd();string error=run.StandardError.ReadToEnd();run.WaitForExit();
  Assert.That(run.ExitCode,Is.Zero,error);return output.Trim();
 }
 [SetUp]public void Setup() {
  root=Path.Combine(TestContext.CurrentContext.WorkDirectory,"automation-"+Guid.NewGuid().ToString("N"));source=Path.Combine(root,"source");Directory.CreateDirectory(source);
  Git("init","-q");File.WriteAllText(Path.Combine(source,"test.csproj"),"<Project />");Git("add","test.csproj");Git("-c","user.name=Offline test","-c","user.email=test@example.invalid","commit","-qm","fixture");
  var release=new SubmissionWorkflowRelease("example/driver",1,"v1",Git("rev-parse","HEAD"),new('a',64),new('b',64),new('c',64));
  settings=new(1,root,release,source,new(Guid.NewGuid().ToString(),"1.0.0.0",PortalSubmissionKind.NewDriver,"Example","test@example.invalid"),"unused",new WorkflowPlan {
   Host="processor.example",CertificateSha256=new('d',64),SshFingerprint="pin",SourceRoots=[source],LocalTests=[new(Path.Combine(source,"test.csproj"),1)],
   TestPackage=new(Path.Combine(source,"test.csproj"),Path.Combine(source,"test.pkg"),"Test fixture",1),ProcessorSuites=[new("unit",1,[])],RemoveTestInstanceAfterRun=true,RemoveTestPackageAfterSuccessfulRun=true });
  string run=Path.Combine(root,"run");Directory.CreateDirectory(run);executions=0;
  context=new(run,new(1,new('e',64),release,SubmissionWorkflowStage.WindowsTests,SubmissionWorkflowStatus.Running,Guid.NewGuid().ToString("N"),null,[],DateTimeOffset.UtcNow));
 }
 [TearDown]public void Cleanup(){foreach(string file in Directory.GetFiles(root,"*",SearchOption.AllDirectories))File.SetAttributes(file,FileAttributes.Normal);Directory.Delete(root,true);}
 private SubmissionAutomationStages Stages(string cleanup="Passed",string lease="Released")=>new(settings,new('f',64),(_,_,folder,_)=> {
  executions++;Directory.CreateDirectory(folder);
  var result=new ProcessorWorkflowResult([new("Local","Passed",new(2,0,0,true)),new("Processor","Passed",new(2,0,0,true)),new("Remove test instance",cleanup),new("Remove temporary test package","Passed")],false,false);
  File.WriteAllText(Path.Combine(folder,"Workflow.json"),JsonSerializer.Serialize(result));File.WriteAllText(Path.Combine(folder,"Lease.json"),JsonSerializer.Serialize(new{State=lease}));
  File.WriteAllText(Path.Combine(folder,"individual-results.xml"),"synthetic retained raw result");return Task.FromResult(result);
 },_=>new NetworkCredential("synthetic","synthetic"));
 [Test]public async Task WindowsAndProcessorStagesConsumeOneWorkflowAndVerifyCleanup() {
  var adapter=Stages();var windows=await adapter.ExecuteAsync(context,default);Assert.That(windows.Status,Is.EqualTo(SubmissionWorkflowStatus.Completed));
  var recovered=await adapter.RecoverAsync(context,default);Assert.That(recovered.Receipt,Is.EqualTo(windows.Receipt));
  context=context with{Checkpoint=context.Checkpoint with{Stage=SubmissionWorkflowStage.ProcessorTests,CompletedStages=new(){[SubmissionWorkflowStage.WindowsTests]=windows.Receipt!}}};
  Assert.That((await adapter.ExecuteAsync(context,default)).Status,Is.EqualTo(SubmissionWorkflowStatus.Completed));Assert.That(executions,Is.EqualTo(1));
 }
 [TestCase("Deferred","Released")][TestCase("Passed","ReleaseUnconfirmed")]
 public async Task CleanupOrLeaseFailureCannotAdvance(string cleanup,string lease) {
  var result=await Stages(cleanup,lease).ExecuteAsync(context,default);Assert.That(result.Status,Is.EqualTo(SubmissionWorkflowStatus.Failed));
 }
 [Test]public async Task InterruptedNUnitIntentWithoutTerminalResultDoesNotLaunchAgain() {
  AutomationFiles.Write(Path.Combine(context.RunDirectory,"nunit-intent.json"),new{context.Checkpoint.OperationId,context.Checkpoint.InputSha256});
  var result=await Stages().RecoverAsync(context,default);Assert.That(result.Status,Is.EqualTo(SubmissionWorkflowStatus.OutcomeUnknown));Assert.That(executions,Is.Zero);
 }
 [Test]public async Task ChangedRawEvidencePreventsProcessorStageFromReusingPass() {
  var adapter=Stages();var windows=await adapter.ExecuteAsync(context,default);
  File.AppendAllText(Path.Combine(context.RunDirectory,"nunit","individual-results.xml"),"changed");
  context=context with{Checkpoint=context.Checkpoint with{Stage=SubmissionWorkflowStage.ProcessorTests,CompletedStages=new(){[SubmissionWorkflowStage.WindowsTests]=windows.Receipt!}}};
  Assert.ThrowsAsync<InvalidDataException>(async()=>await adapter.ExecuteAsync(context,default));Assert.That(executions,Is.EqualTo(1));
 }
 [Test]public void DirtySourceCannotLaunchNUnit() {
  File.AppendAllText(Path.Combine(source,"test.csproj"),"changed");Assert.ThrowsAsync<InvalidDataException>(async()=>await Stages().ExecuteAsync(context,default));Assert.That(executions,Is.Zero);
 }
 [Test]public async Task GeneratedDebugRevisionCanRecoverButFunctionalSourceChangesCannot() {
  string manifest=Path.Combine(source,"test.json");
  File.WriteAllText(manifest,"{\"DriverVersion\":\"1.0.000.0000\",\"VersionDate\":\"original\",\"Model\":\"fixture\"}");
  Git("add","test.json");Git("-c","user.name=Offline test","-c","user.email=test@example.invalid","commit","-qm","manifest");
  settings=settings with{Release=settings.Release with{SourceCommit=Git("rev-parse","HEAD")}};
  context=context with{Checkpoint=context.Checkpoint with{Release=settings.Release}};
  var adapter=Stages();await adapter.ExecuteAsync(context,default);
  File.WriteAllText(manifest,"{\"DriverVersion\":\"1.0.000.0001\",\"VersionDate\":\"generated\",\"Model\":\"fixture\"}");
  Assert.That((await adapter.RecoverAsync(context,default)).Status,Is.EqualTo(SubmissionWorkflowStatus.Completed));
  File.WriteAllText(manifest,"{\"DriverVersion\":\"1.0.001.0001\",\"VersionDate\":\"generated\",\"Model\":\"fixture\"}");
  Assert.ThrowsAsync<InvalidDataException>(async()=>await adapter.RecoverAsync(context,default));Assert.That(executions,Is.EqualTo(1));
 }
 [Test]public async Task MissingAppBindingIsExplicitAndNeverAPass() {
  context=context with{Checkpoint=context.Checkpoint with{Stage=SubmissionWorkflowStage.AppTests}};
  var result=await Stages().ExecuteAsync(context,default);Assert.That(result.Status,Is.EqualTo(SubmissionWorkflowStatus.NeedsInput));Assert.That(executions,Is.Zero);
 }
 [Test]public async Task CandidateConsumesThePublicIntakesPersistedReceiptFormat() {
  using var package=SubmissionPackageTests.Package();string file=Path.Combine(context.RunDirectory,"candidate.pkg");File.WriteAllBytes(file,package.ToArray());
  var release=settings.Release with{PackageSha256=AutomationFiles.Hash(file)};
  settings=settings with{Release=release,PackageRequirements=new("1286a404-144e-4d77-b96b-1d1272f21c64","1.2.003.0000",PortalSubmissionKind.NewDriver,"ExampleDeveloper","support@example.com")};
  context=context with{Checkpoint=context.Checkpoint with{Release=release,Stage=SubmissionWorkflowStage.ValidateCandidate}};
  var inspection=new SubmissionReleaseInspection(SubmissionReleaseAvailability.Ready,release.Repository,release.ReleaseId,release.Tag,release.SourceCommit,12,"ExampleDeveloper_Test_Example_IP.pkg",release.PackageSha256,new FileInfo(file).Length,"ready");
  File.WriteAllText(Path.Combine(context.RunDirectory,"release.json"),JsonSerializer.Serialize(inspection,new JsonSerializerOptions{WriteIndented=true}));
  Assert.That((await Stages().ExecuteAsync(context,default)).Status,Is.EqualTo(SubmissionWorkflowStatus.Completed));Assert.That(executions,Is.Zero);
 }
 [TestCase("Passed",SubmissionWorkflowStatus.Completed)][TestCase("Failed",SubmissionWorkflowStatus.Failed)]
 public async Task AppStageRequiresThePublicRunnersRestoredAndroidOutcome(string outcome,SubmissionWorkflowStatus expected) {
  settings=settings with{NUnit=settings.NUnit with{AndroidTests=new("tests.csproj","profile.json"),ActualDriver=settings.NUnit.TestPackage,
   ReleaseCandidate=new(settings.Release.PackageSha256,Guid.NewGuid().ToString(),"1.0.0.0",source,settings.Release.SourceCommit)}};
  var adapter=Stages();await adapter.ExecuteAsync(context,default);
  string folder=Path.Combine(context.RunDirectory,"nunit");
  var result=new ProcessorWorkflowResult([new("Local","Passed",new(2,0,0,true)),new("Processor","Passed",new(2,0,0,true)),new("Deployed driver live","Passed",new(1,0,0,true)),new("Remove test instance","Passed"),new("Remove temporary test package","Passed")],true,true);
  File.WriteAllText(Path.Combine(folder,"Workflow.json"),JsonSerializer.Serialize(result));
  File.WriteAllText(Path.Combine(folder,"InstalledDriver.xml"),$"<test-run><test-case fullname=\"Android.Workflow\" result=\"{outcome}\" /></test-run>");
  context=context with{Checkpoint=context.Checkpoint with{Stage=SubmissionWorkflowStage.AppTests}};
  Assert.That((await adapter.ExecuteAsync(context,default)).Status,Is.EqualTo(expected));Assert.That(executions,Is.EqualTo(1));
 }
}
