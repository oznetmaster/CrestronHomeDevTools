// Copyright (c) 2026 Neil Colvin. MIT licensed.
using System.Security.Cryptography;
using System.Text.Json;
namespace CrestronHomeDevTools.Automation;

// Document tooling is independent of the already-frozen release/test inputs.
// Only explicit binding at the verified phase-three boundary may select another bundle.
internal static class AutomationReviewTooling
{
 internal const string FileName="document-tooling.json";
 internal sealed record Request(int SchemaVersion,SubmissionAutomationConsole Console,string Reason);
 internal sealed record Binding(int SchemaVersion,string InputSha256,string SettingsSha256,string OriginalConsoleSha256,
  SubmissionWorkflowReceipt FinalTests,Request Request,DateTimeOffset RecordedUtc);
 internal static string Digest(SubmissionAutomationConsole console)=>Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(console,AutomationFiles.Json)));
 internal static void Bind(SubmissionWorkflowStepContext c,SubmissionAutomationSettings settings,string settingsPin,Request request,
  Action<SubmissionAutomationConsole>? verify=null) {
  if(c.Checkpoint.SchemaVersion!=2||c.Checkpoint.Stage!=SubmissionWorkflowStage.PrepareReview||c.Checkpoint.Status!=SubmissionWorkflowStatus.Ready||
   request.SchemaVersion!=1||string.IsNullOrWhiteSpace(request.Reason)||request.Reason.Length>2000||settings.Review==null)
   throw new InvalidDataException("Bind document tools only at the ready, completed-test boundary with an explicit explanation.");
  AutomationFinalTests.VerifyRetained(c,settings,settingsPin);
  var binding=new Binding(1,c.Checkpoint.InputSha256,settingsPin,Digest(settings.Review.Console),c.Checkpoint.CompletedStages[SubmissionWorkflowStage.FinalizeTests],request,DateTimeOffset.UtcNow);
  string path=Path.Combine(c.RunDirectory,FileName);
  if(File.Exists(path)) {
   var previous=AutomationFiles.Read<Binding>(path);
   if(JsonSerializer.Serialize(previous with{RecordedUtc=binding.RecordedUtc},AutomationFiles.Json)!=JsonSerializer.Serialize(binding,AutomationFiles.Json))
    throw new InvalidDataException("A different document-tool binding already exists; it cannot be replaced.");
   _=Resolve(c,settings,settingsPin,verify);return;
  }
  foreach(string entry in Directory.EnumerateFileSystemEntries(c.RunDirectory)) {
   string name=Path.GetFileName(entry);
   if(new[]{"review","sign","deliver","retain"}.Any(prefix=>name.StartsWith(prefix,StringComparison.OrdinalIgnoreCase)))
    throw new InvalidDataException("Document activity already exists; preserve its original tool binding.");
  }
  (verify??AutomationConsole.Verify)(request.Console);
  AutomationFiles.Write(path,binding);
 }
 internal static SubmissionAutomationSettings Resolve(SubmissionWorkflowStepContext c,SubmissionAutomationSettings settings,string? settingsPin=null,
  Action<SubmissionAutomationConsole>? verify=null) {
  string path=Path.Combine(c.RunDirectory,FileName);if(!File.Exists(path))return settings;
  var saved=AutomationFiles.Read<Binding>(path);
  settingsPin??=AutomationFiles.Read<JsonElement>(Path.Combine(c.RunDirectory,"automation-binding.json")).GetProperty("SettingsSha256").GetString();
  if(saved.SchemaVersion!=1||saved.InputSha256!=c.Checkpoint.InputSha256||settings.Review==null||saved.OriginalConsoleSha256!=Digest(settings.Review.Console)||
   settingsPin!=null&&saved.SettingsSha256!=settingsPin||saved.Request.SchemaVersion!=1||string.IsNullOrWhiteSpace(saved.Request.Reason)||
   !c.Checkpoint.CompletedStages.TryGetValue(SubmissionWorkflowStage.FinalizeTests,out var finalized)||finalized!=saved.FinalTests||
   !SubmissionEvidence.SafeEvidencePath(c.RunDirectory,finalized.RelativePath,out var full)||AutomationFiles.Hash(full)!=finalized.Sha256)
   throw new InvalidDataException("Document tooling no longer binds the completed test boundary and frozen original settings.");
  (verify??AutomationConsole.Verify)(saved.Request.Console);
  return settings with{Review=settings.Review with{Console=saved.Request.Console}};
 }
 internal static int Command(AutomationRequest request,string statePin,string toolPlan,string toolPin) {
  if(AutomationFiles.Hash(toolPlan)!=toolPin)throw new InvalidDataException("Document tool request changed.");
  var s=request.Settings;string root=Path.Combine(s.PrivateRoot,SubmissionWorkflow.RunKey(s.Release));
  return SubmissionWorkflow.WithVerifiedCheckpoint(s.PrivateRoot,s.Release,c=> {
  if(AutomationFiles.Hash(Path.Combine(root,"state.json"))!=statePin)throw new InvalidDataException("Inspected workflow state changed.");
  Bind(c,s,request.Sha256,AutomationFiles.Read<Request>(toolPlan));
  Console.WriteLine("Document tools bound independently; test settings and completed results are unchanged. No document, signature or delivery operation started.");return 0;
  });
 }
}
