// Copyright (c) 2026 Neil Colvin. Licensed under the MIT License.
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using NUnit.Framework;

namespace CrestronHomeDevTools.Tests;

[TestFixture]
public sealed class SubmissionReleaseIntakeTests
{
 private string root=null!;
 private static readonly byte[] Package = [1,2,3,4];
 private static string Digest(byte[] data)=>Convert.ToHexStringLower(SHA256.HashData(data));
 [SetUp] public void Setup() { root=Path.Combine(TestContext.CurrentContext.WorkDirectory,"release-intake-"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root); }
 [TearDown] public void Cleanup() { Directory.Delete(root,true); }
 private SubmissionReleaseIntakeSettings Settings()
 {
  var snapshot=Path.Combine(root,"snapshot.encrypted");var tooling=Path.Combine(root,"tooling.json");
  File.WriteAllText(snapshot,"synthetic snapshot");File.WriteAllText(tooling,"synthetic inventory");
  return new(1,"example/driver",22,"Example.pkg",root,snapshot,Digest(File.ReadAllBytes(snapshot)),tooling,Digest(File.ReadAllBytes(tooling)));
 }
 private sealed class Handler : HttpMessageHandler
 {
  public int Downloads,Requests;
  public bool MissingAsset, MoveTagAfterTransfer;
  public byte[] Download=Package;
  public List<string> Tokens=[];
  protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
  {
   Requests++;Tokens.Add(request.Headers.Authorization?.Parameter??"");
   string path=request.RequestUri!.AbsolutePath;
   HttpContent content;
   if (path.EndsWith("/assets/33",StringComparison.Ordinal))
   { Downloads++;content=new ByteArrayContent(Download); }
   else if (path.Contains("/commits/",StringComparison.Ordinal))
    content=new StringContent(JsonSerializer.Serialize(new{sha=new string(MoveTagAfterTransfer&&Downloads>0?'b':'a',40)}));
   else content=new StringContent(JsonSerializer.Serialize(new {
    id=22,tag_name="v1.2.3",target_commitish="main",draft=false,prerelease=false,published_at="2026-09-24T00:00:00Z",
    assets=MissingAsset?[]:new[]{new{id=33,name="Example.pkg",size=Package.Length,state="uploaded",digest="sha256:"+Digest(Package)}} }));
   return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=content});
  }
 }
 [Test] public async Task RepeatedIntakeRetainsSamePackageAndCheckpointWithoutAnotherDownload()
 {
  var settings=Settings();using var h=new Handler();using var http=new HttpClient(h);var github=new GitHubSubmissionRelease(http);
  var first=await SubmissionReleaseIntake.PrepareAsync(settings,github);
  var second=await SubmissionReleaseIntake.PrepareAsync(settings,github);
  Assert.That(first.Availability,Is.EqualTo(SubmissionReleaseAvailability.Ready));Assert.That(second.RunDirectory,Is.EqualTo(first.RunDirectory));
  Assert.That(second.Checkpoint!.UpdatedUtc,Is.EqualTo(first.Checkpoint!.UpdatedUtc));Assert.That(h.Downloads,Is.EqualTo(1));
  Assert.That(File.ReadAllBytes(Path.Combine(first.RunDirectory!,"candidate.pkg")),Is.EqualTo(Package));
  Assert.That(first.Checkpoint.CompletedStages,Is.Empty);Assert.That(first.Checkpoint.Stage,Is.EqualTo(SubmissionWorkflowStage.ValidateCandidate));
 }
 [Test] public async Task MissingAssetWaitsWithoutCreatingAWorkflow()
 {
  var settings=Settings();using var h=new Handler{MissingAsset=true};using var http=new HttpClient(h);
  var result=await SubmissionReleaseIntake.PrepareAsync(settings,new(http));
  Assert.That(result.Availability,Is.EqualTo(SubmissionReleaseAvailability.AwaitingPackage));Assert.That(result.Checkpoint,Is.Null);
  Assert.That(Directory.GetDirectories(root),Is.Empty);Assert.That(h.Downloads,Is.Zero);
 }
 [Test] public async Task ResumingIntakeRemovesOnlyItsAbandonedDownloadScratch()
 {
  var settings=Settings();using var h=new Handler();using var http=new HttpClient(h);
  var release=new SubmissionWorkflowRelease(settings.Repository,settings.ReleaseId,"v1.2.3",new('a',40),Digest(Package),settings.ProfileSnapshotSha256,settings.ToolingSha256);
  string directory=Path.Combine(root,SubmissionWorkflow.RunKey(release));Directory.CreateDirectory(directory);
  string scratch=Path.Combine(directory,"candidate.pkg."+Guid.NewGuid().ToString("N")+".download");File.WriteAllText(scratch,"incomplete");
  string unrelated=Path.Combine(directory,"candidate.pkg.original-error.download");File.WriteAllText(unrelated,"retained error");
  var result=await SubmissionReleaseIntake.PrepareAsync(settings,new(http));
  Assert.That(result.Availability,Is.EqualTo(SubmissionReleaseAvailability.Ready));Assert.That(File.Exists(scratch),Is.False);
  Assert.That(File.ReadAllText(unrelated),Is.EqualTo("retained error"));
 }
 [TestCase("digest")][TestCase("long")][TestCase("short")][TestCase("tag")]
 public void IncorrectOrChangedArtifactNeverStartsRunAndCleansPartialDownload(string failure)
 {
  var settings=Settings();using var h=new Handler{Download=failure switch{"digest"=>[9,8,7,6],"long"=>[1,2,3,4,5],"short"=>[1,2],_=>Package},MoveTagAfterTransfer=failure=="tag"};
  using var http=new HttpClient(h);
  Assert.ThrowsAsync<InvalidDataException>(async()=>await SubmissionReleaseIntake.PrepareAsync(settings,new(http)));
  Assert.That(Directory.GetFiles(root,"state.json",SearchOption.AllDirectories),Is.Empty);
  Assert.That(Directory.GetFiles(root,"candidate.pkg",SearchOption.AllDirectories),Is.Empty);
  Assert.That(Directory.GetFiles(root,"*.download",SearchOption.AllDirectories),Is.Empty);
 }
 [Test] public void ChangedFrozenInputFailsBeforeGitHubRequest()
 {
  var settings=Settings();File.AppendAllText(settings.ProfileSnapshotPath,"changed");using var h=new Handler();using var http=new HttpClient(h);
  Assert.ThrowsAsync<InvalidDataException>(async()=>await SubmissionReleaseIntake.PrepareAsync(settings,new(http)));Assert.That(h.Requests,Is.Zero);
 }
 [Test] public async Task ChangedRetainedPackageIsNotSilentlyReplaced()
 {
  var settings=Settings();using var h=new Handler();using var http=new HttpClient(h);
  var original=await SubmissionReleaseIntake.PrepareAsync(settings,new(http));File.WriteAllBytes(Path.Combine(original.RunDirectory!,"candidate.pkg"),[9,8,7,6]);
  Assert.ThrowsAsync<InvalidDataException>(async()=>await SubmissionReleaseIntake.PrepareAsync(settings,new(http)));Assert.That(h.Downloads,Is.EqualTo(1));
 }
 [Test] public async Task ConsoleAcceptsProtectedLongTokenWithoutPrintingIt()
 {
  var settings=Settings();string path=Path.Combine(root,"settings.json");
  File.WriteAllText(path,JsonSerializer.Serialize(settings,new JsonSerializerOptions{PropertyNamingPolicy=JsonNamingPolicy.CamelCase}));
  string secret="ghs_"+new string('x',520);using var h=new Handler();using var http=new HttpClient(h);using var output=new StringWriter();using var error=new StringWriter();
  int code=await SubmissionReleaseIntakeCommand.RunAsync(["--settings",path,"--token-stdin","true"],new StringReader(secret),output,error,CancellationToken.None,http);
  Assert.That(code,Is.Zero);Assert.That(h.Tokens.All(t=>t==secret),Is.True);Assert.That(output.ToString()+error,Does.Not.Contain(secret));
  Assert.That(output.ToString(),Does.Contain("candidate-retained"));
 }
 [Test] public async Task ConsoleRejectsUnknownSettingsBeforeNetworkAccess()
 {
  string path=Path.Combine(root,"settings.json");File.WriteAllText(path,"{\"executeCommand\":\"bad\"}");
  using var h=new Handler();using var http=new HttpClient(h);using var output=new StringWriter();using var error=new StringWriter();
  int code=await SubmissionReleaseIntakeCommand.RunAsync(["--settings",path],TextReader.Null,output,error,CancellationToken.None,http);
  Assert.That(code,Is.EqualTo(2));Assert.That(h.Requests,Is.Zero);Assert.That(output.ToString(),Is.Empty);
 }
}
