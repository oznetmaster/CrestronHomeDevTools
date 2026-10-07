// Copyright (c) 2026 Neil Colvin. MIT licensed.
using CrestronHomeDevTools.Automation;
using NUnit.Framework;

namespace CrestronHomeDevTools.Tests;

[TestFixture]
public sealed class AutomationInstalledAppInventoryTests
{
 private string root=null!;
 [SetUp] public void Setup() {
  root=Path.Combine(TestContext.CurrentContext.WorkDirectory,"retained-inventory-"+Guid.NewGuid().ToString("N"));
  Directory.CreateDirectory(Path.Combine(root,"installed-app"));
 }
 [TearDown] public void Cleanup()=>Directory.Delete(root,true);
 private string Attempt()=>Directory.CreateDirectory(Path.Combine(root,"installed-app","recovery-attempts",Guid.NewGuid().ToString("N"))).FullName;
 private static void Files(string folder,int count) {
  for(int i=0;i<count;i++)File.WriteAllText(Path.Combine(folder,i.ToString("D4")+".json"),"retained evidence");
 }
 [Test] public void MultipleBoundedAttemptsRetainEveryFileBeyondTheSingleRunLimit() {
  Files(Attempt(),2100);Files(Attempt(),2100);
  var first=AutomationInstalledApp.Inventory(root);
  Assert.That(first,Has.Length.EqualTo(4200));
  Assert.That(first,Is.EqualTo(AutomationInstalledApp.Inventory(root)));
  foreach(var file in first)Assert.That(file.Sha256,Is.EqualTo(AutomationFiles.Hash(Path.Combine(root,file.RelativePath))));
 }
 [Test] public void OneOversizedAttemptStillFails() {
  Files(Attempt(),4097);
  Assert.Throws<InvalidDataException>(()=>AutomationInstalledApp.Inventory(root));
 }
 [Test] public void StepAggregationPreservesSeparateRetryBoundsAndDetectsChanges() {
  string step=Path.Combine(root,"installed-app","steps","000");
  string attempts=Path.Combine(step,"installed-app","recovery-attempts");
  foreach(string id in new[]{Guid.NewGuid().ToString("N"),Guid.NewGuid().ToString("N")})
   Files(Directory.CreateDirectory(Path.Combine(attempts,id)).FullName,2100);
  var child=AutomationInstalledApp.Inventory(step);
  AutomationFiles.Write(Path.Combine(step,"installed-app-tests.json"),new{InputSha256="test",Files=child});
  AutomationInstalledApp.VerifyRetained(step);
  var aggregate=AutomationInstalledApp.Inventory(root);
  Assert.That(aggregate,Has.Length.EqualTo(4201));
  AutomationFiles.Write(Path.Combine(root,"installed-app-tests.json"),new{InputSha256="test",Files=aggregate});
  AutomationInstalledApp.VerifyRetained(root);
  File.AppendAllText(Path.Combine(step,child[0].RelativePath),"changed");
  Assert.Throws<InvalidDataException>(()=>AutomationInstalledApp.VerifyRetained(root));
 }
 [Test] public void NestedOversizedAttemptStillFails() {
  string nested=Directory.CreateDirectory(Path.Combine(root,"installed-app","steps","000","installed-app","recovery-attempts",Guid.NewGuid().ToString("N"))).FullName;
  Files(nested,4097);
  Assert.Throws<InvalidDataException>(()=>AutomationInstalledApp.Inventory(root));
 }
 [TestCase("128")][TestCase("0000")][TestCase("other")]
 public void InvalidStepCannotCreateAnotherPartition(string name) {
  Directory.CreateDirectory(Path.Combine(root,"installed-app","steps",name));
  Assert.Throws<InvalidDataException>(()=>AutomationInstalledApp.Inventory(root));
 }
 [Test] public void ArbitraryNestedRetryFolderCannotEvadeAnOperationLimit() {
  string nested=Directory.CreateDirectory(Path.Combine(root,"installed-app","other","recovery-attempts",Guid.NewGuid().ToString("N"))).FullName;
  Files(nested,4097);
  Assert.Throws<InvalidDataException>(()=>AutomationInstalledApp.Inventory(root));
 }
 [Test] public void RetryDirectoryCountRemainsBounded() {
  for(int i=0;i<129;i++)Attempt();
  Assert.Throws<InvalidDataException>(()=>AutomationInstalledApp.Inventory(root));
 }
 [Test] public void OrdinaryEvidenceStillHasItsOriginalLimit() {
  Files(Path.Combine(root,"installed-app"),4097);
  Assert.Throws<InvalidDataException>(()=>AutomationInstalledApp.Inventory(root));
 }
 [Test] public void ArbitrarySubdirectoriesCannotBecomeUnboundedRetryPartitions() {
  Directory.CreateDirectory(Path.Combine(root,"installed-app","recovery-attempts","not-an-attempt"));
  Assert.Throws<InvalidDataException>(()=>AutomationInstalledApp.Inventory(root));
 }
}
