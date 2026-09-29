// Copyright (c) 2026 Neil Colvin. MIT licensed.
using System.Security.Cryptography;
using System.Text.Json;

namespace CrestronHomeDevTools.Automation;

/// <summary>A notification only; dismissing it grants no test, signature or delivery authority.</summary>
public sealed record SubmissionOperatorAlert(string Key,string Profile,string Repository,string Tag,string State,
 string Stage,string Reason,DateTimeOffset ChangedUtc,string EvidenceDirectory);

/// <summary>Read the worker's stable notices on the controlling computer without advancing the run.</summary>
public static class SubmissionOperatorAlerts
{
 public static IReadOnlyList<SubmissionOperatorAlert> Read(string registry,IReadOnlyList<string> profiles,string statusDirectory) {
  if(!Path.IsPathFullyQualified(statusDirectory))throw new ArgumentException("Use an absolute private worker status directory.");
  var selected=SubmissionOperatorDiscovery.ReadSettings(registry,profiles);
  // This snapshot suppresses ordinary Busy/recovery transitions in the worker.
  string path=Path.Combine(statusDirectory,"notifications","worker-status.json");
  if(!File.Exists(path)) {
   if(selected.Count==0 || Directory.Exists(statusDirectory))return [];
   throw new IOException("Worker status directory is unavailable.");
  }
  var statuses=AutomationFiles.Read<AutomationWorker.Status[]>(path);
  if(statuses.Length>1000 || statuses.Any(s=>s is null) || statuses.GroupBy(s=>(s.Profile,s.ReleaseId,s.Mode)).Any(g=>g.Count()!=1))
   throw new InvalidDataException("Invalid worker notification identities.");
  var result=new List<SubmissionOperatorAlert>();
  var changed=new DateTimeOffset(File.GetLastWriteTimeUtc(path));
  foreach(var (entry,settings) in selected) {
   var state=statuses.SingleOrDefault(s=>s.Profile==entry.Profile && s.ReleaseId==entry.ReleaseId && s.Mode==entry.Mode);
   if(state==null)throw new IOException("Registered run has not yet been observed by this worker.");
   if(state.State is not ("Ready" or "Running" or "Waiting" or "NeedsInput" or "Completed" or "Failed" or "OutcomeUnknown" or "AttentionRequired"))
    throw new InvalidDataException("Unknown worker notification state.");
   bool terminalReview=state.State=="NeedsInput" && state.Stage=="SignReview" && state.Reason=="rehearsal-ready-for-review";
   bool completed=state.State=="Completed" && state.Stage=="Retain";
   if(state.State is not ("Failed" or "OutcomeUnknown" or "AttentionRequired") && !terminalReview && !completed)continue;
   if(state.Stage!=null && (!Enum.TryParse<SubmissionWorkflowStage>(state.Stage,out var stage) || !Enum.IsDefined(stage)))
    throw new InvalidDataException("Invalid worker notification stage.");
   string key=Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new{entry.SettingsSha256,state})));
   result.Add(new(key,entry.Profile,settings.Release.Repository,settings.Release.Tag,state.State,state.Stage??"Not established",
    state.Reason??(completed?"workflow-completed":"Inspect retained error"),changed,Path.Combine(settings.PrivateRoot,SubmissionWorkflow.RunKey(settings.Release))));
  }
  return result;
 }
}
