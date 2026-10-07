// Copyright (c) 2026 Neil Colvin. MIT licensed.
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CrestronHomeDevTools;

public sealed record SubmissionManualOutageSettings(SubmissionOperatorInbox Inbox, string Target,
 string DisconnectInstructions, string ReconnectInstructions, TimeSpan ResponseTimeout);

/// <summary>Independent observations for one coordinated, operator-performed interruption.
/// No member may disconnect equipment. Capture original state before the prompt; retain raw
/// observations under the recording directory. WatchInterruptedAsync must continue until cancelled,
/// and throw if a required endpoint returns early or observation becomes unreliable.</summary>
public interface ISubmissionManualOutageObserver
{
 bool ProgramLoadIsLowerBound => false;
 IReadOnlyList<string> StartupInstanceIds => Array.Empty<string>();
 Task<SubmissionOutageDriverInitialization[]> ObserveDriverInitializationsAsync(CancellationToken token)
     => Task.FromResult(Array.Empty<SubmissionOutageDriverInitialization>());
 IReadOnlyList<string> Components { get; }
 IReadOnlyList<string> Functions { get; }
 Task PreflightAsync(SubmissionOutageRecordingContext context, CancellationToken token);
 Task<SubmissionOutageCapture> CaptureOriginalAsync(CancellationToken token);
 Task<IReadOnlyDictionary<string, SubmissionOutageCapture>> ObserveInterruptedAsync(CancellationToken token);
 Task WatchInterruptedAsync(CancellationToken token);
 Task<IReadOnlyDictionary<string, SubmissionOutageCapture>> ObserveRestoredAsync(CancellationToken token);
 Task<SubmissionOutageCapture?> ObserveProgramLoadedAsync(string component, CancellationToken token);
 Task<SubmissionOutageFunction> VerifyFunctionAsync(string function, CancellationToken token);
 Task<SubmissionOutageRestoredState> RestoreOriginalAsync(SubmissionOutageCapture original, CancellationToken token);
}

/// <summary>Optional independent proof that a component was restored no later than a bounded
/// event. For example, a verified NEW boot proves power was restored by the latest possible
/// boot start. An open port or unchanged old boot cannot prove POWER restoration.
/// For a network-only interruption, fresh connectivity to the bound endpoint can
/// prove network restoration occurred no later than that observation. It cannot
/// establish the earliest physical event time or prove functional recovery.
/// Each returned capture must retain the raw proof; its LatestUtc caps restoration, while
/// the operator request remains the lower bound. Unknown or contradictory bounds fail closed.</summary>
public interface ISubmissionManualRestorationBounds
{
 Task<IReadOnlyDictionary<string,SubmissionOutageCapture>> CaptureRestoredByAsync(CancellationToken token);
}

/// <summary>Optional independently observed physical restoration window. Unlike a
/// restored-by observation, this must bracket the actual bound interface transition
/// using continuous local observation, with retained clock and identity proof. API
/// readiness, an operator reply and a new boot alone cannot supply this window.</summary>
public interface ISubmissionManualRestorationWindow
{
 Task<IReadOnlyDictionary<string,SubmissionOutageCapture>> CaptureRestorationWindowAsync(CancellationToken token);
}

/// <summary>Adapts a grouped manual action to the recorder's component interface. The operator
/// attests to physical scope; independent observations verify connectivity/functions. Event bounds
/// span request publication through acknowledgement/observation, never an invented exact instant.
/// Use one instance per attempt and dispose it after the recorder finishes.</summary>
public sealed class SubmissionManualOutageHardware : ISubmissionOutageHardware, ISubmissionOutageReadiness, IAsyncDisposable
{
 private readonly SubmissionManualOutageSettings _settings;
 private readonly ISubmissionManualOutageObserver _observer;
 private readonly string[] _components, _functions;
 private string? _root;
 private Task<IReadOnlyDictionary<string,SubmissionOutageCapture>>? _interruption, _restoration;
 private CancellationTokenSource? _watchLifetime;
 private Task? _watch;
 private Exception? _watchFailure;
 private static readonly JsonSerializerOptions Json = new() {
  PropertyNamingPolicy=JsonNamingPolicy.CamelCase, WriteIndented=true,
  Converters={new JsonStringEnumConverter()}
 };

