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
}
