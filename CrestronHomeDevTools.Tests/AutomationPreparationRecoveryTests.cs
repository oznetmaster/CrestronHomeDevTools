// Copyright (c) 2026 Neil Colvin. MIT licensed.
using System.Text.Json;
using CrestronHomeDevTools.Automation;
using CrestronHomeNUnit.Workflow;
using NUnit.Framework;

namespace CrestronHomeDevTools.Tests;
public sealed partial class AutomationInstalledAppTests
{
 private async Task<string> FailedPreparation()
 {
  settings=settings with {InstalledAppTests=settings.InstalledAppTests! with {
   Target=settings.InstalledAppTests.Target with {CatalogueId="chdriver.example.platform.ip.developer.1.0.0.0"}}};
  await AutomationInstalledApp.Advance(context,settings,false,(p,c,f,t)=> {
   Directory.CreateDirectory(f);
   File.WriteAllText(Path.Combine(f,"InstalledDriverTests.json"),JsonSerializer.Serialize(new {State="Failed",DriverUpdateAttempted=false}));
   (string,string)[] phases=[("Processor reservation","Starting"),("Processor reservation","Held"),
    ("Android reservation","Starting"),("Android reservation","Held"),("Before candidate verification","Starting"),
    ("Android reservation","Released"),("Processor reservation","Released")];
   string id=Guid.NewGuid().ToString("N");
   File.WriteAllLines(Path.Combine(f,"Phases.jsonl"),phases.Select(x=>JsonSerializer.Serialize(new {RunId=id,Phase=x.Item1,State=x.Item2,ObservedUtc=DateTimeOffset.UtcNow})));
   return Task.FromResult(new InstalledDriverTestResult(null,false,false,false,true,"Failed before tests"));
  },_=>new(),default);
  return AutomationPreparationRecovery.EvidenceHash(context.RunDirectory);
 }
 private Task<SubmissionWorkflowStepResult> RepairPreparation(string digest,string suffix="7066")=>
  AutomationPreparationRecovery.RepairStep(context,settings,settings.InstalledAppTests!.Target.CatalogueId+"."+suffix,digest,Run,new(),default);

 [Test]
 public async Task PreparationRepairKeepsFailedEvidenceAndDoesNotReplayPassingReplacement()
 {
  string digest=await FailedPreparation();
  string original=File.ReadAllText(Path.Combine(context.RunDirectory,"installed-app","InstalledDriverTests.json"));
  var result=await RepairPreparation(digest);
  Assert.That(result.Status,Is.EqualTo(SubmissionWorkflowStatus.Completed));
  Assert.That(calls,Is.EqualTo(1));
  Assert.That(File.ReadAllText(Path.Combine(context.RunDirectory,"installed-app","InstalledDriverTests.json")),Is.EqualTo(original));
  Assert.That(AutomationPreparationRecovery.EvidenceHash(context.RunDirectory),Is.EqualTo(digest));
  AutomationInstalledApp.VerifyRetained(context.RunDirectory);
  Assert.That((await RepairPreparation(digest)).Receipt,Is.EqualTo(result.Receipt));
  Assert.That(calls,Is.EqualTo(1));
 }

 [TestCase("control")][TestCase("missing-release")][TestCase("different-owner")][TestCase("started-fixture")]
 public async Task PreparationRepairRejectsUncertainOrStartedPhysicalOperation(string change)
 {
  string digest=await FailedPreparation();
  string phases=Path.Combine(context.RunDirectory,"installed-app","Phases.jsonl");
  var lines=File.ReadAllLines(phases).ToList();
  switch(change) {
   case "control":lines.Insert(5,lines[4].Replace("Before candidate verification","Control guard"));break;
   case "missing-release":lines.RemoveAt(lines.Count-1);break;
   case "different-owner":using(var row=JsonDocument.Parse(lines[2]))lines[2]=lines[2].Replace(row.RootElement.GetProperty("RunId").GetString()!,Guid.NewGuid().ToString("N"));break;
   default:Directory.CreateDirectory(Path.Combine(context.RunDirectory,"installed-app","AndroidUI"));break;
  }
  File.WriteAllLines(phases,lines);
  await Assert.ThrowsAsync<InvalidDataException>(async()=>await RepairPreparation(digest));
  Assert.That(calls,Is.Zero);
 }

 [TestCase("different-driver")][TestCase("different-version")][TestCase("source")][TestCase("evidence")]
 public async Task PreparationRepairCannotChangeCandidateOrReviewedInputs(string change)
 {
  string digest=await FailedPreparation(),id=settings.InstalledAppTests!.Target.CatalogueId+".7066";
  if(change=="different-driver")id=id.Replace("example","other");
  if(change=="different-version")id=id.Replace("1.0.0.0","1.1.0.0");
  if(change=="source")File.AppendAllText(settings.InstalledAppTests.AndroidTests.Project,"changed");
  if(change=="evidence")File.AppendAllText(Path.Combine(context.RunDirectory,"installed-app","InstalledDriverTests.json")," ");
  await Assert.ThrowsAsync<InvalidDataException>(async()=>await AutomationPreparationRecovery.RepairStep(context,settings,id,digest,Run,new(),default));
  Assert.That(calls,Is.Zero);
 }

 [Test]
 public async Task InterruptedPreparationRecoveryIsNotReplayed()
 {
  string digest=await FailedPreparation();
  await Assert.ThrowsAsync<IOException>(async()=>await AutomationPreparationRecovery.RepairStep(context,settings,
   settings.InstalledAppTests!.Target.CatalogueId+".7066",digest,(p,c,r,t)=> {calls++;throw new IOException("simulated lost process");},new(),default));
  var result=await RepairPreparation(digest);
  Assert.That(result.Status,Is.EqualTo(SubmissionWorkflowStatus.OutcomeUnknown));
  Assert.That(calls,Is.EqualTo(1));
  Assert.That(File.Exists(Path.Combine(context.RunDirectory,"installed-app-tests.json")),Is.False);
 }
}
