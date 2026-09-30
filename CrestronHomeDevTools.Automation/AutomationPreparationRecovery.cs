// Copyright (c) 2026 Neil Colvin. MIT licensed.
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using CrestronHomeNUnit.Workflow;

namespace CrestronHomeDevTools.Automation;

// Explicit repair of a preparation failure, never an automatic retry of a physical test.
// The original intent, failure, plan and evidence remain in place. A successful new
// attempt contributes a separate retained producer and a normal completion receipt.
internal static class AutomationPreparationRecovery
{
 internal sealed record Inspection(string StateSha256,string OriginalEvidenceSha256,string Host,int DeviceId,string CatalogueId);
 private sealed record Intent(string OperationId,string InputSha256,string SourceDigest,string ProfileSha256);
 private sealed record PhaseEntry(string RunId,string Phase,string State,DateTimeOffset ObservedUtc);
 private sealed record Repair(string InputSha256,string OriginalEvidenceSha256,InstalledDriverTestPlan OriginalPlan,
  InstalledDriverTestPlan CorrectedPlan,Dictionary<string,string> Tools);
 private const string RepairName="preparation-recovery";

 internal static void RequirePreparationOnly(string stepRoot)
 {
  string result=Path.Combine(stepRoot,"installed-app","InstalledDriverTests.json");
  if(!SubmissionEvidence.SafeEvidencePath(stepRoot,"installed-app/InstalledDriverTests.json",out _) ||
   !SubmissionEvidence.SafeEvidencePath(stepRoot,"installed-app/Phases.jsonl",out var phases))
   throw new InvalidDataException("Original preparation evidence is missing or unsafe.");
  using var document=JsonDocument.Parse(File.ReadAllBytes(result));
  if(document.RootElement.TryGetProperty("State",out var state)) {
   if(state.GetString()!="Failed" || !document.RootElement.TryGetProperty("DriverUpdateAttempted",out var update) || update.ValueKind!=JsonValueKind.False)
    throw new InvalidDataException("Only a recorded failed preparation can be repaired.");
  } else {
   var outcome=JsonSerializer.Deserialize<InstalledDriverTestResult>(document.RootElement.GetRawText());
   if(outcome==null || outcome.Passed || outcome.Tests!=null || !outcome.ReservationsReleased)
    throw new InvalidDataException("A test invocation or unconfirmed release cannot be retried as preparation.");
  }
  if(new FileInfo(phases).Length>65536)throw new InvalidDataException("Unexpected preparation journal size.");
  var records=File.ReadAllLines(phases).Where(s=>!string.IsNullOrWhiteSpace(s))
   .Select(s=>JsonSerializer.Deserialize<PhaseEntry>(s)??throw new InvalidDataException("Missing phase.")).ToArray();
  (string,string)[] expected=[("Processor reservation","Starting"),("Processor reservation","Held"),
   ("Android reservation","Starting"),("Android reservation","Held"),("Before candidate verification","Starting"),
   ("Android reservation","Released"),("Processor reservation","Released")];
  if(!records.Select(p=>(p.Phase,p.State)).SequenceEqual(expected) ||
   records.Any(p=>!Guid.TryParseExact(p.RunId,"N",out _) || p.RunId!=records[0].RunId || p.ObservedUtc==default) ||
   Directory.Exists(Path.Combine(stepRoot,"installed-app","AndroidUI")) ||
   Directory.Exists(Path.Combine(stepRoot,"installed-app","AndroidManagedChildren")))
   throw new InvalidDataException("Preparation must prove no control guard or fixture started and both reservations were released.");
 }

 internal static SubmissionWorkflowReceipt[] OriginalInventory(string stepRoot)=>AutomationInstalledApp.Inventory(stepRoot)
  .Where(f=>!f.RelativePath.Replace('\\','/').StartsWith("installed-app/"+RepairName+"/",StringComparison.Ordinal)).ToArray();
 internal static string EvidenceHash(string stepRoot)=>Convert.ToHexStringLower(SHA256.HashData(
  JsonSerializer.SerializeToUtf8Bytes(OriginalInventory(stepRoot),AutomationFiles.Json)));

