// Copyright (c) 2026 Neil Colvin. MIT licensed.
using NUnit.Framework;
using System.Text.Json;

namespace CrestronHomeDevTools.Tests;

[TestFixture]
public sealed class WeatherLinkProducerTests
{
 private string directory=null!,baselinePath=null!;
 private ProducerSettingsInput input=null!;
 private static readonly SubmissionEvidenceIdentity Identity=new(new('a',64),new('b',40),new('c',64),new('d',64));
 private static SubmissionEndurancePlan Plan=>new(Identity,new("endurance",TimeSpan.FromHours(24)),"processor:example.invalid","instance","reservation","producer",TimeSpan.FromMinutes(5),TimeSpan.FromMinutes(1));
 private static LifetimeBaseline Baseline=>new(DateTimeOffset.Parse("2026-09-24T10:00:00Z"),DateTimeOffset.Parse("2026-09-24T10:00:01Z"),1,[2,3],DateTimeOffset.Parse("2026-09-24T11:00:00Z"),2,"synthetic-lifetime");
 [SetUp]public void Setup() {
  directory=Path.Combine(TestContext.CurrentContext.WorkDirectory,"weather-producer-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
  baselinePath=Path.Combine(directory,"baseline.json");
  input=new("station.invalid",Path.Combine(directory,"bindings.json"),Path.Combine(directory,"candidate.pkg"),"catalogue","example.invalid",100,101,"Test weather","Weather model","1.0.0.0",Identity,"instance","endurance",baselinePath);
 }
 [TearDown]public void Cleanup()=>Directory.Delete(directory,true);
 [Test]public async Task WeatherParsingBootWindowsAndAsynchronousValuesHaveSelfContainedFixtures()=>await OfflineChecks.RunAsync();
 [Test]public async Task FirstProbeCapturesLifetimeAndLaterInvocationsReuseItWithoutRecapturing() {
  int calls=0;
  Task<LifetimeBaseline> Acquire(CancellationToken token){calls++;return Task.FromResult(Baseline);}
  var first=await LifetimeBaselineStore.ReadOrCreateAsync(baselinePath,Plan,input,Acquire,default);
  byte[] bytes=File.ReadAllBytes(baselinePath);
  var next=await LifetimeBaselineStore.ReadOrCreateAsync(baselinePath,Plan,input,Acquire,default);
  Assert.That(next.Identity,Is.EqualTo(first.Identity));Assert.That(calls,Is.EqualTo(1));
  Assert.That(File.ReadAllBytes(baselinePath),Is.EqualTo(bytes));
 }
 [Test]public async Task ChangedRunOrSettingsCannotRebaselineAnExistingAttempt() {
  await LifetimeBaselineStore.ReadOrCreateAsync(baselinePath,Plan,input,_=>Task.FromResult(Baseline),default);
  byte[] bytes=File.ReadAllBytes(baselinePath);int calls=0;
  Task<LifetimeBaseline> Acquire(CancellationToken token){calls++;return Task.FromResult(Baseline);}
  Assert.ThrowsAsync<InvalidDataException>(()=>LifetimeBaselineStore.ReadOrCreateAsync(baselinePath,Plan with{ReservationId="other"},input,Acquire,default));
  Assert.ThrowsAsync<InvalidDataException>(()=>LifetimeBaselineStore.ReadOrCreateAsync(baselinePath,Plan,input with{DeviceId=102},Acquire,default));
  Assert.That(calls,Is.Zero);Assert.That(File.ReadAllBytes(baselinePath),Is.EqualTo(bytes));
 }
 [Test]public void InterruptedFirstAcquisitionIsRetainedAndCannotSilentlyRestart() {
  int calls=0;
  Task<LifetimeBaseline> Interrupted(CancellationToken token){calls++;throw new IOException("Synthetic interruption");}
  Assert.ThrowsAsync<IOException>(()=>LifetimeBaselineStore.ReadOrCreateAsync(baselinePath,Plan,input,Interrupted,default));
  Assert.That(File.Exists(baselinePath),Is.True);
  Assert.ThrowsAsync<JsonException>(()=>LifetimeBaselineStore.ReadOrCreateAsync(baselinePath,Plan,input,Interrupted,default));
  Assert.That(calls,Is.EqualTo(1));
 }
 [Test]public void MutableBaselineCannotChangeThePinnedProducerDirectory() {
  Assert.Throws<InvalidDataException>(()=>(input with{BaselineFile=Path.Combine(AppContext.BaseDirectory,"baseline.json")}).Validate());
  Assert.DoesNotThrow(()=>(input with{BaselineFile=Path.Combine(Path.GetTempPath(),"weather-producer-baseline.json")}).Validate());
 }
}
