// Copyright (c) 2026 Neil Colvin. MIT licensed.
using System.Text.Json;
using CrestronHomeDevTools.Automation;
using NUnit.Framework;
namespace CrestronHomeDevTools.Tests;
public sealed partial class AutomationInstalledAppTests
{
 private async Task<AutomationPostFixtureRepair.Request> PreparePostFixtureRepair() {
  var repair=await FailedTest();var result=await Replace(repair);
  settings=settings with{PostEnduranceTests=settings.InstalledAppTests! with{AndroidTests=settings.InstalledAppTests.AndroidTests with{RequiredTests=["Post.Selected"]}}};
  context=context with{Checkpoint=context.Checkpoint with{SchemaVersion=2,Stage=SubmissionWorkflowStage.Endurance,Status=SubmissionWorkflowStatus.Ready}};
  context.Checkpoint.CompletedStages[SubmissionWorkflowStage.AppTests]=result.Receipt!;
  AutomationFiles.Write(Path.Combine(context.RunDirectory,"automation-binding.json"),new{SettingsSha256=new string('a',64)});
  return new(1,"",repair.AttemptId,"Carry accepted recorder repair into the same fixture's future checks.");
 }
 private void BindPost(AutomationPostFixtureRepair.Request request)=>AutomationPostFixtureRepair.Bind(context,settings,new('a',64),request);
 [Test]public async Task PostFixtureBindingPreservesScopeOriginalFailureAndCompletedMainReceipt() {
  var request=await PreparePostFixtureRepair();var original=JsonSerializer.Serialize(settings);var receipt=File.ReadAllBytes(Path.Combine(context.RunDirectory,"installed-app-tests.json"));
  BindPost(request);BindPost(request);
  var result=AutomationPostFixtureRepair.Resolve(context,settings);
  Assert.That(result.PostEnduranceTests!.AndroidTests.RequiredTests,Is.EqualTo(new[]{"Post.Selected"}));
  Assert.That(result.PostEnduranceTests.Target,Is.EqualTo(settings.PostEnduranceTests!.Target));
  Assert.That(JsonSerializer.Serialize(settings),Is.EqualTo(original));
  Assert.That(File.ReadAllBytes(Path.Combine(context.RunDirectory,"installed-app-tests.json")),Is.EqualTo(receipt));
  Assert.That(AutomationAppStepRecovery.Failure(context.RunDirectory,"installed-app/InstalledDriverTests.json").Passed,Is.False);
  AutomationInstalledApp.VerifyRetained(context.RunDirectory);Assert.That(calls,Is.EqualTo(1));
 }
 [TestCase("endurance")][TestCase("post-endurance")]
 public async Task PostFixtureCannotChangeAfterIntervalOrPostcheckStarts(string directory) {
  var request=await PreparePostFixtureRepair();Directory.CreateDirectory(Path.Combine(context.RunDirectory,directory));Assert.Throws<InvalidDataException>(()=>BindPost(request));
 }
 [Test]public async Task PostFixtureRejectsDifferentAcceptedAttemptAndChangedSources() {
  var request=await PreparePostFixtureRepair();Assert.Throws<InvalidDataException>(()=>BindPost(request with{AttemptId=Guid.NewGuid().ToString("N")}));
  File.AppendAllText(settings.InstalledAppTests!.AndroidTests.Project,"changed");Assert.Throws<InvalidDataException>(()=>BindPost(request));
 }
 [Test]public async Task PostFixtureRejectsUnrelatedOriginalProjectAndChangedProfile() {
  var request=await PreparePostFixtureRepair();var original=settings;
  settings=settings with{PostEnduranceTests=settings.PostEnduranceTests! with{AndroidTests=settings.PostEnduranceTests.AndroidTests with{Project="other"}}};
  Assert.Throws<InvalidDataException>(()=>BindPost(request));settings=original;
  File.AppendAllText(settings.PostEnduranceTests!.AndroidTests.ProfilePath,"changed");Assert.Throws<InvalidDataException>(()=>BindPost(request));
 }
 [Test]public async Task PostFixtureDetectsChangedEvidenceAndFrozenSettingsAfterBinding() {
  var request=await PreparePostFixtureRepair();BindPost(request);
  Assert.Throws<InvalidDataException>(()=>AutomationPostFixtureRepair.Resolve(context,settings with{PostEnduranceTests=settings.PostEnduranceTests! with{TimeoutSeconds=20}}));
  File.AppendAllText(Path.Combine(context.RunDirectory,"installed-app","failure.txt"),"changed");
  Assert.Throws<InvalidDataException>(()=>AutomationPostFixtureRepair.Resolve(context,settings));
 }
 [TestCase(false)][TestCase(true)]public async Task RecoverySelectionResolvesManagedIdsForUnsplitAndSplitMainSteps(bool split) {
  var opened=SubmissionWorkflow.Open(settings.PrivateRoot,settings.Release);
  string run=Path.Combine(settings.PrivateRoot,SubmissionWorkflow.RunKey(settings.Release));
  var state=context.Checkpoint with{Status=SubmissionWorkflowStatus.NeedsInput,InputSha256=opened.InputSha256};
  var c=new SubmissionWorkflowStepContext(run,state);
  settings=settings with{ManagedDevices=new([new("plug","physical-plug","Demo plug","Model",1,[],[])]),
   InstalledAppFixtureSettings=JsonSerializer.SerializeToElement(new{DeviceId="${managed:plug:deviceId}"}),
   InstalledAppTests=settings.InstalledAppTests! with{AndroidTests=settings.InstalledAppTests.AndroidTests with{RequiredTests=["Selected"]}},
   InstalledAppSteps=split?[new(["Selected"])]:null};
  var operation=new AutomationManagedDevices.Operation(settings.NUnit.Host,settings.NUnit.CertificateSha256,settings.NUnit.SshFingerprint,new(2,"Model","1.0.0.0","Installed"),settings.ManagedDevices);
  await AutomationManagedDevices.AdvanceCore(c,operation,_=>new(),(p,credential,folder,token)=>{
   Directory.CreateDirectory(folder);var outcome=new AutomationManagedDevices.Outcome(true,true,[new("plug",new(2,"Model","1.0.0.0","physical-plug","Demo plug","Model",1),31,null)]);
   AutomationFiles.Write(Path.Combine(folder,"result.json"),outcome);return Task.FromResult(outcome);
  },default);
  foreach(var stage in new[]{SubmissionWorkflowStage.ValidateCandidate,SubmissionWorkflowStage.WindowsTests,SubmissionWorkflowStage.ProcessorTests}){
   string name=stage+".json";File.WriteAllText(Path.Combine(run,name),"synthetic retained stage");state.CompletedStages.Add(stage,new(name,AutomationFiles.Hash(Path.Combine(run,name))));
  }
  File.WriteAllText(Path.Combine(run,"state.json"),JsonSerializer.Serialize(state,new JsonSerializerOptions{PropertyNamingPolicy=JsonNamingPolicy.CamelCase,Converters={new System.Text.Json.Serialization.JsonStringEnumConverter()}}));
  AutomationFiles.Write(Path.Combine(run,"target-plan.json"),settings.InstalledAppTests);
  var selected=AutomationAppStepRecovery.Select(new(settings,new('a',64)),"main",0);
  Assert.That(selected.Settings.InstalledAppFixtureSettings!.Value.GetProperty("DeviceId").GetInt32(),Is.EqualTo(31));
  Assert.That(settings.InstalledAppFixtureSettings!.Value.GetProperty("DeviceId").GetString(),Is.EqualTo("${managed:plug:deviceId}"));
  Assert.That(calls,Is.Zero);
 }
}