 internal static async Task<SubmissionWorkflowStepResult> RepairStep(SubmissionWorkflowStepContext context,
  SubmissionAutomationSettings settings,string catalogueId,string expectedEvidence,
  Func<InstalledDriverTestPlan,NetworkCredential,string,CancellationToken,Task<InstalledDriverTestResult>> run,
  NetworkCredential credential,CancellationToken token)
 {
  string root=context.RunDirectory;
  if(File.Exists(Path.Combine(root,"installed-app-tests.json"))) {
   AutomationInstalledApp.VerifyRetained(root);
   return new(SubmissionWorkflowStatus.Completed,new("installed-app-tests.json",AutomationFiles.Hash(Path.Combine(root,"installed-app-tests.json"))));
  }
  RequirePreparationOnly(root);
  if(EvidenceHash(root)!=expectedEvidence)throw new InvalidDataException("Original preparation evidence changed after inspection.");
  var plan=settings.InstalledAppTests??throw new InvalidDataException("Missing installed plan.");
  if(catalogueId==plan.Target.CatalogueId || !catalogueId.StartsWith("chdriver.",StringComparison.Ordinal) ||
   !plan.Target.CatalogueId.StartsWith("chdriver.",StringComparison.Ordinal) ||
   DriverPayloadInspection.StorageKey(catalogueId,plan.Target.Version)!=DriverPayloadInspection.StorageKey(plan.Target.CatalogueId,plan.Target.Version))
   throw new InvalidDataException("Only the catalogue discriminator for the same driver key and candidate version may be corrected.");
  var intent=AutomationFiles.Read<Intent>(Path.Combine(root,"installed-app-intent.json"));
  if(intent.InputSha256!=context.Checkpoint.InputSha256 || intent.OperationId!=context.Checkpoint.OperationId ||
   intent.SourceDigest!=await WorkflowEvidence.SourceDigestAsync(plan.SourceRoots,token) ||
   intent.ProfileSha256!=AutomationFiles.Hash(plan.AndroidTests.ProfilePath))
   throw new InvalidDataException("Original app operation, source or profile changed.");
  AutomationAppFixture.Check(root,settings,false);
  var corrected=plan with {Target=plan.Target with {CatalogueId=catalogueId}};
  string repair=Path.Combine(root,"installed-app",RepairName);
  bool started=Directory.Exists(repair);
  if(started && !SubmissionEvidence.SafeEvidencePath(repair,"repair.json",out _))
   throw new InvalidDataException("Incomplete recovery intent; inspect without retrying.");
  Directory.CreateDirectory(repair);
  var tools=new[]{typeof(AutomationPreparationRecovery).Assembly,typeof(InstalledDriverTests).Assembly,typeof(DriverPayloadInspection).Assembly}
   .ToDictionary(a=>a.GetName().Name!,a=>AutomationFiles.Hash(a.Location));
  AutomationFiles.Write(Path.Combine(repair,"repair.json"),new Repair(context.Checkpoint.InputSha256,expectedEvidence,plan,corrected,tools));
  AutomationFiles.Write(Path.Combine(repair,"original-evidence.json"),OriginalInventory(root));
  AutomationAppFixture.Check(repair,settings,true);
  string output=Path.Combine(repair,"installed-app"),resultPath=Path.Combine(output,"InstalledDriverTests.json");
  InstalledDriverTestResult? result=null;
  if(!started)result=await run(corrected,credential,output,token);
  else if(SubmissionEvidence.SafeEvidencePath(repair,"installed-app/InstalledDriverTests.json",out _)) {
   using var document=JsonDocument.Parse(File.ReadAllBytes(resultPath));
   if(!document.RootElement.TryGetProperty("State",out _))result=JsonSerializer.Deserialize<InstalledDriverTestResult>(document.RootElement.GetRawText());
  }
  if(result?.Passed!=true)return new(SubmissionWorkflowStatus.OutcomeUnknown,ReasonCode:"inspect-preparation-recovery-no-replay");
  if(!File.Exists(resultPath) || JsonSerializer.Deserialize<InstalledDriverTestResult>(File.ReadAllBytes(resultPath))!=result)
   throw new InvalidDataException("Replacement producer did not retain its complete passing outcome.");
  // Recheck the original immutable attempt and live source before publishing completion.
  if(EvidenceHash(root)!=expectedEvidence || intent.SourceDigest!=await WorkflowEvidence.SourceDigestAsync(plan.SourceRoots,token) ||
   intent.ProfileSha256!=AutomationFiles.Hash(plan.AndroidTests.ProfilePath))throw new InvalidDataException("Inputs changed during recovery.");
  AutomationAppFixture.Check(root,settings,false);
  return AutomationFiles.Complete(context,"installed-app-tests.json",new {context.Checkpoint.InputSha256,Files=AutomationInstalledApp.Inventory(root)});
 }

