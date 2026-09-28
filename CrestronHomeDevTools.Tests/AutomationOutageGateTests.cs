// Copyright (c) 2026 Neil Colvin. MIT licensed.
using System.Net;
using System.Text.Json;
using CrestronHomeDevTools.Automation;
using CrestronHomeNUnit.Workflow;
using NUnit.Framework;

namespace CrestronHomeDevTools.Tests;

public sealed partial class AutomationInstalledAppTests
{
 [TestCase("passed")][TestCase("partial")][TestCase("unretained")][TestCase("unrebased")]
 public async Task SeparateOutageProducerFlowsThroughCombinedReceiptAndPreEnduranceGate(string variant)
 {
  ConfigureSeparateInitial();
  string policyPath=Path.Combine(root,"outage-policy.json"),template=Path.Combine(root,"synthetic-template.pdf");
  File.WriteAllText(template,"synthetic, not a submission form");
  var outageJson=new JsonSerializerOptions(AutomationFiles.Json){PropertyNamingPolicy=JsonNamingPolicy.CamelCase};
  var duration=new SubmissionRequirement("endurance",TimeSpan.FromHours(24),Execution:new("platform","endurance",SubmissionEvidenceOutcome.Passed,null,false,600));
  var policy=new SubmissionEvidencePolicy(1,[new("system.power",TimeSpan.FromSeconds(60),Execution:new("system","outage",SubmissionEvidenceOutcome.Passed,60,true)),
   new("system.network",TimeSpan.FromSeconds(60),Execution:new("system","outage",SubmissionEvidenceOutcome.Passed,60,true)),duration]);
  File.WriteAllBytes(policyPath,JsonSerializer.SerializeToUtf8Bytes(policy,outageJson));
  var identity=new SubmissionEvidenceIdentity(settings.Release.PackageSha256,settings.Release.SourceCommit,AutomationFiles.Hash(policyPath),AutomationFiles.Hash(template));
  const string prefix="installed-app/AndroidUI/system-outage";
  settings=settings with {
   Endurance=new(new(identity,duration,"synthetic","candidate",Guid.NewGuid().ToString("N"),"fixture",TimeSpan.FromMinutes(5),TimeSpan.FromSeconds(30)),new("synthetic","synthetic"),null!),
   Review=new(new(policyPath,identity.PolicySha256),new(template,identity.TemplateSha256),new("unused",new('a',64)),new("unused",new('a',64)),new(root,[]),"Synthetic","Gate test",
    ["pre-endurance/"+prefix+"/observations-power.json","pre-endurance/"+prefix+"/observations-network.json"])};
  Task<InstalledDriverTestResult> Produce(InstalledDriverTestPlan plan,NetworkCredential credential,string folder,CancellationToken token) {
   var result=Run(plan,credential,folder,token);
   if(plan.Host!=settings.PreEnduranceTests!.Host)return result;
   Assert.That(plan.PackageSha256,Is.EqualTo(identity.PackageSha256));
   string outage=Path.Combine(folder,"AndroidUI","system-outage");Directory.CreateDirectory(outage);
   void Write(string name,object data)=>File.WriteAllBytes(Path.Combine(outage,name),JsonSerializer.SerializeToUtf8Bytes(data,outageJson));
   string Hash(string name)=>AutomationFiles.Hash(Path.Combine(outage,name));
   Write("capture.json",new{Synthetic=true});File.Copy(policyPath,Path.Combine(outage,"policy.json"));
   DateTimeOffset start=DateTimeOffset.UtcNow.AddMinutes(-5);
   SubmissionOutageCapture Capture(int seconds)=>new(start.AddSeconds(seconds),start.AddSeconds(seconds),new("capture.json",Hash("capture.json")));
   var record=new SubmissionOutageMeasurementRecord(1,identity,[new("processor",Capture(10),Capture(75)),new("device",Capture(10),Capture(76))],Capture(80),
    [new("control",variant=="partial"?SubmissionEvidenceOutcome.Partial:SubmissionEvidenceOutcome.Passed,Capture(82))],Capture(0),Capture(85),true);
   Write("record.json",record);
   var images=new List<SubmissionEvidenceFile>();
   foreach(string check in new[]{"before","on","off"}) {
    string image=Path.Combine(folder,"AndroidUI","system-outage."+check,"screen.png");Directory.CreateDirectory(Path.GetDirectoryName(image)!);
    File.WriteAllText(image,"Synthetic app capture bytes, not a live UI test.");
    images.Add(new("installed-app/AndroidUI/system-outage."+check+"/screen.png",AutomationFiles.Hash(image)));
   }
   foreach(string kind in new[]{"power","network"}) {
    var measurementPlan=new SubmissionOutageMeasurementPlan(identity,"system."+kind,["processor","device"],["control"],TimeSpan.FromSeconds(60),TimeSpan.FromSeconds(60),
     kind=="power"?SubmissionOutageRecoveryClock.ProgramLoaded:SubmissionOutageRecoveryClock.NetworkRestored,kind=="power"?"processor":null);
    string name="plan-"+kind+".json";Write(name,measurementPlan);
    var imported=SubmissionOutageEvidence.ImportFiles(outage,name,Hash(name),"record.json",Hash("record.json"),"policy.json",DateTimeOffset.UtcNow);
    var observation=variant=="unrebased"?imported.Observations.Observations.Single():AutomationReview.Rebase(imported.Observations.Observations.Single(),prefix);
    observation=observation with {Files=[..observation.Files,..images]};
    if(variant=="unretained") {
     string loose=Path.Combine(Path.GetDirectoryName(folder)!,"loose.json");File.WriteAllText(loose,"outside the installed-app producer inventory");
     observation=observation with {Files=[..observation.Files,new("loose.json",AutomationFiles.Hash(loose))]};
    }
    Write("observations-"+kind+".json",new SubmissionEvidenceDocument(1,[observation]));
   }
   return result;
  }
  var completed=await AdditionalController(Produce).ExecuteAsync(context,default);
  Assert.That(completed.Status,Is.EqualTo(SubmissionWorkflowStatus.Completed));Assert.That(calls,Is.EqualTo(2));
  context.Checkpoint.CompletedStages[SubmissionWorkflowStage.AppTests]=completed.Receipt!;
  foreach(var stage in new[]{SubmissionWorkflowStage.WindowsTests,SubmissionWorkflowStage.ProcessorTests}) {
   string file=stage+".json",path=Path.Combine(context.RunDirectory,file);
   AutomationFiles.Write(path,new{context.Checkpoint.InputSha256,Stage=stage.ToString(),Files=Array.Empty<SubmissionWorkflowReceipt>()});
   context.Checkpoint.CompletedStages[stage]=new(file,AutomationFiles.Hash(path));
  }
  if(variant is "unretained" or "unrebased") {
   Assert.Throws<InvalidDataException>(()=>AutomationPreEndurance.Check(context,settings,default));return;
  }
  var report=AutomationPreEndurance.Check(context,settings,default);
  Assert.That(report.EvidenceChecksPassed,Is.EqualTo(variant=="passed"),JsonSerializer.Serialize(report.Issues));
  Assert.That(Directory.Exists(Path.Combine(context.RunDirectory,"endurance")),Is.False);
  if(variant=="passed") {
   File.AppendAllText(Path.Combine(context.RunDirectory,"pre-endurance","installed-app","AndroidUI","system-outage.on","screen.png"),"changed");
   Assert.Throws<InvalidDataException>(()=>AutomationPreEndurance.Check(context,settings,default));
  }
 }
}
