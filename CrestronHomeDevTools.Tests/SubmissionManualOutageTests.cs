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
 [TestCase(false)][TestCase(true)]
 public async Task RecoverySessionStartsBeforeAcknowledgementAndIsJoinedBeforeRestoration(bool failClock) {
  var observer=new Observer{NetworkUpperBounds=true,SessionEnabled=true,FailSessionClock=failClock};
  await using var hardware=Hardware(observer);
  var task=Record(hardware);await Answer("disconnect");
  var reconnect=await Pending("outage-reconnect");
  await observer.SessionStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
  Assert.That(SubmissionOperatorStep.Read(reconnect).Response,Is.Null);
  SubmissionOperatorStep.Respond(reconnect,SubmissionOperatorOutcome.Done);
  var result=await task;
  Assert.That(result.Passed,Is.EqualTo(!failClock));
  Assert.That(observer.RestoredOriginal,Is.True);Assert.That(observer.SessionStoppedBeforeRestore,Is.True);
  Assert.That(result.Disposition,Is.EqualTo(failClock?SubmissionRecoveryDisposition.HarnessFailed:SubmissionRecoveryDisposition.Passed));
 }
 [TestCase(false)][TestCase(true)] public async Task IndependentNewBootProofKeepsLateAcknowledgementOutOfProgramStartOrdering(bool lowerBound) {
  var observer=new Observer{RestorationProof="valid",ProgramLoadIsLowerBound=lowerBound};await using var hardware=Hardware(observer);
  var task=Record(hardware,power:true);await Answer("disconnect");
  var reconnect=await Pending("outage-reconnect");await observer.Recovered.Task.WaitAsync(TimeSpan.FromSeconds(5));
  await Task.Delay(80);SubmissionOperatorStep.Respond(reconnect,SubmissionOperatorOutcome.Done);
  var result=await task;
  Assert.That(result.Passed,Is.True,string.Join(",",result.Issues.Concat(result.Measurements?.Issues??[])));
  using var measurement=System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(_root,"recording","measurements.json")));
  Assert.That(measurement.RootElement.GetProperty("schemaVersion").GetInt32(),Is.EqualTo(3));
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
 [TestCase(false)][TestCase(true)]
 public async Task NetworkUpperBoundsRetainEarlyChecksWithoutUsingLateDoneAsRestoration(bool acknowledgeLate) {
  var observer=new Observer{NetworkUpperBounds=true};await using var hardware=Hardware(observer);
  var task=Record(hardware);await Answer("disconnect");
  var reconnect=await Pending("outage-reconnect");
  if(acknowledgeLate){await observer.Recovered.Task.WaitAsync(TimeSpan.FromSeconds(5));await Task.Delay(100);}
  SubmissionOperatorStep.Respond(reconnect,SubmissionOperatorOutcome.Done);
  var result=await task;
  Assert.That(result.Passed,Is.True,string.Join(",",result.Issues.Concat(result.Measurements?.Issues??[])));
  using var doc=System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(_root,"recording","manual-reconnect-capture.json")));
  var bounds=doc.RootElement.GetProperty("componentBounds");
  var status=SubmissionOperatorStep.Read(reconnect);
  foreach(string component in observer.Components) {
   Assert.That(bounds.GetProperty(component).GetProperty("earliestUtc").GetDateTimeOffset(),Is.EqualTo(status.Request.CreatedUtc),"A successful network probe supplies no lower bound.");
   Assert.That(bounds.GetProperty(component).GetProperty("latestUtc").GetDateTimeOffset(),Is.LessThanOrEqualTo(observer.EarlyFunction!.Observation.EarliestUtc));
  }
  Assert.That(observer.FunctionsChecked,Is.EqualTo(1));
  Assert.That(observer.RestoredOriginal,Is.True);
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
 [TestCase(false)][TestCase(true)]
 public async Task PhysicalWindowExcludesOperatorDelayAndRetainsOriginalProof(bool acknowledgeLate) {
  var observer=new Observer{NetworkUpperBounds=true,PhysicalWindow="valid"};await using var hardware=Hardware(observer);
  var task=Record(hardware);await Answer("disconnect");var reconnect=await Pending("outage-reconnect");
  if(acknowledgeLate){await observer.Recovered.Task.WaitAsync(TimeSpan.FromSeconds(5));await Task.Delay(100);}
  SubmissionOperatorStep.Respond(reconnect,SubmissionOperatorOutcome.Done);
  var result=await task;Assert.That(result.Passed,Is.True,string.Join(",",result.Issues.Concat(result.Measurements?.Issues??[])));
  using var doc=System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(_root,"recording","manual-reconnect-capture.json")));
  var window=doc.RootElement.GetProperty("componentBounds").GetProperty("processor");
  Assert.That(window.GetProperty("earliestUtc").GetDateTimeOffset(),Is.GreaterThan(SubmissionOperatorStep.Read(reconnect).Request.CreatedUtc));
  Assert.That(window.GetProperty("latestUtc").GetDateTimeOffset(),Is.LessThan(observer.EarlyFunction!.Observation.EarliestUtc));
  Assert.That(doc.RootElement.GetProperty("restorationWindows").GetProperty("processor").ValueKind,Is.EqualTo(System.Text.Json.JsonValueKind.Object));
  Assert.That(observer.RestoredOriginal,Is.True);
 }
 [TestCase("stale")][TestCase("future")][TestCase("unknown")][TestCase("modified")][TestCase("reversed")]
 public async Task InvalidPhysicalWindowFailsAndStillRestores(string error) {
  var observer=new Observer{NetworkUpperBounds=true,PhysicalWindow=error};await using var hardware=Hardware(observer);
  var task=Record(hardware);await Answer("disconnect");await Answer("reconnect");
  Assert.That((await task).Passed,Is.False);Assert.That(observer.RestoredOriginal,Is.True);Assert.That(observer.FunctionsChecked,Is.Zero);
 }
 private sealed class Observer:ISubmissionManualOutageObserver,ISubmissionManualRestorationBounds,ISubmissionManualRestorationWindow,ISubmissionManualRecoverySession {
  public bool ProgramLoadIsLowerBound {get;set;}
  public IReadOnlyList<string> Components=>["processor","device"];
  public IReadOnlyList<string> Functions=>["control"];
  public bool WaitForObservation,EarlyReturn,WrongScope,RestoredOriginal,WatchCancelled;
  public bool LinkedWatchCancellation,UnexpectedWatchCancellation;
  public bool NetworkUpperBounds;
  public bool SessionEnabled,FailSessionClock,SessionStoppedBeforeRestore;
  public TaskCompletionSource SessionStarted=new(TaskCreationOptions.RunContinuationsAsynchronously);
  private SubmissionRecoverySession? session;
  private CancellationToken recordingToken;
  public Task StartRecoveryAsync(CancellationToken token) {
   if(!SessionEnabled)return Task.CompletedTask;
   var probed=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
   session=new SubmissionRecoverySession(async ct=>{
    await probed.Task.WaitAsync(ct);if(FailSessionClock)throw new IOException("Synthetic delayed collector failure");return null;
   },ct=>{var found=new[]{new SubmissionOutageFunction("control",SubmissionEvidenceOutcome.Passed,Capture())};probed.SetResult();return Task.FromResult(found);},TimeSpan.FromSeconds(5),recordingToken);
   SessionStarted.SetResult();return Task.CompletedTask;
  }
  public async Task StopRecoveryAsync(){if(session!=null){await session.StopAsync();SessionStoppedBeforeRestore=session.Completion.IsCompleted;}}

  public SubmissionOutageFunction? EarlyFunction;
  private IReadOnlyDictionary<string,SubmissionOutageCapture>? _networkBounds;
  public int DisconnectObservations,RestoreObservations,FunctionsChecked;
  public TaskCompletionSource Observed=new(TaskCreationOptions.RunContinuationsAsynchronously);
  public TaskCompletionSource Recovered=new(TaskCreationOptions.RunContinuationsAsynchronously);
  public string? RestorationProof;
  public string? PhysicalWindow;
  public SubmissionOutageCapture? ProgramLoaded;
  private SubmissionOutageCapture? _restoredBy;
  private string _root=null!;private int _sequence;
  public Task PreflightAsync(SubmissionOutageRecordingContext context,CancellationToken token) {_root=context.EvidenceDirectory;recordingToken=token;return Task.CompletedTask;}
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
   if(NetworkUpperBounds){_networkBounds=result;await Task.Delay(2,token);EarlyFunction=new("control",SubmissionEvidenceOutcome.Passed,Capture());}
   Recovered.TrySetResult();return result;
  }
  public Task<IReadOnlyDictionary<string,SubmissionOutageCapture>> CaptureRestoredByAsync(CancellationToken token) {
   if(NetworkUpperBounds)return Task.FromResult(_networkBounds!);
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
  public Task<IReadOnlyDictionary<string,SubmissionOutageCapture>> CaptureRestorationWindowAsync(CancellationToken token) {
   var result=new Dictionary<string,SubmissionOutageCapture>();
   if(PhysicalWindow!=null) {
    var proof=_restoredBy!;
    if(PhysicalWindow=="stale")proof=proof with {EarliestUtc=proof.EarliestUtc.AddMinutes(-1),LatestUtc=proof.LatestUtc.AddMinutes(-1)};
    if(PhysicalWindow=="future")proof=proof with {EarliestUtc=proof.EarliestUtc.AddMinutes(1),LatestUtc=proof.LatestUtc.AddMinutes(1)};
    if(PhysicalWindow=="reversed")proof=proof with {EarliestUtc=proof.LatestUtc.AddSeconds(1)};
    if(PhysicalWindow=="modified")File.AppendAllText(Path.Combine(_root,proof.Evidence.RelativePath),"changed");
    result[PhysicalWindow=="unknown"?"unknown":"processor"]=proof;
   }
   return Task.FromResult<IReadOnlyDictionary<string,SubmissionOutageCapture>>(result);
  }
  public async Task<SubmissionOutageFunction> VerifyFunctionAsync(string function,CancellationToken token) {
   FunctionsChecked++;if(session!=null)return (await session.Completion).Functions.Single(f=>f.Id==function);return EarlyFunction ?? new SubmissionOutageFunction(function,SubmissionEvidenceOutcome.Passed,Capture());
  }
  public Task<SubmissionOutageRestoredState> RestoreOriginalAsync(SubmissionOutageCapture original,CancellationToken token) {
   RestoredOriginal=true;return Task.FromResult(new SubmissionOutageRestoredState(Capture(),true));
  }
 }
}
