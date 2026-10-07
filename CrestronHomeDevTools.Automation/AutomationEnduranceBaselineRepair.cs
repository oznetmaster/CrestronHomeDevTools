// Copyright (c) 2026 Neil Colvin. MIT licensed.
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
namespace CrestronHomeDevTools.Automation;

// Corrects only a baseline destination inherited from a previous collection.
// Original samples, baseline, producer binaries and test criteria are immutable.
internal static class AutomationEnduranceBaselineRepair
{
 internal const string FileName="endurance-baseline-repair.json";
 internal sealed record Request(int SchemaVersion,string AttemptId,string OriginalEvidenceSha256,string Reason);
 private sealed record BaselinePin(string Path,string Sha256);
 private sealed record Binding(int SchemaVersion,string InputSha256,Request Request,SubmissionWorkflowReceipt[] Originals,
  BaselinePin OriginalBaseline,SubmissionEnduranceWorkerPlan Worker,DateTimeOffset RecordedUtc);
 private sealed record Sample(DateTimeOffset ObservedUtc,SubmissionEvidenceOutcome Outcome,string Reason,SubmissionEnduranceProbeResult Probe);
 private static string Digest<T>(T value)=>Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value,AutomationFiles.Json)));
 private static void RequireId(string value){if(!Guid.TryParseExact(value,"N",out _))throw new InvalidDataException("A fresh attempt GUID is required.");}
 private static string PreviousDirectory(SubmissionWorkflowStepContext c)=>AutomationEnduranceIdentityRepair.DirectoryName(c);
 private static SubmissionWorkflowReceipt[] OriginalFiles(SubmissionWorkflowStepContext c) {
  string previous=PreviousDirectory(c);
  var names=new List<string>{"endurance-deployment-binding.json",previous+"/monitor.json",previous+"/observations/checkpoint.json",previous+"/observations/sample-000000.json"};
  if(File.Exists(Path.Combine(c.RunDirectory,AutomationEnduranceIdentityRepair.FileName)))names.Add(AutomationEnduranceIdentityRepair.FileName);
  return names.Select(p=>{
   if(!SubmissionEvidence.SafeEvidencePath(c.RunDirectory,p,out var path))throw new InvalidDataException("Original failure is missing.");
   return new SubmissionWorkflowReceipt(p,AutomationFiles.Hash(path));
  }).ToArray();
 }
 private static (JsonObject Node,string Key,string Path) Settings(SubmissionEnduranceWorkerPlan worker) {
  var node=JsonNode.Parse(File.ReadAllBytes(worker.Probe.SettingsFile!))!.AsObject();
  var fields=node.Where(p=>p.Key.Equals("BaselineFile",StringComparison.OrdinalIgnoreCase)).ToArray();
  if(fields.Length!=1||fields[0].Value is not JsonValue value||!value.TryGetValue<string>(out var path)||!Path.IsPathFullyQualified(path))
   throw new InvalidDataException("A single absolute baseline destination is required.");
  AutomationDeploymentEndurance.ValidateSettingsBinding(node,worker.Plan);
  return(node,fields[0].Key,path);
 }
 private static BaselinePin OriginalBaseline(SubmissionEnduranceWorkerPlan worker) {
  string path=Settings(worker).Path;
  if(!SubmissionEvidence.SafeEvidencePath(Path.GetDirectoryName(path)!,Path.GetFileName(path),out var safe)||new FileInfo(safe).Length>65536)
   throw new InvalidDataException("Original baseline is missing, unsafe or oversized.");
  using var document=JsonDocument.Parse(File.ReadAllBytes(path));
  var saved=document.RootElement.EnumerateObject().Single(p=>p.Name.Equals("PlanSha256",StringComparison.OrdinalIgnoreCase)).Value.GetString();
  var json=new JsonSerializerOptions(AutomationFiles.Json){PropertyNamingPolicy=JsonNamingPolicy.CamelCase};
  string current=Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(worker.Plan,json)));
  if(saved is not{Length:64}||saved.Any(c=>!char.IsAsciiHexDigit(c))||saved.Equals(current,StringComparison.OrdinalIgnoreCase))
   throw new InvalidDataException("The retained baseline must demonstrably belong to a different plan.");
  return new(path,AutomationFiles.Hash(path));
 }
 internal static string Inspect(SubmissionWorkflowStepContext c,SubmissionEnduranceWorkerPlan original) {
  string previous=PreviousDirectory(c);
  var status=SubmissionEnduranceMonitor.ReadStatus(Path.Combine(c.RunDirectory,previous),original.Plan,original.Processor);
  if(status.ReservationState!="Released"||status.Checkpoint is not{State:SubmissionEnduranceState.Failed,Samples.Count:1} checkpoint||
   checkpoint.Reason!="functional-check-not-passed"||checkpoint.Samples[0].BootIdentity!="unverified")
   throw new InvalidDataException("Inspect a released first-sample baseline failure before correction.");
  var sample=AutomationFiles.Read<Sample>(Path.Combine(c.RunDirectory,previous,"observations/sample-000000.json"));
  if(sample.Reason!="functional-check-not-passed"||sample.Outcome!=SubmissionEvidenceOutcome.Failed||sample.Probe.Outcome!=SubmissionEvidenceOutcome.Failed||
   sample.Probe.Identity!=original.Plan.Identity||sample.Probe.ProducerId!=original.Plan.ProducerId||sample.Probe.ReservationId!=original.Plan.ReservationId||
   sample.Probe.ProcessorIdentity!=original.Plan.ProcessorIdentity||sample.Probe.InstallationIdentity!=original.Plan.InstallationIdentity||sample.Probe.BootIdentity!="unverified")
   throw new InvalidDataException("Failed sample does not match the frozen producer.");
  using var evidence=JsonDocument.Parse(sample.Probe.Evidence);
  if(evidence.RootElement.GetProperty("phase").GetString()!="lifetime-baseline"||evidence.RootElement.GetProperty("errorType").GetString()!="InvalidDataException")
   throw new InvalidDataException("A functional observation or unknown failure cannot be replaced by a baseline correction.");
  return Digest(new{Files=OriginalFiles(c),Baseline=OriginalBaseline(original)});
 }
 private static byte[] Corrected(SubmissionEnduranceWorkerPlan original,string baseline) {
  var input=Settings(original);input.Node[input.Key]=baseline;
  return JsonSerializer.SerializeToUtf8Bytes(input.Node,AutomationFiles.Json);
 }
 private static Binding Read(SubmissionWorkflowStepContext c) {
  if(!SubmissionEvidence.SafeEvidencePath(c.RunDirectory,FileName,out var path))throw new InvalidDataException("Unsafe baseline correction binding.");
  var b=AutomationFiles.Read<Binding>(path);RequireId(b.Request.AttemptId);
  if(b.SchemaVersion!=1||b.Request.SchemaVersion!=1||b.InputSha256!=c.Checkpoint.InputSha256||string.IsNullOrWhiteSpace(b.Request.Reason))
   throw new InvalidDataException("Baseline correction belongs to another run.");
  return b;
 }
 internal static string DirectoryName(SubmissionWorkflowStepContext c)=>"endurance-baseline-recovery/"+Read(c).Request.AttemptId+"/collection";
 internal static SubmissionEnduranceWorkerPlan Resolve(SubmissionWorkflowStepContext c,SubmissionAutomationSettings settings) {
  var b=Read(c);var original=AutomationEnduranceIdentityRepair.Resolve(c,settings,verifyOnly:true)!;
  if(Digest(OriginalFiles(c))!=Digest(b.Originals)||OriginalBaseline(original)!=b.OriginalBaseline||Inspect(c,original)!=b.Request.OriginalEvidenceSha256)
   throw new InvalidDataException("Original baseline or failure evidence changed.");
  var worker=b.Worker;string root=Path.Combine(c.RunDirectory,"endurance-baseline-recovery",b.Request.AttemptId);
  if(worker.Processor!=original.Processor||Digest(worker.Plan with{ProducerId=original.Plan.ProducerId,ReservationId=original.Plan.ReservationId})!=Digest(original.Plan)||
   worker.Plan.ReservationId==original.Plan.ReservationId||worker.Probe.Executable!=original.Probe.Executable||
   worker.Probe.Directory!=Path.Combine(root,"producer")||worker.Probe.SettingsFile!=Path.Combine(root,"producer","settings.generated.json"))
   throw new InvalidDataException("Baseline correction changed frozen test scope.");
  RequireId(worker.Plan.ReservationId);
  var previousSettings=Path.GetRelativePath(original.Probe.Directory,original.Probe.SettingsFile!).Replace(Path.DirectorySeparatorChar,'/');
  if(Digest(worker.Probe.Files.Where(f=>f.RelativePath!="settings.generated.json").ToArray())!=Digest(original.Probe.Files.Where(f=>f.RelativePath!=previousSettings).ToArray())||
   !File.ReadAllBytes(worker.Probe.SettingsFile).AsSpan().SequenceEqual(Corrected(original,Path.Combine(root,"lifetime.json"))))
   throw new InvalidDataException("Baseline correction changed producer code or other settings.");
  SubmissionEnduranceProcessProbe.Validate(worker.Probe,worker.Plan);return worker;
 }
 internal static void Bind(SubmissionWorkflowStepContext c,SubmissionAutomationSettings settings,Request request) {
  RequireId(request.AttemptId);
  if(request.SchemaVersion!=1||string.IsNullOrWhiteSpace(request.Reason)||request.Reason.Length>2000||
   c.Checkpoint.Stage!=SubmissionWorkflowStage.Endurance||c.Checkpoint.Status!=SubmissionWorkflowStatus.Failed||
   c.Checkpoint.CompletedStages.ContainsKey(SubmissionWorkflowStage.Endurance)||File.Exists(Path.Combine(c.RunDirectory,FileName))||Directory.Exists(Path.Combine(c.RunDirectory,"post-endurance")))
   throw new InvalidDataException("Bind one correction at an inspected stopped endurance boundary.");
  var original=AutomationEnduranceIdentityRepair.Resolve(c,settings,verifyOnly:true)!;
  if(Inspect(c,original)!=request.OriginalEvidenceSha256)throw new InvalidDataException("Inspected baseline failure changed.");
  string root=Path.Combine(c.RunDirectory,"endurance-baseline-recovery",request.AttemptId);
  if(Directory.Exists(root))throw new InvalidDataException("An existing attempt cannot be replayed.");
  var bytes=Corrected(original,Path.Combine(root,"lifetime.json"));
  var previousSettings=Path.GetRelativePath(original.Probe.Directory,original.Probe.SettingsFile!).Replace(Path.DirectorySeparatorChar,'/');
  var binary=original.Probe with{SettingsFile=null,Files=original.Probe.Files.Where(f=>f.RelativePath!=previousSettings).ToArray()};
  var replacement=AutomationProbePreparation.Publish(original with{Probe=binary,Plan=original.Plan with{ReservationId=Guid.NewGuid().ToString("N")}},Path.Combine(root,"producer"),bytes);
  var binding=new Binding(1,c.Checkpoint.InputSha256,request,OriginalFiles(c),OriginalBaseline(original),replacement,DateTimeOffset.UtcNow);
  File.Copy(Path.Combine(c.RunDirectory,"state.json"),Path.Combine(root,"original-state.json"),false);
  AutomationFiles.Write(Path.Combine(c.RunDirectory,FileName),binding);_=Resolve(c,settings);
 }
 internal static int Command(AutomationRequest request,string statePin,string path,string pin) {
  if(AutomationFiles.Hash(path)!=pin)throw new InvalidDataException("Baseline repair request changed.");
  var s=request.Settings;
  return SubmissionWorkflow.WithVerifiedCheckpoint(s.PrivateRoot,s.Release,c=>{
   if(AutomationFiles.Hash(Path.Combine(c.RunDirectory,"state.json"))!=statePin)throw new InvalidDataException("Inspected workflow state changed.");
   Bind(c,s,AutomationFiles.Read<Request>(path));Console.WriteLine("Fresh lifetime baseline bound to an unstarted interval; previous baselines and failures retained.");return 0;
  });
 }
}

internal static class AutomationEnduranceSelection
{
 internal static SubmissionEnduranceWorkerPlan? Resolve(SubmissionWorkflowStepContext c,SubmissionAutomationSettings settings,bool verifyOnly=false)=>
  File.Exists(Path.Combine(c.RunDirectory,AutomationEnduranceBaselineRepair.FileName))?AutomationEnduranceBaselineRepair.Resolve(c,settings):AutomationEnduranceIdentityRepair.Resolve(c,settings,verifyOnly);
 internal static string DirectoryName(SubmissionWorkflowStepContext c)=>File.Exists(Path.Combine(c.RunDirectory,AutomationEnduranceBaselineRepair.FileName))?
  AutomationEnduranceBaselineRepair.DirectoryName(c):AutomationEnduranceIdentityRepair.DirectoryName(c);
 internal static string EvidenceDirectory(SubmissionWorkflowStepContext c)=>DirectoryName(c)+"/observations";
}