 public SubmissionManualOutageHardware(SubmissionManualOutageSettings settings, ISubmissionManualOutageObserver observer)
 {
  ArgumentNullException.ThrowIfNull(settings); ArgumentNullException.ThrowIfNull(observer);
  if(settings.Inbox==null || !Path.IsPathFullyQualified(settings.Inbox.Directory) ||
   settings.Inbox.RunKey is not { Length:64 } || settings.Inbox.RunKey.Any(c=>!char.IsAsciiHexDigit(c)) ||
   string.IsNullOrWhiteSpace(settings.Target) || settings.Target.Length>256 ||
   string.IsNullOrWhiteSpace(settings.DisconnectInstructions) || settings.DisconnectInstructions.Length>3500 ||
   string.IsNullOrWhiteSpace(settings.ReconnectInstructions) || settings.ReconnectInstructions.Length>3500 ||
   settings.ResponseTimeout<TimeSpan.FromSeconds(1) || settings.ResponseTimeout>TimeSpan.FromMinutes(30))
   throw new ArgumentException("Manual interruption needs exact scope, both instructions and a bounded operator response time.");
  static string[] Copy(IReadOnlyList<string> values) {
   if(values is not { Count:>0 and <=128 } || values.Any(string.IsNullOrWhiteSpace) ||
    values.Distinct(StringComparer.Ordinal).Count()!=values.Count) throw new ArgumentException("Bind distinct component and function names.");
   return [..values];
  }
  _settings=settings; _observer=observer; _components=Copy(observer.Components); _functions=Copy(observer.Functions);
 }
 public IReadOnlyList<string> Components=>Array.AsReadOnly(_components);
 public bool ProgramLoadIsLowerBound=>_observer.ProgramLoadIsLowerBound;
 public IReadOnlyList<string> StartupInstanceIds=>_observer.StartupInstanceIds;
 public Task<SubmissionOutageDriverInitialization[]> ObserveDriverInitializationsAsync(CancellationToken token)
     => _observer.ObserveDriverInitializationsAsync(token);
 public IReadOnlyList<string> Functions=>Array.AsReadOnly(_functions);
 public async Task PreflightAsync(SubmissionOutageRecordingContext context,CancellationToken token)
 {
  if(_root!=null)throw new InvalidOperationException("Manual outage bindings cannot be replayed.");
  _root=context.EvidenceDirectory;
  await _observer.PreflightAsync(context,token).ConfigureAwait(false);
 }
 public Task<SubmissionOutageCapture> CaptureOriginalAsync(CancellationToken token)=>_observer.CaptureOriginalAsync(token);
 public async Task WaitUntilReadyAsync(CancellationToken token) {
  var status=await SubmissionPreparedReadiness.WaitAsync(_settings.Inbox,_settings.Target,token).ConfigureAwait(false);
  if(status!=null) {
   using var output=new FileStream(Path.Combine(_root!,"manual-readiness.json"),FileMode.CreateNew,FileAccess.Write,FileShare.Read);
   JsonSerializer.Serialize(output,status,Json);output.Flush(true);
  }
 }
 private void Component(string component) {
  if(_root==null || !_components.Contains(component,StringComparer.Ordinal))throw new InvalidDataException("Unbound interruption component.");
 }
 public async Task<SubmissionOutageCapture> InterruptAsync(string component,CancellationToken token) {
  Component(component); return (await (_interruption ??= DisconnectAsync(token)).ConfigureAwait(false))[component];
 }
 private async Task<IReadOnlyDictionary<string,SubmissionOutageCapture>> DisconnectAsync(CancellationToken token) {
  var result=await TransitionAsync(false,token).ConfigureAwait(false);
  _watchLifetime=new CancellationTokenSource();
  // Independent of the caller: watch until restoration even after the main observation expires.
  try { _watch=_observer.WatchInterruptedAsync(_watchLifetime.Token); }
  catch(Exception error) { _watch=Task.FromException(error); }
  return result;
 }
 public async Task<SubmissionOutageCapture> RestoreConnectivityAsync(string component,CancellationToken token) {
  Component(component);
  // The first interruption may have thrown after the operator acted. Always request restoration.
  return (await (_restoration ??= ReconnectAsync(token)).ConfigureAwait(false))[component];
 }
 private async Task<IReadOnlyDictionary<string,SubmissionOutageCapture>> ReconnectAsync(CancellationToken token) {
  await StopWatchAsync().ConfigureAwait(false);
  return await TransitionAsync(true,token).ConfigureAwait(false);
 }
 private async Task StopWatchAsync() {
  if(_watchLifetime==null || _watch==null)return;
  bool ended=_watch.IsCompleted;
  await _watchLifetime.CancelAsync().ConfigureAwait(false);
  try {
   await _watch.ConfigureAwait(false);
   if(ended)_watchFailure=new InvalidDataException("Interruption observer stopped before restoration.");
  // Socket/HTTP observations may use a linked timeout token. On our deliberate stop,
  // that linked token is cancelled too; its identity need not equal the parent token.
  // An observer already stopped/cancelled before our request still fails below.
  } catch(OperationCanceledException error) when(!ended && _watchLifetime.IsCancellationRequested && error.CancellationToken.IsCancellationRequested) { }
  catch(Exception error) { _watchFailure=error; }
  _watch=null;
 }
 private async Task<IReadOnlyDictionary<string,SubmissionOutageCapture>> TransitionAsync(bool reconnect,CancellationToken token) {
  string phase=reconnect?"reconnect":"disconnect";
  SubmissionOperatorHandle? handle=null;
  try {
   var result=await SubmissionPhysicalAction.ObserveAsync<IReadOnlyDictionary<string,SubmissionOutageCapture>>(_settings.Inbox,"outage-"+phase,_settings.Target,
    (reconnect?_settings.ReconnectInstructions:_settings.DisconnectInstructions)+
    (reconnect?" Confirm only after every listed connection has been restored.":
     " Confirm only after every listed disconnection is complete. Leave everything disconnected until the reconnect request appears."),
    _settings.ResponseTimeout,reconnect?ObserveRestoredAndStartAsync:_observer.ObserveInterruptedAsync,
    token,h=>handle=h).ConfigureAwait(false);
   var status=SubmissionOperatorStep.Read(result.Handle);
   var observations=result.Observation;
   if(!_components.Order(StringComparer.Ordinal).SequenceEqual(observations.Keys.Order(StringComparer.Ordinal),StringComparer.Ordinal) ||
    observations.Values.Any(c=>c==null || c.LatestUtc<status.Request.CreatedUtc || c.LatestUtc<c.EarliestUtc || c.LatestUtc>DateTimeOffset.UtcNow))
    throw new InvalidDataException("Manual interruption observer returned incomplete scope or stale bounds.");
   IReadOnlyDictionary<string,SubmissionOutageCapture> restoredBy = reconnect && _observer is ISubmissionManualRestorationBounds bounded
    ? await bounded.CaptureRestoredByAsync(token).ConfigureAwait(false) : new Dictionary<string,SubmissionOutageCapture>();
   if(restoredBy.Any(p=>!_components.Contains(p.Key,StringComparer.Ordinal) || p.Value==null ||
    p.Value.EarliestUtc>p.Value.LatestUtc || p.Value.LatestUtc<status.Request.CreatedUtc ||
    p.Value.LatestUtc>observations[p.Key].LatestUtc))
    throw new InvalidDataException("Independent restoration proof is stale, contradictory or outside the observed scope.");
   IReadOnlyDictionary<string,SubmissionOutageCapture> windows = reconnect && _observer is ISubmissionManualRestorationWindow windowed
    ? await windowed.CaptureRestorationWindowAsync(token).ConfigureAwait(false) : new Dictionary<string,SubmissionOutageCapture>();
   if(windows.Any(p=>!_components.Contains(p.Key,StringComparer.Ordinal) || p.Value==null ||
    p.Value.EarliestUtc>p.Value.LatestUtc || p.Value.LatestUtc<status.Request.CreatedUtc ||
    p.Value.EarliestUtc>observations[p.Key].LatestUtc ||
    (restoredBy.TryGetValue(p.Key,out var by) && p.Value.EarliestUtc>by.LatestUtc)))
    throw new InvalidDataException("Physical restoration window contradicts the request, connectivity or component scope.");
   // Copies reference immutable raw evidence. The enclosing record includes the full operator
   // request/response, so source inbox retention is not needed to interpret these bounds.
   var raw=new Dictionary<string,string>(StringComparer.Ordinal);
   foreach(var capture in observations.Values.Concat(restoredBy.Values).Concat(windows.Values)) {
    if(!SubmissionEvidence.SafeEvidencePath(_root!,capture.Evidence.RelativePath,out string path) ||
     new FileInfo(path).Length>65536)
     throw new InvalidDataException("Manual transition raw observation is missing or exceeds 64 KiB; retain a bounded observation summary.");
    byte[] content=File.ReadAllBytes(path);
    if(!string.Equals(Convert.ToHexStringLower(SHA256.HashData(content)),capture.Evidence.Sha256,StringComparison.OrdinalIgnoreCase))
     throw new InvalidDataException("Manual transition raw evidence is missing or changed.");
    raw.TryAdd(capture.Evidence.RelativePath,Convert.ToBase64String(content));
   }
   DateTimeOffset latest=observations.Values.Select(c=>c.LatestUtc).Append(result.Response.RecordedUtc).Max();
   var bounds=_components.ToDictionary(c=>c,c=>new {
    EarliestUtc=windows.TryGetValue(c,out var window) && window.EarliestUtc>status.Request.CreatedUtc?window.EarliestUtc:status.Request.CreatedUtc,
    LatestUtc=new[]{latest,restoredBy.TryGetValue(c,out var proof)?proof.LatestUtc:latest,
     windows.TryGetValue(c,out var endWindow)?endWindow.LatestUtc:latest}.Min() },StringComparer.Ordinal);
   byte[] bytes=JsonSerializer.SerializeToUtf8Bytes(new { Operator=status, Observations=observations, RestoredBy=restoredBy, RestorationWindows=windows, ComponentBounds=bounds, RawCapturesBase64=raw,
    HoldObserverErrorType=reconnect?_watchFailure?.GetType().Name:null,
    PhysicalScope="Operator attestation, independently observed connectivity. Bounds are not exact physical timestamps.",
    EarliestUtc=status.Request.CreatedUtc, LatestUtc=latest },Json);
   string relative="manual-"+phase+"-capture.json";
   using(var stream=new FileStream(Path.Combine(_root!,relative),FileMode.CreateNew,FileAccess.Write,FileShare.Read)) {
    stream.Write(bytes);stream.Flush(true);
   }
   var evidence=new SubmissionEvidenceFile(relative,Convert.ToHexStringLower(SHA256.HashData(bytes)));
   return bounds.ToDictionary(p=>p.Key,p=>new SubmissionOutageCapture(p.Value.EarliestUtc,p.Value.LatestUtc,evidence),StringComparer.Ordinal);
  } finally {
   if(handle!=null) {
    using var stream=new FileStream(Path.Combine(_root!,"manual-"+phase+"-operator.json"),FileMode.CreateNew);
    JsonSerializer.Serialize(stream,SubmissionOperatorStep.Read(handle),Json);stream.Flush(true);
   }
  }
 }
 private async Task<IReadOnlyDictionary<string,SubmissionOutageCapture>> ObserveRestoredAndStartAsync(CancellationToken token) {
  var connected=await _observer.ObserveRestoredAsync(token).ConfigureAwait(false);
  if(_observer is ISubmissionManualRecoverySession recovery)await recovery.StartRecoveryAsync(token).ConfigureAwait(false);
  return connected;
 }
 public async Task<SubmissionOutageCapture?> ObserveProgramLoadedAsync(string component,CancellationToken token) {
  Component(component); RequireWatchPassed();
  return await _observer.ObserveProgramLoadedAsync(component,token).ConfigureAwait(false);
 }
 private void RequireWatchPassed() {
  if(_watchFailure!=null)throw new InvalidDataException("Continuous interruption observation failed ("+_watchFailure.GetType().Name+").");
 }
 public Task<SubmissionOutageFunction> VerifyFunctionAsync(string function,CancellationToken token) {
  RequireWatchPassed();
  if(!_functions.Contains(function,StringComparer.Ordinal))throw new InvalidDataException("Unbound recovery function.");
  return _observer.VerifyFunctionAsync(function,token);
 }
 public async Task<SubmissionOutageRestoredState> RestoreOriginalAsync(SubmissionOutageCapture original,CancellationToken token) {
  if(_observer is ISubmissionManualRecoverySession recovery)await recovery.StopRecoveryAsync().ConfigureAwait(false);
  return await _observer.RestoreOriginalAsync(original,token).ConfigureAwait(false);
 }
 public async ValueTask DisposeAsync() {
  if(_observer is ISubmissionManualRecoverySession recovery)await recovery.StopRecoveryAsync().ConfigureAwait(false);
  await StopWatchAsync().ConfigureAwait(false);_watchLifetime?.Dispose();
 }
}
