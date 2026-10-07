// Copyright (c) 2026 Neil Colvin. MIT licensed.
using System.Net;
using System.Security.Cryptography;
using MimeKit;
using NUnit.Framework;
using CrestronHomeDevTools.Automation;
namespace CrestronHomeDevTools.Tests;
[TestFixture]
public sealed class SubmissionPackageLinkTests
{
 const string Url="https://packages.example.test/release/driver.pkg";
 static readonly byte[] Package="synthetic package"u8.ToArray(), Form="synthetic signed form"u8.ToArray();
 static string Hash(byte[] b)=>Convert.ToHexStringLower(SHA256.HashData(b));
 string root=null!;
 [SetUp] public void Setup(){root=Path.Combine(TestContext.CurrentContext.WorkDirectory,"linked-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);}
 [TearDown] public void Cleanup()=>Directory.Delete(root,true);
 SubmissionDeliveryPlan Plan()=>new(new('a',64),new('b',64),new('c',64),Hash(Package),Hash(Form),"driver.pkg","signed.pdf","sender@example.test","recipient@example.test")
 {Environment=SubmissionDeliveryEnvironment.Rehearsal,SendRehearsalEmail=true,RehearsalPackageDownloadUrl=Url};
 sealed class Handler(Func<HttpRequestMessage,HttpResponseMessage> handle):HttpMessageHandler
 {
  protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token){token.ThrowIfCancellationRequested();return Task.FromResult(handle(request));}
 }
 static HttpResponseMessage Bytes(byte[] bytes)=>new(HttpStatusCode.OK){Content=new ByteArrayContent(bytes)};
 [Test] public async Task DownloadsAndHashesRedirectedPackageWithoutCredentials()
 {
  int calls=0;
  using var client=new HttpClient(new Handler(request=>{
   Assert.That(request.Headers.Authorization,Is.Null);Assert.That(request.Headers.Contains("Cookie"),Is.False);calls++;
   if(calls==1){var redirect=new HttpResponseMessage(HttpStatusCode.Found);redirect.Headers.Location=new Uri("https://cdn.example.test/asset");return redirect;}
   Assert.That(request.RequestUri!.Host,Is.EqualTo("cdn.example.test"));return Bytes(Package);
  }));
  await SubmissionPackageLink.VerifyAsync(client,Url,Hash(Package),default);Assert.That(calls,Is.EqualTo(2));
 }
 [TestCase("")][TestCase("different bytes")]
 public async Task WrongOrEmptyDownloadFails(string value)
 {
  using var client=new HttpClient(new Handler(_=>Bytes(System.Text.Encoding.UTF8.GetBytes(value))));
  await Assert.ThrowsAsync<InvalidDataException>(()=>SubmissionPackageLink.VerifyAsync(client,Url,Hash(Package),default));
 }
 [Test] public async Task HttpFailureIsNotSuccessfulVerification()
 {
  using var client=new HttpClient(new Handler(_=>new(HttpStatusCode.Forbidden)));
  await Assert.ThrowsAsync<HttpRequestException>(()=>SubmissionPackageLink.VerifyAsync(client,Url,Hash(Package),default));
 }
 [Test] public async Task AdvertisedOversizeDownloadRejected()
 {
  using var client=new HttpClient(new Handler(_=>{var response=Bytes(Package);response.Content.Headers.ContentLength=64L*1024*1024+1;return response;}));
  await Assert.ThrowsAsync<InvalidDataException>(()=>SubmissionPackageLink.VerifyAsync(client,Url,Hash(Package),default));
 }
 [TestCase("http://example.test/file")][TestCase("https://user:password@example.test/file")][TestCase("https://rehearsal.invalid/placeholder")][TestCase("https://example.test/file#fragment")]
 public void InvalidLinksCannotEnterApprovedPlans(string url)
 {
  Assert.Throws<ArgumentException>(()=>SubmissionDelivery.PlanDigest(Plan() with{RehearsalPackageDownloadUrl=url}));
 }
 [Test] public async Task RedirectCannotDowngradeToHttp()
 {
  using var client=new HttpClient(new Handler(_=>{var response=new HttpResponseMessage(HttpStatusCode.Found);response.Headers.Location=new Uri("http://example.test/asset");return response;}));
  await Assert.ThrowsAsync<ArgumentException>(()=>SubmissionPackageLink.VerifyAsync(client,Url,Hash(Package),default));
 }
 [Test] public async Task CancellationPreventsDownload()
 {
  using var client=new HttpClient(new Handler(_=>throw new AssertionException("No request")));
  using var cancel=new CancellationTokenSource();cancel.Cancel();
  await Assert.ThrowsAsync<TaskCanceledException>(()=>SubmissionPackageLink.VerifyAsync(client,Url,Hash(Package),cancel.Token));
 }
 [Test] public void LinkIsBoundToApprovalAndCannotChangeProduction()
 {
  var plan=Plan();Assert.That(SubmissionDelivery.PlanDigest(plan),Is.Not.EqualTo(SubmissionDelivery.PlanDigest(plan with{RehearsalPackageDownloadUrl=Url+"?other"})));
  Assert.Throws<ArgumentException>(()=>SubmissionDelivery.PlanDigest(plan with{Environment=SubmissionDeliveryEnvironment.Production,SendRehearsalEmail=false}));
  string legacy=System.Text.Json.JsonSerializer.Serialize(plan with{RehearsalPackageDownloadUrl=null});
  Assert.That(legacy,Does.Not.Contain("RehearsalPackageDownloadUrl"));
 }
 [Test] public void AutomationUsesSelectedReleaseAndEscapesTagAndFilename()
 {
  var release=new SubmissionWorkflowRelease("owner/repo",1,"release/test","commit","hash","profile","tools");
  Assert.That(AutomationDelivery.ReleasePackageLink(release,"driver 1.pkg"),Is.EqualTo("https://github.com/owner/repo/releases/download/release%2Ftest/driver%201.pkg"));
 }
 sealed class Session:ISubmissionSmtpSession
 {
  internal byte[]? Bytes;internal int Sends,Connects;
  public Task ConnectAsync(string host,int port,NetworkCredential credential,CancellationToken token){Connects++;return Task.CompletedTask;}
  public Task<string> SendAsync(MimeMessage message,CancellationToken token){Sends++;using var b=new MemoryStream();message.WriteTo(b);Bytes=b.ToArray();return Task.FromResult("synthetic SMTP accepted");}
  public void Dispose(){}
 }
 [TestCase(false,false)][TestCase(true,false)][TestCase(false,true)][TestCase(true,true)]
 public async Task SharedCoordinatorUsesOnePdfPreservesGapsAndRejectsUnverifiedLink(bool qualified,bool corruptDownload)
 {
  var p=Plan();string pkg=Path.Combine(root,p.PackageFileName),pdf=Path.Combine(root,p.SignedFormFileName);
  File.WriteAllBytes(pkg,Package);File.WriteAllBytes(pdf,Form);
  string archive=Directory.CreateDirectory(Path.Combine(root,"archive")).FullName;
  string receipts=Directory.CreateDirectory(Path.Combine(root,"mail")).FullName;
  var session=new Session();int downloads=0;
  using var client=new HttpClient(new Handler(_=>{downloads++;return Bytes(corruptDownload?"wrong"u8.ToArray():Package);}));
  var mailer=new SubmissionSmtpMailer("smtp.example.test",587,p.Sender,new NetworkCredential("synthetic","synthetic"),receipts,TimeSpan.FromSeconds(5),()=>session,
   (url,hash,token)=>SubmissionPackageLink.VerifyAsync(client,url,hash,token));
  var q=new SubmissionReviewDeliveryPlan(p.CandidateSha256,p.ReviewSha256,p.AuthorizationSha256,p.PackageSha256,p.SignedFormSha256,p.PackageFileName,p.SignedFormFileName,p.Sender,p.Recipient,
   SubmissionReviewMode.DeclaredGaps,SubmissionVerificationStatus.GapsDeclared,SubmissionReviewAttachmentKind.SignedSelfTest,new('d',64),"One hour; 24 hours not completed.",null)
   {Environment=p.Environment,SendRehearsalEmail=true,RehearsalPackageDownloadUrl=Url,
    CorrespondenceOverride=new("[REHEARSAL] Driver Submission Package","One hour; 24 hours not completed. Download: {{PACKAGE_DOWNLOAD_URL}}")};
  var completeTransport=qualified?null:new SubmissionRehearsalMailTransport(archive,p,mailer);
  var reviewTransport=qualified?new SubmissionRehearsalReviewMailTransport(archive,q,mailer):null;
  Task<SubmissionDeliveryAuthorization> Authorize(SubmissionDeliveryStep step,CancellationToken token)=>Task.FromResult(new SubmissionDeliveryAuthorization(qualified?SubmissionDelivery.ReviewPlanDigest(q):SubmissionDelivery.PlanDigest(p),DateTimeOffset.UtcNow.AddMinutes(1)));
  async Task Run(){if(qualified)await SubmissionDelivery.ExecuteReviewAuthorizedAsync(root,q,pkg,pdf,reviewTransport!,Authorize);else await SubmissionDelivery.ExecuteAuthorizedAsync(root,p,pkg,pdf,completeTransport!,Authorize);}
  if(corruptDownload){await Assert.ThrowsAsync<InvalidDataException>(Run);Assert.That(session.Connects,Is.Zero);Assert.That(session.Sends,Is.Zero);return;}
  await Run();await Run();
  Assert.That(session.Sends,Is.EqualTo(1));Assert.That(downloads,Is.EqualTo(1));
  using var message=MimeMessage.Load(new MemoryStream(session.Bytes!));
  Assert.That(message.TextBody,Does.Contain(Url));Assert.That(message.TextBody,Does.Contain("REHEARSAL ONLY"));
  Assert.That(message.Attachments.Count(),Is.EqualTo(1));var attachment=(MimePart)message.Attachments.Single();
  using var bytes=new MemoryStream();attachment.Content!.DecodeTo(bytes);Assert.That(Hash(bytes.ToArray()),Is.EqualTo(Hash(Form)));
  if(qualified)Assert.That(message.TextBody,Does.Contain(q.GapSummary));
  Assert.That(message.TextBody,Does.Not.Contain("{{PACKAGE_DOWNLOAD_URL}}"));
 }
}

