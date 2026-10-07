// Copyright (c) 2026 Neil Colvin. Licensed under the MIT License.
using System.Security.Cryptography;
using NUnit.Framework;

namespace CrestronHomeDevTools.Tests;

[TestFixture]
public sealed class SubmissionWorkflowTests
{
 private string root = null!;
 private readonly SubmissionWorkflowRelease release = new("example/driver", 12, "v1.0.0", new('a',40), new('b',64), new('c',64), new('d',64));
 [SetUp] public void Setup() { root=Path.Combine(TestContext.CurrentContext.WorkDirectory,"workflow-"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root); }
 [TearDown] public void Cleanup() { Directory.Delete(root,true); }
 private string RunDirectory => Path.Combine(root,SubmissionWorkflow.RunKey(release));
 private static string Hash(string path)=>Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
 private sealed class Steps : ISubmissionWorkflowSteps
 {
  public readonly List<SubmissionWorkflowStage> Started=[];
  public readonly List<(SubmissionWorkflowStage Stage,string? Id)> Recovered=[];
  public Func<SubmissionWorkflowStepContext,SubmissionWorkflowStepResult>? Start;
  public Func<SubmissionWorkflowStepContext,SubmissionWorkflowStepResult>? Recover;
  public Task<SubmissionWorkflowStepResult> ExecuteAsync(SubmissionWorkflowStepContext c,CancellationToken token) { Started.Add(c.Checkpoint.Stage); return Task.FromResult(Start?.Invoke(c) ?? Complete(c)); }
  public Task<SubmissionWorkflowStepResult> RecoverAsync(SubmissionWorkflowStepContext c,CancellationToken token) { Recovered.Add((c.Checkpoint.Stage,c.Checkpoint.OperationId)); return Task.FromResult(Recover?.Invoke(c) ?? Complete(c)); }
  public static SubmissionWorkflowStepResult Complete(SubmissionWorkflowStepContext c) {
   string name=c.Checkpoint.Stage+".json",path=Path.Combine(c.RunDirectory,name);
   if (!File.Exists(path)) File.WriteAllText(path,"{\"synthetic\":true}");
   return new(SubmissionWorkflowStatus.Completed,new(name,Hash(path)));
  }
 }

 [Test] public async Task OneInvocationAdvancesAllReadyStagesAndDuplicateReleaseDoesNotReplay()
 {
  SubmissionWorkflow.Open(root,release);var steps=new Steps();
  var done=await SubmissionWorkflow.AdvanceAsync(root,release,steps);
  Assert.That(done.Status,Is.EqualTo(SubmissionWorkflowStatus.Completed));
  Assert.That(steps.Started,Is.EqualTo(new[]{SubmissionWorkflowStage.ValidateCandidate,SubmissionWorkflowStage.WindowsTests,SubmissionWorkflowStage.ProcessorTests,SubmissionWorkflowStage.AppTests,SubmissionWorkflowStage.Endurance,SubmissionWorkflowStage.FinalizeTests,SubmissionWorkflowStage.PrepareReview,SubmissionWorkflowStage.SignReview,SubmissionWorkflowStage.Deliver,SubmissionWorkflowStage.Retain}));
  var reopened=SubmissionWorkflow.Open(root,release);
  Assert.That(reopened.Status,Is.EqualTo(done.Status));
  Assert.That(reopened.CompletedStages,Is.EquivalentTo(done.CompletedStages));
  Assert.That(reopened.UpdatedUtc,Is.EqualTo(done.UpdatedUtc));
  await SubmissionWorkflow.AdvanceAsync(root,release,steps);
  Assert.That(steps.Started.Count,Is.EqualTo(10));Assert.That(steps.Recovered,Is.Empty);
 }

