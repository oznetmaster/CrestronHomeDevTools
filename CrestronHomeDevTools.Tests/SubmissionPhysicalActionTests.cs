// Copyright (c) 2026 Neil Colvin. MIT licensed.
using NUnit.Framework;
namespace CrestronHomeDevTools.Tests;

[TestFixture]
public sealed class SubmissionPhysicalActionTests
{
 private string _root=null!;
 private SubmissionOperatorInbox Inbox=>new(_root,new('a',64));
 [SetUp] public void Setup() { _root=Path.Combine(TestContext.CurrentContext.WorkDirectory,"physical-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(_root); }
 [TearDown] public void Cleanup()=>Directory.Delete(_root,true);
 private Task<SubmissionPhysicalActionResult<int>> Run(Func<CancellationToken,Task<int>> observe,CancellationToken token=default)=>
  SubmissionPhysicalAction.ObserveAsync(Inbox,"trigger","Synthetic sensor","Synthetic test action only.",TimeSpan.FromSeconds(10),observe,token);
 private SubmissionOperatorHandle Pending()=>SubmissionOperatorStep.Pending(_root,Inbox.RunKey).Single();
 [Test] public async Task AcknowledgementAloneDoesNotCompleteObservation() {
  var observed=new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
  var task=Run(ct=>observed.Task.WaitAsync(ct));SubmissionOperatorStep.Respond(Pending(),SubmissionOperatorOutcome.Done);
  Assert.That(task.IsCompleted,Is.False);observed.SetResult(42);
  var result=await task;Assert.That(result.Observation,Is.EqualTo(42));
 }
 [Test] public async Task FastObservedEventIsRetainedBeforeAcknowledgement() {
  var task=Run(_=>Task.FromResult(7));Assert.That(task.IsCompleted,Is.False);
  SubmissionOperatorStep.Respond(Pending(),SubmissionOperatorOutcome.Done);
  Assert.That((await task).Observation,Is.EqualTo(7));
 }
 [Test] public async Task FailedObserverClosesTriggerAndFreshRestorationRequestStillWorks() {
  var observed=new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
  var task=Run(ct=>observed.Task.WaitAsync(ct));var handle=Pending();
  observed.SetException(new InvalidDataException("Synthetic event failure"));
  await Assert.ThrowsAsync<InvalidDataException>(async()=>await task);
  Assert.That(SubmissionOperatorStep.Read(handle).Response!.Outcome,Is.EqualTo(SubmissionOperatorOutcome.Cancelled));
  using var restoreBudget=new CancellationTokenSource(TimeSpan.FromSeconds(10));
  var restore=Run(_=>Task.FromResult(0),restoreBudget.Token);
  SubmissionOperatorStep.Respond(Pending(),SubmissionOperatorOutcome.Done);
  Assert.That((await restore).Observation,Is.Zero);
 }
 [Test] public async Task UnableCancelsObserverAndCannotPass() {
  bool cancelled=false;
  var task=Run(async ct=> {try {await Task.Delay(Timeout.Infinite,ct);return 1;} finally {cancelled=ct.IsCancellationRequested;}});
  SubmissionOperatorStep.Respond(Pending(),SubmissionOperatorOutcome.Unable);
  await Assert.ThrowsAsync<InvalidOperationException>(async()=>await task);
  Assert.That(cancelled,Is.True);
 }
 [Test] public async Task CompletedInboxPreservesEvidenceAndRejectsNewActions() {
  var task=Run(_=>Task.FromResult(1));var handle=Pending();
  Assert.Throws<InvalidOperationException>(()=>SubmissionOperatorInboxLifecycle.Close(Inbox));
  SubmissionOperatorStep.Respond(handle,SubmissionOperatorOutcome.Done);await task;
  SubmissionOperatorInboxLifecycle.Close(Inbox);
  Assert.That(SubmissionOperatorInboxLifecycle.IsClosed(Inbox),Is.True);
  Assert.That(SubmissionOperatorInboxLifecycle.IsClosed(Inbox with {RunKey=new('b',64)}),Is.False);
  Assert.That(SubmissionOperatorStep.Read(handle).Response!.Outcome,Is.EqualTo(SubmissionOperatorOutcome.Done));
  Assert.Throws<InvalidOperationException>(()=>SubmissionOperatorStep.Create(_root,Inbox.RunKey,"again","target","instruction",TimeSpan.FromMinutes(1)));
 }
}
