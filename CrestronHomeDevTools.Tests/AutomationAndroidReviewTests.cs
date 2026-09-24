// Copyright (c) 2026 Neil Colvin. MIT licensed.
using System.Text.Json;
using CrestronHomeDevTools.Automation;
using NUnit.Framework;

namespace CrestronHomeDevTools.Tests;

[TestFixture]
public sealed class AutomationAndroidReviewTests
{
 private string root=null!,prefix=null!,receiptName=null!;
 private SubmissionWorkflowStepContext context=null!;
 private string P(string p)=>Path.Combine(root,p);
 private string Hash(string p)=>AutomationFiles.Hash(P(p));
 [SetUp]public void Setup()=>root=Path.Combine(TestContext.CurrentContext.WorkDirectory,"android-review-"+Guid.NewGuid().ToString("N"));
 [TearDown]public void Cleanup(){if(Directory.Exists(root))Directory.Delete(root,true);}
 private void Arrange(bool separate,int version=2) {
  prefix=separate?"installed-app/AndroidUI/":"nunit/AndroidUI/";
  receiptName=separate?"installed-app-tests.json":"windows-tests.json";
  Directory.CreateDirectory(P(prefix+"assembly"));Directory.CreateDirectory(P("review-inputs"));
  var release=new SubmissionWorkflowRelease("example/driver",2,"v1",new('a',40),new('b',64),new('c',64),new('d',64));
  context=new(root,new(1,new('e',64),release,SubmissionWorkflowStage.PrepareReview,SubmissionWorkflowStatus.Running,"operation",null,[],DateTimeOffset.UtcNow));
  File.WriteAllText(P("review-inputs/candidate.json"),"synthetic candidate: binding tests only");
  string runId=new('f',32);
  AutomationFiles.Write(P(prefix+"context.json"),new{RunId=runId,release.PackageSha256,ReleaseSourceCommit=release.SourceCommit});
  File.WriteAllText(P(prefix+"assembly/Fixture.dll"),"synthetic producer bytes");
  File.WriteAllText(P(prefix+"discovery.dump"),"<NUnitXml><test-run><test-suite type=\"Assembly\" name=\"Fixture.dll\"/></test-run></NUnitXml>");
  AutomationReview.WriteDocument(P(prefix+"producer-manifest.json"),new{schemaVersion=1,files=new[]{new{relativePath="Fixture.dll",sha256=Hash(prefix+"assembly/Fixture.dll")}}});
  var pin=new Dictionary<string,object>{["SchemaVersion"]=version,["RunId"]=runId,["PackageSha256"]=release.PackageSha256,
   ["ProducerManifestSha256"]=Hash(prefix+"producer-manifest.json").ToUpperInvariant(),["DiscoverySha256"]=Hash(prefix+"discovery.dump").ToUpperInvariant()};
  if(version==2){File.WriteAllText(P(prefix+"selection.json"),"{}");pin.Add("SelectionSha256",Hash(prefix+"selection.json").ToUpperInvariant());}
  AutomationFiles.Write(P(prefix+"producer-pin.json"),pin);Retain(separate);
 }
 private void Retain(bool separate) {
  File.WriteAllText(P(receiptName),JsonSerializer.Serialize(new{context.Checkpoint.InputSha256,Files=Directory.GetFiles(P(prefix),"*",SearchOption.AllDirectories)
   .Select(p=>new SubmissionWorkflowReceipt(Path.GetRelativePath(root,p),AutomationFiles.Hash(p))).ToArray()}));
  context.Checkpoint.CompletedStages[separate?SubmissionWorkflowStage.AppTests:SubmissionWorkflowStage.WindowsTests]=new(receiptName,Hash(receiptName));
 }
 private AutomationAndroidReview.Binding Bind(bool separate)=>AutomationAndroidReview.Bind(context,context.Checkpoint.Release,separate,P("review-inputs/candidate.json"),default);
 [TestCase(false,1)][TestCase(false,2)][TestCase(true,1)][TestCase(true,2)]
 public void RetainedCoordinatorPinsBindToCandidateAndSelectedRoute(bool separate,int version) {
  Arrange(separate,version);var binding=Bind(separate);
  using var pins=JsonDocument.Parse(File.ReadAllBytes(binding.PinsPath));var run=pins.RootElement.GetProperty("runs")[0];
  Assert.That(pins.RootElement.GetProperty("candidateSha256").GetString(),Is.EqualTo(Hash("review-inputs/candidate.json")));
  Assert.That(run.GetProperty("assemblySha256").GetString(),Is.EqualTo(Hash(prefix+"assembly/Fixture.dll")));
  Assert.That(run.TryGetProperty("selectionSha256",out _),Is.EqualTo(version==2));
  Assert.That(binding.Evidence[0].GetProperty("path").GetString(),Is.EqualTo(Path.GetDirectoryName(P(prefix+"context.json"))));
 }
 [TestCase(false)][TestCase(true)]public void UncompletedProducerCannotAuthorizePins(bool separate) {
  Arrange(separate);context.Checkpoint.CompletedStages.Clear();Assert.Throws<InvalidDataException>(()=>Bind(separate));
 }
 [TestCase("producer-pin.json")][TestCase("context.json")][TestCase("discovery.dump")][TestCase("assembly/Fixture.dll")][TestCase("selection.json")]
 public void ChangedRetainedInputsCannotBeRehashedIntoNewProof(string file) {
  Arrange(false);File.AppendAllText(P(prefix+file),"changed");Assert.Throws<InvalidDataException>(()=>Bind(false));
 }
 [Test]public void ReplacedCoordinatorReceiptIsRejected() {
  Arrange(false);File.AppendAllText(P(receiptName)," ");Assert.Throws<InvalidDataException>(()=>Bind(false));
 }
 [Test]public void WrongReleaseCannotSupplyAuditPins() {
  Arrange(false);File.WriteAllText(P(prefix+"context.json"),JsonSerializer.Serialize(new{RunId=new string('f',32),context.Checkpoint.Release.PackageSha256,ReleaseSourceCommit=new string('0',40)}));
  Retain(false);Assert.Throws<InvalidDataException>(()=>Bind(false));
 }
 [Test]public void CoordinatorPinMustMatchPreExecutionInventory() {
  Arrange(false);File.AppendAllText(P(prefix+"discovery.dump")," ");Retain(false);
  Assert.Throws<InvalidDataException>(()=>Bind(false));
 }
}
