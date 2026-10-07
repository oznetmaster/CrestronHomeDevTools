// Copyright (c) 2026 Neil Colvin. MIT licensed.
using System.Text.Json;
using CrestronHomeDevTools.Automation;
using NUnit.Framework;
namespace CrestronHomeDevTools.Tests;

public sealed partial class AutomationInstalledAppTests
{
 private async Task<(AutomationAppStepRecovery.Request Request,string Attempt,string Pin)> UnstartedToolAttempt() {
  var r=(await FailedTest()) with {SchemaVersion=2,ScopeRevision=new("synthetic repair",[],[])};
  string attempt=Path.Combine(context.RunDirectory,"installed-app","recovery-attempts",r.AttemptId);
  Directory.CreateDirectory(attempt);
  string binding=Path.Combine(attempt,"attempt.json");
  File.WriteAllText(binding,JsonSerializer.Serialize(new{Request=r,InputSha256=context.Checkpoint.InputSha256,Tools=new Dictionary<string,string>{{"old-tool",new('a',64)}}},AutomationFiles.Json));
  File.WriteAllText(Path.Combine(attempt,"original-evidence.json"),"[]");
  return(r,attempt,AutomationFiles.Hash(binding));
 }
 [Test] public async Task ExplicitUnstartedToolRepairPreservesOriginalBindingAndCannotRepeat() {
  var (request,attempt,pin)=await UnstartedToolAttempt();
  AutomationAppStepRecovery.AuthorizeUnstartedToolRepair(context.RunDirectory,request,pin,"Dependency reader mismatch before invocation");
  Assert.That(AutomationFiles.Hash(Path.Combine(attempt,"attempt.json")),Is.EqualTo(pin));
  using var doc=JsonDocument.Parse(File.ReadAllText(Path.Combine(attempt,"tool-repair.json")));
  Assert.That(doc.RootElement.GetProperty("OriginalBindingSha256").GetString(),Is.EqualTo(pin));
  Assert.That(doc.RootElement.GetProperty("Tools").EnumerateObject().Count(),Is.EqualTo(3));
  Assert.Throws<InvalidDataException>(()=>AutomationAppStepRecovery.AuthorizeUnstartedToolRepair(context.RunDirectory,request,pin,"another repair"));
  Assert.That(calls,Is.Zero);
 }
 [TestCase("invocations")][TestCase("installed-app")][TestCase("reconciliation")]
 public async Task ToolRepairRejectsAnyStartedChildDirectory(string child) {
  var (r,a,p)=await UnstartedToolAttempt();Directory.CreateDirectory(Path.Combine(a,child));
  Assert.Throws<InvalidDataException>(()=>AutomationAppStepRecovery.AuthorizeUnstartedToolRepair(context.RunDirectory,r,p,"repair"));
  Assert.That(File.Exists(Path.Combine(a,"tool-repair.json")),Is.False);
 }
 [TestCase("pin")][TestCase("request")][TestCase("evidence")][TestCase("file")]
 public async Task ToolRepairRejectsChangedInputs(string change) {
  var (r,a,p)=await UnstartedToolAttempt();
  if(change=="pin")p=new('f',64);
  if(change=="request")r=r with{SourceSha256=new('b',64)};
  if(change=="evidence")File.AppendAllText(Path.Combine(context.RunDirectory,r.FailedOutcome)," ");
  if(change=="file")File.WriteAllText(Path.Combine(a,"unexpected.json"),"{}");
  Assert.Throws<InvalidDataException>(()=>AutomationAppStepRecovery.AuthorizeUnstartedToolRepair(context.RunDirectory,r,p,"repair"));
  Assert.That(File.Exists(Path.Combine(a,"tool-repair.json")),Is.False);
 }
}
