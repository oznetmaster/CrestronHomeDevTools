// Copyright (c) 2026 Neil Colvin. MIT licensed.
using System.Text.Json;
using CrestronHomeDevTools.Automation;
using CrestronHomeNUnit.Android;
using NUnit.Framework;

namespace CrestronHomeDevTools.Tests;

[TestFixture]
public sealed class AutomationAndroidReadinessTests
{
 private string root=null!,profilePath=null!;
 private AndroidSessionProfile profile=null!;
 [SetUp] public void Setup() {
  root=Path.Combine(TestContext.CurrentContext.WorkDirectory,"android-readiness-"+Guid.NewGuid().ToString("N"));
  Directory.CreateDirectory(root);
  string adb=Path.Combine(root,"adb.exe");File.WriteAllText(adb,"");
  profile=new(adb,"synthetic","com.crestron.phoenix.app","Submission",Path.Combine(root,"android.lock")) {AllowedStartingHomes=["Development"]};
  profilePath=Path.Combine(root,"profile.json");File.WriteAllText(profilePath,JsonSerializer.Serialize(profile));
 }
 [TearDown] public void Cleanup()=>Directory.Delete(root,true);
 private AndroidHierarchy Screen(string name,string overlay="")=>new($"""
  <hierarchy><node package="com.crestron.phoenix.app" resource-id="com.crestron.phoenix.app:id/home_wholeHouse_name" text="{name}" enabled="true" bounds="[0,0][100,100]" />
  <node package="com.crestron.phoenix.app" password="true" text="synthetic-secret" content-desc="synthetic-secret" />{overlay}</hierarchy>
  """,profile.Application);
 [TestCase("Submission",true)]
 [TestCase("Development",true)]
 [TestCase("Unconfigured",false)]
 public async Task CapturesBeforeCheckingExplicitHome(string home,bool expected) {
  string folder=Path.Combine(root,"check");
  Task Check()=>AutomationAndroidReadiness.Check(profilePath,folder,default,_=>Task.FromResult(Screen(home)));
  if(expected)await Check();else await Assert.ThrowsAsync<InvalidDataException>(async()=>await Check());
  string screen=File.ReadAllText(Path.Combine(folder,"screen.xml"));
  Assert.That(screen,Does.Not.Contain("synthetic-secret"));
  using var report=JsonDocument.Parse(File.ReadAllText(Path.Combine(folder,"readiness.json")));
  Assert.That(report.RootElement.GetProperty("Passed").GetBoolean(),Is.EqualTo(expected));
  Assert.That(report.RootElement.GetProperty("Captured").GetBoolean(),Is.True);
  Assert.That(report.RootElement.GetProperty("InputSent").GetBoolean(),Is.False);
  using var lease=AndroidSessionLease.Acquire(profile.LockPath,Guid.NewGuid().ToString("N"));lease.Release();
  await Assert.ThrowsAsync<InvalidDataException>(async()=>await Check(),"Existing evidence cannot be overwritten.");
 }
 [TestCase("android:id/aerr_close","android")]
 [TestCase("android:id/aerr_wait","android")]
 [TestCase("android:id/aerr_restart","android")]
 [TestCase("com.crestron.phoenix.app:id/homeswitcher_title","com.crestron.phoenix.app")]
 public void RejectsBlockingOverlayEvenWithHomeBehindIt(string id,string package) {
  var screen=Screen("Submission",$"<node resource-id=\"{id}\" package=\"{package}\" />");
  Assert.That(()=>AutomationAndroidReadiness.RequireReadyHome(screen,profile),Throws.Exception);
 }
 [Test] public async Task CaptureFailureRecordsFailureAndReleasesLease() {
  string folder=Path.Combine(root,"failed");
  await Assert.ThrowsAsync<IOException>(async()=>await AutomationAndroidReadiness.Check(profilePath,folder,default,_=>throw new IOException("synthetic capture failure")));
  using var report=JsonDocument.Parse(File.ReadAllText(Path.Combine(folder,"readiness.json")));
  Assert.That(report.RootElement.GetProperty("Passed").GetBoolean(),Is.False);
  Assert.That(report.RootElement.GetProperty("Captured").GetBoolean(),Is.False);
  using var lease=AndroidSessionLease.Acquire(profile.LockPath,Guid.NewGuid().ToString("N"));lease.Release();
 }
 [Test] public void MissingHomeIsNotReadiness() {
  Assert.That(()=>AutomationAndroidReadiness.RequireReadyHome(new AndroidHierarchy("<hierarchy/>",profile.Application),profile),Throws.Exception);
 }
}
