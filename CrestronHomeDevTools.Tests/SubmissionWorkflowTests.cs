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
  Assert.That(steps.Started,Is.EqualTo(Enum.GetValues<SubmissionWorkflowStage>()));
  var reopened=SubmissionWorkflow.Open(root,release);
  Assert.That(reopened.Status,Is.EqualTo(done.Status));
  Assert.That(reopened.CompletedStages,Is.EquivalentTo(done.CompletedStages));
  Assert.That(reopened.UpdatedUtc,Is.EqualTo(done.UpdatedUtc));
  await SubmissionWorkflow.AdvanceAsync(root,release,steps);
  Assert.That(steps.Started.Count,Is.EqualTo(9));Assert.That(steps.Recovered,Is.Empty);
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
  Directory.CreateDirectory(storage);Directory.CreateSymbolicLink(mount,storage);
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
  string target=Path.Combine(root,"outside.json"),link=Path.Combine(RunDirectory,"linked.json");
  File.WriteAllText(target,"{}");File.CreateSymbolicLink(link,target);
  try {
   var steps=new Steps{Start=_=>new(SubmissionWorkflowStatus.Completed,new("linked.json",Hash(target)))};
			await Assert.ThrowsAsync<InvalidDataException>(async()=>await SubmissionWorkflow.AdvanceAsync(root,release,steps));
  } finally {File.Delete(link);}
 }
}
