// Copyright (c) 2026 Neil Colvin. MIT licensed.
using System.Diagnostics;

namespace CrestronHomeDevTools;

public enum SubmissionRecoveryPhase { Clock, Binding, Readiness, Control }

/// <summary>Preserves the failing concurrent branch without exposing transport messages.</summary>
public sealed class SubmissionRecoveryException : IOException
{
 public SubmissionRecoveryPhase Phase { get; }
 public SubmissionRecoveryException(SubmissionRecoveryPhase phase,Exception cause)
  :base("Recovery observation failed.",cause)
 {
  if(!Enum.IsDefined(phase))throw new ArgumentOutOfRangeException(nameof(phase));
  ArgumentNullException.ThrowIfNull(cause);Phase=phase;
 }
 public static async Task<T> ObserveAsync<T>(SubmissionRecoveryPhase phase,Func<Task<T>> observe)
  => await ObserveAsync(phase,observe,default).ConfigureAwait(false);
 public static async Task<T> ObserveAsync<T>(SubmissionRecoveryPhase phase,Func<Task<T>> observe,CancellationToken token)
 {
  try{return await observe().ConfigureAwait(false);}
  catch(OperationCanceledException) when(token.IsCancellationRequested){throw;}
  catch(OperationCanceledException error){throw new SubmissionRecoveryException(phase,
   new IOException("Recovery observer cancelled without a cancellation request.",error));}
  catch(SubmissionRecoveryException){throw;}
  catch(Exception error){throw new SubmissionRecoveryException(phase,error);}
 }
}

public sealed record SubmissionRecoveryFailure(string AwaitingStage,string FailedStage,
 string ErrorType,string? ReasonCode,string[] CallSites)
{
 public string Issue=>FailedStage+":"+ErrorType;
 public static SubmissionRecoveryFailure Capture(string awaitingStage,Exception error)
 {
  var branch=error as SubmissionRecoveryException;
  var cause=branch?.InnerException??error;
  var stage=branch==null?awaitingStage:"recovery-"+branch.Phase.ToString().ToLowerInvariant();
  // Only fixed messages produced by our checks become reason codes. Never retain
  // arbitrary messages, exception Data, request arguments or response bodies.
  var reason=cause is InvalidDataException?cause.Message switch {
   "Configuration or identity differs from the approved baseline."=>"binding-changed",
   "Processor or Home program restarted during recovery verification, or the timing clock is inconsistent."=>"epoch-inconsistent",
   "Expected the verified Home program and its explicit local start timestamp."=>"program-start-invalid",
   "Log date, program clock or bounded input is invalid."=>"log-input-invalid",
   "Ambiguous Home startup epoch."=>"startup-ambiguous",
   "Home restarted after the selected epoch."=>"home-restarted",
   "Ambiguous Home load completion."=>"load-ambiguous",
   "Home load duration and calendar disagree; do not infer a recovery clock."=>"load-clock-inconsistent",
   "Home load event is outside the verified observation."=>"load-outside-observation",
   "Home diagnostic log exceeds the capture budget."=>"log-budget-exceeded",
   _=>"unclassified-invalid-data"
  }:null;
  var sites=new StackTrace(cause,true).GetFrames().Take(12).Select(f=>new{Method=f.GetMethod(),Line=f.GetFileLineNumber()})
   .Where(f=>f.Method!=null).Select(f=>f.Method!.DeclaringType?.FullName+"."+f.Method.Name+":"+f.Line).ToArray();
  return new(awaitingStage,stage,cause.GetType().Name,reason,sites);
 }
}
