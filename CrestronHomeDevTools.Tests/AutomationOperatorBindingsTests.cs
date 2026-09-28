// Copyright (c) 2026 Neil Colvin. MIT licensed.
using System.Text.Json;
using CrestronHomeDevTools.Automation;
using NUnit.Framework;

namespace CrestronHomeDevTools.Tests;

public sealed partial class AutomationInstalledAppTests
{
 private SubmissionOperatorInbox ConfigureInboxes(string phase="main") {
  var inbox=new SubmissionOperatorInbox(Path.Combine(root,"operator-inbox"),SubmissionWorkflow.RunKey(settings.Release));
  settings=settings with {OperatorInbox=inbox};
  SetFixture(phase,JsonSerializer.SerializeToElement(new {OperatorInbox=inbox,SystemOutage=new {Manual=new {Inbox=inbox}}}));
  return inbox;
 }
 private void SetFixture(string phase,JsonElement fixture)=>settings=phase switch {
  "pre"=>settings with {PreEnduranceFixtureSettings=fixture},
  "post"=>settings with {PostEnduranceFixtureSettings=fixture},
  _=>settings with {InstalledAppFixtureSettings=fixture}};
 [TestCase("main")][TestCase("pre")][TestCase("post")]
 public void EachPhaseRequiresTheSameDeclaredInbox(string phase) {
  var inbox=ConfigureInboxes(phase);Assert.DoesNotThrow(()=>AutomationOperatorBindings.Validate(settings));
  SetFixture(phase,JsonSerializer.SerializeToElement(new {OperatorInbox=inbox,SystemOutage=new {Manual=new {Inbox=inbox with {Directory=Path.Combine(root,"other")}}}}));
  Assert.Throws<InvalidDataException>(()=>AutomationOperatorBindings.Validate(settings));
 }
 [TestCase("key")][TestCase("directory")][TestCase("missing-worker")][TestCase("malformed")]
 public async Task InboxMismatchStopsBeforeRunnerCredentialsOrBindingWrites(string change) {
  var inbox=ConfigureInboxes();
  if(change=="missing-worker")settings=settings with {OperatorInbox=null};
  else SetFixture("main",change=="malformed"?JsonSerializer.SerializeToElement(new {OperatorInbox="bad"}):
   JsonSerializer.SerializeToElement(new {OperatorInbox=inbox with {RunKey=change=="key"?new('0',64):inbox.RunKey,Directory=change=="directory"?Path.Combine(root,"elsewhere"):inbox.Directory}}));
  var controller=new SubmissionAutomationStages(settings,new('f',64),(_,_,_,_)=>throw new AssertionException("NUnit invoked"),
   _=>throw new AssertionException("Credentials opened"),installedApp:Run);
  await Assert.ThrowsAsync<InvalidDataException>(async()=>await controller.ExecuteAsync(context,default));
  Assert.That(calls,Is.Zero);Assert.That(File.Exists(Path.Combine(context.RunDirectory,"automation-binding.json")),Is.False);
  Assert.That(Directory.Exists(inbox.Directory),Is.False);
 }
 [Test] public void BadWorkerKeyCannotBeMadeValidByMatchingFixtureKey() {
  var inbox=ConfigureInboxes() with {RunKey=new('0',64)};
  settings=settings with {OperatorInbox=inbox,InstalledAppFixtureSettings=JsonSerializer.SerializeToElement(new {OperatorInbox=inbox})};
  Assert.Throws<InvalidDataException>(()=>AutomationOperatorBindings.Validate(settings));
 }
 [Test] public void TemplatePlaceholderOnlyAllowedBeforeExpansionAndMustAgreeEverywhere() {
  var inbox=new SubmissionOperatorInbox("${run}/operator-inbox","${runKey}");
  settings=settings with {OperatorInbox=inbox,InstalledAppFixtureSettings=JsonSerializer.SerializeToElement(new {OperatorInbox=inbox})};
  Assert.DoesNotThrow(()=>AutomationOperatorBindings.Validate(settings,true));
  Assert.Throws<InvalidDataException>(()=>AutomationOperatorBindings.Validate(settings));
  settings=settings with {PreEnduranceFixtureSettings=JsonSerializer.SerializeToElement(new {Manual=new {Inbox=inbox with {RunKey=new('0',64)}}})};
  Assert.That(SubmissionAutomationConfiguration.Check(settings,true).MissingBindings,Does.Contain("Every fixture operator inbox must match the workflow's declared directory and run key."));
 }
 [Test] public void ApplicationSpecificInboxWithoutProtocolFieldsIsNotReinterpreted() {
  settings=settings with {InstalledAppFixtureSettings=JsonSerializer.SerializeToElement(new {Inbox=new {Folder="app-specific"}})};
  Assert.DoesNotThrow(()=>AutomationOperatorBindings.Validate(settings));
 }
}
