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
 [TestCase("Failed")][TestCase("OutcomeUnknown")][TestCase("AttentionRequired")]
 public void PublicControllerReadsActionableWorkerNoticeWithoutAdvancingRun(string state) {
  var entry=RegisterOperator();string registry=OperatorRegistry(entry);
  string status=Path.Combine(root,"status");Directory.CreateDirectory(status);
  AutomationWorker.Notification(status,[new(entry.Profile,entry.ReleaseId,entry.Mode,state,"AppTests","synthetic-failure")],DateTimeOffset.UtcNow);
  var alert=SubmissionOperatorAlerts.Read(registry,[entry.Profile],status).Single();
  Assert.That(alert.State,Is.EqualTo(state));Assert.That(alert.Stage,Is.EqualTo("AppTests"));
  Assert.That(alert.EvidenceDirectory,Is.EqualTo(Path.Combine(settings.PrivateRoot,SubmissionWorkflow.RunKey(settings.Release with {ReleaseId=entry.ReleaseId}))));
  Assert.That(Directory.Exists(alert.EvidenceDirectory),Is.False,"Reading a notice cannot create or advance a run.");
 }
 [TestCase("Ready")][TestCase("Running")][TestCase("Waiting")][TestCase("NeedsInput")][TestCase("Completed")]
 public void HealthyAndPhysicalInputStatusesDoNotCreateGenericFailureWindows(string state) {
  var entry=RegisterOperator();string registry=OperatorRegistry(entry),status=Path.Combine(root,"status");Directory.CreateDirectory(status);
  AutomationWorker.Notification(status,[new(entry.Profile,entry.ReleaseId,entry.Mode,state,"Endurance","synthetic")],DateTimeOffset.UtcNow);
  Assert.That(SubmissionOperatorAlerts.Read(registry,[entry.Profile],status),Is.Empty);
 }
 [Test] public void BusyObservationDoesNotReopenDismissedFailureAndDismissalSurvivesRestart() {
  var entry=RegisterOperator();string registry=OperatorRegistry(entry),status=Path.Combine(root,"status");Directory.CreateDirectory(status);
  var failure=new AutomationWorker.Status(entry.Profile,entry.ReleaseId,entry.Mode,"Failed","AppTests","synthetic");
  AutomationWorker.Notification(status,[failure],DateTimeOffset.UtcNow);
  var key=SubmissionOperatorAlerts.Read(registry,[entry.Profile],status).Single().Key;
  string saved=Path.Combine(root,"controller","dismissals.json");var ledger=new SubmissionAlertAcknowledgements(saved);ledger.Dismiss(key);
  AutomationWorker.Notification(status,[failure with {State="Busy",Stage=null,Reason="workflow-in-use"}],DateTimeOffset.UtcNow);
  AutomationWorker.Notification(status,[failure],DateTimeOffset.UtcNow);
  Assert.That(SubmissionOperatorAlerts.Read(registry,[entry.Profile],status).Single().Key,Is.EqualTo(key));
  Assert.That(new SubmissionAlertAcknowledgements(saved).IsDismissed(key),Is.True);
  ledger.RetainActive([]);Assert.That(new SubmissionAlertAcknowledgements(saved).IsDismissed(key),Is.False,"A later new failure after recovery should alert again.");
 }
 [Test] public void AlertsRequireMatchingTrustedProfileAndRelease() {
  var entry=RegisterOperator();string registry=OperatorRegistry(entry),status=Path.Combine(root,"status");Directory.CreateDirectory(status);
  AutomationWorker.Notification(status,[new(entry.Profile,entry.ReleaseId+1,entry.Mode,"Failed","AppTests","synthetic")],DateTimeOffset.UtcNow);
  Assert.Throws<IOException>(()=>SubmissionOperatorAlerts.Read(registry,[entry.Profile],status));
  File.AppendAllText(entry.SettingsPath," ");Assert.Throws<InvalidDataException>(()=>SubmissionOperatorAlerts.Read(registry,[entry.Profile],status));
 }
 [TestCase("Completed","Retain",null)]
 [TestCase("NeedsInput","SignReview","rehearsal-ready-for-review")]
 public void ControllerReportsFinalRetentionAndUnsignedReviewOnce(string state,string stage,string? reason) {
  var entry=RegisterOperator();string registry=OperatorRegistry(entry),status=Path.Combine(root,"status");Directory.CreateDirectory(status);
  AutomationWorker.Notification(status,[new(entry.Profile,entry.ReleaseId,entry.Mode,state,stage,reason)],DateTimeOffset.UtcNow);
  Assert.That(SubmissionOperatorAlerts.Read(registry,[entry.Profile],status).Single().State,Is.EqualTo(state));
 }
}
