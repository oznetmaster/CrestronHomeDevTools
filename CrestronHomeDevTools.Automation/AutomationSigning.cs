// Copyright (c) 2026 Neil Colvin. MIT licensed.
using System.Text.Json;
namespace CrestronHomeDevTools.Automation;

internal static class AutomationSigning
{
 internal static async Task<SubmissionWorkflowStepResult> Advance(SubmissionWorkflowStepContext c,SubmissionAutomationSettings settings,bool recover,
  CancellationToken token,Func<SubmissionAutomationConsole,string[],string,CancellationToken,Task<int>>? execute=null)
 {
  if(settings.Mode!=SubmissionAutomationMode.Submit)throw new InvalidOperationException("Rehearsal cannot sign.");
  if(settings.Protected is not {} plan || settings.Review is not {} review)return new(SubmissionWorkflowStatus.NeedsInput,ReasonCode:"protected-signing-plan-required");
  AutomationReview.VerifyRetained(c.RunDirectory);
  string reviewHash=AutomationFiles.Hash(Path.Combine(c.RunDirectory,"review","review-receipt.json"));
  // This is a request for review, never an authorization document or an applied signature.
  AutomationFiles.Write(Path.Combine(c.RunDirectory,"signing-request.json"),new{c.Checkpoint.InputSha256,ReviewSha256=reviewHash,
   ReviewDirectory=Path.Combine(c.RunDirectory,"review"),SignatureAuthorized=false});
  string intent=Path.Combine(c.RunDirectory,"signing-intent.json"),output=Path.Combine(c.RunDirectory,"signed-review");
  if(recover && File.Exists(intent)) {
   if(!File.Exists(Path.Combine(output,"COMPLETE")))return new(SubmissionWorkflowStatus.OutcomeUnknown,ReasonCode:"inspect-signing-operation");
   using var previous=JsonDocument.Parse(File.ReadAllBytes(intent));var p=previous.RootElement;
   if(p.GetProperty("OperationId").GetString()!=c.Checkpoint.OperationId || p.GetProperty("InputSha256").GetString()!=c.Checkpoint.InputSha256 || p.GetProperty("ReviewSha256").GetString()!=reviewHash)
    throw new InvalidDataException("Signing intent differs from this review.");
   return Check(c,reviewHash,p.GetProperty("AuthorizationSha256").GetString()!);
  }
  if(Directory.Exists(output))return new(SubmissionWorkflowStatus.OutcomeUnknown,ReasonCode:"inspect-existing-signed-review");
  string? pin=ApprovalPin(plan.SigningApproval,c.RunDirectory);
  if(pin==null)return new(SubmissionWorkflowStatus.Waiting,ReasonCode:"signing-authorization-required");
  string path=Path.Combine(c.RunDirectory,"signing-settings.json");
  AutomationReview.WriteDocument(path,new{schemaVersion=1,reviewDirectory=Path.Combine(c.RunDirectory,"review"),authorization=plan.SigningApproval.DocumentPath,output});
  AutomationFiles.Write(intent,new{c.Checkpoint.OperationId,c.Checkpoint.InputSha256,ReviewSha256=reviewHash,AuthorizationSha256=pin});
  int result=await (execute??AutomationConsole.Run)(review.Console,["submission","prepare-signed-review","--settings",path,
   "--review-sha256",reviewHash,"--authorization-sha256",pin,"--credentials",plan.CredentialBindings],Path.Combine(c.RunDirectory,"signing-process"),token);
  if(result!=0)return new(SubmissionWorkflowStatus.Failed,ReasonCode:"signing-preparation-failed");
  return Check(c,reviewHash,pin);
 }
 internal static string? ApprovalPin(SubmissionAutomationApprovalChannel channel,string runRoot) {
  foreach(string path in new[]{channel.DocumentPath,channel.PinPath}) {
   if(!Path.IsPathFullyQualified(path))throw new InvalidDataException("Approval channels require protected absolute paths.");
   string relative=Path.GetRelativePath(runRoot,path);
   if(!Path.IsPathRooted(relative) && relative!=".." && !relative.StartsWith(".."+Path.DirectorySeparatorChar,StringComparison.Ordinal))
    throw new InvalidDataException("The independent approval channel must be outside producer-writable run storage.");
   if(File.Exists(path) && (File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("Approval channels cannot be redirected.");
  }
  if(!File.Exists(channel.DocumentPath)||!File.Exists(channel.PinPath))return null;
  if(new FileInfo(channel.PinPath).Length>128 || new FileInfo(channel.DocumentPath).Length>1024*1024)throw new InvalidDataException("Oversized approval input.");
  string pin=File.ReadAllText(channel.PinPath).Trim();
  if(pin.Length!=64 || pin.Any(c=>!(char.IsAsciiDigit(c)||c is >= 'a' and <= 'f')) || AutomationFiles.Hash(channel.DocumentPath)!=pin)
   throw new InvalidDataException("Approval differs from its independently retained digest.");
  return pin;
 }
 internal static SubmissionWorkflowStepResult Check(SubmissionWorkflowStepContext c,string reviewHash,string approvalHash) {
  string root=Path.Combine(c.RunDirectory,"signed-review"),receipt=Path.Combine(root,"signed-review-receipt.json");
  string digest=AutomationFiles.Hash(receipt);
  if(File.ReadAllText(Path.Combine(root,"COMPLETE")).Trim()!=digest)throw new InvalidDataException("Signed review did not finish.");
  using var data=JsonDocument.Parse(File.ReadAllBytes(receipt));var r=data.RootElement;
  void Match(string key,string value) {if(r.GetProperty(key).GetString()!=value)throw new InvalidDataException("Signed review identity changed.");}
  Match("state","SignedReviewPrepared");Match("reviewReceiptSha256",reviewHash);Match("authorizationSha256",approvalHash);
  Match("sourceCommit",c.Checkpoint.Release.SourceCommit);Match("packageSha256",c.Checkpoint.Release.PackageSha256);
  if(!r.GetProperty("signatureApplied").GetBoolean() || r.GetProperty("deliveryAuthorized").GetBoolean() || r.GetProperty("deliveryAttempted").GetBoolean())
   throw new InvalidDataException("Signed review has unexpected delivery state.");
  foreach(var (name,hash) in new[]{("signing-report.json","signingReportSha256"),("validation-report.json","validationReportSha256")})Match(hash,AutomationFiles.Hash(Path.Combine(root,name)));
  foreach(var (name,hash) in new[]{("packageFileName","packageSha256"),("signedFormFileName","signedFormSha256")}) {
   string file=r.GetProperty(name).GetString()!;
   if(Path.GetFileName(file)!=file || file.Contains('/') || file.Contains('\\'))throw new InvalidDataException("Invalid signed artifact filename.");
   Match(hash,AutomationFiles.Hash(Path.Combine(root,"delivery",file)));
  }
  return AutomationFiles.Complete(c,"signed-evidence.json",new AutomationReview.Receipt(c.Checkpoint.InputSha256,digest,
   Directory.GetFiles(root,"*",SearchOption.AllDirectories).Order(StringComparer.Ordinal).Select(p=>new SubmissionWorkflowReceipt(Path.GetRelativePath(c.RunDirectory,p),AutomationFiles.Hash(p))).ToArray()));
 }
 internal static void VerifyRetained(string root) {
  foreach(var file in AutomationFiles.Read<AutomationReview.Receipt>(Path.Combine(root,"signed-evidence.json")).Files)
   if(!SubmissionEvidence.SafeEvidencePath(root,file.RelativePath.Replace('\\','/'),out var path)||AutomationFiles.Hash(path)!=file.Sha256)throw new InvalidDataException("Signed packet changed.");
 }
}
