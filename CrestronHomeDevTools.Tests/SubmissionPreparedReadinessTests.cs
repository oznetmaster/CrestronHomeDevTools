// Copyright (c) 2026 Neil Colvin. MIT licensed.
using System.Diagnostics;
using System.Text.Json;
using NUnit.Framework;
namespace CrestronHomeDevTools.Tests;

[NonParallelizable]
public sealed class SubmissionPreparedReadinessTests
{
 [Test] public async Task PreparedFixtureWaitsThenArmsAndPublishesWithoutAnotherWorkerPoll() {
  string root=Path.Combine(TestContext.CurrentContext.WorkDirectory,"prepared-"+Guid.NewGuid().ToString("N"));
  var inbox=new SubmissionOperatorInbox(root,new('a',64));
  string? old=Environment.GetEnvironmentVariable(SubmissionPreparedReadiness.EnvironmentVariable);
  Directory.CreateDirectory(root);
  try {
   Environment.SetEnvironmentVariable(SubmissionPreparedReadiness.EnvironmentVariable,JsonSerializer.Serialize(new {
    inbox.Directory,inbox.RunKey,Step="synthetic.prepared-ready",Instructions="Synthetic diagnostic only; no hardware."}));
   bool armed=false;
   var observed=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
   async Task RunFixture() {
    await SubmissionPreparedReadiness.WaitAsync(inbox,"Synthetic target");
    await SubmissionPhysicalAction.ObserveAsync(inbox,"synthetic.action","Synthetic target","No hardware action",TimeSpan.FromSeconds(10),
     ct=>{armed=true;return observed.Task.WaitAsync(ct);});
   }
   var fixture=RunFixture();
   var ready=SubmissionOperatorStep.Pending(root,inbox.RunKey).Single();
   Assert.That(armed,Is.False);
   var timer=Stopwatch.StartNew();
   SubmissionOperatorStep.Respond(ready,SubmissionOperatorOutcome.Done);
   SubmissionOperatorHandle? action=null;
   using var limit=new CancellationTokenSource(TimeSpan.FromSeconds(5));
   while(action==null) {
    action=SubmissionOperatorStep.Pending(root,inbox.RunKey).SingleOrDefault();
    if(action==null)await Task.Delay(10,limit.Token);
   }
   Assert.That(armed,Is.True);
   Assert.That(SubmissionOperatorStep.Read(action).Request.IsReadiness,Is.False);
   TestContext.Out.WriteLine($"Synthetic Ready-to-action: {timer.Elapsed.TotalMilliseconds:F1} ms on one worker, excluding desktop polling.");
   observed.SetResult(true);SubmissionOperatorStep.Respond(action,SubmissionOperatorOutcome.Done);
   await fixture;
   await Assert.ThrowsAsync<InvalidOperationException>(()=>SubmissionPreparedReadiness.WaitAsync(inbox,"Synthetic target"));
  } finally {
   Environment.SetEnvironmentVariable(SubmissionPreparedReadiness.EnvironmentVariable,old);
   Directory.Delete(root,true);
  }
 }
}
