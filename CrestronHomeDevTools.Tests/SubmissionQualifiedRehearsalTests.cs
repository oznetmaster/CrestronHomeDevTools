// Copyright (c) 2026 Neil Colvin. MIT licensed.
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using MimeKit;
using NUnit.Framework;
namespace CrestronHomeDevTools.Tests;
[TestFixture]
public sealed class SubmissionQualifiedRehearsalTests
{
 string root=null!,package=null!,form=null!;
 SubmissionReviewDeliveryPlan plan=null!;
 static string Hash(byte[] bytes)=>Convert.ToHexStringLower(SHA256.HashData(bytes));
 [SetUp] public void Setup(){
  root=Path.Combine(TestContext.CurrentContext.WorkDirectory,"qualified-rehearsal-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
  package=Path.Combine(root,"Example_Test_IP.pkg");form=Path.Combine(root,"form.pdf");
  File.WriteAllText(package,"synthetic package");File.WriteAllText(form,"synthetic qualified form");
  plan=new(new('a',64),new('b',64),new('c',64),Hash(File.ReadAllBytes(package)),Hash(File.ReadAllBytes(form)),
   Path.GetFileName(package),Path.GetFileName(form),"sender@example.test","rehearsal@example.test",SubmissionReviewMode.DeclaredGaps,
   SubmissionVerificationStatus.GapsDeclared,SubmissionReviewAttachmentKind.SignedSelfTest,new('d',64),"One hour observed; required 24 hours not completed.",null)
   {Environment=SubmissionDeliveryEnvironment.Rehearsal,SendRehearsalEmail=true};
 }
 [TearDown] public void Cleanup()=>Directory.Delete(root,true);
 SubmissionRehearsalReviewMailTransport Transport(Session session){
  string archive=Directory.CreateDirectory(Path.Combine(root,"archive")).FullName;
  string receipts=Directory.CreateDirectory(Path.Combine(root,"mail")).FullName;
  return new(archive,plan,new SubmissionSmtpMailer("smtp.example.test",587,plan.Sender,new NetworkCredential("test","synthetic"),receipts,TimeSpan.FromSeconds(10),()=>session));
 }
 Task<SubmissionReviewDeliveryReceipt> Execute(ISubmissionReviewDeliveryTransport transport,Func<SubmissionDeliveryStep,CancellationToken,Task<SubmissionDeliveryAuthorization>>? approval=null)=>
  SubmissionDelivery.ExecuteReviewAuthorizedAsync(root,plan,package,form,transport,approval??((_,_)=>Task.FromResult(new SubmissionDeliveryAuthorization(SubmissionDelivery.ReviewPlanDigest(plan),DateTimeOffset.UtcNow.AddMinutes(5)))));
 [Test] public async Task ExactQualifiedPacketIsMailedOnceAndRetainsItsEnvironment(){
  var session=new Session();var transport=Transport(session);var steps=new List<SubmissionDeliveryStep>();
  var result=await Execute(transport,(step,_)=>{steps.Add(step);return Task.FromResult(new SubmissionDeliveryAuthorization(SubmissionDelivery.ReviewPlanDigest(plan),DateTimeOffset.UtcNow.AddMinutes(5)));});
  Assert.That(steps,Is.EqualTo(new[]{SubmissionDeliveryStep.Upload,SubmissionDeliveryStep.Send}));
  Assert.That(result.VerificationStatus,Is.EqualTo(SubmissionVerificationStatus.GapsDeclared));Assert.That(result.Delivery.Environment,Is.EqualTo(SubmissionDeliveryEnvironment.Rehearsal));
  using var mail=MimeMessage.Load(new MemoryStream(session.Bytes!));
  Assert.That(mail.Subject,Is.EqualTo("[REHEARSAL] Driver Submission Package"));Assert.That(mail.To.Mailboxes.Single().Address,Is.EqualTo(plan.Recipient));
  Assert.That(mail.TextBody,Does.Contain(plan.GapSummary));Assert.That(mail.TextBody,Does.Contain("REHEARSAL ONLY"));Assert.That(mail.TextBody,Does.Not.Contain("https://rehearsal.invalid"));
  var files=mail.Attachments.Cast<MimePart>().ToDictionary(p=>p.FileName??throw new AssertionException("Missing attachment filename"),p=>{using var b=new MemoryStream();p.Content!.DecodeTo(b);return Hash(b.ToArray());});
  Assert.That(files.Count,Is.EqualTo(2));Assert.That(files[plan.PackageFileName],Is.EqualTo(plan.PackageSha256));Assert.That(files[plan.AttachmentFileName],Is.EqualTo(plan.AttachmentSha256));
  Assert.That(SubmissionDelivery.ReadReview(root,plan),Is.EqualTo(result));
  Assert.That(await Execute(Transport(session),(_,_)=>throw new AssertionException("No repeat authorization")),Is.EqualTo(result));Assert.That(session.Sends,Is.EqualTo(1));
 }
 [Test] public async Task AmbiguousMailIsRetainedAndNotAutomaticallyRetried(){
  var session=new Session{Fail=true};await Assert.ThrowsAsync<InvalidDataException>(async()=>await Execute(Transport(session)));
  Assert.That(SubmissionDelivery.ReadReview(root,plan)!.Delivery.State,Is.EqualTo(SubmissionDeliveryState.OutcomeUnknown));
  session.Fail=false;await Assert.ThrowsAsync<InvalidOperationException>(async()=>await Execute(Transport(session)));Assert.That(session.Sends,Is.EqualTo(1));
  SubmissionDelivery.ReconcileReview(root,plan,SubmissionDeliveryStep.Send,true,"Synthetic provider confirms original send",mail:new("synthetic confirmation"));
  Assert.That((await Execute(Transport(session))).Delivery.State,Is.EqualTo(SubmissionDeliveryState.Submitted));Assert.That(session.Sends,Is.EqualTo(1));
 }
 [TestCase("drivers@crestron.com")][TestCase("test@sub.crestron.com")]
 public void VendorRecipientCannotBeUsed(string recipient){plan=plan with{Recipient=recipient};Assert.Throws<ArgumentException>(()=>SubmissionDelivery.ReviewPlanDigest(plan));}
 [Test] public async Task ProductionTransportCannotReceiveRehearsalPlan(){await Assert.ThrowsAsync<InvalidDataException>(async()=>await Execute(new Production()));}
 [Test] public void ProductionSerializationAndDigestDoNotAddDefaultEnvironmentFields(){
  var production=plan with{Environment=SubmissionDeliveryEnvironment.Production,SendRehearsalEmail=false};
  string json=JsonSerializer.Serialize(production);Assert.That(json,Does.Not.Contain("Environment"));Assert.That(json,Does.Not.Contain("SendRehearsalEmail"));
  Assert.That(SubmissionDelivery.ReviewPlanDigest(production),Is.Not.EqualTo(SubmissionDelivery.ReviewPlanDigest(plan)));
  Assert.That(SubmissionReviewApproval.Preview(production).PacketSha256,Is.Not.EqualTo(SubmissionReviewApproval.Preview(plan).PacketSha256));
 }
 [Test] public async Task ChangedGapInvalidatesApprovalBeforeUpload(){
  string digest=SubmissionDelivery.ReviewPlanDigest(plan);plan=plan with{GapSummary="Changed reason"};var session=new Session();
  await Assert.ThrowsAsync<InvalidOperationException>(async()=>await Execute(Transport(session),(_,_)=>Task.FromResult(new SubmissionDeliveryAuthorization(digest,DateTimeOffset.UtcNow.AddMinutes(5)))));
  Assert.That(File.Exists(Path.Combine(root,"archive/package.pkg")),Is.False);Assert.That(session.Sends,Is.Zero);
 }
 [Test] public async Task TamperedLocallyRetainedPackageCannotReachSmtp(){
  var session=new Session();var transport=Transport(session);
  await Assert.ThrowsAsync<InvalidDataException>(async()=>await Execute(transport,(step,_)=>{
   if(step==SubmissionDeliveryStep.Send)File.AppendAllText(Path.Combine(root,"archive/package.pkg"),"changed");
   return Task.FromResult(new SubmissionDeliveryAuthorization(SubmissionDelivery.ReviewPlanDigest(plan),DateTimeOffset.UtcNow.AddMinutes(5)));}));Assert.That(session.Sends,Is.Zero);
 }
 sealed class Production:ISubmissionReviewDeliveryTransport{
  public Task<SubmissionUploadReceipt> UploadAsync(Stream s,string n,CancellationToken t)=>throw new AssertionException("No production upload");
  public Task<SubmissionMailReceipt> SendReviewAsync(SubmissionReviewDeliveryPlan p,SubmissionUploadReceipt u,Stream s,string m,CancellationToken t)=>throw new AssertionException("No production mail");
 }
 sealed class Session:ISubmissionSmtpSession{
  public bool Fail;public int Sends;public byte[]? Bytes;
  public Task ConnectAsync(string host,int port,NetworkCredential credential,CancellationToken t)=>Task.CompletedTask;
  public Task<string> SendAsync(MimeMessage m,CancellationToken t){Sends++;using var b=new MemoryStream();m.WriteTo(b);Bytes=b.ToArray();if(Fail)throw new IOException("Synthetic ambiguous SMTP");return Task.FromResult("accepted synthetic message");}
  public void Dispose(){}
 }
}
