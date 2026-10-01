// Copyright (c) 2026 Neil Colvin. MIT licensed.
using System.Security.Cryptography;
using NUnit.Framework;

namespace CrestronHomeDevTools.Tests;

[TestFixture]
public sealed class SubmissionManualOutageTests
{
 private string _root=null!;
 private readonly string _key=new('a',64);
 [SetUp] public void Setup() {
  _root=Path.Combine(TestContext.CurrentContext.WorkDirectory,"manual-outage-"+Guid.NewGuid().ToString("N"));
  Directory.CreateDirectory(Path.Combine(_root,"inbox"));
 }
 [TearDown] public void Cleanup()=>Directory.Delete(_root,true);
 private SubmissionOutageMeasurementPlan Plan=>new(new(new('a',64),new('b',40),new('c',64),new('d',64)),
  "system.network",["processor","device"],["control"],TimeSpan.FromMilliseconds(40),TimeSpan.FromMinutes(1),SubmissionOutageRecoveryClock.NetworkRestored);
 private SubmissionManualOutageHardware Hardware(Observer observer)=>new(new(new(Path.Combine(_root,"inbox"),_key),
  "Synthetic equipment only","Synthetic disconnect.","Synthetic reconnect.",TimeSpan.FromSeconds(5)),observer);
 private async Task<SubmissionOperatorHandle> Pending(string step) {
  using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(8));
  while(true) {
   timeout.Token.ThrowIfCancellationRequested();
   var handles=SubmissionOperatorStep.Pending(Path.Combine(_root,"inbox"),_key);
   var match=handles.SingleOrDefault(h=>SubmissionOperatorStep.Read(h).Request.Step==step);
   if(match!=null)return match;
   await Task.Delay(10,timeout.Token);
  }
 }
 private Task<SubmissionOutageRecordingResult> Record(SubmissionManualOutageHardware hardware,CancellationToken token=default, bool power=false)=>
  SubmissionOutageRecorder.RecordAsync(power?Plan with {RecoveryClock=SubmissionOutageRecoveryClock.ProgramLoaded,ProgramComponent="processor"}:Plan,
   hardware,Path.Combine(_root,"recording"),TimeSpan.FromSeconds(20),TimeSpan.FromSeconds(8),token);
 [TestCase(false)][TestCase(true)] public async Task IndependentNewBootProofKeepsLateAcknowledgementOutOfProgramStartOrdering(bool lowerBound) {
  var observer=new Observer{RestorationProof="valid",ProgramLoadIsLowerBound=lowerBound};await using var hardware=Hardware(observer);
  var task=Record(hardware,power:true);await Answer("disconnect");
  var reconnect=await Pending("outage-reconnect");await observer.Recovered.Task.WaitAsync(TimeSpan.FromSeconds(5));
  await Task.Delay(80);SubmissionOperatorStep.Respond(reconnect,SubmissionOperatorOutcome.Done);
  var result=await task;
  Assert.That(result.Passed,Is.True,string.Join(",",result.Issues.Concat(result.Measurements?.Issues??[])));
  using var measurement=System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(_root,"recording","measurements.json")));
  Assert.That(measurement.RootElement.GetProperty("schemaVersion").GetInt32(),Is.EqualTo(lowerBound?2:1));
  using var doc=System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(_root,"recording","manual-reconnect-capture.json")));
  var bounds=doc.RootElement.GetProperty("componentBounds");
  Assert.That(bounds.GetProperty("processor").GetProperty("latestUtc").GetDateTimeOffset(),Is.LessThan(observer.ProgramLoaded!.EarliestUtc));
  Assert.That(bounds.GetProperty("device").GetProperty("latestUtc").GetDateTimeOffset(),Is.GreaterThan(observer.ProgramLoaded.LatestUtc));
  Assert.That(doc.RootElement.GetProperty("restoredBy").GetProperty("processor").ValueKind,Is.EqualTo(System.Text.Json.JsonValueKind.Object));
 }
 [TestCase("stale")][TestCase("future")][TestCase("unknown")][TestCase("modified")]
 public async Task InvalidIndependentRestorationProofCannotPassAndStillRestoresOriginal(string error) {
  var observer=new Observer{RestorationProof=error};await using var hardware=Hardware(observer);
  var task=Record(hardware);await Answer("disconnect");await Answer("reconnect");var result=await task;
  Assert.That(result.Passed,Is.False);Assert.That(observer.RestoredOriginal,Is.True);Assert.That(observer.FunctionsChecked,Is.Zero);
 }
 [Test] public void PolicyPreflightRejectsInsufficientDurationBeforeAnyOperatorRequest() {
  var policy=new SubmissionEvidencePolicy(1,[new("system.network",TimeSpan.FromMinutes(1),false,new("system","outage",SubmissionEvidenceOutcome.Passed,60,true))]);
  Assert.Throws<InvalidDataException>(()=>SubmissionOutageEvidence.ValidatePlanPolicy(Plan,policy));
  Assert.That(Directory.GetDirectories(Path.Combine(_root,"inbox")),Is.Empty);
 }
 [Test] public void PolicyPreflightRejectsWeakerRecoveryDeadlineBeforeAnyOperatorRequest() {
  var policy=new SubmissionEvidencePolicy(1,[new("system.network",TimeSpan.Zero,false,new("system","outage",SubmissionEvidenceOutcome.Passed,15,true))]);
  Assert.Throws<InvalidDataException>(()=>SubmissionOutageEvidence.ValidatePlanPolicy(Plan,policy));
  Assert.That(Directory.GetDirectories(Path.Combine(_root,"inbox")),Is.Empty);
 }
 private async Task Answer(string phase) {
  var h=await Pending("outage-"+phase);SubmissionOperatorStep.Respond(h,SubmissionOperatorOutcome.Done);
 }
 [Test] public async Task OneDisconnectAndOneReconnectCoverAllComponentsAndRetainBoundedEvidence() {
  var observer=new Observer();await using var hardware=Hardware(observer);
  var task=Record(hardware);await Answer("disconnect");await Answer("reconnect");var result=await task;
  Assert.Multiple(()=> {
   Assert.That(result.Passed,Is.True);
   Assert.That(observer.DisconnectObservations,Is.EqualTo(1));Assert.That(observer.RestoreObservations,Is.EqualTo(1));
   Assert.That(observer.RestoredOriginal,Is.True);Assert.That(observer.WatchCancelled,Is.True);
   Assert.That(Directory.GetDirectories(Path.Combine(_root,"inbox")),Has.Length.EqualTo(2));
   Assert.That(result.Measurements!.GuaranteedInterruptionSeconds,Is.GreaterThanOrEqualTo(.04));
   Assert.That(File.ReadAllText(Path.Combine(_root,"recording","manual-disconnect-capture.json")),Does.Contain("rawCapturesBase64"));
  });
 }
 [Test] public async Task AcknowledgementAloneDoesNotStartHoldOrReconnect() {
  var observer=new Observer{WaitForObservation=true};await using var hardware=Hardware(observer);
  var task=Record(hardware);await Answer("disconnect");
  Assert.That(observer.RestoreObservations,Is.Zero);Assert.That(task.IsCompleted,Is.False);
  observer.Observed.TrySetResult();await Answer("reconnect");Assert.That((await task).Passed,Is.True);
 }
 [Test] public async Task CancelAfterPhysicalActionStillRequestsGroupedRestorationWithIndependentBudget() {
  var observer=new Observer{WaitForObservation=true};await using var hardware=Hardware(observer);
  using var cancel=new CancellationTokenSource();var task=Record(hardware,cancel.Token);
  var request=await Pending("outage-disconnect");cancel.Cancel();await Answer("reconnect");var result=await task;
  Assert.That(result.Passed,Is.False);Assert.That(observer.RestoredOriginal,Is.True);
  Assert.That(SubmissionOperatorStep.Read(request).Response!.Outcome,Is.EqualTo(SubmissionOperatorOutcome.Cancelled));
  Assert.That(observer.RestoreObservations,Is.EqualTo(1));
 }
 [Test] public async Task EarlyReturnDuringHoldPreventsPassingButRestores() {
  var observer=new Observer{EarlyReturn=true};await using var hardware=Hardware(observer);
  var task=Record(hardware);await Answer("disconnect");await Answer("reconnect");var result=await task;
  Assert.That(result.Passed,Is.False);Assert.That(observer.RestoredOriginal,Is.True);
  Assert.That(observer.FunctionsChecked,Is.Zero);
 }
 [Test] public async Task StoppingHoldWithLinkedCancellationDoesNotFailRecovery() {
  var observer=new Observer{LinkedWatchCancellation=true,RestorationProof="valid"};await using var hardware=Hardware(observer);
  var task=Record(hardware,power:true);await Answer("disconnect");await Answer("reconnect");var result=await task;
  Assert.That(result.Passed,Is.True,string.Join(",",result.Issues));
  Assert.That(observer.FunctionsChecked,Is.EqualTo(1));
 }
 [Test] public async Task IndependentWatchCancellationBeforeReconnectStillFails() {
  var observer=new Observer{UnexpectedWatchCancellation=true};await using var hardware=Hardware(observer);
  var task=Record(hardware);await Answer("disconnect");await Answer("reconnect");var result=await task;
  Assert.That(result.Passed,Is.False);Assert.That(observer.FunctionsChecked,Is.Zero);
  Assert.That(observer.RestoredOriginal,Is.True);
 }
 [Test] public async Task UnableStillRestoresAndCannotPass() {
  var observer=new Observer{WaitForObservation=true};await using var hardware=Hardware(observer);
  var task=Record(hardware);SubmissionOperatorStep.Respond(await Pending("outage-disconnect"),SubmissionOperatorOutcome.Unable);
  await Answer("reconnect");Assert.That((await task).Passed,Is.False);Assert.That(observer.RestoredOriginal,Is.True);
 }
 [Test] public async Task WrongObservationScopeFailsBeforeHoldButStillRestores() {
  var observer=new Observer{WrongScope=true};await using var hardware=Hardware(observer);
  var task=Record(hardware);await Answer("disconnect");await Answer("reconnect");
  Assert.That((await task).Passed,Is.False);Assert.That(observer.RestoredOriginal,Is.True);
 }
 private sealed class Observer:ISubmissionManualOutageObserver,ISubmissionManualRestorationBounds {
  public bool ProgramLoadIsLowerBound {get;set;}
  public IReadOnlyList<string> Components=>["processor","device"];
  public IReadOnlyList<string> Functions=>["control"];
  public bool WaitForObservation,EarlyReturn,WrongScope,RestoredOriginal,WatchCancelled;
  public bool LinkedWatchCancellation,UnexpectedWatchCancellation;
  public int DisconnectObservations,RestoreObservations,FunctionsChecked;
  public TaskCompletionSource Observed=new(TaskCreationOptions.RunContinuationsAsynchronously);
  public TaskCompletionSource Recovered=new(TaskCreationOptions.RunContinuationsAsynchronously);
  public string? RestorationProof;
  public SubmissionOutageCapture? ProgramLoaded;
  private SubmissionOutageCapture? _restoredBy;
  private string _root=null!;private int _sequence;
  public Task PreflightAsync(SubmissionOutageRecordingContext context,CancellationToken token) {_root=context.EvidenceDirectory;return Task.CompletedTask;}
  private SubmissionOutageCapture Capture() {
   var now=DateTimeOffset.UtcNow;string name=$"synthetic-{_sequence++}.json";byte[] raw="{\"synthetic\":true}"u8.ToArray();
   File.WriteAllBytes(Path.Combine(_root,name),raw);return new(now,now,new(name,Convert.ToHexStringLower(SHA256.HashData(raw))));
  }
  public Task<SubmissionOutageCapture> CaptureOriginalAsync(CancellationToken token)=>Task.FromResult(Capture());
  public async Task<IReadOnlyDictionary<string,SubmissionOutageCapture>> ObserveInterruptedAsync(CancellationToken token) {
   DisconnectObservations++;await Task.Delay(30,token);
   if(WaitForObservation)await Observed.Task.WaitAsync(token);
   return new Dictionary<string,SubmissionOutageCapture>{{"processor",Capture()},{WrongScope?"unknown":"device",Capture()}};
  }
  public async Task WatchInterruptedAsync(CancellationToken token) {
   if(EarlyReturn)throw new InvalidDataException("Synthetic endpoint returned early.");
   if(UnexpectedWatchCancellation)throw new OperationCanceledException(new CancellationToken(true));
   if(LinkedWatchCancellation) {
    using var linked=CancellationTokenSource.CreateLinkedTokenSource(token);
    await Task.Delay(Timeout.Infinite,linked.Token);return;
   }
   try {await Task.Delay(Timeout.Infinite,token);}finally{WatchCancelled=token.IsCancellationRequested;}
  }
  public async Task<IReadOnlyDictionary<string,SubmissionOutageCapture>> ObserveRestoredAsync(CancellationToken token) {
   RestoreObservations++;await Task.Delay(30,token);_restoredBy=Capture();await Task.Delay(2,token);ProgramLoaded=Capture();
   var result=new Dictionary<string,SubmissionOutageCapture>{{"processor",Capture()},{"device",Capture()}};
   Recovered.TrySetResult();return result;
  }
  public Task<IReadOnlyDictionary<string,SubmissionOutageCapture>> CaptureRestoredByAsync(CancellationToken token) {
   var result=new Dictionary<string,SubmissionOutageCapture>();
   if(RestorationProof!=null) {
    var proof=_restoredBy!;
    if(RestorationProof=="stale")proof=proof with {EarliestUtc=proof.EarliestUtc.AddMinutes(-1),LatestUtc=proof.LatestUtc.AddMinutes(-1)};
    if(RestorationProof=="future")proof=proof with {LatestUtc=proof.LatestUtc.AddMinutes(1)};
    if(RestorationProof=="modified")File.AppendAllText(Path.Combine(_root,proof.Evidence.RelativePath),"changed");
    result[RestorationProof=="unknown"?"unknown":"processor"]=proof;
   }
   return Task.FromResult<IReadOnlyDictionary<string,SubmissionOutageCapture>>(result);
  }
  public Task<SubmissionOutageCapture?> ObserveProgramLoadedAsync(string component,CancellationToken token)=>Task.FromResult(ProgramLoaded);
  public Task<SubmissionOutageFunction> VerifyFunctionAsync(string function,CancellationToken token) {
   FunctionsChecked++;return Task.FromResult(new SubmissionOutageFunction(function,SubmissionEvidenceOutcome.Passed,Capture()));
  }
  public Task<SubmissionOutageRestoredState> RestoreOriginalAsync(SubmissionOutageCapture original,CancellationToken token) {
   RestoredOriginal=true;return Task.FromResult(new SubmissionOutageRestoredState(Capture(),true));
  }
 }
}