 internal static (string Run,string Step,SubmissionAutomationSettings Settings,SubmissionWorkflowCheckpoint State) Select(
  AutomationRequest request,int index)
 {
  var settings=request.Settings;
  var state=SubmissionWorkflow.Read(settings.PrivateRoot,settings.Release);
  if(state.Stage!=SubmissionWorkflowStage.AppTests || state.Status is not (SubmissionWorkflowStatus.OutcomeUnknown or SubmissionWorkflowStatus.NeedsInput or SubmissionWorkflowStatus.Failed) ||
   !settings.PreEnduranceSeparateProcessor || settings.PreEnduranceAppSteps is not {} steps || index<0 || index>=steps.Length)
   throw new InvalidDataException("Recovery requires an attention state in the declared separate initial app phase.");
  string root=Path.Combine(settings.PrivateRoot,SubmissionWorkflow.RunKey(settings.Release));
  AutomationInstalledApp.VerifyRetained(root);
  AutomationInitialAdditionalTests.Validate(settings);
  string phase=Path.Combine(root,AutomationInitialAdditionalTests.DirectoryName);
  var original=AutomationFiles.Read<InstalledDriverTestPlan>(Path.Combine(phase,"target-plan.json"));
  if(!JsonSerializer.SerializeToUtf8Bytes(original,AutomationFiles.Json).AsSpan().SequenceEqual(
   JsonSerializer.SerializeToUtf8Bytes(settings.PreEnduranceTests,AutomationFiles.Json)))
   throw new InvalidDataException("Retained separate target differs from frozen settings.");
  string stepRoot=Path.Combine(phase,"installed-app","steps",index.ToString("D3",System.Globalization.CultureInfo.InvariantCulture));
  for(int i=0;i<index;i++)AutomationInstalledApp.VerifyRetained(Path.Combine(phase,"installed-app","steps",i.ToString("D3",System.Globalization.CultureInfo.InvariantCulture)));
  var step=steps[index];
  var child=AutomationInitialAdditionalTests.Settings(settings) with {InstalledAppSteps=null,InstalledAppTests=original with {
   AndroidTests=original.AndroidTests with {RequiredTests=step.Tests},
   OperatorReadiness=step.PrepareBeforeReadiness?AutomationAppSteps.Prepared(settings.OperatorInbox!,phase,index,step):null}};
  AutomationInstalledApp.Validate(child);
  return(root,stepRoot,child,state);
 }

 internal static async Task<int> Command(AutomationRequest request,int index,string? catalogue,string? stateHash,string? evidenceHash,CancellationToken token)
 {
  var selected=Select(request,index);
  // Same lock as the worker. Holding it prevents a concurrent retry or stage advance.
  using(var gate=new FileStream(Path.Combine(selected.Run,"run.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None)) {
   string actual=AutomationFiles.Hash(Path.Combine(selected.Run,"state.json"));
   RequirePreparationOnly(selected.Step);
   var inspection=new Inspection(actual,EvidenceHash(selected.Step),selected.Settings.InstalledAppTests!.Host,
    selected.Settings.InstalledAppTests.Target.DeviceId,selected.Settings.InstalledAppTests.Target.CatalogueId);
   if(catalogue==null){Console.WriteLine(JsonSerializer.Serialize(inspection,AutomationFiles.Json));return 0;}
   if(stateHash!=actual || evidenceHash!=inspection.OriginalEvidenceSha256)throw new InvalidDataException("Inspect and pin the current attention state and original evidence first.");
   if(!OperatingSystem.IsWindows())throw new PlatformNotSupportedException();
   var plan=selected.Settings.InstalledAppTests;
   var saved=DevToolsCredentialBindings.Read(selected.Settings.CredentialBindings).Resolve(DevToolsCredentialPurpose.Processor,plan.Host);
   SubmissionAutomationStages.VerifyProcessorPins(request.Settings,plan.Host,saved.CertificateSha256,saved.SshFingerprint);
   var result=await RepairStep(new(selected.Step,selected.State),selected.Settings,catalogue,evidenceHash,
    async(p,c,r,t)=>{
     await AutomationDriverReadiness.Check(p.Host,p.CertificateSha256,c,new(p.Target.DeviceId,p.Target.Model,p.Target.Version,"Existing"),r+"-readiness",t);
     await AutomationAndroidReadiness.Check(p.AndroidTests.ProfilePath,Path.Combine(r+"-readiness","android"),t);
     return await InstalledDriverTests.RunAsync(p,c,r,t);
    },new(saved.UserName,saved.Password),token);
   Console.WriteLine(JsonSerializer.Serialize(result,AutomationFiles.Json));
   if(result.Status!=SubmissionWorkflowStatus.Completed)return 3;
  }
  // Request recovery through the ordinary state API; never rewrite a checkpoint or mark its stage passed.
  SubmissionWorkflow.RequestRecovery(request.Settings.PrivateRoot,request.Settings.Release,stateHash!);
  return 0;
 }
}
