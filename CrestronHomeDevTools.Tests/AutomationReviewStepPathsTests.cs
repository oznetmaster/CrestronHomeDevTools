// Copyright (c) 2026 Neil Colvin. MIT licensed.
using CrestronHomeDevTools.Automation;
using NUnit.Framework;

namespace CrestronHomeDevTools.Tests;
public sealed class AutomationReviewStepPathsTests
{
 [Test]
 public void EveryCompletedStepAndReplacementRetainsItsOwnProducerPins() {
  string[] prefixes=["installed-app/steps/000/installed-app/AndroidUI/","installed-app/steps/001/installed-app/AndroidUI/",
   "pre-endurance/installed-app/steps/000/installed-app/preparation-recovery/installed-app/AndroidUI/"];
  Assert.That(AutomationAndroidReview.ProducerPrefixes(prefixes.Select(p=>p+"producer-pin.json"),true,true),Is.EquivalentTo(prefixes));
 }
 [TestCase("other/AndroidUI/")][TestCase("installed-app/../AndroidUI/")][TestCase("installed-app/steps/arbitrary/installed-app/AndroidUI/")]
 public void UnexpectedProducerLocationCannotBecomeReviewAuthority(string path)=>
  Assert.Throws<InvalidDataException>(()=>AutomationAndroidReview.ProducerPrefixes([path+"producer-pin.json"],true,false));
 [Test]
 public void CombinedReviewRequiresBothPhases()=>Assert.Throws<InvalidDataException>(()=>AutomationAndroidReview.ProducerPrefixes(
  ["installed-app/steps/000/installed-app/AndroidUI/producer-pin.json"],true,true));
 [TestCase("installed-app/AndroidUI/a.json",null)]
 [TestCase("installed-app/steps/002/installed-app/AndroidUI/a.json","installed-app/steps/002")]
 [TestCase("pre-endurance/installed-app/AndroidUI/a.json","pre-endurance")]
 [TestCase("pre-endurance/installed-app/steps/000/installed-app/preparation-recovery/installed-app/AndroidUI/a.json",
  "pre-endurance/installed-app/steps/000/installed-app/preparation-recovery")]
 public void ObservationFilesAreRebasedFromTheirActualProducerRoot(string path,string? expected)=>
  Assert.That(AutomationReview.ObservationBase(path),Is.EqualTo(expected));
 [TestCase(true)][TestCase(false)]
 public void RecoveryObservationNeedsRetainedLineage(bool lineage) {
  string old="pre-endurance/installed-app/steps/000/installed-app/AndroidUI/system-outage/observations-0.json";
  string root="pre-endurance/installed-app/steps/000/installed-app/preparation-recovery/";
  string replacement=root+"installed-app/AndroidUI/system-outage/observations-0.json";
  var retained=new Dictionary<string,string>{{replacement,new('a',64)}};
  if(lineage){retained.Add(root+"repair.json",new('b',64));retained.Add(root+"original-evidence.json",new('c',64));}
  Assert.That(AutomationReview.ResolveObservationSource(old,retained),Is.EqualTo(lineage?replacement:old));
  retained.Add(old,new('d',64));
  Assert.That(AutomationReview.ResolveObservationSource(old,retained),Is.EqualTo(old));
 }

 [Test] public void AcceptedReplacementSelectsNewObservationsAndKeepsFailedProducerOutOfPassingAudit() {
  string folder=Path.Combine(TestContext.CurrentContext.WorkDirectory,"replacement-review-"+Guid.NewGuid().ToString("N"));
  Directory.CreateDirectory(folder);
  try {
   string step="pre-endurance/installed-app/steps/000/",id=new('a',32);
   string attempt="installed-app/recovery-attempts/"+id+"/";
   string before=step+"installed-app/preparation-recovery/installed-app/AndroidUI/";
   string after=step+attempt+"installed-app/AndroidUI/";
   string pointer=step+"installed-app/replacement.json";
   string path=Path.Combine(folder,pointer);Directory.CreateDirectory(Path.GetDirectoryName(path)!);
   File.WriteAllText(path,System.Text.Json.JsonSerializer.Serialize(new AutomationAppStepRecovery.Completion(id,attempt+"installed-app/AndroidUI/",new('b',64))));
   var retained=new Dictionary<string,string>{{pointer,AutomationFiles.Hash(path)},{step+attempt+"attempt.json",new('c',64)},
    {step+attempt+"original-evidence.json",new('d',64)},{after+"observations.json",new('e',64)},{before+"observations.json",new('f',64)}};
   Assert.That(AutomationAndroidReview.ActiveProducerPrefixes(folder,retained,[before,after]),Is.EqualTo(new[]{after}));
   Assert.That(AutomationReview.ResolveObservationSource(step+"installed-app/AndroidUI/observations.json",retained,folder),Is.EqualTo(after+"observations.json"));
   Assert.That(File.Exists(path),Is.True);
   File.AppendAllText(path," ");
   Assert.Throws<InvalidDataException>(()=>AutomationAndroidReview.ActiveProducerPrefixes(folder,retained,[before,after]));
  } finally {Directory.Delete(folder,true);}
 }
}
