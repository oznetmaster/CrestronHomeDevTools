// Copyright (c) 2026 Neil Colvin. MIT licensed.
using System.Xml.Linq;
using CrestronHomeNUnit.Android;

namespace CrestronHomeDevTools.Automation;

internal static class AutomationAndroidReadiness
{
 internal static async Task Check(string profilePath,string folder,CancellationToken token,
  Func<CancellationToken,Task<AndroidHierarchy>>? capture=null) {
  var profile=AndroidWorkflowSession.Read<AndroidSessionProfile>(profilePath);
  profile.Validate();
  if(Directory.Exists(folder))throw new InvalidDataException("Android readiness evidence already exists; inspect it before retrying.");
  Directory.CreateDirectory(folder);
  string profileHash=AutomationFiles.Hash(profilePath);
  bool passed=false,captured=false;
  using var lease=AndroidSessionLease.Acquire(profile.LockPath,Guid.NewGuid().ToString("N"));
  try {
   var device=new AndroidDevice(new AdbCommandTransport(profile.AdbExecutable,profile.DeviceSerial,TimeSpan.FromSeconds(25)),profile.Application);
   var hierarchy=await (capture?.Invoke(token)??device.CaptureAsync(token));
   // Retain the actual screen BEFORE assertions: an ANR/setup failure must have evidence too.
   File.WriteAllText(Path.Combine(folder,"screen.xml"),hierarchy.MaskedXml);
   captured=true;
   RequireReadyHome(hierarchy,profile);
   if(profileHash!=AutomationFiles.Hash(profilePath))throw new InvalidDataException("Android readiness profile changed during capture.");
   passed=true;
  } finally {
   try {AutomationFiles.Write(Path.Combine(folder,"readiness.json"),new{ObservedUtc=DateTimeOffset.UtcNow,Passed=passed,Captured=captured,ProfileSha256=profileHash,InputSent=false});}
   finally {lease.Release();}
  }
 }

 internal static void RequireReadyHome(AndroidHierarchy hierarchy,AndroidSessionProfile profile) {
  var xml=XDocument.Parse(hierarchy.MaskedXml);
  if(xml.Descendants("node").Any(n=>((string?)n.Attribute("resource-id")) is "android:id/aerr_close" or "android:id/aerr_wait" or "android:id/aerr_restart"))
   throw new InvalidDataException("Android reports an unresponsive or crashed application. Inspect the retained screen; no input was sent.");
  if(profile.Application!="com.crestron.phoenix.app") {
   _=hierarchy.RequireUnique(new(AndroidSelectorKind.Text,profile.ExpectedHomeText));
   return;
  }
  string current=hierarchy.RequireUnique(new(AndroidSelectorKind.ResourceId,"com.crestron.phoenix.app:id/home_wholeHouse_name")).Text;
  if(current!=profile.ExpectedHomeText && !profile.AllowedStartingHomes.Contains(current,StringComparer.Ordinal))
   throw new InvalidDataException("The app is not on an explicitly configured starting Home. Inspect the retained screen; no input was sent.");
  CrestronHomePages.RequireHome(hierarchy,current);
 }
}