 [Test] public async Task SelectedStagesNeverExecuteTheNextStageAndReuseVerifiedReceipts() {
  SubmissionWorkflow.Open(root,release);var steps=new Steps();
  var order=new[]{SubmissionWorkflowStage.ValidateCandidate,SubmissionWorkflowStage.WindowsTests,SubmissionWorkflowStage.ProcessorTests,
   SubmissionWorkflowStage.AppTests,SubmissionWorkflowStage.Endurance,SubmissionWorkflowStage.FinalizeTests};
  foreach(var stage in order) {
   var checkpoint=await SubmissionWorkflow.AdvanceStageAsync(root,release,steps,stage);
   Assert.That(checkpoint.CompletedStages.ContainsKey(stage),Is.True);
   Assert.That(steps.Started.Last(),Is.EqualTo(stage));
  }
  Assert.That(SubmissionWorkflow.Read(root,release).Stage,Is.EqualTo(SubmissionWorkflowStage.PrepareReview));
  string before=Hash(Path.Combine(RunDirectory,"state.json"));
  foreach(var stage in order)await SubmissionWorkflow.AdvanceStageAsync(root,release,steps,stage);
  Assert.That(steps.Started,Is.EqualTo(order));Assert.That(steps.Recovered,Is.Empty);
  Assert.That(Hash(Path.Combine(RunDirectory,"state.json")),Is.EqualTo(before));
 }
 [Test] public async Task SelectingALaterStageDoesNotExecuteItsPrerequisites() {
  SubmissionWorkflow.Open(root,release);var steps=new Steps();var before=Hash(Path.Combine(RunDirectory,"state.json"));
  await Assert.ThrowsAsync<InvalidOperationException>(async()=>await SubmissionWorkflow.AdvanceStageAsync(root,release,steps,SubmissionWorkflowStage.Endurance));
  Assert.That(steps.Started,Is.Empty);Assert.That(Hash(Path.Combine(RunDirectory,"state.json")),Is.EqualTo(before));
 }
 [Test] public async Task SelectedWaitRecoversOriginalOperationAndStopsAfterItsReceipt() {
  SubmissionWorkflow.Open(root,release);var steps=new Steps{Start=_=>new(SubmissionWorkflowStatus.Waiting,ReasonCode:"collecting")};
  var wait=await SubmissionWorkflow.AdvanceStageAsync(root,release,steps,SubmissionWorkflowStage.ValidateCandidate);
  var done=await SubmissionWorkflow.AdvanceStageAsync(root,release,steps,SubmissionWorkflowStage.ValidateCandidate);
  Assert.That(done.Stage,Is.EqualTo(SubmissionWorkflowStage.WindowsTests));Assert.That(done.Status,Is.EqualTo(SubmissionWorkflowStatus.Ready));
  Assert.That(steps.Recovered.Single().Id,Is.EqualTo(wait.OperationId));Assert.That(steps.Started,Has.Count.EqualTo(1));
 }
 [Test] public async Task CompletedSelectionStillRejectsChangedReceipt() {
  SubmissionWorkflow.Open(root,release);var steps=new Steps();await SubmissionWorkflow.AdvanceStageAsync(root,release,steps,SubmissionWorkflowStage.ValidateCandidate);
  File.AppendAllText(Path.Combine(RunDirectory,"ValidateCandidate.json"),"changed");
  await Assert.ThrowsAsync<InvalidDataException>(async()=>await SubmissionWorkflow.AdvanceStageAsync(root,release,steps,SubmissionWorkflowStage.ValidateCandidate));
  Assert.That(steps.Started,Has.Count.EqualTo(1));
 }
 [Test] public async Task CancelledSelectionDoesNotStartAnyStage() {
  SubmissionWorkflow.Open(root,release);var steps=new Steps();using var cancel=new CancellationTokenSource();cancel.Cancel();
  await Assert.ThrowsAsync<OperationCanceledException>(async()=>await SubmissionWorkflow.AdvanceStageAsync(root,release,steps,SubmissionWorkflowStage.ValidateCandidate,cancel.Token));
  Assert.That(steps.Started,Is.Empty);
 }

