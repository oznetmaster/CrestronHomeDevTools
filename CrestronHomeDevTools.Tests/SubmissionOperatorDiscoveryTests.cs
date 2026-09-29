// Copyright (c) 2026 Neil Colvin. MIT licensed.
using System.Text.Json;
using CrestronHomeDevTools.Automation;
using NUnit.Framework;
namespace CrestronHomeDevTools.Tests;

public sealed partial class AutomationInstalledAppTests
{
 private SubmissionAutomationRegistration RegisterOperator(string name="demo",long releaseId=1) {
  var release=settings.Release with {ReleaseId=releaseId};
  var inbox=new SubmissionOperatorInbox(Path.Combine(root,"run-"+releaseId,"operator-inbox"),SubmissionWorkflow.RunKey(release));
  var value=settings with {Release=release,OperatorInbox=inbox};
  string path=Path.Combine(root,$"settings-{releaseId}.json");
  File.WriteAllText(path,JsonSerializer.Serialize(value,AutomationFiles.Json));
  return new(name,releaseId,value.Mode,path,AutomationFiles.Hash(path));
 }
 private string OperatorRegistry(params SubmissionAutomationRegistration[] entries) {
  string path=Path.Combine(root,"registry.json");
  File.WriteAllText(path,JsonSerializer.Serialize(new SubmissionAutomationRegistry(1,entries),AutomationFiles.Json));return path;
 }
 [Test] public void PersistentOperatorFindsLaterReleaseWithoutPerRunInstallation() {
  string path=OperatorRegistry();Assert.That(SubmissionOperatorDiscovery.Read(path,["demo"]),Is.Empty);
  var first=RegisterOperator();OperatorRegistry(first);
  var inbox=SubmissionOperatorDiscovery.Read(path,["demo"]).Single();
  Assert.That(Directory.Exists(inbox.Directory),Is.False,"Discovery must not create run evidence.");
  var request=SubmissionOperatorStep.Create(inbox.Directory,inbox.RunKey,"synthetic","synthetic only","No hardware action",TimeSpan.FromMinutes(1));
  Assert.That(SubmissionOperatorStep.Pending(inbox.Directory,inbox.RunKey),Is.EqualTo(new[]{request}));
  SubmissionOperatorStep.Respond(request,SubmissionOperatorOutcome.Done);SubmissionOperatorInboxLifecycle.Close(inbox);
  var second=RegisterOperator(releaseId:2);OperatorRegistry(first,second);
  var discovered=SubmissionOperatorDiscovery.Read(path,["demo"]);
  Assert.That(discovered,Has.Count.EqualTo(2));
  Assert.That(discovered.Count(SubmissionOperatorInboxLifecycle.IsClosed),Is.EqualTo(1));
  Assert.That(discovered.Select(i=>i.RunKey).Distinct().Count(),Is.EqualTo(2));
 }
 [Test] public void OperatorDoesNotOpenUnselectedProfileSettings() {
  var selected=RegisterOperator();var other=selected with {Profile="other",SettingsPath="missing",SettingsSha256="bad"};
  Assert.That(SubmissionOperatorDiscovery.Read(OperatorRegistry(selected,other),["demo"]),Has.Count.EqualTo(1));
 }
 [TestCase("modified")][TestCase("selector")][TestCase("duplicate")][TestCase("run-key")]
 public void OperatorRejectsUntrustedOrMismatchedRegistration(string change) {
  var registration=RegisterOperator();
  if(change=="modified")File.AppendAllText(registration.SettingsPath," ");
  if(change=="selector")registration=registration with {ReleaseId=2};
  if(change=="run-key") {
   var value=AutomationFiles.Read<SubmissionAutomationSettings>(registration.SettingsPath);
   File.WriteAllText(registration.SettingsPath,JsonSerializer.Serialize(value with {OperatorInbox=value.OperatorInbox! with {RunKey=new('0',64)}},AutomationFiles.Json));
   registration=registration with {SettingsSha256=AutomationFiles.Hash(registration.SettingsPath)};
  }
  string path=change=="duplicate"?OperatorRegistry(registration,registration):OperatorRegistry(registration);
  Assert.Throws<InvalidDataException>(()=>SubmissionOperatorDiscovery.Read(path,["demo"]));
 }
 [TestCase("")][TestCase("demo,demo")][TestCase("../other")]
 public void OperatorProfileSelectionMustBeExplicit(string profiles) {
  Assert.Throws<ArgumentException>(()=>SubmissionOperatorDiscovery.Read(OperatorRegistry(),profiles.Split(',')));
 }
}
