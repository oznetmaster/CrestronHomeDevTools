// Copyright (c) 2026 Neil Colvin. MIT licensed.
using System.Net;
using System.Text.Json;

namespace CrestronHomeDevTools.Automation;

internal static class AutomationDelivery
{
 internal static SubmissionDeliveryEnvironment EnvironmentFor(SubmissionAutomationMode mode)=>mode switch {
  SubmissionAutomationMode.Submit=>SubmissionDeliveryEnvironment.Production,
  SubmissionAutomationMode.Rehearsal=>SubmissionDeliveryEnvironment.Rehearsal,
  _=>throw new InvalidDataException("Unknown submission mode.")};
 internal static void ValidateMode(SubmissionAutomationSettings settings) {
  var expected=EnvironmentFor(settings.Mode);
  if(settings.Protected is not {} plan)return;
  if(plan.Environment!=expected)throw new InvalidDataException("Protected authority belongs to another delivery environment.");
  if(plan.Delivery is not {} delivery)return;
  if(expected==SubmissionDeliveryEnvironment.Production) {
   if(delivery.RehearsalRecipient!=null)throw new InvalidDataException("Production cannot consume rehearsal recipient settings.");
  } else {
   string? recipient=delivery.RehearsalRecipient;
   if(recipient==null || recipient.Length>254 || recipient.Any(char.IsControl) ||
    !System.Net.Mail.MailAddress.TryCreate(recipient,out var parsed) || parsed.Address!=recipient ||
    recipient.EndsWith("@crestron.com",StringComparison.OrdinalIgnoreCase) || recipient.EndsWith(".crestron.com",StringComparison.OrdinalIgnoreCase))
    throw new InvalidDataException("Configure an explicit rehearsal test mailbox outside Crestron.");
  }
 }
 internal sealed record Operation(SubmissionDeliveryPlan? Complete,SubmissionReviewDeliveryPlan? Qualified,SubmissionBundledRevalidationSettings? Revalidation);
 internal static async Task<SubmissionWorkflowStepResult> Advance(SubmissionWorkflowStepContext c,SubmissionAutomationSettings settings,bool recover,CancellationToken token,
  Func<SubmissionAutomationConsole,string[],string,CancellationToken,Task<int>>? documentCommand=null,
  Func<Operation,CancellationToken,Task<SubmissionDeliveryReceipt>>? dispatch=null)
 {
  ValidateMode(settings);
  if(settings.Protected is not {Delivery:{} delivery} protection || settings.Review is not {} review)
   return new(SubmissionWorkflowStatus.NeedsInput,ReasonCode:"protected-delivery-plan-required");
  bool rehearsal=settings.Mode==SubmissionAutomationMode.Rehearsal;
  var environment=EnvironmentFor(settings.Mode);
  string recipient=rehearsal?delivery.RehearsalRecipient!:"drivers@crestron.com";
  AutomationSigning.VerifyRetained(c.RunDirectory);AutomationReview.VerifyRetained(c.RunDirectory);
  string P(string name)=>Path.Combine(c.RunDirectory,name);
  string signedHash=AutomationFiles.Hash(P("signed-review/signed-review-receipt.json"));
  using var signed=JsonDocument.Parse(File.ReadAllBytes(P("signed-review/signed-review-receipt.json")));var r=signed.RootElement;
  string selectedReview=AutomationReviewSelection.Resolve(c,settings,token);
  if(r.GetProperty("reviewReceiptSha256").GetString()!=AutomationFiles.Hash(Path.Combine(selectedReview,"review-receipt.json")))
   throw new InvalidDataException("The selected review differs from the signed packet.");
  string Text(string key)=>r.GetProperty(key).GetString()!;
  bool qualified=r.TryGetProperty("reviewMode",out var mode)&&mode.GetString()=="DeclaredGaps";
  if(!qualified && (delivery.Correspondence!=null || delivery.GapSummary!=null))throw new InvalidDataException("Custom correspondence and gap summaries apply only to the declared-gap route; do not silently ignore them.");
  SubmissionReviewDeliveryPlan Qualified(string approval)=>new(Text("candidateSha256"),signedHash,approval,Text("packageSha256"),Text("signedFormSha256"),
   Text("packageFileName"),Text("signedFormFileName"),delivery.Sender,recipient,SubmissionReviewMode.DeclaredGaps,
   Enum.Parse<SubmissionVerificationStatus>(Text("verificationStatus")),SubmissionReviewAttachmentKind.SignedSelfTest,Text("declarationsSha256"),delivery.GapSummary,null)
   {CorrespondenceOverride=delivery.Correspondence,Environment=environment,SendRehearsalEmail=rehearsal};
  if(qualified)AutomationReview.WriteDocument(P("delivery-request.json"),SubmissionReviewApproval.Preview(Qualified(new('0',64))));
  else {
   var request=new Dictionary<string,object?>{{"schemaVersion",1},{"signedReviewSha256",signedHash},{"candidateSha256",Text("candidateSha256")},
    {"packageSha256",Text("packageSha256")},{"signedFormSha256",Text("signedFormSha256")},{"sender",delivery.Sender},{"recipient",recipient},
    {"subject",rehearsal?"[REHEARSAL] Driver Submission Package":"Driver Submission Package"},{"deliveryAuthorized",false}};
   if(rehearsal){request["environment"]="Rehearsal";request["sendRehearsalEmail"]=true;}
   AutomationReview.WriteDocument(P("delivery-request.json"),request);
  }

  string operationPath=P("delivery-operation.json"),journal=P("delivery-journal");
  foreach(string folder in new[]{journal,P("delivery-attempts"),P("upload-receipts"),P("mail-receipts")})Directory.CreateDirectory(folder);
  Operation operation;
  if(File.Exists(operationPath)) {
   operation=AutomationFiles.Read<Operation>(operationPath);
   if(operation.Complete is {} p) {
    if(p.PackageSha256!=settings.Release.PackageSha256 || p.ReviewSha256!=signedHash || p.Sender!=delivery.Sender || p.Recipient!=recipient || p.Environment!=environment || p.SendRehearsalEmail!=rehearsal)throw new InvalidDataException("Retained delivery targets another packet.");
    var v=operation.Revalidation??throw new InvalidDataException("Missing retained revalidation.");
    if(v.ConsoleDirectory!=review.Console.Directory || !v.ConsoleFiles.SequenceEqual(review.Console.Files) || v.PreparationSettingsPath!=P("delivery-preparation-settings.json") ||
     v.PreparedDirectory!=P("delivery-prepared") || v.AttemptsDirectory!=P("delivery-attempts") || v.PreparationSettingsSha256!=AutomationFiles.Hash(v.PreparationSettingsPath))
     throw new InvalidDataException("Retained delivery tooling or settings changed.");
   } else if(operation.Qualified is not {} q || q!=Qualified(q.AuthorizationSha256))throw new InvalidDataException("Retained qualified delivery targets another packet.");
   var existing=Read(journal,operation);
   if(existing?.State==SubmissionDeliveryState.Submitted)return Completed(c,existing,environment);
   if(existing?.State is SubmissionDeliveryState.OutcomeUnknown or SubmissionDeliveryState.UploadPending or SubmissionDeliveryState.SendPending)
    return new(SubmissionWorkflowStatus.OutcomeUnknown,ReasonCode:"inspect-delivery-provider-outcome");
  } else {
   string? approval=AutomationSigning.ApprovalPin(protection.DeliveryApproval,c.RunDirectory);
   if(approval==null)return new(SubmissionWorkflowStatus.Waiting,ReasonCode:"delivery-authorization-required");
   if(qualified) {
    var plan=Qualified(approval);
    _=SubmissionReviewApproval.Verify(plan,protection.DeliveryApproval.DocumentPath,approval,DateTimeOffset.UtcNow);
    operation=new(null,plan,null);
   } else {
    string prep=P("delivery-preparation-settings.json"),output=P("delivery-prepared"),intent=P("delivery-preparation-intent.json");
    var preparation=new Dictionary<string,object?>{{"schemaVersion",1},{"signedReviewDirectory",P("signed-review")},
     {"reviewDirectory",selectedReview},{"authorization",protection.DeliveryApproval.DocumentPath},{"output",output}};
    if(rehearsal){preparation["environment"]="Rehearsal";preparation["rehearsalRecipient"]=recipient;preparation["sendRehearsalEmail"]=true;}
    AutomationReview.WriteDocument(prep,preparation);
    if(File.Exists(intent)) {
     if(!File.Exists(Path.Combine(output,"COMPLETE")))return new(SubmissionWorkflowStatus.OutcomeUnknown,ReasonCode:"inspect-delivery-preparation");
     using var prior=JsonDocument.Parse(File.ReadAllBytes(intent));
     if(prior.RootElement.GetProperty("AuthorizationSha256").GetString()!=approval || prior.RootElement.GetProperty("OperationId").GetString()!=c.Checkpoint.OperationId)
      throw new InvalidDataException("Delivery preparation inputs changed.");
    } else {
     if(Directory.Exists(output))return new(SubmissionWorkflowStatus.OutcomeUnknown,ReasonCode:"inspect-existing-delivery-preparation");
     AutomationFiles.Write(intent,new{c.Checkpoint.OperationId,AuthorizationSha256=approval});
     int result=await (documentCommand??AutomationConsole.Run)(review.Console,["submission","prepare-delivery","--settings",prep,"--signed-review-sha256",signedHash,"--authorization-sha256",approval],P("delivery-preparation-process"),token);
     if(result!=0)return new(SubmissionWorkflowStatus.Failed,ReasonCode:"delivery-preparation-failed");
    }
    string receipt=Path.Combine(output,"delivery-review-receipt.json"),receiptHash=AutomationFiles.Hash(receipt);
    if(File.ReadAllText(Path.Combine(output,"COMPLETE")).Trim()!=receiptHash)throw new InvalidDataException("Incomplete delivery preparation.");
    using var receiptDoc=JsonDocument.Parse(File.ReadAllBytes(receipt));
    string planFile=Path.Combine(output,"delivery-plan.json");
    if(AutomationFiles.Hash(planFile)!=receiptDoc.RootElement.GetProperty("planFileSha256").GetString())throw new InvalidDataException("Delivery plan changed.");
    var plan=AutomationFiles.Read<SubmissionDeliveryPlan>(planFile);
    if(plan.PackageSha256!=settings.Release.PackageSha256 || plan.ReviewSha256!=signedHash || plan.AuthorizationSha256!=approval || plan.Sender!=delivery.Sender || plan.Recipient!=recipient || plan.Environment!=environment || plan.SendRehearsalEmail!=rehearsal)
     throw new InvalidDataException("Delivery plan differs from the selected packet.");
    operation=new(plan,null,new(review.Console.Directory,review.Console.Files,prep,AutomationFiles.Hash(prep),output,P("delivery-attempts"),receiptHash,TimeSpan.FromMinutes(3)));
   }
   AutomationFiles.Write(operationPath,operation);
  }
  // Domain journals reconcile the same operation, including a confirmed upload followed by a blocked email.
  // The public coordinators revalidate evidence and exact authority before EACH provider operation.
  var delivered=await (dispatch??Send)(operation,token);
  return delivered.State==SubmissionDeliveryState.Submitted?Completed(c,delivered,environment):new(SubmissionWorkflowStatus.OutcomeUnknown,ReasonCode:"inspect-delivery-provider-outcome");

  async Task<SubmissionDeliveryReceipt> Send(Operation op,CancellationToken ct) {
   if(!OperatingSystem.IsWindows())throw new PlatformNotSupportedException();
   var bindings=DevToolsCredentialBindings.Read(protection.CredentialBindings);

   var smtp=bindings.Resolve(DevToolsCredentialPurpose.Smtp,delivery.SmtpHost,delivery.SmtpPort,delivery.Sender);
   var mailer=new SubmissionSmtpMailer(delivery.SmtpHost,delivery.SmtpPort,delivery.Sender,new NetworkCredential(smtp.UserName,smtp.Password),P("mail-receipts"),TimeSpan.FromSeconds(90));
   if(rehearsal && op.Qualified is {} qualifiedPlan) {
    var archive=Directory.CreateDirectory(P("rehearsal-upload")).FullName;
    var testTransport=new SubmissionRehearsalReviewMailTransport(archive,qualifiedPlan,mailer);
    var result=await SubmissionReviewRequestDelivery.ExecuteAsync(P("signed-review"),qualifiedPlan,protection.DeliveryApproval.DocumentPath,
     qualifiedPlan.AuthorizationSha256,journal,P("delivery-attempts"),testTransport,cancellationToken:ct);
    return result.Delivery;
   }
   if(rehearsal) {
    var testPlan=op.Complete??throw new InvalidDataException("Missing complete rehearsal plan.");
    var archive=Directory.CreateDirectory(P("rehearsal-upload")).FullName;
    return await SendComplete(op,new SubmissionRehearsalMailTransport(archive,testPlan,mailer),ct);
   }
   var upload=bindings.Resolve(DevToolsCredentialPurpose.Uploader,"uploader.crestron.com");
   using var uploader=new CrestronSubmissionUploader(new NetworkCredential(upload.UserName,upload.Password),delivery.ReviewedUploadFormSha256,delivery.AcceptedUploadTermsSha256,P("upload-receipts"),TimeSpan.FromSeconds(90));
   var transport=new CrestronSubmissionTransport(uploader,mailer);
   if(op.Qualified is {} q) {
    var result=await SubmissionReviewRequestDelivery.ExecuteAsync(P("signed-review"),q,protection.DeliveryApproval.DocumentPath,q.AuthorizationSha256,journal,P("delivery-attempts"),transport,cancellationToken:ct);
    return result.Delivery;
   }
   return await SendComplete(op,transport,ct);
  }
  async Task<SubmissionDeliveryReceipt> SendComplete(Operation op,ISubmissionDeliveryTransport transport,CancellationToken ct) {
   var p=op.Complete??throw new InvalidDataException("Missing delivery plan.");
   return await SubmissionDelivery.ExecuteAuthorizedAsync(journal,p,P("delivery-prepared/delivery/"+p.PackageFileName),P("delivery-prepared/delivery/"+p.SignedFormFileName),transport,
    (step,t)=>SubmissionDeliveryRevalidation.CheckAsync(op.Revalidation??throw new InvalidDataException("Missing delivery revalidation."),p,step,t),cancellationToken:ct);
  }
 }
 private static SubmissionDeliveryReceipt? Read(string journal,Operation op) {
  if((op.Complete==null)==(op.Qualified==null))throw new InvalidDataException("Select one delivery plan.");
  return op.Complete is {} p?SubmissionDelivery.Read(journal,p,p.Environment):SubmissionDelivery.ReadReview(journal,op.Qualified!)?.Delivery;
 }
 private static SubmissionWorkflowStepResult Completed(SubmissionWorkflowStepContext c,SubmissionDeliveryReceipt receipt,SubmissionDeliveryEnvironment environment) {
  if(receipt.Environment!=environment)throw new InvalidDataException("Delivery confirmation belongs to another environment.");
  if(receipt.Upload==null || receipt.Mail==null)throw new InvalidDataException("Provider confirmations are missing.");
  return AutomationFiles.Complete(c,"delivery-evidence.json",receipt);
 }
 internal static SubmissionWorkflowStepResult Retain(SubmissionWorkflowStepContext c,SubmissionAutomationMode mode=SubmissionAutomationMode.Submit) {
  var delivery=AutomationFiles.Read<SubmissionDeliveryReceipt>(Path.Combine(c.RunDirectory,"delivery-evidence.json"));
  if(delivery.Environment!=EnvironmentFor(mode) || delivery.State!=SubmissionDeliveryState.Submitted || delivery.Upload==null || delivery.Mail==null)throw new InvalidDataException("Submission delivery is not confirmed.");
  string[] excluded=["run.lock","intake.lock","state.json","retained.json"];
  // Bound before hashing, while allowing the original attempts and detailed app evidence to survive closeout.
  const int maximumFiles=65536;
  var paths=Directory.EnumerateFiles(c.RunDirectory,"*",SearchOption.AllDirectories)
   .Where(p=>!excluded.Contains(Path.GetRelativePath(c.RunDirectory,p))).Take(maximumFiles+1).ToArray();
  if(paths.Length>maximumFiles)throw new InvalidDataException("Retained output exceeds the bounded workflow inventory.");
  var files=paths.Order(StringComparer.Ordinal).Select(p=>{
   string relative=Path.GetRelativePath(c.RunDirectory,p);
   if(!SubmissionEvidence.SafeEvidencePath(c.RunDirectory,relative.Replace('\\','/'),out var verified))throw new InvalidDataException("Retained evidence is redirected or missing.");
   return new SubmissionWorkflowReceipt(relative,AutomationFiles.Hash(verified));
  }).ToArray();
  var receipt=new{c.Checkpoint.InputSha256,State=mode==SubmissionAutomationMode.Rehearsal?"RehearsalCompleted":"Submitted",CrestronAcceptanceEstablished=false,Files=files};
  if(JsonSerializer.SerializeToUtf8Bytes(receipt,AutomationFiles.Json).Length>16*1024*1024)
   throw new InvalidDataException("Retained inventory exceeds its document size limit.");
  return AutomationFiles.Complete(c,"retained.json",receipt);
 }
}
