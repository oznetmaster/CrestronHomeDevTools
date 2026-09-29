// Copyright (c) 2026 Neil Colvin. MIT licensed.
using System.Text.Json;
namespace CrestronHomeDevTools;

/// <summary>A fixture calls this after setup/navigation and before refreshing its event baseline.
/// The workflow supplies the exact request and excludes this indefinite wait from its active-work budget.</summary>
public static class SubmissionPreparedReadiness
{
 public const string EnvironmentVariable = "CRESTRON_SUBMISSION_PREPARED_READINESS";
 private sealed record Binding(string Directory,string RunKey,string Step,string Instructions);
 public static async Task<SubmissionOperatorStatus?> WaitAsync(SubmissionOperatorInbox inbox,string target,CancellationToken token=default) {
  string? value=Environment.GetEnvironmentVariable(EnvironmentVariable);
  if(string.IsNullOrWhiteSpace(value))return null; // Legacy outer readiness remains compatible.
  if(value.Length>8192)throw new InvalidDataException("Prepared readiness binding is too large.");
  var binding=JsonSerializer.Deserialize<Binding>(value,new JsonSerializerOptions {
   UnmappedMemberHandling=System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
   RespectRequiredConstructorParameters=true,AllowDuplicateProperties=false
  })??throw new InvalidDataException("Missing prepared readiness binding.");
  if(binding.RunKey!=inbox.RunKey || !Path.GetFullPath(binding.Directory).Equals(Path.GetFullPath(inbox.Directory),StringComparison.OrdinalIgnoreCase))
   throw new InvalidDataException("Fixture readiness inbox differs from its frozen workflow binding.");
  var handle=SubmissionOperatorStep.GetOrCreateReadiness(inbox.Directory,inbox.RunKey,binding.Step,target,binding.Instructions);
  if(!SubmissionOperatorStep.Read(handle).Waiting)throw new InvalidOperationException("This prepared fixture was already acknowledged; do not replay a physical attempt.");
  var response=await SubmissionOperatorStep.WaitAsync(handle,token).ConfigureAwait(false);
  if(response.Outcome!=SubmissionOperatorOutcome.Done)throw new InvalidOperationException("Operator cannot perform the prepared test: "+response.Reason);
  return SubmissionOperatorStep.Read(handle);
 }
}
