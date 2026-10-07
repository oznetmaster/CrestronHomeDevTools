// Copyright (c) 2026 Neil Colvin. MIT licensed.
using System.Net;
using System.Text.Json;

namespace CrestronHomeDevTools.Automation;

/// <summary>Observe installed placement before endurance, independently of final removal.</summary>
internal static class AutomationPlacement
{
 internal const string ReceiptName="placement-evidence.json";
 internal const string ObservationPath="placement/observations.json";
 private sealed record Intent(string InputSha256,SubmissionWorkflowReceipt AppTests,string RequirementId,
  DriverRemovalWorkflowPlan Plan,DateTimeOffset StartedUtc);
 private sealed record Receipt(string InputSha256,SubmissionWorkflowReceipt[] Files,string OperationPath="placement/operation");
 internal sealed record RecoveryRequest(string InputSha256,SubmissionWorkflowReceipt[] OriginalFiles,SubmissionWorkflowReceipt Review,string Reason,int Attempt=1);
 internal sealed record RecoveryReview(string InputSha256,bool RestorationConfirmed,bool ReservationsReleased,bool OriginalFailurePreserved,
  string? StartingHome=null,string? Owner=null,int PhysicalActions=0,DateTimeOffset? Utc=null,JsonElement? Payload=null);
 private static bool Equal<T>(T a,T b)=>JsonSerializer.Serialize(a,AutomationFiles.Json)==JsonSerializer.Serialize(b,AutomationFiles.Json);
 internal static async Task<SubmissionWorkflowStepResult> Advance(SubmissionWorkflowStepContext c,SubmissionAutomationSettings settings,
  Func<string,NetworkCredential> credentials,CancellationToken token,
  Func<DriverRemovalWorkflowPlan,NetworkCredential,string,CancellationToken,Task<DriverRemovalWorkflowResult>>? run=null)
 {
  _=AutomationRemoval.Validate(settings);
  string id=settings.Removal!.PlacementRequirementId??throw new InvalidDataException("Placement requirement is missing.");
  if(!c.Checkpoint.CompletedStages.TryGetValue(SubmissionWorkflowStage.AppTests,out var app) ||
   !SubmissionEvidence.SafeEvidencePath(c.RunDirectory,app.RelativePath,out var appPath) || AutomationFiles.Hash(appPath)!=app.Sha256)
   throw new InvalidDataException("Placement requires completed unchanged app tests.");
  var plan=AutomationRemoval.Resolve(c,settings);
  string folder=Path.Combine(c.RunDirectory,"placement"),intentPath=Path.Combine(folder,"intent.json");
  if(File.Exists(Path.Combine(c.RunDirectory,ReceiptName))) {
   VerifyRetained(c);
   var saved=AutomationFiles.Read<Intent>(intentPath);
   if(saved.AppTests!=app || saved.RequirementId!=id || !Equal(saved.Plan,plan))
    throw new InvalidDataException("Placement binding differs from its completed operation.");
   return new(SubmissionWorkflowStatus.Completed,new(ReceiptName,AutomationFiles.Hash(Path.Combine(c.RunDirectory,ReceiptName))));
  }
  string operationPath="placement/operation";
  Intent intent;
  if(Directory.Exists(folder)) {
   string requestPath=Path.Combine(folder,"recovery-request.json");
   if(!File.Exists(requestPath))return new(SubmissionWorkflowStatus.OutcomeUnknown,ReasonCode:"inspect-placement-operation-no-replay");
   int attempt=1;
   for(int number=2;number<=16;number++) {
    string next=Path.Combine(folder,$"recovery-{number:000}-request.json");
    if(!File.Exists(next))continue;
    string prior=attempt==1?"recovery":$"recovery-{attempt:000}";
    if(number!=attempt+1 || !Directory.Exists(Path.Combine(folder,prior)))throw new InvalidDataException("Placement recovery history is not sequential.");
    attempt=number;requestPath=next;
   }
   string retryName=attempt==1?"recovery":$"recovery-{attempt:000}";
   string reviewName=attempt==1?"reviewed-restoration.json":$"reviewed-restoration-{attempt:000}.json";
   var request=AutomationFiles.Read<RecoveryRequest>(requestPath);
   var original=Inventory(c.RunDirectory).Where(f=>f.RelativePath!="placement/"+Path.GetFileName(requestPath) && !f.RelativePath.StartsWith("placement/"+retryName+"/",StringComparison.Ordinal)).ToArray();
   if(request.Attempt!=attempt || request.InputSha256!=c.Checkpoint.InputSha256 || string.IsNullOrWhiteSpace(request.Reason) || !request.OriginalFiles.SequenceEqual(original) ||
    request.Review.RelativePath!="placement/"+reviewName || !original.Contains(request.Review))throw new InvalidDataException("Placement recovery must bind unchanged original evidence and a reviewed restoration receipt.");
   var review=AutomationFiles.Read<RecoveryReview>(Path.Combine(c.RunDirectory,request.Review.RelativePath));
   if(review.InputSha256!=c.Checkpoint.InputSha256 || !review.RestorationConfirmed || !review.ReservationsReleased || !review.OriginalFailurePreserved || review.PhysicalActions!=0)
    throw new InvalidDataException("Placement recovery restoration and reservation release were not confirmed.");
   intent=AutomationFiles.Read<Intent>(intentPath);
   if(intent.InputSha256!=c.Checkpoint.InputSha256 || intent.AppTests!=app || intent.RequirementId!=id || !Equal(intent.Plan,plan))throw new InvalidDataException("Placement recovery binding changed.");
   string retry=Path.Combine(folder,retryName);
   if(Directory.Exists(retry))return new(SubmissionWorkflowStatus.OutcomeUnknown,ReasonCode:"inspect-placement-recovery-no-replay");
   Directory.CreateDirectory(retry);
   intent=intent with{StartedUtc=DateTimeOffset.UtcNow};
   AutomationFiles.Write(Path.Combine(retry,"intent.json"),intent);
   operationPath="placement/"+retryName+"/operation";
  } else {
   Directory.CreateDirectory(folder);
   intent=new Intent(c.Checkpoint.InputSha256,app,id,plan,DateTimeOffset.UtcNow);
   AutomationFiles.Write(intentPath,intent);
  }
  var operation=Path.Combine(c.RunDirectory,operationPath);
  var result=await(run??DriverRemovalWorkflow.ObserveBaselineAsync)(plan,credentials(plan.Host),operation,token);
  if(!Equal(result,AutomationFiles.Read<DriverRemovalWorkflowResult>(Path.Combine(operation,"result.json"))))
   throw new InvalidDataException("Placement result differs from retained producer output.");
  if(result.RemovalRequested || !result.CandidateVerified || !result.ReservationsReleased || result.Baseline?.HomeRestored!=true)
   return new(SubmissionWorkflowStatus.OutcomeUnknown,ReasonCode:"inspect-placement-operation-and-restoration");
  if(!result.Passed)return new(SubmissionWorkflowStatus.NeedsInput,ReasonCode:"placement-check-failed-inspect-retained-evidence");
  var requirement=AutomationFiles.Read<SubmissionEvidencePolicy>(settings.Review!.Policy.Path).Requirements.Single(r=>r.Id==id);
  var identity=new SubmissionEvidenceIdentity(settings.Release.PackageSha256,settings.Release.SourceCommit,settings.Review.Policy.Sha256,settings.Review.Template.Sha256);
  var observation=new SubmissionObservation(id,identity,SubmissionEvidenceOutcome.Passed,intent.StartedUtc,DateTimeOffset.UtcNow,
   Inventory(c.RunDirectory).Select(f=>new SubmissionEvidenceFile(f.RelativePath,f.Sha256)).ToArray(),
   "Initial installed-candidate placement: exact API tree and reviewed Home, Room and native Lights membership, with bounded traversal and return Home. No removal or device control; no glyph or timing claim.",new(requirement.Execution!.Target,"combined"));
  AutomationFiles.Write(Path.Combine(c.RunDirectory,ObservationPath),new SubmissionEvidenceDocument(1,[observation]));
  AutomationFiles.Write(Path.Combine(c.RunDirectory,ReceiptName),new Receipt(c.Checkpoint.InputSha256,Inventory(c.RunDirectory),operationPath));
  VerifyRetained(c);
  return new(SubmissionWorkflowStatus.Completed,new(ReceiptName,AutomationFiles.Hash(Path.Combine(c.RunDirectory,ReceiptName))));
 }
 private static SubmissionWorkflowReceipt[] Inventory(string root) {
  var pending=new Stack<DirectoryInfo>();pending.Push(new(Path.Combine(root,"placement")));
  var files=new List<SubmissionWorkflowReceipt>();int count=0;
  while(pending.Count>0) {
   var directory=pending.Pop();
   if((directory.Attributes&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("Unsafe placement evidence link.");
   foreach(var entry in directory.EnumerateFileSystemInfos()) {
    if(++count>4096 || (entry.Attributes&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("Placement evidence exceeds its bound or contains a link.");
    if(entry is DirectoryInfo child)pending.Push(child);
    else files.Add(new(Path.GetRelativePath(root,entry.FullName).Replace('\\','/'),AutomationFiles.Hash(entry.FullName)));
   }
  }
  return files.OrderBy(f=>f.RelativePath,StringComparer.Ordinal).ToArray();
 }
 internal static SubmissionEvidenceFile VerifyRetained(SubmissionWorkflowStepContext c) {
  var receipt=AutomationFiles.Read<Receipt>(Path.Combine(c.RunDirectory,ReceiptName));
  if(receipt.InputSha256!=c.Checkpoint.InputSha256 || !receipt.Files.SequenceEqual(Inventory(c.RunDirectory)) || !receipt.Files.Any(f=>f.RelativePath==ObservationPath))
   throw new InvalidDataException("Placement evidence changed or belongs to another workflow.");
  var intent=AutomationFiles.Read<Intent>(Path.Combine(c.RunDirectory,"placement","intent.json"));
  if(intent.InputSha256!=c.Checkpoint.InputSha256 || !c.Checkpoint.CompletedStages.TryGetValue(SubmissionWorkflowStage.AppTests,out var app) || intent.AppTests!=app ||
   !SubmissionEvidence.SafeEvidencePath(c.RunDirectory,app.RelativePath,out var appPath) || AutomationFiles.Hash(appPath)!=app.Sha256)
   throw new InvalidDataException("Placement belongs to another completed app stage.");
  if(receipt.OperationPath is not ("placement/operation" or "placement/recovery/operation") &&
   !Enumerable.Range(2,15).Any(n=>receipt.OperationPath==$"placement/recovery-{n:000}/operation"))throw new InvalidDataException("Invalid placement operation path.");
  var result=AutomationFiles.Read<DriverRemovalWorkflowResult>(Path.Combine(c.RunDirectory,receipt.OperationPath,"result.json"));
  if(result.RemovalRequested || !result.Passed || result.Baseline?.HomeRestored!=true)
   throw new InvalidDataException("Non-removing placement and restoration were not confirmed.");
  return new(ObservationPath,receipt.Files.Single(f=>f.RelativePath==ObservationPath).Sha256);
 }
}
