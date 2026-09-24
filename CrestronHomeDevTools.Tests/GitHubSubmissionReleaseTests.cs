// Copyright (c) 2026 Neil Colvin. Licensed under the MIT License.
using System.Net;
using System.Text.Json;
using NUnit.Framework;

namespace CrestronHomeDevTools.Tests;

[TestFixture]
public sealed class GitHubSubmissionReleaseTests
{
 private sealed class Handler(string release) : HttpMessageHandler
 {
  public List<string> Requests=[];
  public string? Token;
  protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
  {
   Requests.Add(request.RequestUri!.AbsolutePath);Token=request.Headers.Authorization?.Parameter;
   Assert.That(request.Headers.GetValues("X-GitHub-Api-Version").Single(),Is.EqualTo("2026-03-10"));
   return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(Requests.Count==1?release:JsonSerializer.Serialize(new{sha=new string('a',40)}))});
  }
 }
 private static string Release(bool draft=false,bool prerelease=false,bool asset=true,string state="uploaded",string? digest="default") => JsonSerializer.Serialize(new {
  id=22,tag_name="v1.2.3",target_commitish="main",draft,prerelease,published_at=draft?null:"2026-09-24T00:00:00Z",
  assets=asset?new[]{new{id=33,name="Example_IP.pkg",size=100,state,digest=digest=="default"?"sha256:"+new string('b',64):digest}}:[] });

 [Test] public async Task ResolvesTagInsteadOfAssumingReleaseTargetBranchIsCommit()
 {
  using var handler=new Handler(Release());using var client=new HttpClient(handler);
  string token="ghs_"+new string('x',520);client.DefaultRequestHeaders.Authorization=new("Bearer",token);
  var result=await new GitHubSubmissionRelease(client).InspectAsync("example/driver",22,"Example_IP.pkg");
  Assert.That(result.Availability,Is.EqualTo(SubmissionReleaseAvailability.Ready));Assert.That(result.SourceCommit,Is.EqualTo(new string('a',40)));
  Assert.That(handler.Requests,Is.EqualTo(new[]{"/repos/example/driver/releases/22","/repos/example/driver/commits/v1.2.3"}));
  Assert.That(handler.Token,Is.EqualTo(token));Assert.That(result.PackageSha256,Is.EqualTo(new string('b',64)));
 }
 [TestCase(true,false,"not-published")][TestCase(false,true,"prerelease-not-selected")]
 public async Task IneligibleReleaseNeverResolvesOrRunsCode(bool draft,bool prerelease,string reason)
 {
  using var h=new Handler(Release(draft,prerelease));using var client=new HttpClient(h);
  var result=await new GitHubSubmissionRelease(client).InspectAsync("example/driver",22,"Example_IP.pkg");
  Assert.That(result.Availability,Is.EqualTo(SubmissionReleaseAvailability.NotEligible));Assert.That(result.ReasonCode,Is.EqualTo(reason));Assert.That(h.Requests.Count,Is.EqualTo(1));
 }
 [TestCase(false,"uploaded","default","package-not-uploaded")][TestCase(true,"starter","default","package-upload-in-progress")][TestCase(true,"uploaded",null,"package-digest-unavailable")]
 public async Task PublicationBeforePackageCompletionIsWaitingNotFailure(bool asset,string state,string? digest,string reason)
 {
  using var h=new Handler(Release(asset:asset,state:state,digest:digest));using var client=new HttpClient(h);
  var result=await new GitHubSubmissionRelease(client).InspectAsync("example/driver",22,"Example_IP.pkg");
  Assert.That(result.Availability,Is.EqualTo(SubmissionReleaseAvailability.AwaitingPackage));Assert.That(result.ReasonCode,Is.EqualTo(reason));Assert.That(h.Requests.Count,Is.EqualTo(1));
 }
 [Test] public void MalformedDigestIsRejected()
 {
  using var h=new Handler(Release(digest:"sha256:invalid"));using var client=new HttpClient(h);
  Assert.ThrowsAsync<InvalidDataException>(async()=>await new GitHubSubmissionRelease(client).InspectAsync("example/driver",22,"Example_IP.pkg"));
 }
}
