// Copyright (c) 2026 Neil Colvin. MIT licensed.
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using CrestronHomeDevTools.Automation;
using CrestronHomeNUnit.Workflow;
using NUnit.Framework;
namespace CrestronHomeDevTools.Tests;

[TestFixture]
public sealed class AutomationReleaseDiscoveryTests
{
 private string root=null!,registry=null!,profilesFile=null!;
 private SubmissionAutomationReleaseProfile profile=null!;
 private Handler handler=null!;private HttpClient http=null!;
 private static readonly byte[] Package=[1,2,3,4];
 private static readonly string Sha=Convert.ToHexStringLower(SHA256.HashData(Package));
 private sealed class Handler:HttpMessageHandler {
  public bool HasPackage=true;public List<string> Paths=[];
  protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token) {
   string path=request.RequestUri!.AbsolutePath;Paths.Add(path);
   object Release(long id,string date,bool draft=false,bool prerelease=false)=>new {id,tag_name="v1.2.3",published_at=date,draft,prerelease,
    assets=HasPackage?new[]{new{id=5,name="Driver_1.2.3.pkg",size=Package.Length,state="uploaded",digest="sha256:"+Sha}}:[]};
   HttpContent content=path.EndsWith("/releases")?new StringContent(JsonSerializer.Serialize(new[]{
    Release(90,"2026-09-20T00:00:00Z"),Release(91,"2026-09-24T01:00:00Z"),Release(92,"2026-09-24T01:00:00Z",draft:true),Release(93,"2026-09-24T01:00:00Z",prerelease:true)})):
    path.EndsWith("/releases/91")?new StringContent(JsonSerializer.Serialize(Release(91,"2026-09-24T01:00:00Z"))):
    path.EndsWith("/commits/v1.2.3")?new StringContent(JsonSerializer.Serialize(new{sha=new string('a',40)})):
    path.EndsWith("/releases/assets/5")?new ByteArrayContent(Package):throw new InvalidOperationException("Unexpected GitHub request: "+path);
   return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=content});
  }
 }
 [SetUp]public void Setup() {
  root=Path.Combine(TestContext.CurrentContext.WorkDirectory,"release-discovery-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
  registry=Path.Combine(root,"registry.json");AutomationFiles.Write(registry,new SubmissionAutomationRegistry(1,[]));
  var settings=new SubmissionAutomationSettings(1,"overridden",new("fixture/driver",1,"old",new('a',40),new('b',64),new('c',64),new('d',64)),"${source}",
   new(Guid.NewGuid().ToString(),"${version4}",PortalSubmissionKind.NewDriver,"Fixture","fixture@example.org"),Path.Combine(root,"credentials.json"),
   new WorkflowPlan{Host="fixture",CertificateSha256=new('e',64),SshFingerprint="fixture",SourceRoots=["${source}"],LocalTests=[],TestPackage=new("${source}/tests.csproj","${run}/tests.pkg","fixture",1),ProcessorSuites=[]});
  string template=Path.Combine(root,"template.json"),tooling=Path.Combine(root,"tooling.json");AutomationFiles.Write(template,settings);File.WriteAllText(tooling,"{}");
  profile=new("fixture","fixture/driver",DateTimeOffset.Parse("2026-09-24T00:00:00Z"),Path.Combine(root,"runs"),"Driver_${version}.pkg",
   new(template,AutomationFiles.Hash(template)),new(tooling,AutomationFiles.Hash(tooling)));
  profilesFile=Path.Combine(root,"profiles.json");AutomationFiles.Write(profilesFile,new SubmissionAutomationReleaseProfiles(1,[profile]));
  handler=new();http=new(handler);
 }
 [TearDown]public void Cleanup(){http.Dispose();Directory.Delete(root,true);}
 [Test]public async Task PublishedReleaseAutomaticallyRegistersPinnedSettingsAndRepeatedDiscoveryDoesNotRestartIt() {
  int checkouts=0;
  Task Checkout(string repo,string commit,string directory,CancellationToken t){checkouts++;Assert.That(repo,Is.EqualTo(profile.Repository));Assert.That(commit,Is.EqualTo(new string('a',40)));Directory.CreateDirectory(directory);return Task.CompletedTask;}
  var first=await AutomationReleaseDiscovery.Tick(profilesFile,registry,new(http),default,Checkout);
  Assert.That(first.Single().State,Is.EqualTo("Registered"));Assert.That(first.Single().ReleaseId,Is.EqualTo(91));
  var entry=AutomationFiles.Read<SubmissionAutomationRegistry>(registry).Entries.Single();
  var request=AutomationRequest.Load(["--registry",registry,"--profile","fixture","--release-id","91","--mode","rehearsal"]);
  Assert.That(request.Settings.Release.PackageSha256,Is.EqualTo(Sha));Assert.That(request.Settings.PackageRequirements.DriverVersion,Is.EqualTo("1.2.3.0"));
  Assert.That(request.Settings.NUnit.SourceRoots,Is.EqualTo(new[]{request.Settings.SourceRepository}));
  string run=Path.GetDirectoryName(entry.SettingsPath)!;Assert.That(File.ReadAllBytes(Path.Combine(run,"candidate.pkg")),Is.EqualTo(Package));
  string state=AutomationFiles.Hash(Path.Combine(run,"state.json"));
  Assert.That(await AutomationReleaseDiscovery.Tick(profilesFile,registry,new(http),default,Checkout),Is.Empty);
  Assert.That(checkouts,Is.EqualTo(1));Assert.That(AutomationFiles.Hash(Path.Combine(run,"state.json")),Is.EqualTo(state));
  Assert.That(AutomationFiles.Read<SubmissionAutomationRegistry>(registry).Entries.Length,Is.EqualTo(1));
 }
 [Test]public async Task PublicationBeforeAssetUploadWaitsThenRegistersWithoutManualInput() {
  handler.HasPackage=false;int calls=0;
  Task Checkout(string r,string c,string d,CancellationToken t){calls++;Directory.CreateDirectory(d);return Task.CompletedTask;}
  var waiting=await AutomationReleaseDiscovery.Tick(profilesFile,registry,new(http),default,Checkout);
  Assert.That(waiting.Single().Reason,Is.EqualTo("package-not-uploaded"));Assert.That(calls,Is.Zero);
  handler.HasPackage=true;
  Assert.That((await AutomationReleaseDiscovery.Tick(profilesFile,registry,new(http),default,Checkout)).Single().State,Is.EqualTo("Registered"));
 }
 [Test]public async Task ChangedPrivateTemplateStopsBeforeAnyNetworkOrSourceOperation() {
  File.AppendAllText(profile.SettingsTemplate.Path," ");
  Assert.That((await AutomationReleaseDiscovery.Tick(profilesFile,registry,new(http),default)).Single().State,Is.EqualTo("AttentionRequired"));
  Assert.That(handler.Paths,Is.Empty);
 }
 [Test]public async Task InterruptedCheckoutRetainsCandidateAndResumesRegistrationWithoutReplacingIt() {
  var failed=await AutomationReleaseDiscovery.Tick(profilesFile,registry,new(http),default,(_,_,_,_)=>throw new IOException("fixture interrupted"));
  Assert.That(failed.Single().State,Is.EqualTo("AttentionRequired"));Assert.That(AutomationFiles.Read<SubmissionAutomationRegistry>(registry).Entries,Is.Empty);
  var before=Directory.GetFiles(profile.PrivateRoot,"candidate.pkg",SearchOption.AllDirectories).Single();var stamp=File.GetLastWriteTimeUtc(before);
  var success=await AutomationReleaseDiscovery.Tick(profilesFile,registry,new(http),default,(_,_,d,_)=>{Directory.CreateDirectory(d);return Task.CompletedTask;});
  Assert.That(success.Single().State,Is.EqualTo("Registered"));Assert.That(File.GetLastWriteTimeUtc(before),Is.EqualTo(stamp));
 }
 [Test]public void RegistrationIsIdempotentAndCannotReplaceFrozenSettings() {
  var entry=new SubmissionAutomationRegistration("fixture",91,SubmissionAutomationMode.Rehearsal,Path.Combine(root,"a"),new('a',64));
  AutomationReleaseDiscovery.Register(registry,entry);AutomationReleaseDiscovery.Register(registry,entry);
  Assert.Throws<InvalidDataException>(()=>AutomationReleaseDiscovery.Register(registry,entry with{SettingsSha256=new('b',64)}));
 }
 [TestCase("v1.2.3","1.2.3")][TestCase("1.2.3.4","1.2.3.4")]
 public void SupportedVersionTokens(string tag,string expected)=>Assert.That(AutomationReleaseDiscovery.Version(tag),Is.EqualTo(expected));
 [TestCase("v1.2.3/../../x")][TestCase("main")]
 public void ReleaseTextCannotBecomeSourcePathsOrCommands(string tag)=>Assert.Throws<InvalidDataException>(()=>AutomationReleaseDiscovery.Version(tag));
}
