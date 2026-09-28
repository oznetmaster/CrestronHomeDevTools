// Copyright (c) 2026 Neil Colvin. MIT licensed.
namespace CrestronHomeDevTools;

public sealed record SubmissionOperatorInbox(string Directory,string RunKey);
public sealed record SubmissionPhysicalActionResult<T>(SubmissionOperatorHandle Handle,SubmissionOperatorResponse Response,T Observation);

/// <summary>Pairs planned human participation with independent observation. Does not establish
/// physical event timing or implement hardware-specific restoration.</summary>
public static class SubmissionPhysicalAction
{
 public static async Task<SubmissionPhysicalActionResult<T>> ObserveAsync<T>(SubmissionOperatorInbox inbox,
  string step,string target,string instructions,TimeSpan timeout,
  Func<CancellationToken,Task<T>> observe,CancellationToken cancellationToken=default,
  Action<SubmissionOperatorHandle>? requested=null)
 {
  ArgumentNullException.ThrowIfNull(observe);
  cancellationToken.ThrowIfCancellationRequested();
  using var lifetime=CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
  lifetime.CancelAfter(timeout);
  // The observer arms first. It must use a previously recorded baseline to reject stale state.
  var observation=observe(lifetime.Token);
  SubmissionOperatorHandle? handle=null;
  Task<SubmissionOperatorResponse>? response=null;
  try {
   if(observation.IsFaulted || observation.IsCanceled)await observation.ConfigureAwait(false);
   handle=SubmissionOperatorStep.Create(inbox.Directory,inbox.RunKey,step,target,instructions,timeout);
   response=SubmissionOperatorStep.WaitAsync(handle,lifetime.Token);
   requested?.Invoke(handle);
   var first=await Task.WhenAny(observation,response).ConfigureAwait(false);
   if(first==observation)await observation.ConfigureAwait(false); // Surface a failed observer without waiting for a reply.
   var answer=await response.ConfigureAwait(false);
   if(answer.Outcome!=SubmissionOperatorOutcome.Done)
    throw new InvalidOperationException($"Physical action ended with {answer.Outcome}; no test pass is implied.");
   T observed=await observation.ConfigureAwait(false);
   cancellationToken.ThrowIfCancellationRequested();
   return new(handle,answer,observed);
  } finally {
   // Close a published request even if observation fails; never leave a stale trigger prompt active.
   await lifetime.CancelAsync().ConfigureAwait(false);
   try {await observation.ConfigureAwait(false);} catch(Exception e) when(e is not OutOfMemoryException) { /* Original failure is propagated by the body. */ }
   if(response!=null)await response.ConfigureAwait(false);
  }
 }
}
