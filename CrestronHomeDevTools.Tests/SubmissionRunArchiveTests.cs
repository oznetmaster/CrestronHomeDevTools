// Copyright (c) 2026 Neil Colvin. Licensed under the MIT License.
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using NUnit.Framework;
namespace CrestronHomeDevTools.Tests;
[TestFixture]
public sealed class SubmissionRunArchiveTests
{
 private string root=null!;
 private static SubmissionWorkflowRelease R(int id,string repository="example/driver")=>new(repository,id,"v"+id,new('a',40),new('b',64),new('c',64),new('d',64));
 private string Dir(int id)=>Path.Combine(root,SubmissionWorkflow.RunKey(R(id)));
 private void Open(int id)=>SubmissionWorkflow.Open(root,R(id));
 private void Close(int id)=>SubmissionRunArchive.Close(root,R(id),_=>{});
 private string Tombstone(int id)=>Path.Combine(root,".retention","pruned",SubmissionWorkflow.RunKey(R(id))+".json");
 [SetUp] public void Setup(){root=Path.Combine(TestContext.CurrentContext.WorkDirectory,"archive-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);}
 [TearDown] public void Cleanup(){Directory.Delete(root,true);}
 [Test] public void NewRunRemovesOnlyOlderClosedRoutineGeneration() {
  Open(1);File.WriteAllText(Path.Combine(Dir(1),"baseline.json"),"old");Close(1);Open(2);Close(2);Open(3);
  Assert.That(Directory.Exists(Dir(1)),Is.False);Assert.That(Directory.Exists(Dir(2)),Is.True);Assert.That(Directory.Exists(Dir(3)),Is.True);
  Assert.That(File.Exists(Tombstone(1)),Is.True);Assert.That(Directory.GetFiles(Dir(3),"baseline.json"),Is.Empty);
 }
 [Test] public void PrunedReleaseCannotBeReplayed() {
  Open(1);Close(1);Open(2);Close(2);Open(3);
  Assert.Throws<InvalidOperationException>(()=>Open(1));Assert.That(Directory.Exists(Dir(1)),Is.False);
 }
 [Test] public void ResumingExistingGenerationDoesNotRotateOrChangeEvidence() {
  Open(1);Open(2);File.WriteAllText(Path.Combine(Dir(1),"baseline.json"),"same");
  var before=File.ReadAllBytes(Path.Combine(Dir(1),"state.json"));Open(1);
  Assert.That(File.ReadAllBytes(Path.Combine(Dir(1),"state.json")),Is.EqualTo(before));
  Assert.That(File.ReadAllText(Path.Combine(Dir(1),"baseline.json")),Is.EqualTo("same"));Assert.That(Directory.Exists(Dir(2)),Is.True);
 }
 [Test] public void ActiveOlderGenerationSurvivesRotation() {
  Open(1);Open(2);Close(2);Open(3);Close(3);Open(4);
  Assert.That(Directory.Exists(Dir(1)),Is.True);Assert.That(Directory.Exists(Dir(2)),Is.False);Assert.That(Directory.Exists(Dir(3)),Is.True);
 }
 [Test] public void QuiescenceRejectionDoesNotCloseOrDeleteRun() {
  Open(1);Assert.Throws<InvalidOperationException>(()=>SubmissionRunArchive.Close(root,R(1),_=>throw new InvalidOperationException("worker still live")));
  Assert.That(File.Exists(Path.Combine(Dir(1),"retention-closed.json")),Is.False);
  Open(2);Close(2);Open(3);Assert.That(Directory.Exists(Dir(1)),Is.True);
 }
 private sealed class Steps:ISubmissionWorkflowSteps {
  public int Calls;public bool Wait;
  public Task<SubmissionWorkflowStepResult> ExecuteAsync(SubmissionWorkflowStepContext c,CancellationToken token){Calls++;return Task.FromResult(Complete(c));}
  public Task<SubmissionWorkflowStepResult> RecoverAsync(SubmissionWorkflowStepContext c,CancellationToken token)=>ExecuteAsync(c,token);
  private SubmissionWorkflowStepResult Complete(SubmissionWorkflowStepContext c) {
   if(Wait)return new(SubmissionWorkflowStatus.Waiting,ReasonCode:"running-child");
   string name=c.Checkpoint.Stage+".json";File.WriteAllText(Path.Combine(c.RunDirectory,name),"{}");
   return new(SubmissionWorkflowStatus.Completed,new(name,Convert.ToHexStringLower(SHA256.HashData("{}"u8))));
  }
 }
 [Test] public async Task ClosedRunCannotExecuteAnyStage() {
  Open(1);Close(1);var steps=new Steps();
  await Assert.ThrowsAsync<InvalidOperationException>(async()=>await SubmissionWorkflow.AdvanceAsync(root,R(1),steps));
  Assert.That(steps.Calls,Is.Zero);Assert.That(SubmissionWorkflow.Read(root,R(1)).Status,Is.EqualTo(SubmissionWorkflowStatus.Ready));
 }
 [Test] public async Task WaitingOperationCannotBeClosedByStatusAlone() {
  Open(1);await SubmissionWorkflow.AdvanceAsync(root,R(1),new Steps{Wait=true});
  bool inspected=false;Assert.Throws<InvalidOperationException>(()=>SubmissionRunArchive.Close(root,R(1),_=>inspected=true));
  Assert.That(inspected,Is.False);
 }
 [Test] public async Task SubmittedRunIsProtectedWithoutConsumingRoutineArchiveSlot() {
  Open(1);await SubmissionWorkflow.AdvanceAsync(root,R(1),new Steps());Close(1);
  Open(2);Close(2);Open(3);Close(3);Open(4);
  Assert.That(Directory.Exists(Dir(1)),Is.True);Assert.That(Directory.Exists(Dir(2)),Is.False);Assert.That(Directory.Exists(Dir(3)),Is.True);
 }
 [Test] public async Task PreparedReviewEvidenceIsProtected() {
  Open(1);var steps=new Steps();foreach(var stage in new[]{SubmissionWorkflowStage.ValidateCandidate,SubmissionWorkflowStage.WindowsTests,
   SubmissionWorkflowStage.ProcessorTests,SubmissionWorkflowStage.AppTests,SubmissionWorkflowStage.Endurance,SubmissionWorkflowStage.FinalizeTests,SubmissionWorkflowStage.PrepareReview})
   await SubmissionWorkflow.AdvanceStageAsync(root,R(1),steps,stage);
  Close(1);Open(2);Close(2);Open(3);Assert.That(Directory.Exists(Dir(1)),Is.True);
 }
 [Test] public void RegisteredDependenciesSurviveRotation() {
  Open(1);Close(1);Open(2);SubmissionRunArchive.Protect(root,R(2),[SubmissionWorkflow.RunKey(R(1))]);Close(2);Open(3);
  Assert.That(Directory.Exists(Dir(1)),Is.True);
 }
 [Test] public void PinnedRecordSurvivesRotation() {
  Open(1);SubmissionRunArchive.Protect(root,R(1),[],pin:true);Close(1);Open(2);Close(2);Open(3);Close(3);Open(4);
  Assert.That(Directory.Exists(Dir(1)),Is.True);Assert.That(Directory.Exists(Dir(2)),Is.False);
 }
 [Test] public void PrunedEvidenceCannotBecomeANewDependency() {
  Open(1);Close(1);Open(2);Close(2);Open(3);
  Assert.Throws<InvalidDataException>(()=>SubmissionRunArchive.Protect(root,R(3),[SubmissionWorkflow.RunKey(R(1))]));
 }
 [Test] public void MissingRegisteredCheckpointIsNotRecreated() {
  Open(1);File.Delete(Path.Combine(Dir(1),"state.json"));Assert.Throws<InvalidDataException>(()=>Open(1));
  Assert.That(File.Exists(Path.Combine(Dir(1),"state.json")),Is.False);
 }
 [Test] public void UnregisteredLegacyEvidenceIsNeverAdoptedOrRemoved() {
  Open(1);Directory.Delete(Path.Combine(root,".retention"),true);Open(1);
  Assert.Throws<InvalidDataException>(()=>Close(1));Open(2);Close(2);Open(3);Close(3);Open(4);
  Assert.That(Directory.Exists(Dir(1)),Is.True);Assert.That(Directory.Exists(Dir(2)),Is.False);
 }
 [Test] public void ChangedClosedCheckpointPreventsPruning() {
  Open(1);Close(1);Open(2);Close(2);File.AppendAllText(Path.Combine(Dir(1),"state.json")," ");
  Assert.Throws<InvalidDataException>(()=>Open(3));Assert.That(Directory.Exists(Dir(1)),Is.True);Assert.That(File.Exists(Tombstone(1)),Is.False);
 }
 [Test] public void IntakeOwnershipPreventsPruning() {
  Open(1);Close(1);Open(2);Close(2);
  using var held=new FileStream(Path.Combine(Dir(1),"intake.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
  Assert.Throws<IOException>(()=>Open(3));Assert.That(File.Exists(Tombstone(1)),Is.False);
 }
 [Test] public void InterruptedPruningFinishesBeforeResumeWithoutReplay() {
  Open(1);Close(1);Open(2);Close(2);Open(3);
  var json=JsonNode.Parse(File.ReadAllText(Tombstone(1)))!;json["Removed"]=false;File.WriteAllText(Tombstone(1),json.ToJsonString());
  Directory.CreateDirectory(Dir(1));File.WriteAllText(Path.Combine(Dir(1),"remaining.log"),"partial");Open(3);
  Assert.That(Directory.Exists(Dir(1)),Is.False);Assert.That(JsonNode.Parse(File.ReadAllText(Tombstone(1)))!["Removed"]!.GetValue<bool>(),Is.True);
 }
 [Test] public void UnexpectedRecreationOfFullyPrunedDirectoryIsNotDeleted() {
  Open(1);Close(1);Open(2);Close(2);Open(3);Directory.CreateDirectory(Dir(1));File.WriteAllText(Path.Combine(Dir(1),"unexpected.txt"),"preserve");
  Assert.Throws<InvalidDataException>(()=>Open(3));Assert.That(File.ReadAllText(Path.Combine(Dir(1),"unexpected.txt")),Is.EqualTo("preserve"));
 }
 [Test] public void AnotherDriversArchivesRemainUntouched() {
  var other=R(1,"owner/other");SubmissionWorkflow.Open(root,other);SubmissionRunArchive.Close(root,other,_=>{});
  Open(1);Close(1);Open(2);Close(2);Open(3);
  Assert.That(Directory.Exists(Path.Combine(root,SubmissionWorkflow.RunKey(other))),Is.True);
 }

 [Test][Platform("Win")] public void RedirectedDescendantPreventsDeletionOfBothArchiveAndTarget() {
  Open(1);Close(1);Open(2);Close(2);string outside=Path.Combine(root,"outside"),link=Path.Combine(Dir(1),"redirected");
  Directory.CreateDirectory(outside);File.WriteAllText(Path.Combine(outside,"sentinel.txt"),"keep");CreateJunction(link,outside);
  try {Assert.Throws<InvalidDataException>(()=>Open(3));Assert.That(Directory.Exists(Dir(1)),Is.True);
   Assert.That(File.ReadAllText(Path.Combine(outside,"sentinel.txt")),Is.EqualTo("keep"));Assert.That(File.Exists(Tombstone(1)),Is.False);
  } finally {Directory.Delete(link);}
 }
 [Test][Platform("Win")] public void RedirectedRetentionMetadataIsRejectedBeforeWriting() {
  string supplied=Path.Combine(root,"private"),outside=Path.Combine(root,"outside");Directory.CreateDirectory(supplied);Directory.CreateDirectory(outside);
  string link=Path.Combine(supplied,".retention");CreateJunction(link,outside);
  try {Assert.Throws<InvalidDataException>(()=>SubmissionWorkflow.Open(supplied,R(1)));Assert.That(Directory.GetFileSystemEntries(outside),Is.Empty);}
  finally {Directory.Delete(link);}
 }
 private static void CreateJunction(string link,string target) {
  // Junctions exercise Windows reparse-point handling without an administrator
  // token or Developer Mode. All targets are isolated inside this test's root.
  var info=new System.Diagnostics.ProcessStartInfo("powershell.exe") {UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
  foreach(var arg in new[]{"-NoProfile","-NonInteractive","-Command",
   "$ErrorActionPreference='Stop'; New-Item -ItemType Junction -Path '"+link.Replace("'","''")+"' -Target '"+target.Replace("'","''")+"' | Out-Null"})info.ArgumentList.Add(arg);
  using var process=System.Diagnostics.Process.Start(info)!;
  string output=process.StandardOutput.ReadToEnd(),error=process.StandardError.ReadToEnd();process.WaitForExit();
  if(process.ExitCode!=0)throw new IOException("Could not create isolated test junction: "+error+output);
 }

 [Test] public async Task ExplicitlyClosedTestOnlyRunDoesNotBecomePermanentSubmissionRecord() {
  Open(1);var steps=new Steps();foreach(var stage in new[]{SubmissionWorkflowStage.ValidateCandidate,SubmissionWorkflowStage.WindowsTests,
   SubmissionWorkflowStage.ProcessorTests,SubmissionWorkflowStage.AppTests,SubmissionWorkflowStage.Endurance,SubmissionWorkflowStage.FinalizeTests})
   await SubmissionWorkflow.AdvanceStageAsync(root,R(1),steps,stage);
  Close(1);Open(2);Close(2);Open(3);Assert.That(Directory.Exists(Dir(1)),Is.False);
 }
 [Test] public void AlteredClosureRecordCannotAuthorizeDeletion() {
  Open(1);Close(1);Open(2);Close(2);File.WriteAllText(Path.Combine(Dir(1),"retention-closed.json"),"{}");
  Assert.Throws<System.Text.Json.JsonException>(()=>Open(3));Assert.That(Directory.Exists(Dir(1)),Is.True);Assert.That(File.Exists(Tombstone(1)),Is.False);
 }
}
