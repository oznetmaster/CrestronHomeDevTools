// Copyright (c) 2026 Neil Colvin. MIT licensed.
using System.Text.Json;
using CrestronHomeDevTools.Automation;
using NUnit.Framework;
namespace CrestronHomeDevTools.Tests;
[TestFixture] public sealed class AutomationRetentionTests
{
 string root=null!;SubmissionWorkflowStepContext context=null!;
 [SetUp] public void Setup(){
  root=Path.Combine(TestContext.CurrentContext.WorkDirectory,"retention-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
  var release=new SubmissionWorkflowRelease("synthetic/test",1,"synthetic",new('a',40),new('b',64),new('c',64),new('d',64));
  context=new(root,new(2,new('e',64),release,SubmissionWorkflowStage.Retain,SubmissionWorkflowStatus.Running,"synthetic-retention",null,[],DateTimeOffset.UtcNow));
  AutomationFiles.Write(Path.Combine(root,"delivery-evidence.json"),new SubmissionDeliveryReceipt(1,new('f',64),SubmissionDeliveryState.Submitted,
   "<synthetic@example.test>",DateTimeOffset.UtcNow,Upload:new("https://rehearsal.invalid/synthetic","local"),Mail:new("synthetic accepted SMTP"),Environment:SubmissionDeliveryEnvironment.Rehearsal));
 }
 [TearDown] public void Cleanup()=>Directory.Delete(root,true);
 [Test] public void LargeRehearsalRetainsEveryOriginalFileAndReusesExactReceipt(){
  string attempts=Directory.CreateDirectory(Path.Combine(root,"original-attempts")).FullName;
  for(int i=0;i<10080;i++)File.WriteAllText(Path.Combine(attempts,i.ToString("D5")+".txt"),"original failure or passing evidence "+i);
  File.WriteAllText(Path.Combine(root,"state.json"),"mutable workflow status");
  var result=AutomationDelivery.Retain(context,SubmissionAutomationMode.Rehearsal);
  Assert.That(result.Status,Is.EqualTo(SubmissionWorkflowStatus.Completed));
  using var saved=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root,"retained.json")));
  var files=saved.RootElement.GetProperty("Files");Assert.That(files.GetArrayLength(),Is.EqualTo(10081));
  foreach(var file in files.EnumerateArray())Assert.That(AutomationFiles.Hash(Path.Combine(root,file.GetProperty("RelativePath").GetString()!)),Is.EqualTo(file.GetProperty("Sha256").GetString()));
  Assert.That(saved.RootElement.GetProperty("CrestronAcceptanceEstablished").GetBoolean(),Is.False);
  Assert.That(AutomationDelivery.Retain(context,SubmissionAutomationMode.Rehearsal).Receipt,Is.EqualTo(result.Receipt));
  File.AppendAllText(Path.Combine(attempts,"00000.txt"),"changed");Assert.Throws<InvalidDataException>(()=>AutomationDelivery.Retain(context,SubmissionAutomationMode.Rehearsal));
 }
 [Test] public void RehearsalReceiptCannotCompleteProductionRetention(){
  Assert.Throws<InvalidDataException>(()=>AutomationDelivery.Retain(context,SubmissionAutomationMode.Submit));Assert.That(File.Exists(Path.Combine(root,"retained.json")),Is.False);
 }
}
