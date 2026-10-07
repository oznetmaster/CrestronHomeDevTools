// Copyright (c) 2026 Neil Colvin. MIT licensed.
using NUnit.Framework;
namespace CrestronHomeDevTools.Tests;

public sealed class SubmissionRecoverySessionTests
{
 private static readonly DateTimeOffset Start=new(2026,10,2,0,0,0,TimeSpan.Zero);
 private static SubmissionOutageCapture At(int second)=>new(Start.AddSeconds(second),Start.AddSeconds(second),new("synthetic",new('a',64)));
 [TestCase(40)][TestCase(90)]
 public async Task DelayedClockEvidenceNeverDelaysProbesOrMovesTheirTimestamp(int arrival)
 {
  var observed=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
  int calls=0;
  await using var session=new SubmissionRecoverySession(async ct=>{
   await observed.Task.WaitAsync(ct); // Deliberately unavailable until after successful probes.
   Assert.That(arrival,Is.GreaterThan(20));return At(0);
  },ct=>{calls++;observed.SetResult();return Task.FromResult(new[]{new SubmissionOutageFunction("control",SubmissionEvidenceOutcome.Passed,At(20))});},TimeSpan.FromSeconds(3),default);
  var result=await session.Completion;
  Assert.That(calls,Is.EqualTo(1));Assert.That(result.Functions[0].Observation.LatestUtc,Is.EqualTo(Start.AddSeconds(20)));
 }
 [Test] public async Task PreLoadObservationIsRetainedButNeverRelabelled()
 {
  var batches=new List<SubmissionOutageFunction[]>();
  await using var session=new SubmissionRecoverySession(_=>Task.FromResult<SubmissionOutageCapture?>(At(10)),ct=>{
   var batch=new[]{new SubmissionOutageFunction("control",SubmissionEvidenceOutcome.Passed,At(batches.Count==0?5:20))};
   batches.Add(batch);return Task.FromResult(batch);
  },TimeSpan.FromSeconds(3),default);
  var result=await session.Completion;
  Assert.That(batches,Has.Count.EqualTo(2));Assert.That(batches[0][0].Observation.LatestUtc,Is.EqualTo(Start.AddSeconds(5)));
  Assert.That(result.Functions[0].Observation.LatestUtc,Is.EqualTo(Start.AddSeconds(20)));
 }
 [TestCase(true)][TestCase(false)] public async Task FailureCancelsAndJoinsTheOtherObserver(bool clockFails)
 {
  var started=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);bool joined=false;
  async Task Wait(CancellationToken ct){started.SetResult();try{await Task.Delay(Timeout.Infinite,ct);}finally{joined=true;}}
  async Task Fail(CancellationToken ct){await started.Task.WaitAsync(ct);throw new InvalidDataException("synthetic failure");}
  await using var session=new SubmissionRecoverySession(async ct=>{if(clockFails)await Fail(ct);else await Wait(ct);return At(0);},
   async ct=>{if(clockFails)await Wait(ct);else await Fail(ct);return [];},TimeSpan.FromSeconds(3),default);
  await Assert.ThrowsAsync<InvalidDataException>(async()=>await session.Completion);
  await session.StopAsync();Assert.That(joined,Is.True);
 }
 [Test] public async Task StopJoinsBothObserversBeforeRestorationCanStart()
 {
  int joined=0,started=0;
  var ready=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
  async Task Wait(CancellationToken ct){if(Interlocked.Increment(ref started)==2)ready.SetResult();try{await Task.Delay(Timeout.Infinite,ct);}finally{Interlocked.Increment(ref joined);}}
  await using var session=new SubmissionRecoverySession(async ct=>{await Wait(ct);return At(0);},async ct=>{await Wait(ct);return [];},TimeSpan.FromSeconds(3),default);
  await ready.Task;await session.StopAsync();Assert.That(joined,Is.EqualTo(2));Assert.That(session.Completion.IsCanceled,Is.True);
 }
 [TestCase(true)][TestCase(false)] public async Task DeadlineAndCallerCancellationRemainDistinct(bool callerCancels)
 {
  using var stop=new CancellationTokenSource();
  if(callerCancels)stop.CancelAfter(30);
  await using var session=new SubmissionRecoverySession(async ct=>{await Task.Delay(Timeout.Infinite,ct);return null;},
   async ct=>{await Task.Delay(Timeout.Infinite,ct);return [];},callerCancels?TimeSpan.FromSeconds(3):TimeSpan.FromMilliseconds(30),stop.Token);
  if(callerCancels)await Assert.CatchAsync<OperationCanceledException>(async()=>await session.Completion);
  else await Assert.ThrowsAsync<TimeoutException>(async()=>await session.Completion);
 }
}