 [Test] public async Task LegacyCheckpointCanBeInspectedButCannotSilentlyReplayThroughNewBoundary() {
  SubmissionWorkflow.Open(root,release);string path=Path.Combine(RunDirectory,"state.json");
  var node=System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!;node["schemaVersion"]=1;File.WriteAllText(path,node.ToJsonString());
  var before=File.ReadAllBytes(path);Assert.That(SubmissionWorkflow.Read(root,release).SchemaVersion,Is.EqualTo(1));
  var steps=new Steps();await Assert.ThrowsAsync<InvalidDataException>(async()=>await SubmissionWorkflow.AdvanceAsync(root,release,steps));
  Assert.That(steps.Started,Is.Empty);Assert.That(File.ReadAllBytes(path),Is.EqualTo(before));
 }
 [Test] public async Task FinalTestFailurePreventsReviewAndResumeKeepsOriginalOperation() {
  SubmissionWorkflow.Open(root,release);
  var steps=new Steps{Start=c=>c.Checkpoint.Stage==SubmissionWorkflowStage.FinalizeTests?new(SubmissionWorkflowStatus.Failed,ReasonCode:"post-test-failed"):Steps.Complete(c)};
  var failed=await SubmissionWorkflow.AdvanceAsync(root,release,steps);
  Assert.That(failed.Stage,Is.EqualTo(SubmissionWorkflowStage.FinalizeTests));Assert.That(steps.Started,Does.Not.Contain(SubmissionWorkflowStage.PrepareReview));
  SubmissionWorkflow.RequestRecovery(root,release,Hash(Path.Combine(RunDirectory,"state.json")));
  var completed=await SubmissionWorkflow.AdvanceAsync(root,release,steps);
  Assert.That(completed.Status,Is.EqualTo(SubmissionWorkflowStatus.Completed));
  Assert.That(steps.Recovered.Single().Id,Is.EqualTo(failed.OperationId));
  Assert.That(steps.Started.Count(s=>s==SubmissionWorkflowStage.FinalizeTests),Is.EqualTo(1));
 }
 [Test] public async Task EnduranceWaitReturnsAndNextTickRecoversSameOperationWithoutRepeatedStart()
 {
  SubmissionWorkflow.Open(root,release);var steps=new Steps { Start=c=>c.Checkpoint.Stage==SubmissionWorkflowStage.Endurance ? new(SubmissionWorkflowStatus.Waiting,ReasonCode:"collecting") : Steps.Complete(c) };
  var wait=await SubmissionWorkflow.AdvanceAsync(root,release,steps);string before=Hash(Path.Combine(RunDirectory,"state.json"));
  Assert.That(wait.Stage,Is.EqualTo(SubmissionWorkflowStage.Endurance));Assert.That(wait.Status,Is.EqualTo(SubmissionWorkflowStatus.Waiting));
  steps.Recover=_=>new(SubmissionWorkflowStatus.Waiting,ReasonCode:"collecting");
  await SubmissionWorkflow.AdvanceAsync(root,release,steps);
  Assert.That(Hash(Path.Combine(RunDirectory,"state.json")),Is.EqualTo(before));
  steps.Recover=Steps.Complete;
  var done=await SubmissionWorkflow.AdvanceAsync(root,release,steps);
  Assert.That(done.Status,Is.EqualTo(SubmissionWorkflowStatus.Completed));
  Assert.That(steps.Started.Count(s=>s==SubmissionWorkflowStage.Endurance),Is.EqualTo(1));
  Assert.That(steps.Recovered.All(r=>r.Id==wait.OperationId),Is.True);
 }

