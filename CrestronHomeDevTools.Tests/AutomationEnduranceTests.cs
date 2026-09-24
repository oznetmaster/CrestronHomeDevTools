// Copyright (c) 2026 Neil Colvin. MIT licensed.
using CrestronHomeDevTools.Automation;
using NUnit.Framework;

namespace CrestronHomeDevTools.Tests;

[TestFixture]
public sealed class AutomationEnduranceTests
{
 private string root=null!;
 private SubmissionWorkflowStepContext context=null!;
 private Fake monitor=null!;
 [SetUp]public void Setup() {
  root=Path.Combine(TestContext.CurrentContext.WorkDirectory,"automation-endurance-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
  var release=new SubmissionWorkflowRelease("example/driver",1,"v1",new('a',40),new('b',64),new('c',64),new('d',64));
  context=new(root,new(1,new('e',64),release,SubmissionWorkflowStage.Endurance,SubmissionWorkflowStatus.Running,Guid.NewGuid().ToString("N"),null,[],DateTimeOffset.UtcNow));
  monitor=new(root);
 }
 [TearDown]public void Cleanup()=>Directory.Delete(root,true);
 private sealed class Fake(string root):IAutomationEndurance {
  public string Reservation="Held";
  public SubmissionEnduranceState State=SubmissionEnduranceState.Collecting;
  public int Starts,Collects,Finishes,Exports;
  public SubmissionEnduranceCheckpoint Checkpoint()=>new(1,new('a',64),State,DateTimeOffset.UtcNow,[]);
  public Task Start(CancellationToken t){Starts++;Directory.CreateDirectory(Path.Combine(root,"endurance"));return Task.CompletedTask;}
  public SubmissionEnduranceMonitorStatus Read()=>new(Reservation,Checkpoint());
  public Task<SubmissionEnduranceCheckpoint> Collect(CancellationToken t){Collects++;return Task.FromResult(Checkpoint());}
  public Task Finish(CancellationToken t){Finishes++;Reservation="Released";return Task.CompletedTask;}
  public SubmissionObservation Export(){Exports++;return new("endurance",new(new('b',64),new('a',40),new('c',64),new('d',64)),SubmissionEvidenceOutcome.Passed,DateTimeOffset.UnixEpoch,DateTimeOffset.UnixEpoch.AddHours(24),[]);}
 }
 [Test]public async Task WaitingInvocationResumesExistingMonitorWithoutStartingAgain() {
  Assert.That((await AutomationEndurance.Advance(context,false,monitor,default)).Status,Is.EqualTo(SubmissionWorkflowStatus.Waiting));
  Assert.That((await AutomationEndurance.Advance(context,true,monitor,default)).Status,Is.EqualTo(SubmissionWorkflowStatus.Waiting));
  Assert.That(monitor.Starts,Is.EqualTo(1));Assert.That(monitor.Finishes,Is.Zero);
 }
 [Test]public async Task CompletionReleasesAndExportsWithoutRepeatingProbe() {
  await monitor.Start(default);monitor.State=SubmissionEnduranceState.Passed;
  var first=await AutomationEndurance.Advance(context,true,monitor,default);
  var second=await AutomationEndurance.Advance(context,true,monitor,default);
  Assert.That(first.Status,Is.EqualTo(SubmissionWorkflowStatus.Completed));Assert.That(second.Receipt,Is.EqualTo(first.Receipt));
  Assert.That(monitor.Finishes,Is.EqualTo(1));Assert.That(monitor.Collects,Is.Zero);
 }
 [Test]public async Task KnownFailureReleasesReadOnlyReservationAndStaysFailed() {
  await monitor.Start(default);monitor.State=SubmissionEnduranceState.Failed;
  Assert.That((await AutomationEndurance.Advance(context,true,monitor,default)).Status,Is.EqualTo(SubmissionWorkflowStatus.Failed));
  Assert.That(monitor.Finishes,Is.EqualTo(1));Assert.That(monitor.Exports,Is.Zero);Assert.That(monitor.Collects,Is.Zero);
 }
 [TestCase(SubmissionEnduranceState.ProbePending)][TestCase(SubmissionEnduranceState.Interrupted)]
 public async Task InterruptedProbeIsNotReplayedOrReleased(SubmissionEnduranceState state) {
  await monitor.Start(default);monitor.State=state;
  Assert.That((await AutomationEndurance.Advance(context,true,monitor,default)).Status,Is.EqualTo(SubmissionWorkflowStatus.OutcomeUnknown));
  Assert.That(monitor.Finishes,Is.Zero);Assert.That(monitor.Collects,Is.Zero);
 }
 [Test]public async Task MissingJournalOnRecoveryDoesNotStartAnotherRun() {
  Assert.That((await AutomationEndurance.Advance(context,true,monitor,default)).Status,Is.EqualTo(SubmissionWorkflowStatus.OutcomeUnknown));
  Assert.That(monitor.Starts,Is.Zero);Assert.That(monitor.Collects,Is.Zero);
 }
}
