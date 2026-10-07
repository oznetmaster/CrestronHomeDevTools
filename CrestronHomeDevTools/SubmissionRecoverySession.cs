// Copyright (c) 2026 Neil Colvin. MIT licensed.
namespace CrestronHomeDevTools;

/// <summary>A manual observer may start one bounded session as soon as connectivity returns.
/// Start must not await operator acknowledgement or evidence export. Stop must join every task.</summary>
public interface ISubmissionManualRecoverySession
{
 Task StartRecoveryAsync(CancellationToken token);
 Task StopRecoveryAsync();
}

public sealed record SubmissionRecoveryObservation(SubmissionOutageCapture? Clock,
 SubmissionOutageFunction[] Functions);

/// <summary>Owns clock collection and functional observation independently. A delayed log cannot
/// postpone the first probes. All work is joined before completion or original-state restoration.</summary>
public sealed class SubmissionRecoverySession : IAsyncDisposable
{
 private readonly CancellationTokenSource lifetime;
 private readonly CancellationToken caller;
 private int stopped;
 public Task<SubmissionRecoveryObservation> Completion { get; }

 public SubmissionRecoverySession(Func<CancellationToken,Task<SubmissionOutageCapture?>> clock,
  Func<CancellationToken,Task<SubmissionOutageFunction[]>> probes,
  TimeSpan timeout,CancellationToken cancellationToken)
 {
  ArgumentNullException.ThrowIfNull(clock);ArgumentNullException.ThrowIfNull(probes);
  if(timeout<=TimeSpan.Zero || timeout>TimeSpan.FromMinutes(30))throw new ArgumentOutOfRangeException(nameof(timeout));
  caller=cancellationToken;
  lifetime=CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
  lifetime.CancelAfter(timeout);
  Completion=Run(clock,probes);
 }
 private async Task<SubmissionRecoveryObservation> Run(Func<CancellationToken,Task<SubmissionOutageCapture?>> clock,
  Func<CancellationToken,Task<SubmissionOutageFunction[]>> probes)
 {
  async Task<T> Observe<T>(Func<CancellationToken,Task<T>> operation) {
   try {lifetime.Token.ThrowIfCancellationRequested();return await operation(lifetime.Token).ConfigureAwait(false);}
   catch(OperationCanceledException error) when(!lifetime.IsCancellationRequested) {
    await lifetime.CancelAsync().ConfigureAwait(false);
    throw new IOException("Recovery observer cancelled without a cancellation request.",error);
   }
   catch {await lifetime.CancelAsync().ConfigureAwait(false);throw;}
  }
  var clockTask=Observe(clock);
  var probesTask=Observe(probes);
  // WhenAll joins both even if one fails or is cancelled.
  try {await Task.WhenAll(clockTask,probesTask).ConfigureAwait(false);}
  catch(OperationCanceledException error) when(!caller.IsCancellationRequested && Volatile.Read(ref stopped)==0) {
   throw new TimeoutException("Recovery observation exhausted its budget.",error);
  }
  var bound=await clockTask.ConfigureAwait(false);
  var observed=await probesTask.ConfigureAwait(false);
  // A pre-load success must not be relabelled as a post-load observation. Retain the
  // first batch in the producer journal and take a genuinely fresh batch if needed.
  if(bound!=null && observed.Any(f=>f.Observation.EarliestUtc<bound.LatestUtc))
   observed=await Observe(probes).ConfigureAwait(false);
  return new(bound,observed);
 }
 public async Task StopAsync() {
  Interlocked.Exchange(ref stopped,1);
  await lifetime.CancelAsync().ConfigureAwait(false);
  try {await Completion.ConfigureAwait(false);}
  catch(Exception error) when(error is not OutOfMemoryException) { /* Completion retains the original failure. */ }
 }
 public async ValueTask DisposeAsync(){await StopAsync().ConfigureAwait(false);lifetime.Dispose();}
}