 [Test] public async Task InterruptedDeliveryRecoversExistingJournalAndNeverCallsStartTwice()
 {
  SubmissionWorkflow.Open(root,release);
  var steps=new Steps { Start=c=>c.Checkpoint.Stage==SubmissionWorkflowStage.Deliver ? throw new IOException("simulated lost acknowledgement") : Steps.Complete(c) };
		await Assert.ThrowsAsync<IOException>(async()=>await SubmissionWorkflow.AdvanceAsync(root,release,steps));
  var interrupted=SubmissionWorkflow.Read(root,release);
  Assert.That(interrupted.Status,Is.EqualTo(SubmissionWorkflowStatus.Running));
  steps.Recover=_=>new(SubmissionWorkflowStatus.OutcomeUnknown,ReasonCode:"inspect-provider-journal");
  var unknown=await SubmissionWorkflow.AdvanceAsync(root,release,steps);
  await SubmissionWorkflow.AdvanceAsync(root,release,steps);
  Assert.That(steps.Recovered.Count,Is.EqualTo(1));
  Assert.That(steps.Recovered[0].Id,Is.EqualTo(interrupted.OperationId));
  string pin=Hash(Path.Combine(RunDirectory,"state.json"));
  SubmissionWorkflow.RequestRecovery(root,release,pin);
  steps.Recover=Steps.Complete;
  Assert.That((await SubmissionWorkflow.AdvanceAsync(root,release,steps)).Status,Is.EqualTo(SubmissionWorkflowStatus.Completed));
  Assert.That(steps.Started.Count(s=>s==SubmissionWorkflowStage.Deliver),Is.EqualTo(1));
 }

 [TestCase("package")][TestCase("profile")][TestCase("tooling")][TestCase("commit")]
 public void DuplicateReleaseWithChangedInputsIsRejected(string change)
 {
  SubmissionWorkflow.Open(root,release);
  var changed=change switch { "package"=>release with{PackageSha256=new('e',64)},"profile"=>release with{ProfileSnapshotSha256=new('e',64)},"tooling"=>release with{ToolingSha256=new('e',64)},_=>release with{SourceCommit=new('e',40)} };
  Assert.Throws<InvalidDataException>(()=>SubmissionWorkflow.Open(root,changed));
 }

 [Test] public async Task ChangedCompletedReceiptStopsLaterStages()
 {
  SubmissionWorkflow.Open(root,release);var steps=new Steps { Start=c=>c.Checkpoint.Stage==SubmissionWorkflowStage.ProcessorTests ? new(SubmissionWorkflowStatus.NeedsInput,ReasonCode:"equipment-unavailable") : Steps.Complete(c) };
  await SubmissionWorkflow.AdvanceAsync(root,release,steps);
  File.AppendAllText(Path.Combine(RunDirectory,"WindowsTests.json"),"changed");
		await Assert.ThrowsAsync<InvalidDataException>(async()=>await SubmissionWorkflow.AdvanceAsync(root,release,steps));
 }

 [Test] public async System.Threading.Tasks.Task AnotherWorkerCannotEnterTheSameRun ()
 {
  SubmissionWorkflow.Open(root,release);using var held=new FileStream(Path.Combine(RunDirectory,"run.lock"),FileMode.Open,FileAccess.ReadWrite,FileShare.None);
		await Assert.ThrowsAsync<IOException>(async()=>await SubmissionWorkflow.AdvanceAsync(root,release,new Steps()));
 }

 [Test] public async System.Threading.Tasks.Task ReceiptOutsideTheRunIsRejected ()
 {
  SubmissionWorkflow.Open(root,release);var outside=Path.Combine(root,"outside.json");File.WriteAllText(outside,"{}");
  var steps=new Steps {Start=_=>new(SubmissionWorkflowStatus.Completed,new("../outside.json",Hash(outside)))};
		await Assert.ThrowsAsync<InvalidDataException>(async()=>await SubmissionWorkflow.AdvanceAsync(root,release,steps));
 }

 [TestCase(false)][TestCase(true)][Platform("Win")]
 public async Task TrustedMountAboveRunAcceptsEquivalentWindowsPathSpellings(bool upperCase)
 {
  string storage=Path.Combine(root,"storage"),mount=Path.Combine(root,"mount");
  Directory.CreateDirectory(storage);CreateJunction(mount,storage);
  try {
   string supplied=mount.Replace('\\','/');if(upperCase)supplied=supplied.ToUpperInvariant();
   SubmissionWorkflow.Open(supplied,release);
   await SubmissionWorkflow.AdvanceAsync(supplied,release,new Steps());
   var original=SubmissionWorkflow.Read(storage,release);
   var shared=SubmissionWorkflow.Read(supplied,release);
   Assert.That(shared.Status,Is.EqualTo(SubmissionWorkflowStatus.Completed));
   Assert.That(shared.CompletedStages,Is.EquivalentTo(original.CompletedStages));
   Assert.That(shared.UpdatedUtc,Is.EqualTo(original.UpdatedUtc));
  } finally {Directory.Delete(mount);}
 }

