// Copyright (c) 2026 Neil Colvin. Licensed under the MIT License.
using System.Text.Json.Nodes;
using NUnit.Framework;

namespace CrestronHomeDevTools.Tests;

[TestFixture]
public sealed class SubmissionOperatorStepTests
{
 private string _root=null!;
 [SetUp] public void SetUp()=>_root=Path.Combine(TestContext.CurrentContext.WorkDirectory,"operator-"+Guid.NewGuid().ToString("N"));
 [TearDown] public void TearDown() {if(Directory.Exists(_root))Directory.Delete(_root,true);}
 private SubmissionOperatorHandle Create()=>SubmissionOperatorStep.Create(_root,new('a',64),"single-press","Demo button","Press once and confirm.",TimeSpan.FromMinutes(10));
 [Test]
 public async Task IndependentFixtureProcessResumesFromPublicCliResponse()
 {
  var handle=Create();
  var start=new System.Diagnostics.ProcessStartInfo("dotnet") {UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
  start.ArgumentList.Add(Path.Combine(TestContext.CurrentContext.TestDirectory,"test-probe","CrestronHomeDevTools.Tests.Probe.dll"));
  foreach(string arg in new[]{"--operator-wait",handle.Directory,handle.RequestSha256})start.ArgumentList.Add(arg);
  using var process=System.Diagnostics.Process.Start(start)!;
  var error=process.StandardError.ReadToEndAsync();
  try {
   Assert.That(await process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10)),Is.EqualTo("Waiting"));
   var output=process.StandardOutput.ReadToEndAsync();
   using var cliOutput=new StringWriter();using var cliError=new StringWriter();
   Assert.That(SubmissionOperatorCommand.Run(["respond","--request-directory",handle.Directory,"--request-sha256",handle.RequestSha256,"--outcome","done"],cliOutput,cliError),Is.Zero,cliError.ToString());
   await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
   Assert.That(process.ExitCode,Is.Zero,await error);
   Assert.That((await output).Trim(),Is.EqualTo("Done"));
  } finally {if(!process.HasExited) {process.Kill(true);await process.WaitForExitAsync();}}
 }
 [Test]
 public void InboxSelectsOnlyPendingRequestsForThisRunAndUsesLocalPaths()
 {
  var pending=Create();var completed=Create();SubmissionOperatorStep.Respond(completed,SubmissionOperatorOutcome.Done);
  _=SubmissionOperatorStep.Create(_root,new('b',64),"other","other device","Other action",TimeSpan.FromMinutes(1));
  Assert.That(SubmissionOperatorStep.Pending(_root,new('a',64)),Is.EqualTo(new[]{pending}));
  // A second mount path is represented by relocating the entire retained inbox.
  string moved=_root+"-moved";Directory.Move(_root,moved);_root=moved;
  var observed=SubmissionOperatorStep.Pending(_root,new('a',64)).Single();
  Assert.That(observed.RequestSha256,Is.EqualTo(pending.RequestSha256));
  Assert.That(observed.Directory,Does.StartWith(moved));
 }
 [Test]
 public void InboxIgnoresUnpublishedRequestButRejectsChangedPublishedRequest()
 {
  var handle=Create();string ready=Path.Combine(handle.Directory,"ready.sha256");File.Delete(ready);
  Assert.That(SubmissionOperatorStep.Pending(_root,new('a',64)),Is.Empty);
  File.WriteAllText(ready,new string('f',64));
  Assert.Throws<InvalidDataException>(()=>SubmissionOperatorStep.Pending(_root,new('a',64)));
 }
 [Test]
 public async Task PublicCommandAnswersExactRequestAndWaitingFixtureResumes()
 {
  var handle=Create();
  var wait=SubmissionOperatorStep.WaitAsync(handle);
  Assert.That(wait.IsCompleted,Is.False);
  using var output=new StringWriter();using var error=new StringWriter();
  int code=SubmissionOperatorCommand.Run(["respond","--request-directory",handle.Directory,"--request-sha256",handle.RequestSha256,"--outcome","done"],output,error);
  Assert.That(code,Is.Zero,error.ToString());
  var response=await wait.WaitAsync(TimeSpan.FromSeconds(5));
  Assert.That(response.Outcome,Is.EqualTo(SubmissionOperatorOutcome.Done));
  Assert.That(SubmissionOperatorStep.Read(handle).Response,Is.EqualTo(response));
  Assert.That(output.ToString(),Does.Not.Contain("Passed"));
 }
 [Test]
 public void CannotAnswerTwice()
 {
  var handle=Create();var first=SubmissionOperatorStep.Respond(handle,SubmissionOperatorOutcome.Unable);
  Assert.Throws<InvalidOperationException>(()=>SubmissionOperatorStep.Respond(handle,SubmissionOperatorOutcome.Done));
  Assert.That(SubmissionOperatorStep.Read(handle).Response,Is.EqualTo(first));
 }
 [Test]
 public void RejectsResponseCopiedFromAnotherRequest()
 {
  var a=Create();var b=Create();SubmissionOperatorStep.Respond(a,SubmissionOperatorOutcome.Done);
  File.Copy(Path.Combine(a.Directory,"response.json"),Path.Combine(b.Directory,"response.json"));
  Assert.Throws<InvalidDataException>(()=>SubmissionOperatorStep.Read(b));
 }
 [Test]
 public void ChangedInstructionsCannotReceiveAnOldAcknowledgement()
 {
  var handle=Create();File.AppendAllText(Path.Combine(handle.Directory,"request.json")," ");
  Assert.Throws<InvalidDataException>(()=>SubmissionOperatorStep.Respond(handle,SubmissionOperatorOutcome.Done));
  Assert.That(File.Exists(Path.Combine(handle.Directory,"response.json")),Is.False);
 }
 [Test]
 public async Task CancelledWaitRetainsCancellation()
 {
  var handle=Create();using var cancel=new CancellationTokenSource();cancel.Cancel();
  var response=await SubmissionOperatorStep.WaitAsync(handle,cancel.Token);
  Assert.That(response.Outcome,Is.EqualTo(SubmissionOperatorOutcome.Cancelled));
  Assert.That(SubmissionOperatorStep.Read(handle).Waiting,Is.False);
 }
 [Test]
 public async Task ExpiredRequestCannotBeCompleted()
 {
  var clock=new RecordingClock();
  var handle=SubmissionOperatorStep.Create(_root,new('b',64),"restore","test target","Restore connection.",TimeSpan.FromMinutes(1),clock);
  clock.Elapsed=TimeSpan.FromMinutes(2);
  var acknowledgement=SubmissionOperatorStep.Respond(handle,SubmissionOperatorOutcome.Done);
  var response=await SubmissionOperatorStep.WaitAsync(handle,CancellationToken.None,clock);
  Assert.That(acknowledgement.Outcome,Is.EqualTo(SubmissionOperatorOutcome.Done));
  Assert.That(response.Outcome,Is.EqualTo(SubmissionOperatorOutcome.Expired));
  Assert.That(await SubmissionOperatorStep.WaitAsync(handle),Is.EqualTo(response));
  Assert.That(JsonNode.Parse(File.ReadAllText(Path.Combine(handle.Directory,"response.json")))!["outcome"]!.GetValue<string>(),Is.EqualTo("Done"));
 }
 [TestCase(-86400)]
 [TestCase(0.25)]
 [TestCase(86400)]
 public async Task OperatorWallClockIsAuditOnly(double skewSeconds)
 {
  var clock=new RecordingClock();
  var handle=SubmissionOperatorStep.Create(_root,new('a',64),"press","button","Press once.",TimeSpan.FromMinutes(1),clock);
  var operatorClock=new FixedClock(clock.Utc.AddSeconds(skewSeconds));
  var acknowledgement=SubmissionOperatorStep.Finish(handle,SubmissionOperatorOutcome.Done,operatorClock);
  Assert.That(SubmissionOperatorStep.Read(handle).Response,Is.EqualTo(acknowledgement));
  clock.Elapsed=TimeSpan.FromSeconds(10);
  Assert.That((await SubmissionOperatorStep.WaitAsync(handle,CancellationToken.None,clock)).Outcome,Is.EqualTo(SubmissionOperatorOutcome.Done));
 }
 [TestCase(-86400)] [TestCase(86400)]
 public async Task WorkerUtcJumpDoesNotExtendOrShortenItsElapsedDeadline(double jumpSeconds) {
  var clock=new RecordingClock();
  var handle=SubmissionOperatorStep.Create(_root,new('a',64),"press","button","Press once.",TimeSpan.FromMinutes(1),clock);
  clock.Utc=clock.Utc.AddSeconds(jumpSeconds);clock.Elapsed=TimeSpan.FromSeconds(10);
  SubmissionOperatorStep.Respond(handle,SubmissionOperatorOutcome.Done);
  Assert.That((await SubmissionOperatorStep.WaitAsync(handle,CancellationToken.None,clock)).Outcome,Is.EqualTo(SubmissionOperatorOutcome.Done));
  var second=SubmissionOperatorStep.Create(_root,new('a',64),"second","button","Press once.",TimeSpan.FromMinutes(1),clock);
  clock.Elapsed+=TimeSpan.FromSeconds(61);clock.Utc=clock.Utc.AddSeconds(-jumpSeconds);
  Assert.That((await SubmissionOperatorStep.WaitAsync(second,CancellationToken.None,clock)).Outcome,Is.EqualTo(SubmissionOperatorOutcome.Expired));
 }
 private sealed class RecordingClock:TimeProvider {
  internal DateTimeOffset Utc=DateTimeOffset.UtcNow;
  internal TimeSpan Elapsed;
  public override DateTimeOffset GetUtcNow()=>Utc;
  public override long TimestampFrequency=>TimeSpan.TicksPerSecond;
  public override long GetTimestamp()=>100+Elapsed.Ticks;
 }
 [Test] public async Task DesktopCanIdentifyARecorderWithoutUsingEitherWallClock() {
  var handle=Create();
  Assert.That(SubmissionOperatorStep.IsRecorderAvailable(handle),Is.False);
  using var cancel=new CancellationTokenSource();
  var wait=SubmissionOperatorStep.WaitAsync(handle,cancel.Token);
  Assert.That(SubmissionOperatorStep.IsRecorderAvailable(handle),Is.True);
  await cancel.CancelAsync();await wait;
  Assert.That(SubmissionOperatorStep.IsRecorderAvailable(handle),Is.False);
 }
 [Test]
 public void PermanentWriteFailureIsNotRetriedForever()
 {
  var handle=Create();Directory.CreateDirectory(Path.Combine(handle.Directory,"response.json"));
  using var cancel=new CancellationTokenSource();cancel.Cancel();
  Assert.That(async()=>await SubmissionOperatorStep.WaitAsync(handle,cancel.Token).WaitAsync(TimeSpan.FromSeconds(5)),
   Throws.InstanceOf<IOException>().Or.InstanceOf<UnauthorizedAccessException>());
 }
 [Test]
 public async Task CompetingRespondersRetainOnlyOneOutcome()
 {
  var handle=Create();
  var attempts=await Task.WhenAll(Enumerable.Range(0,8).Select(i=>Task.Run(()=> {
   try {SubmissionOperatorStep.Respond(handle,i%2==0?SubmissionOperatorOutcome.Done:SubmissionOperatorOutcome.Unable);return true;}
   catch(Exception e) when(e is IOException or InvalidOperationException) {return false;}
  })));
  Assert.That(attempts.Count(x=>x),Is.EqualTo(1));
  Assert.That(SubmissionOperatorStep.Read(handle).Response,Is.Not.Null);
 }
 [TestCase("--outcome","done")]
 [TestCase("--unexpected","value")]
 public void ShowRejectsUnusedOptions(string key,string value)
 {
  var handle=Create();using var output=new StringWriter();using var error=new StringWriter();
  Assert.That(SubmissionOperatorCommand.Run(["show","--request-directory",handle.Directory,"--request-sha256",handle.RequestSha256,key,value],output,error),Is.EqualTo(2));
 }
 private sealed class FixedClock(DateTimeOffset now):TimeProvider {public override DateTimeOffset GetUtcNow()=>now;}
 [Test] public async Task OvernightReadinessSurvivesWorkerCancellationAndReopensSameRequest() {
  var handle=SubmissionOperatorStep.Create(_root,new('a',64),"ready","Demo button","Wait for recording.",
   Timeout.InfiniteTimeSpan,new FixedClock(DateTimeOffset.UtcNow.AddDays(-3)));
  using var cancel=new CancellationTokenSource();cancel.Cancel();
  await Assert.ThrowsAsync<OperationCanceledException>(async()=>await SubmissionOperatorStep.WaitAsync(handle,cancel.Token));
  Assert.That(SubmissionOperatorStep.Read(handle).Waiting,Is.True);
  Assert.That(SubmissionOperatorStep.Pending(_root,new('a',64)),Is.EqualTo(new[]{handle}));
  Assert.That(SubmissionOperatorStep.GetOrCreateReadiness(_root,new('a',64),"ready","Demo button","Wait for recording."),Is.EqualTo(handle));
  Assert.That(SubmissionOperatorStep.Read(handle).Request.IsExpired(DateTimeOffset.UtcNow.AddYears(10)),Is.False);
  SubmissionOperatorStep.Respond(handle,SubmissionOperatorOutcome.Done);
  Assert.That((await SubmissionOperatorStep.WaitAsync(handle)).Outcome,Is.EqualTo(SubmissionOperatorOutcome.Done));
 }
 [Test] public void CannotPerformRetainsReasonAndCannotBecomeReadinessOrPass() {
  var handle=SubmissionOperatorStep.GetOrCreateReadiness(_root,new('a',64),"ready","Demo button","Wait.");
  Assert.Throws<ArgumentException>(()=>SubmissionOperatorStep.Respond(handle,SubmissionOperatorOutcome.Unable));
  var response=SubmissionOperatorStep.Respond(handle,SubmissionOperatorOutcome.Unable,"Device is not available until tomorrow.");
  Assert.That(SubmissionOperatorStep.Read(handle).Response,Is.EqualTo(response));
  Assert.That(response.Reason,Is.EqualTo("Device is not available until tomorrow."));
  Assert.Throws<InvalidOperationException>(()=>SubmissionOperatorStep.Respond(handle,SubmissionOperatorOutcome.Done));
  Assert.That(SubmissionOperatorStep.GetOrCreateReadiness(_root,new('a',64),"ready","Demo button","Wait."),Is.EqualTo(handle));
  Assert.Throws<InvalidDataException>(()=>SubmissionOperatorStep.GetOrCreateReadiness(_root,new('a',64),"ready","Different device","Wait."));
 }
 [Test] public void PublicCliRetainsCannotPerformReason() {
  var handle=SubmissionOperatorStep.GetOrCreateReadiness(_root,new('a',64),"ready","Demo button","Wait.");
  using var output=new StringWriter();using var error=new StringWriter();
  Assert.That(SubmissionOperatorCommand.Run(["respond","--request-directory",handle.Directory,"--request-sha256",handle.RequestSha256,
   "--outcome","unable","--reason","Device is unavailable"],output,error),Is.Zero,error.ToString());
  Assert.That(SubmissionOperatorStep.Read(handle).Response!.Reason,Is.EqualTo("Device is unavailable"));
 }
}
