// Copyright (c) 2026 Neil Colvin. MIT licensed.
using System.Text.Json;
using CrestronHomeDevTools.Automation;
using NUnit.Framework;
namespace CrestronHomeDevTools.Tests;

public sealed partial class AutomationReviewSelectionTests
{
 // Exercise the production adapters together. The document subprocess and
 // transport are synthetic: no real signature, credentials or provider calls.
 [Test] public async Task ProtectedAdaptersCarryReviewedPacketThroughSigningDeliveryAndRecovery() {
  string approvals=root+"-protected";Directory.CreateDirectory(approvals);
  try {
   var signing=new SubmissionAutomationApprovalChannel(Path.Combine(approvals,"sign.json"),Path.Combine(approvals,"sign.sha256"));
   var sending=new SubmissionAutomationApprovalChannel(Path.Combine(approvals,"send.json"),Path.Combine(approvals,"send.sha256"));
   var delivery=new SubmissionAutomationDeliverySettings("sender@example.test","smtp.example.test",587,new('a',64),new('b',64),"Synthetic missing sensor evidence.");
   settings=settings with{Protected=settings.Protected! with{ReviewRevision=null,SigningApproval=signing,DeliveryApproval=sending,Delivery=delivery}};
   int signs=0,sends=0;
   async Task<int> Sign(SubmissionAutomationConsole _,string[] args,string logs,CancellationToken token) {
    signs++;
    Assert.That(args.Take(2),Is.EqualTo(new[]{"submission","prepare-signed-review"}));
    using var json=JsonDocument.Parse(await File.ReadAllTextAsync(args[3],token));var fields=json.RootElement;
    Assert.That(fields.GetProperty("reviewDirectory").GetString(),Is.EqualTo(P("review")));
    Assert.That(fields.GetProperty("authorization").GetString(),Is.EqualTo(signing.DocumentPath));
    Assert.That(fields.GetProperty("output").GetString(),Is.EqualTo(P("signed-review")));
    Directory.CreateDirectory(P("signed-review/delivery"));
    const string package="ExampleDeveloper_Test_Example_IP.pkg",form="Driver-Self-Test.signed.pdf";
    File.Copy(P(package),P("signed-review/delivery/"+package));
    File.WriteAllText(P("signed-review/delivery/"+form),"Synthetic signed-form substitute; never submit.");
    Write("signed-review/signing-report.json",new{synthetic=true});Write("signed-review/validation-report.json",new{synthetic=true});
    Write("signed-review/signed-review-receipt.json",new{state="SignedReviewPrepared",reviewReceiptSha256=Hash("review/review-receipt.json"),
     authorizationSha256=AutomationFiles.Hash(signing.DocumentPath),sourceCommit=settings.Release.SourceCommit,
     candidateSha256=Hash("candidate.json"),packageSha256=settings.Release.PackageSha256,packageFileName=package,
     signedFormFileName=form,signedFormSha256=Hash("signed-review/delivery/"+form),signatureApplied=true,deliveryAuthorized=false,deliveryAttempted=false,
     signingReportSha256=Hash("signed-review/signing-report.json"),validationReportSha256=Hash("signed-review/validation-report.json"),
     reviewMode="DeclaredGaps",verificationStatus="GapsDeclared",declarationsSha256=Hash("declarations.json")});
    File.WriteAllText(P("signed-review/COMPLETE"),Hash("signed-review/signed-review-receipt.json"));return 0;
   }
   var waiting=await AutomationSigning.Advance(context,settings,false,default,Sign);
   Assert.That(waiting.ReasonCode,Is.EqualTo("signing-authorization-required"));Assert.That(signs,Is.Zero);
   // The fake document subprocess deliberately does not perform cryptographic signing.
   File.WriteAllText(signing.DocumentPath,"{\"syntheticAuthorityForAdapterTestOnly\":true}");
   File.WriteAllText(signing.PinPath,AutomationFiles.Hash(signing.DocumentPath));
   var signed=await AutomationSigning.Advance(context,settings,false,default,Sign);
   Assert.That(signed.Status,Is.EqualTo(SubmissionWorkflowStatus.Completed));
   Assert.That((await AutomationSigning.Advance(context,settings,true,default,Sign)).Receipt,Is.EqualTo(signed.Receipt));
   Assert.That(signs,Is.EqualTo(1));
   context=context with{Checkpoint=context.Checkpoint with{Stage=SubmissionWorkflowStage.Deliver}};
   var transport=new ProtectedFlowTransport();
   async Task<SubmissionDeliveryReceipt> Dispatch(AutomationDelivery.Operation op,CancellationToken token) {
    sends++;Assert.That(op.Complete,Is.Null);var plan=op.Qualified!;
    Assert.That(plan.PackageSha256,Is.EqualTo(settings.Release.PackageSha256));
    Assert.That(plan.AttachmentKind,Is.EqualTo(SubmissionReviewAttachmentKind.SignedSelfTest));
    Assert.That(plan.GapSummary,Is.EqualTo(delivery.GapSummary));
    return (await SubmissionDelivery.ExecuteReviewAuthorizedAsync(P("delivery-journal"),plan,
     P("signed-review/delivery/"+plan.PackageFileName),P("signed-review/delivery/"+plan.AttachmentFileName),transport,
     (_,_)=>Task.FromResult(SubmissionReviewApproval.Verify(plan,sending.DocumentPath,plan.AuthorizationSha256,DateTimeOffset.UtcNow)),cancellationToken:token)).Delivery;
   }
   waiting=await AutomationDelivery.Advance(context,settings,false,default,dispatch:Dispatch);
   Assert.That(waiting.ReasonCode,Is.EqualTo("delivery-authorization-required"));Assert.That(sends,Is.Zero);
   using var preview=JsonDocument.Parse(File.ReadAllText(P("delivery-request.json")));
   var p=preview.RootElement;
   AutomationReview.WriteDocument(sending.DocumentPath,new SubmissionReviewApprovalDocument(1,p.GetProperty("packetSha256").GetString()!,
    p.GetProperty("correspondenceSha256").GetString()!,DateTimeOffset.UtcNow.AddMinutes(5),true,true,true));
   File.WriteAllText(sending.PinPath,AutomationFiles.Hash(sending.DocumentPath));
   var sent=await AutomationDelivery.Advance(context,settings,false,default,dispatch:Dispatch);
   Assert.That(sent.Status,Is.EqualTo(SubmissionWorkflowStatus.Completed));
   Assert.That((await AutomationDelivery.Advance(context,settings,true,default,dispatch:Dispatch)).Receipt,Is.EqualTo(sent.Receipt));
   Assert.That(sends,Is.EqualTo(1));Assert.That(transport.Calls,Is.EqualTo(new[]{"upload","mail"}));
   context=context with{Checkpoint=context.Checkpoint with{Stage=SubmissionWorkflowStage.Retain}};
   Assert.That(AutomationDelivery.Retain(context).Status,Is.EqualTo(SubmissionWorkflowStatus.Completed));
   using var retained=JsonDocument.Parse(File.ReadAllText(P("retained.json")));
   Assert.That(retained.RootElement.GetProperty("CrestronAcceptanceEstablished").GetBoolean(),Is.False);
  } finally {Directory.Delete(approvals,true);}
 }
 private sealed class ProtectedFlowTransport : ISubmissionReviewDeliveryTransport {
  internal List<string> Calls {get;}=[];
  public Task<SubmissionUploadReceipt> UploadAsync(Stream package,string filename,CancellationToken token) {
   Calls.Add("upload");return Task.FromResult(new SubmissionUploadReceipt("https://example.test/synthetic-download","synthetic"));
  }
  public Task<SubmissionMailReceipt> SendReviewAsync(SubmissionReviewDeliveryPlan plan,SubmissionUploadReceipt upload,Stream attachment,string messageId,CancellationToken token) {
   Calls.Add("mail");Assert.That(upload.DownloadUrl,Is.EqualTo("https://example.test/synthetic-download"));
   return Task.FromResult(new SubmissionMailReceipt("synthetic"));
  }
 }
}
