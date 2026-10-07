// Copyright (c) 2026 Neil Colvin. Licensed under the MIT License.
using NUnit.Framework;
namespace CrestronHomeDevTools.Tests;
[TestFixture]
public sealed class SubmissionRunRetentionTests
{
 private static string K(int number)=>number.ToString("x64");
 private static SubmissionRetentionGeneration G(int number,bool closed=true,bool submitted=false,bool pinned=false,
  int[]? dependencies=null,string repository="owner/driver",long? sequence=null)=>
  new(K(number),repository,sequence??number,closed,submitted,pinned,(dependencies??[]).Select(K).ToArray());
 [Test] public void NewRunKeepsOnePriorRoutineGeneration() {
  var p=SubmissionRunRetention.Plan(K(4),[G(1),G(2),G(3),G(4,false)]);
  Assert.That(p.Previous,Is.EqualTo(K(3)));Assert.That(p.Keep,Is.EquivalentTo(new[]{K(3),K(4)}));
  Assert.That(p.Remove,Is.EqualTo(new[]{K(1),K(2)}));
 }
 [Test] public void FirstRunHasNothingToRemove() {
  var p=SubmissionRunRetention.Plan(K(1),[G(1,false)]);
  Assert.That(p.Previous,Is.Null);Assert.That(p.Remove,Is.Empty);
 }
 [Test] public void ActiveOlderGenerationIsPreservedAlongsideOneClosedArchive() {
  var p=SubmissionRunRetention.Plan(K(4),[G(1),G(2),G(3,false),G(4,false)]);
  Assert.That(p.Previous,Is.EqualTo(K(2)));Assert.That(p.Keep,Is.EquivalentTo(new[]{K(2),K(3),K(4)}));
  Assert.That(p.Remove,Is.EqualTo(new[]{K(1)}));
 }
 [Test] public void SubmittedAndPinnedRecordsDoNotConsumeRoutineArchiveSlot() {
  var p=SubmissionRunRetention.Plan(K(5),[G(1),G(2),G(3,submitted:true),G(4,pinned:true),G(5,false)]);
  Assert.That(p.Previous,Is.EqualTo(K(2)));Assert.That(p.Keep,Is.EquivalentTo(new[]{K(2),K(3),K(4),K(5)}));
 }
 [Test] public void TransitiveDependenciesOfSubmittedEvidenceAreProtected() {
  var p=SubmissionRunRetention.Plan(K(6),[G(1),G(2,dependencies:[1]),G(3,submitted:true,dependencies:[2]),G(4),G(5),G(6,false)]);
  Assert.That(p.Remove,Is.EqualTo(new[]{K(4)}));
 }
 [Test] public void CurrentAndPreviousEvidenceDependenciesAreProtected() {
  var p=SubmissionRunRetention.Plan(K(5),[G(1),G(2),G(3),G(4,dependencies:[1]),G(5,false,dependencies:[2])]);
  Assert.That(p.Remove,Is.EqualTo(new[]{K(3)}));
 }
 [Test] public void DependencyCycleDoesNotLoseOrLoopOverEvidence() {
  var p=SubmissionRunRetention.Plan(K(4),[G(1,dependencies:[2]),G(2,dependencies:[1]),G(3,dependencies:[1]),G(4,false)]);
  Assert.That(p.Remove,Is.Empty);
 }
 [Test] public void AnotherDriverAndItsDependenciesArePreserved() {
  var p=SubmissionRunRetention.Plan(K(4),[G(1),G(2),G(3),G(4,false),G(5,dependencies:[1],repository:"owner/other")]);
  Assert.That(p.Remove,Is.EqualTo(new[]{K(2)}));Assert.That(p.Keep,Does.Contain(K(5)));
 }
 [Test] public void RepositoryComparisonIsCaseInsensitive() {
  var p=SubmissionRunRetention.Plan(K(3),[G(1,repository:"OWNER/DRIVER"),G(2),G(3,false)]);
  Assert.That(p.Remove,Is.EqualTo(new[]{K(1)}));
 }
 [Test] public void ResumingOlderRunMustNotRotateNewerGenerations() {
  Assert.Throws<InvalidDataException>(()=>SubmissionRunRetention.Plan(K(2),[G(1),G(2,false),G(3,false)]));
 }
 [Test] public void MissingDependencyPreventsRemovalPlan() {
  Assert.Throws<InvalidDataException>(()=>SubmissionRunRetention.Plan(K(3),[G(1,dependencies:[9]),G(2),G(3,false)]));
 }
 [Test] public void DuplicateSequencePreventsAmbiguousRotation() {
  Assert.Throws<InvalidDataException>(()=>SubmissionRunRetention.Plan(K(3),[G(1,sequence:2),G(2),G(3,false)]));
 }
 [Test] public void DuplicateKeyIsRejected() {
  Assert.Throws<ArgumentException>(()=>SubmissionRunRetention.Plan(K(2),[G(1),G(1),G(2,false)]));
 }
 [Test] public void MissingCurrentIsRejected() {
  Assert.Throws<ArgumentException>(()=>SubmissionRunRetention.Plan(K(3),[G(1),G(2)]));
 }
 [TestCase("../elsewhere")][TestCase("C:/outside")][TestCase("")]
 public void GenerationKeysCannotContainPaths(string key) {
  Assert.Throws<ArgumentException>(()=>SubmissionRunRetention.Plan(key,[G(1) with {Key=key}]));
 }
 [Test] public void SelectionDoesNotMutateCallerInventory() {
  var dependencies=new[]{K(1)};var input=new[]{G(1),G(2),G(3,false) with{Dependencies=dependencies}};
  _=SubmissionRunRetention.Plan(K(3),input);
  Assert.That(input[2].Dependencies,Is.SameAs(dependencies));Assert.That(dependencies,Is.EqualTo(new[]{K(1)}));
 }
}
