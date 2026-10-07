// Copyright (c) 2026 Neil Colvin. MIT licensed.
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
namespace CrestronHomeDevTools.Automation;

// A reviewed correction of stale producer policy metadata, never a replay or
// relabelling of functional evidence. The original failed collection stays intact.
internal static class AutomationEnduranceIdentityRepair
{
 internal const string FileName="endurance-identity-repair.json";
 internal sealed record Request(int SchemaVersion,string AttemptId,string OriginalEvidenceSha256,string Reason);
 private sealed record Binding(int SchemaVersion,string InputSha256,Request Request,SubmissionWorkflowReceipt[] Originals,
  SubmissionEnduranceWorkerPlan Worker,DateTimeOffset RecordedUtc);
 private sealed record Sample(DateTimeOffset ObservedUtc,SubmissionEvidenceOutcome Outcome,string Reason,SubmissionEnduranceProbeResult Probe);
 private static string Digest<T>(T value)=>Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value,AutomationFiles.Json)));
 private static void RequireId(string value){if(!Guid.TryParseExact(value,"N",out _))throw new InvalidDataException("A fresh attempt GUID is required.");}
 private static SubmissionWorkflowReceipt[] OriginalFiles(SubmissionWorkflowStepContext c) => new[]{"endurance-deployment-binding.json","endurance/monitor.json","endurance/observations/checkpoint.json","endurance/observations/sample-000000.json"}.Select(p=>{
  if(!SubmissionEvidence.SafeEvidencePath(c.RunDirectory,p,out var path))throw new InvalidDataException("Original endurance failure is missing.");
  return new SubmissionWorkflowReceipt(p,AutomationFiles.Hash(path));
 }).ToArray();
 internal static string Inspect(SubmissionWorkflowStepContext c,SubmissionEnduranceWorkerPlan original) {
  var status=SubmissionEnduranceMonitor.ReadStatus(Path.Combine(c.RunDirectory,"endurance"),original.Plan,original.Processor);
  if(status.ReservationState!="Released"||status.Checkpoint is not{State:SubmissionEnduranceState.Failed,Samples.Count:1} checkpoint||
   checkpoint.Reason!="functional-check-not-passed"||checkpoint.Samples[0].BootIdentity!="unverified")
   throw new InvalidDataException("Only a released, first-sample configuration failure can be corrected here.");
  var sample=AutomationFiles.Read<Sample>(Path.Combine(c.RunDirectory,"endurance/observations/sample-000000.json"));
  if(sample.Outcome!=SubmissionEvidenceOutcome.Failed||sample.Probe.Outcome!=SubmissionEvidenceOutcome.Failed||sample.Probe.Identity!=original.Plan.Identity||
   sample.Reason!="functional-check-not-passed"||sample.Probe.ProcessorIdentity!=original.Plan.ProcessorIdentity||sample.Probe.InstallationIdentity!=original.Plan.InstallationIdentity||
   sample.Probe.ProducerId!=original.Plan.ProducerId||sample.Probe.ReservationId!=original.Plan.ReservationId||sample.Probe.BootIdentity!="unverified")
   throw new InvalidDataException("Original failed sample does not match its producer.");
  using var evidence=JsonDocument.Parse(sample.Probe.Evidence);
  if(evidence.RootElement.GetProperty("phase").GetString()!="validate-plan"||evidence.RootElement.GetProperty("errorType").GetString()!="InvalidDataException")
   throw new InvalidDataException("This repair cannot replace a device observation or unknown failure.");
  return Digest(OriginalFiles(c));
 }
 private static byte[] Corrected(SubmissionEnduranceWorkerPlan original) {
  var node=JsonNode.Parse(File.ReadAllBytes(original.Probe.SettingsFile!))!.AsObject();
  var fields=node.Where(p=>p.Key.Equals("Identity",StringComparison.OrdinalIgnoreCase)).ToArray();
  if(fields.Length!=1||fields[0].Value==null)throw new InvalidDataException("Producer identity is missing or ambiguous.");
  var identity=fields[0].Value!.Deserialize<SubmissionEvidenceIdentity>(new JsonSerializerOptions(AutomationFiles.Json){PropertyNameCaseInsensitive=true})!;
  if(identity==original.Plan.Identity||(identity with{PolicySha256=original.Plan.Identity.PolicySha256})!=original.Plan.Identity)
   throw new InvalidDataException("Only stale policy metadata can be corrected; candidate and template must already match.");
  node[fields[0].Key]=JsonSerializer.SerializeToNode(original.Plan.Identity,AutomationFiles.Json);
  AutomationDeploymentEndurance.ValidateSettingsBinding(node,original.Plan);
  return JsonSerializer.SerializeToUtf8Bytes(node,AutomationFiles.Json);
 }
 internal static SubmissionEnduranceWorkerPlan? Resolve(SubmissionWorkflowStepContext c,SubmissionAutomationSettings settings,bool verifyOnly=false) {
  string path=Path.Combine(c.RunDirectory,FileName);
  if(!File.Exists(path))return AutomationDeploymentEndurance.Resolve(c,settings,verifyOnly);
  var binding=Read(c);
  var original=AutomationDeploymentEndurance.Resolve(c,settings,verifyOnly:true,inspectStaleIdentity:true)!;
  if(Digest(OriginalFiles(c))!=Digest(binding.Originals)||Inspect(c,original)!=binding.Request.OriginalEvidenceSha256)
   throw new InvalidDataException("Original failed evidence changed after identity correction.");
  var worker=binding.Worker;
  if(worker.Processor!=original.Processor||Digest(worker.Plan with{ProducerId=original.Plan.ProducerId,ReservationId=original.Plan.ReservationId})!=Digest(original.Plan)||
   worker.Plan.ReservationId==original.Plan.ReservationId||worker.Probe.Executable!=original.Probe.Executable||
   worker.Probe.Directory!=Path.Combine(c.RunDirectory,"endurance-recovery",binding.Request.AttemptId,"producer")||
   worker.Probe.SettingsFile!=Path.Combine(worker.Probe.Directory,"settings.generated.json"))
   throw new InvalidDataException("Identity correction changed the frozen test scope.");
  RequireId(worker.Plan.ReservationId);
  var oldSettings=Path.GetRelativePath(original.Probe.Directory,original.Probe.SettingsFile!).Replace(Path.DirectorySeparatorChar,'/');
  if(Digest(worker.Probe.Files.Where(f=>f.RelativePath!="settings.generated.json").ToArray())!=Digest(original.Probe.Files.Where(f=>f.RelativePath!=oldSettings).ToArray())||
   !File.ReadAllBytes(worker.Probe.SettingsFile).AsSpan().SequenceEqual(Corrected(original)))
   throw new InvalidDataException("Identity correction changed producer code or other settings.");
  SubmissionEnduranceProcessProbe.Validate(worker.Probe,worker.Plan);
  return worker;
 }
 private static Binding Read(SubmissionWorkflowStepContext c) {
  if(!SubmissionEvidence.SafeEvidencePath(c.RunDirectory,FileName,out var path))throw new InvalidDataException("Unsafe identity repair binding.");
  var b=AutomationFiles.Read<Binding>(path);RequireId(b.Request.AttemptId);
  if(b.SchemaVersion!=1||b.Request.SchemaVersion!=1||b.InputSha256!=c.Checkpoint.InputSha256||string.IsNullOrWhiteSpace(b.Request.Reason))
   throw new InvalidDataException("Identity repair belongs to another run.");
  return b;
 }
 internal static string DirectoryName(SubmissionWorkflowStepContext c)=>File.Exists(Path.Combine(c.RunDirectory,FileName))?"endurance-recovery/"+Read(c).Request.AttemptId+"/collection":"endurance";
 internal static string EvidenceDirectory(SubmissionWorkflowStepContext c)=>DirectoryName(c)+"/observations";
 internal static void Bind(SubmissionWorkflowStepContext c,SubmissionAutomationSettings settings,Request request) {
  RequireId(request.AttemptId);
  if(request.SchemaVersion!=1||string.IsNullOrWhiteSpace(request.Reason)||request.Reason.Length>2000||
   c.Checkpoint.Stage!=SubmissionWorkflowStage.Endurance||c.Checkpoint.Status!=SubmissionWorkflowStatus.Failed||
   c.Checkpoint.CompletedStages.ContainsKey(SubmissionWorkflowStage.Endurance)||File.Exists(Path.Combine(c.RunDirectory,FileName))||
   Directory.Exists(Path.Combine(c.RunDirectory,"post-endurance")))throw new InvalidDataException("Inspect a stopped first-sample failure before binding one correction.");
  var original=AutomationDeploymentEndurance.Resolve(c,settings,verifyOnly:true,inspectStaleIdentity:true)!;
  if(Inspect(c,original)!=request.OriginalEvidenceSha256)throw new InvalidDataException("Inspected failure changed.");
  var bytes=Corrected(original); // Validate before creating an attempt directory.
  string root=Path.Combine(c.RunDirectory,"endurance-recovery",request.AttemptId);
  if(Directory.Exists(root))throw new InvalidDataException("An existing attempt cannot be replayed.");
  var oldSettings=Path.GetRelativePath(original.Probe.Directory,original.Probe.SettingsFile!).Replace(Path.DirectorySeparatorChar,'/');
  var binary=original.Probe with{SettingsFile=null,Files=original.Probe.Files.Where(f=>f.RelativePath!=oldSettings).ToArray()};
  var replacement=AutomationProbePreparation.Publish(original with{Probe=binary,Plan=original.Plan with{ReservationId=Guid.NewGuid().ToString("N")}},Path.Combine(root,"producer"),bytes);
  var binding=new Binding(1,c.Checkpoint.InputSha256,request,OriginalFiles(c),replacement,DateTimeOffset.UtcNow);
  File.Copy(Path.Combine(c.RunDirectory,"state.json"),Path.Combine(root,"original-state.json"),false);
  AutomationFiles.Write(Path.Combine(c.RunDirectory,FileName),binding);
  _=Resolve(c,settings);
 }
 internal static int Command(AutomationRequest request,string statePin,string path,string pin) {
  if(AutomationFiles.Hash(path)!=pin)throw new InvalidDataException("Identity repair request changed.");
  var s=request.Settings;
  return SubmissionWorkflow.WithVerifiedCheckpoint(s.PrivateRoot,s.Release,c=>{
   if(AutomationFiles.Hash(Path.Combine(c.RunDirectory,"state.json"))!=statePin)throw new InvalidDataException("Inspected workflow state changed.");
   Bind(c,s,AutomationFiles.Read<Request>(path));Console.WriteLine("Corrected producer bound to a fresh, unstarted interval; original failure retained.");return 0;
  });
 }
}