 [Test][Platform("Win")]
 public async System.Threading.Tasks.Task ReceiptLinkInsideRunRemainsRejected ()
 {
  SubmissionWorkflow.Open(root,release);
  string storage=Path.Combine(root,"outside");Directory.CreateDirectory(storage);
  string target=Path.Combine(storage,"proof.json"),link=Path.Combine(RunDirectory,"linked");
  File.WriteAllText(target,"{}");CreateJunction(link,storage);
  try {
   var steps=new Steps{Start=_=>new(SubmissionWorkflowStatus.Completed,new("linked/proof.json",Hash(target)))};
			await Assert.ThrowsAsync<InvalidDataException>(async()=>await SubmissionWorkflow.AdvanceAsync(root,release,steps));
  } finally {Directory.Delete(link);}
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

 private async Task<SubmissionWorkflowCheckpoint> LegacyBoundary(SubmissionWorkflowStatus status=SubmissionWorkflowStatus.Failed) {
  SubmissionWorkflow.Open(root,release);
  var steps=new Steps{Start=c=>c.Checkpoint.Stage==SubmissionWorkflowStage.FinalizeTests
   ?new(status==SubmissionWorkflowStatus.Ready?SubmissionWorkflowStatus.Failed:status,ReasonCode:"retained-original-failure"):Steps.Complete(c)};
  await SubmissionWorkflow.AdvanceAsync(root,release,steps);
  string path=Path.Combine(RunDirectory,"state.json");
  var node=System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!;
  node["schemaVersion"]=1;node["stage"]="PrepareReview";
  if(status==SubmissionWorkflowStatus.Ready){node["status"]="Ready";node["operationId"]=null;node["reasonCode"]=null;}
  File.WriteAllText(path,node.ToJsonString());return SubmissionWorkflow.Read(root,release);
 }
 [TestCase(SubmissionWorkflowStatus.Failed)][TestCase(SubmissionWorkflowStatus.NeedsInput)]
 [TestCase(SubmissionWorkflowStatus.OutcomeUnknown)][TestCase(SubmissionWorkflowStatus.Ready)]
 public async Task ExplicitBoundaryMigrationPreservesOriginalEvidenceAndDoesNotAuthorizeRecovery(SubmissionWorkflowStatus status) {
  var prior=await LegacyBoundary(status);string path=Path.Combine(RunDirectory,"state.json"),pin=Hash(path);
  byte[] original=File.ReadAllBytes(path);int verified=0;
  var migrated=SubmissionWorkflow.MigrateTestBoundary(root,release,pin,c=>{verified++;Assert.That(c.Checkpoint.OperationId,Is.EqualTo(prior.OperationId));});
  Assert.That(migrated.SchemaVersion,Is.EqualTo(2));Assert.That(migrated.Stage,Is.EqualTo(SubmissionWorkflowStage.FinalizeTests));
  Assert.That(migrated.OperationId,Is.EqualTo(prior.OperationId));Assert.That(migrated.Status,Is.EqualTo(prior.Status));
  Assert.That(migrated.ReasonCode,Is.EqualTo(prior.ReasonCode));Assert.That(migrated.CompletedStages,Is.EquivalentTo(prior.CompletedStages));
  Assert.That(File.ReadAllBytes(Path.Combine(RunDirectory,"legacy-boundary-"+pin+".json")),Is.EqualTo(original));
  var after=File.ReadAllBytes(path);
  SubmissionWorkflow.MigrateTestBoundary(root,release,pin,_=>verified++);
  Assert.That(verified,Is.EqualTo(2));Assert.That(File.ReadAllBytes(path),Is.EqualTo(after));
  if(status!=SubmissionWorkflowStatus.Ready) {
   var steps=new Steps();await SubmissionWorkflow.AdvanceStageAsync(root,release,steps,SubmissionWorkflowStage.FinalizeTests);
   Assert.That(steps.Started,Is.Empty);Assert.That(steps.Recovered,Is.Empty);
   SubmissionWorkflow.RequestRecovery(root,release,Hash(path));
   await SubmissionWorkflow.AdvanceStageAsync(root,release,steps,SubmissionWorkflowStage.FinalizeTests);
   Assert.That(steps.Started,Is.Empty);Assert.That(steps.Recovered.Single().Id,Is.EqualTo(prior.OperationId));
   Assert.That(SubmissionWorkflow.Read(root,release).Stage,Is.EqualTo(SubmissionWorkflowStage.PrepareReview));
  }
 }
 [TestCase("pin")][TestCase("receipt")][TestCase("release")][TestCase("archive")][TestCase("verifier")]
 public async Task BoundaryMigrationRejectsInvalidEvidenceBeforeChangingCheckpoint(string change) {
  await LegacyBoundary();string path=Path.Combine(RunDirectory,"state.json"),pin=Hash(path);
  byte[] original=File.ReadAllBytes(path);var target=release;
  if(change=="pin")pin=new('e',64);
  if(change=="receipt")File.AppendAllText(Path.Combine(RunDirectory,"WindowsTests.json"),"changed");
  if(change=="release")target=release with {PackageSha256=new('e',64)};
  if(change=="archive")File.WriteAllText(Path.Combine(RunDirectory,"legacy-boundary-"+pin+".json"),"changed");
  Assert.Throws<InvalidDataException>(()=>SubmissionWorkflow.MigrateTestBoundary(root,target,pin,_=>{if(change=="verifier")throw new InvalidDataException("worker still active or review began");}));
  Assert.That(File.ReadAllBytes(path),Is.EqualTo(original));
  if(change!="archive")Assert.That(Directory.GetFiles(RunDirectory,"legacy-boundary-*.json"),Is.Empty);
 }
 [TestCase(SubmissionWorkflowStatus.Running)][TestCase(SubmissionWorkflowStatus.Waiting)]
 public async Task ActiveLegacyBoundaryCannotBeMigrated(SubmissionWorkflowStatus status) {
  await LegacyBoundary();string path=Path.Combine(RunDirectory,"state.json");
  var node=System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!;node["status"]=status.ToString();File.WriteAllText(path,node.ToJsonString());
  string pin=Hash(path);bool called=false;
  Assert.Throws<InvalidDataException>(()=>SubmissionWorkflow.MigrateTestBoundary(root,release,pin,_=>called=true));
  Assert.That(called,Is.False);Assert.That(Hash(path),Is.EqualTo(pin));
 }
 [Test] public async Task MigrationCannotEnterAnOwnedRun() {
  await LegacyBoundary();string pin=Hash(Path.Combine(RunDirectory,"state.json"));
  using var gate=new FileStream(Path.Combine(RunDirectory,"run.lock"),FileMode.Open,FileAccess.ReadWrite,FileShare.None);
  Assert.Throws<IOException>(()=>SubmissionWorkflow.MigrateTestBoundary(root,release,pin,_=>{}));
 }
 [Test] public async Task InterruptedMigrationReusesExactOriginalArchive() {
  await LegacyBoundary();string path=Path.Combine(RunDirectory,"state.json"),pin=Hash(path);
  File.Copy(path,Path.Combine(RunDirectory,"legacy-boundary-"+pin+".json"));
  Assert.That(SubmissionWorkflow.MigrateTestBoundary(root,release,pin,_=>{}).Stage,Is.EqualTo(SubmissionWorkflowStage.FinalizeTests));
  Assert.That(Directory.GetFiles(RunDirectory,"legacy-boundary-*.json"),Has.Length.EqualTo(1));
 }
 [Test] public async Task MigrationRetryRejectsAnAlreadyAdvancedRun() {
  await LegacyBoundary(SubmissionWorkflowStatus.Ready);string path=Path.Combine(RunDirectory,"state.json"),pin=Hash(path);
  SubmissionWorkflow.MigrateTestBoundary(root,release,pin,_=>{});
  await SubmissionWorkflow.AdvanceStageAsync(root,release,new Steps(),SubmissionWorkflowStage.FinalizeTests);
  string after=Hash(path);
  Assert.Throws<InvalidDataException>(()=>SubmissionWorkflow.MigrateTestBoundary(root,release,pin,_=>{}));
  Assert.That(Hash(path),Is.EqualTo(after));
 }
 [Test] public void MigrationRejectsEarlierLegacyStage() {
  SubmissionWorkflow.Open(root,release);string path=Path.Combine(RunDirectory,"state.json");
  var node=System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!;node["schemaVersion"]=1;File.WriteAllText(path,node.ToJsonString());
  string pin=Hash(path);Assert.Throws<InvalidDataException>(()=>SubmissionWorkflow.MigrateTestBoundary(root,release,pin,_=>{}));
  Assert.That(Hash(path),Is.EqualTo(pin));
 }


 private static readonly SubmissionWorkflowStage[] TestStages=[SubmissionWorkflowStage.ValidateCandidate,
  SubmissionWorkflowStage.WindowsTests,SubmissionWorkflowStage.ProcessorTests,SubmissionWorkflowStage.AppTests,
  SubmissionWorkflowStage.Endurance,SubmissionWorkflowStage.FinalizeTests];
 private async Task CompleteTestStages() {
  SubmissionWorkflow.Open(root,release);
  foreach(var stage in TestStages)await SubmissionWorkflow.AdvanceStageAsync(root,release,new Steps(),stage);
 }
 [Test] public async Task PhaseThreeRejectsEveryIncompleteTestPositionWithoutCallingAdapters(
  [ValueSource(nameof(TestStages))] SubmissionWorkflowStage stage,
  [Values(SubmissionWorkflowStatus.Ready,SubmissionWorkflowStatus.Running,SubmissionWorkflowStatus.Waiting,
   SubmissionWorkflowStatus.NeedsInput,SubmissionWorkflowStatus.Failed,SubmissionWorkflowStatus.OutcomeUnknown)] SubmissionWorkflowStatus status) {
  SubmissionWorkflow.Open(root,release);
  foreach(var preceding in TestStages.TakeWhile(s=>s!=stage))
   await SubmissionWorkflow.AdvanceStageAsync(root,release,new Steps(),preceding);
  string path=Path.Combine(RunDirectory,"state.json");
  var node=System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!;
  node["status"]=status.ToString();
  node["operationId"]=status==SubmissionWorkflowStatus.Ready?null:Guid.NewGuid().ToString("N");
  node["reasonCode"]=status is SubmissionWorkflowStatus.Ready or SubmissionWorkflowStatus.Running?null:"retained-test-wait";
  File.WriteAllText(path,node.ToJsonString());
  string before=Hash(path);var steps=new Steps();
  await Assert.ThrowsAsync<InvalidOperationException>(async()=>await SubmissionWorkflow.AdvancePhaseThreeAsync(root,release,steps));
  Assert.That(Hash(path),Is.EqualTo(before));Assert.That(steps.Started,Is.Empty);Assert.That(steps.Recovered,Is.Empty);
 }
 [Test] public async Task PhaseThreeRunsOnlyDocumentSigningDeliveryAndRetentionAndDoesNotReplayCompletion() {
  await CompleteTestStages();var steps=new Steps();
  var complete=await SubmissionWorkflow.AdvancePhaseThreeAsync(root,release,steps);
  Assert.That(complete.Status,Is.EqualTo(SubmissionWorkflowStatus.Completed));
  Assert.That(steps.Started,Is.EqualTo(new[]{SubmissionWorkflowStage.PrepareReview,SubmissionWorkflowStage.SignReview,
   SubmissionWorkflowStage.Deliver,SubmissionWorkflowStage.Retain}));
  var repeat=new Steps();string before=Hash(Path.Combine(RunDirectory,"state.json"));
  await SubmissionWorkflow.AdvancePhaseThreeAsync(root,release,repeat);
  Assert.That(repeat.Started,Is.Empty);Assert.That(repeat.Recovered,Is.Empty);
  Assert.That(Hash(Path.Combine(RunDirectory,"state.json")),Is.EqualTo(before));
 }
 [Test] public async Task PhaseThreeRejectsTamperedTestReceiptBeforeDocumentExecution() {
  await CompleteTestStages();string before=Hash(Path.Combine(RunDirectory,"state.json"));var steps=new Steps();
  File.AppendAllText(Path.Combine(RunDirectory,"FinalizeTests.json"),"changed");
  await Assert.ThrowsAsync<InvalidDataException>(async()=>await SubmissionWorkflow.AdvancePhaseThreeAsync(root,release,steps));
  Assert.That(steps.Started,Is.Empty);Assert.That(steps.Recovered,Is.Empty);
  Assert.That(Hash(Path.Combine(RunDirectory,"state.json")),Is.EqualTo(before));
 }
 [Test] public async Task PhaseThreeRecoversWaitingDeliveryWithOriginalOperationInsteadOfStartingAnotherSend() {
  await CompleteTestStages();var first=new Steps {Start=c=>c.Checkpoint.Stage==SubmissionWorkflowStage.Deliver
   ?new(SubmissionWorkflowStatus.Waiting,ReasonCode:"provider-pending"):Steps.Complete(c)};
  var wait=await SubmissionWorkflow.AdvancePhaseThreeAsync(root,release,first);
  var next=new Steps();var complete=await SubmissionWorkflow.AdvancePhaseThreeAsync(root,release,next);
  Assert.That(complete.Status,Is.EqualTo(SubmissionWorkflowStatus.Completed));
  Assert.That(next.Recovered,Is.EqualTo(new[]{(SubmissionWorkflowStage.Deliver,wait.OperationId)}));
  Assert.That(next.Started,Is.EqualTo(new[]{SubmissionWorkflowStage.Retain}));
 }
 [TestCase(SubmissionWorkflowStatus.OutcomeUnknown)][TestCase(SubmissionWorkflowStatus.Failed)]
 [TestCase(SubmissionWorkflowStatus.NeedsInput)]
 public async Task PhaseThreeDoesNotAutomaticallyRecoverAttentionStates(SubmissionWorkflowStatus status) {
  await CompleteTestStages();var first=new Steps {Start=c=>c.Checkpoint.Stage==SubmissionWorkflowStage.Deliver
   ?new(status,ReasonCode:"delivery-needs-inspection"):Steps.Complete(c)};
  var stopped=await SubmissionWorkflow.AdvancePhaseThreeAsync(root,release,first);
  string before=Hash(Path.Combine(RunDirectory,"state.json"));var next=new Steps();
  var observed=await SubmissionWorkflow.AdvancePhaseThreeAsync(root,release,next);
  Assert.That(observed.Status,Is.EqualTo(status));Assert.That(observed.OperationId,Is.EqualTo(stopped.OperationId));
  Assert.That(next.Started,Is.Empty);Assert.That(next.Recovered,Is.Empty);
  Assert.That(Hash(Path.Combine(RunDirectory,"state.json")),Is.EqualTo(before));
 }
 [Test] public async Task PhaseThreeCannotEnterAnOwnedRun() {
  await CompleteTestStages();
  using var gate=new FileStream(Path.Combine(RunDirectory,"run.lock"),FileMode.Open,FileAccess.ReadWrite,FileShare.None);
  var steps=new Steps();
  await Assert.ThrowsAsync<IOException>(async()=>await SubmissionWorkflow.AdvancePhaseThreeAsync(root,release,steps));
  Assert.That(steps.Started,Is.Empty);
 }

}
