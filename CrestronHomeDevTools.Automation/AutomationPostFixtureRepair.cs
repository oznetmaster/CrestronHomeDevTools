// Copyright (c) 2026 Neil Colvin. MIT licensed.
using System.Text.Json;
using System.Security.Cryptography;
using CrestronHomeNUnit.Workflow;
namespace CrestronHomeDevTools.Automation;

// Explicitly carry a successful fixture-code repair into an unstarted postcheck.
// Candidate, devices, test selection, profile, timing and original settings stay frozen.
internal static class AutomationPostFixtureRepair
{
 internal const string FileName="post-fixture-repair.json";
 internal sealed record Request(int SchemaVersion,string CompletedStep,string AttemptId,string Reason);
 private sealed record Binding(int SchemaVersion,string InputSha256,string SettingsSha256,string OriginalPlanSha256,
  SubmissionWorkflowReceipt MainTests,Request Request,DateTimeOffset RecordedUtc);
 private static string Digest<T>(T value)=>Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value,AutomationFiles.Json)));
 private static AutomationAppStepRecovery.Request Accepted(SubmissionWorkflowStepContext c,SubmissionAutomationSettings s,Request request) {
  if(request.SchemaVersion!=1||string.IsNullOrWhiteSpace(request.Reason)||request.Reason.Length>2000||
   !(request.CompletedStep==""||System.Text.RegularExpressions.Regex.IsMatch(request.CompletedStep,@"\Ainstalled-app/steps/[0-9]{3}\z")))
   throw new InvalidDataException("Select one completed main app step and explain its fixture repair.");
  AutomationAppStepRecovery.RequireId(request.AttemptId);
  string prefix=request.CompletedStep==""?"":request.CompletedStep+"/";
  string completion=prefix+"installed-app/replacement.json";
  if(!SubmissionEvidence.SafeEvidencePath(c.RunDirectory,completion,out var completed))throw new InvalidDataException("Completed main fixture repair is missing.");
  AutomationInstalledApp.VerifyRetained(c.RunDirectory);
  var accepted=AutomationFiles.Read<AutomationAppStepRecovery.Completion>(completed);
  if(accepted.AttemptId!=request.AttemptId||accepted.CaseRecovery!=null)throw new InvalidDataException("Select the accepted whole-step repair.");
  string relative=prefix+"installed-app/recovery-attempts/"+request.AttemptId+"/attempt.json";
  if(!SubmissionEvidence.SafeEvidencePath(c.RunDirectory,relative,out var attempt))throw new InvalidDataException("Accepted repair binding is missing.");
  using var inventory=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(c.RunDirectory,"installed-app-tests.json")));
  foreach(var pair in new[]{(relative,attempt),(completion,completed)}) {
   if(!inventory.RootElement.GetProperty("Files").EnumerateArray().Any(f=>f.GetProperty("RelativePath").GetString()!.Replace('\\','/')==pair.Item1&&f.GetProperty("Sha256").GetString()==AutomationFiles.Hash(pair.Item2)))
    throw new InvalidDataException("Fixture repair must be covered by the completed main test receipt.");
  }
  using var document=JsonDocument.Parse(File.ReadAllBytes(attempt));
  var repair=document.RootElement.GetProperty("Request").Deserialize<AutomationAppStepRecovery.Request>(AutomationFiles.Json)!;
  if(repair.SchemaVersion!=1||repair.Phase!="main"||repair.AttemptId!=request.AttemptId||s.InstalledAppTests==null||s.PostEnduranceTests==null||
   s.InstalledAppTests.AndroidTests.Project!=s.PostEnduranceTests.AndroidTests.Project||
   !s.InstalledAppTests.SourceRoots.SequenceEqual(s.PostEnduranceTests.SourceRoots)||
   repair.Replacement.PackageSha256!=s.Release.PackageSha256||repair.Replacement.PackageSourceCommit!=s.Release.SourceCommit||
   repair.SourceSha256!=WorkflowEvidence.SourceDigestAsync(repair.Replacement.SourceRoots,default).GetAwaiter().GetResult()||
   repair.ProfileSha256!=AutomationFiles.Hash(s.PostEnduranceTests.AndroidTests.ProfilePath))
   throw new InvalidDataException("Postchecks must use the same unchanged fixture/profile/candidate as the accepted repair.");
  return repair;
 }
 private static SubmissionWorkflowReceipt MainReceipt(SubmissionWorkflowStepContext c) {
  if(!c.Checkpoint.CompletedStages.TryGetValue(SubmissionWorkflowStage.AppTests,out var receipt)||receipt.RelativePath!="installed-app-tests.json"||
   !SubmissionEvidence.SafeEvidencePath(c.RunDirectory,receipt.RelativePath,out var path)||AutomationFiles.Hash(path)!=receipt.Sha256)
   throw new InvalidDataException("A completed main app-test receipt is required.");
  return receipt;
 }
 internal static void Bind(SubmissionWorkflowStepContext c,SubmissionAutomationSettings s,string settingsPin,Request request) {
  if(c.Checkpoint.SchemaVersion!=2||c.Checkpoint.Stage!=SubmissionWorkflowStage.Endurance||c.Checkpoint.Status!=SubmissionWorkflowStatus.Ready||
   c.Checkpoint.CompletedStages.ContainsKey(SubmissionWorkflowStage.Endurance)||s.PostEnduranceTests==null)
   throw new InvalidDataException("Bind the repaired fixture at the ready endurance boundary, before the interval starts.");
  string path=Path.Combine(c.RunDirectory,FileName);
  if(Directory.Exists(Path.Combine(c.RunDirectory,"post-endurance"))||Directory.Exists(Path.Combine(c.RunDirectory,"endurance"))||File.Exists(Path.Combine(c.RunDirectory,"endurance-intent.json")))
   throw new InvalidDataException("The interval or postchecks have already started.");
  var binding=new Binding(1,c.Checkpoint.InputSha256,settingsPin,Digest(s.PostEnduranceTests),MainReceipt(c),request,DateTimeOffset.UtcNow);
  _=Accepted(c,s,request);
  if(File.Exists(path)) {
   var previous=AutomationFiles.Read<Binding>(path);
   if(Digest(previous with{RecordedUtc=binding.RecordedUtc})!=Digest(binding))throw new InvalidDataException("An existing fixture binding cannot be replaced.");
  } else AutomationFiles.Write(path,binding);
 }
 internal static SubmissionAutomationSettings Resolve(SubmissionWorkflowStepContext c,SubmissionAutomationSettings s) {
  string path=Path.Combine(c.RunDirectory,FileName);if(!File.Exists(path))return s;
  if(!SubmissionEvidence.SafeEvidencePath(c.RunDirectory,FileName,out path))throw new InvalidDataException("Unsafe fixture binding.");
  var saved=AutomationFiles.Read<Binding>(path);
  using var original=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(c.RunDirectory,"automation-binding.json")));
  if(saved.SchemaVersion!=1||saved.InputSha256!=c.Checkpoint.InputSha256||saved.SettingsSha256!=original.RootElement.GetProperty("SettingsSha256").GetString()||
   saved.OriginalPlanSha256!=Digest(s.PostEnduranceTests)||saved.MainTests!=MainReceipt(c))throw new InvalidDataException("Post fixture binding changed.");
  var repair=Accepted(c,s,saved.Request);
  var post=s.PostEnduranceTests!;
  return s with{PostEnduranceTests=post with{SourceRoots=repair.Replacement.SourceRoots,AndroidTests=post.AndroidTests with{Project=repair.Replacement.AndroidTests.Project}}};
 }
 internal static int Command(AutomationRequest request,string statePin,string path,string pin) {
  if(AutomationFiles.Hash(path)!=pin)throw new InvalidDataException("Fixture binding request changed.");
  var s=request.Settings;
  return SubmissionWorkflow.WithVerifiedCheckpoint(s.PrivateRoot,s.Release,c=>{
   if(AutomationFiles.Hash(Path.Combine(c.RunDirectory,"state.json"))!=statePin)throw new InvalidDataException("Inspected workflow state changed.");
   Bind(c,s,request.Sha256,AutomationFiles.Read<Request>(path));
   Console.WriteLine("Accepted main fixture repair bound to unstarted postchecks. No tests or documents started.");return 0;
  });
 }
}
