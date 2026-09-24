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
 private void WithProbeTemplate() {
  string bundle=Path.Combine(root,"published-producer");Directory.CreateDirectory(bundle);
  File.WriteAllText(Path.Combine(bundle,"probe.exe"),"Synthetic executable inventory; never executed");
  var probe=new SubmissionEnduranceProbeProgram(bundle,"probe.exe",[new("probe.exe",AutomationFiles.Hash(Path.Combine(bundle,"probe.exe")))]);
  var plan=new SubmissionEndurancePlan(new("${packageSha256}","${commit}",new('c',64),new('d',64)),
   new("endurance",TimeSpan.FromHours(24),Execution:new("gateway","endurance",SubmissionEvidenceOutcome.Passed,null,false,600)),
   "processor:fixture","instance","${reservationId}","prepared-at-intake",TimeSpan.FromMinutes(5),TimeSpan.FromMinutes(1));
  string input=Path.Combine(root,"probe-settings-template.json");
  File.WriteAllText(input,"{\"packagePath\":\"${package}\",\"sourceCommit\":\"${commit}\",\"baselineFile\":\"${run}/baseline.json\"}");
  var settings=AutomationFiles.Read<SubmissionAutomationSettings>(profile.SettingsTemplate.Path) with {
   Endurance=new(plan,new("fixture","fixture"),probe),EnduranceProbeSettingsTemplate=new(input,AutomationFiles.Hash(input))};
  File.WriteAllBytes(profile.SettingsTemplate.Path,JsonSerializer.SerializeToUtf8Bytes(settings,AutomationFiles.Json));
  profile=profile with{SettingsTemplate=new(profile.SettingsTemplate.Path,AutomationFiles.Hash(profile.SettingsTemplate.Path))};
 }
 private SubmissionAutomationSettings ExpandProbe(string run)=>AutomationReleaseDiscovery.Expand(profile,
  new(profile.Repository,91,"v1.2.3",new('a',40),Sha,new('c',64),new('d',64)),run,Path.Combine(run,"source"),"1.2.3");
 [Test]public void ReleaseExpansionPreparesImmutablePerReleaseProducerAndSettingsBeforeRegistration() {
  WithProbeTemplate();string run=Path.Combine(root,"new-run");
  var settings=ExpandProbe(run);var worker=settings.Endurance!;
  Assert.That(worker.Probe.Directory,Is.EqualTo(Path.Combine(run,"endurance-producer")));
  Assert.That(worker.Plan.Identity.PackageSha256,Is.EqualTo(Sha));
  Assert.That(worker.Plan.ProducerId,Is.EqualTo(SubmissionEnduranceProcessProbe.GetProducerId(worker.Probe)));
  using var generated=JsonDocument.Parse(File.ReadAllBytes(worker.Probe.SettingsFile!));
  Assert.That(generated.RootElement.GetProperty("packagePath").GetString(),Is.EqualTo(Path.Combine(run,"candidate.pkg")));
  Assert.That(generated.RootElement.GetProperty("sourceCommit").GetString(),Is.EqualTo(new string('a',40)));
  Assert.That(Directory.GetFiles(Path.Combine(root,"published-producer")),Has.Length.EqualTo(1));
  Assert.That(File.Exists(Path.Combine(run,"baseline.json")),Is.False,"Intake must not observe hardware or create a lifetime baseline.");
  var repeated=ExpandProbe(run);
  Assert.That(repeated.Endurance!.Plan.ProducerId,Is.EqualTo(worker.Plan.ProducerId));
  Assert.That(Guid.TryParseExact(worker.Plan.ReservationId,"N",out _),Is.True);
  Assert.That(repeated.Endurance.Plan.ReservationId,Is.EqualTo(worker.Plan.ReservationId));
 }
 [Test]public void ReservationIdentityChangesWithReleaseOrFrozenProfile() {
  WithProbeTemplate();
  var release=new SubmissionWorkflowRelease(profile.Repository,91,"v1.2.3",new('a',40),Sha,new('c',64),new('d',64));
  string Expand(SubmissionWorkflowRelease r,string suffix) {
   string run=Path.Combine(root,suffix);
   return AutomationReleaseDiscovery.Expand(profile,r,run,Path.Combine(run,"source"),"1.2.3").Endurance!.Plan.ReservationId;
  }
  var first=Expand(release,"first");
  Assert.That(Expand(release with{ReleaseId=92},"next-release"),Is.Not.EqualTo(first));
  Assert.That(Expand(release with{ProfileSnapshotSha256=new('e',64)},"next-profile"),Is.Not.EqualTo(first));
 }
 [Test]public void InvalidReservationStopsExpansionAndExplicitSettingsBeforeHardwareOrProducerCopy() {
  WithProbeTemplate();
  var template=AutomationFiles.Read<SubmissionAutomationSettings>(profile.SettingsTemplate.Path);
  var invalid=template with{Endurance=template.Endurance! with{Plan=template.Endurance.Plan with{ReservationId="weatherlink-rehearsal"}}};
  File.WriteAllBytes(profile.SettingsTemplate.Path,JsonSerializer.SerializeToUtf8Bytes(invalid,AutomationFiles.Json));
  profile=profile with{SettingsTemplate=new(profile.SettingsTemplate.Path,AutomationFiles.Hash(profile.SettingsTemplate.Path))};
  string run=Path.Combine(root,"invalid-run");
  Assert.Throws<InvalidDataException>(()=>ExpandProbe(run));Assert.That(Directory.Exists(run),Is.False);
  Assert.Throws<InvalidDataException>(()=>AutomationRequest.Load(["--settings",profile.SettingsTemplate.Path,"--settings-sha256",profile.SettingsTemplate.Sha256]));
  var checkedSettings=AutomationRequest.ReadForCheck(profile.SettingsTemplate.Path,profile.SettingsTemplate.Sha256);
  Assert.That(SubmissionAutomationConfiguration.Check(checkedSettings.Settings).MissingBindings,Does.Contain("Endurance.Plan.ReservationId (GUID in N format)"));
  Assert.Throws<InvalidDataException>(()=>AutomationRequest.ReadForCheck(profile.SettingsTemplate.Path,new string('0',64)));
 }
 [Test]public void ModifiedGeneratedProducerSettingsStopRecoveryWithoutReplacingThem() {
  WithProbeTemplate();string run=Path.Combine(root,"new-run");var settings=ExpandProbe(run);
  File.AppendAllText(settings.Endurance!.Probe.SettingsFile!," ");
  byte[] changed=File.ReadAllBytes(settings.Endurance.Probe.SettingsFile!);
  Assert.Throws<InvalidDataException>(()=>ExpandProbe(run));
  Assert.That(File.ReadAllBytes(settings.Endurance.Probe.SettingsFile!),Is.EqualTo(changed));
 }
 [Test]public void ModifiedProbeTemplateFailsBeforeCreatingAReleaseProducer() {
  WithProbeTemplate();string run=Path.Combine(root,"new-run");
  File.AppendAllText(Path.Combine(root,"probe-settings-template.json")," ");
  Assert.Throws<InvalidDataException>(()=>ExpandProbe(run));
  Assert.That(Directory.Exists(run),Is.False);
 }
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
