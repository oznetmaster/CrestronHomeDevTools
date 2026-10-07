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
  var retained=selected.SelectMany(pair=>RetainedAppFailures(pair.Entry,pair.Settings)).ToArray();
  if(!File.Exists(path)) {
   if(retained.Length>0)return retained;
   if(selected.Count==0 || Directory.Exists(statusDirectory))return [];
   throw new IOException("Worker status directory is unavailable.");
  }
  var statuses=AutomationFiles.Read<AutomationWorker.Status[]>(path);
  if(statuses.Length>1000 || statuses.Any(s=>s is null) || statuses.GroupBy(s=>(s.Profile,s.ReleaseId,s.Mode)).Any(g=>g.Count()!=1))
   throw new InvalidDataException("Invalid worker notification identities.");
  var result=new List<SubmissionOperatorAlert>(retained);
  var changed=new DateTimeOffset(File.GetLastWriteTimeUtc(path));
  foreach(var (entry,settings) in selected) {
   // A terminal producer result is independent of the background status writer.
   // In particular, an explicit recovery command can exit before that writer starts.
   if(retained.Any(a=>a.Profile==entry.Profile && a.EvidenceDirectory==Path.Combine(settings.PrivateRoot,SubmissionWorkflow.RunKey(settings.Release))))continue;
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

 private static IEnumerable<SubmissionOperatorAlert> RetainedAppFailures(SubmissionAutomationRegistration entry,SubmissionAutomationSettings settings) {
  string run=Path.Combine(settings.PrivateRoot,SubmissionWorkflow.RunKey(settings.Release));
  var steps=new List<string>();
  void AddSteps(string phase,int count) {
   if(count==0)steps.Add(phase);
   else for(int i=0;i<count;i++)steps.Add(Path.Combine(phase,"installed-app","steps",i.ToString("D3",System.Globalization.CultureInfo.InvariantCulture)));
  }
  if(settings.InstalledAppTests!=null)AddSteps(run,settings.InstalledAppSteps?.Length??0);
  if(settings.PreEnduranceTests!=null)AddSteps(Path.Combine(run,AutomationInitialAdditionalTests.DirectoryName),settings.PreEnduranceAppSteps?.Length??0);
  if(settings.PostEnduranceTests!=null)AddSteps(Path.Combine(run,AutomationPostEndurance.DirectoryName),settings.PostEnduranceAppSteps?.Length??0);
  foreach(string step in steps) {
   if(File.Exists(Path.Combine(step,"installed-app-tests.json")))continue;
   string relative=Path.GetRelativePath(run,Path.Combine(step,"installed-app","InstalledDriverTests.json"));
   string recovery=Path.GetRelativePath(run,Path.Combine(step,"installed-app","preparation-recovery","installed-app","InstalledDriverTests.json"));
   if(File.Exists(Path.Combine(run,recovery)))relative=recovery;
   var outcomes=new List<string>{relative};
   string attempts=Path.Combine(step,"installed-app",AutomationAppStepRecovery.Attempts);
   if(Directory.Exists(attempts)) {
    var directories=Directory.GetDirectories(attempts);
    if(directories.Length>128)throw new InvalidDataException("Too many app replacement attempts.");
    foreach(string directory in directories) {
     AutomationAppStepRecovery.RequireId(Path.GetFileName(directory));
     outcomes.Add(Path.GetRelativePath(run,Path.Combine(directory,"installed-app","InstalledDriverTests.json")));
    }
   }
   foreach(string outcome in outcomes) {
   relative=outcome;
   if(!File.Exists(Path.Combine(run,relative)))continue;
   if(!SubmissionEvidence.SafeEvidencePath(run,relative,out var file) || new FileInfo(file).Length>65536)
    throw new InvalidDataException("Unsafe or oversized installed-app outcome.");
   // InstalledDriverTests holds its report open for writing with FileShare.Read
   // throughout the run. Readers must reciprocally allow that existing writer;
   // File.ReadAllBytes uses FileShare.Read and fails even for a flushed snapshot.
   byte[] bytes;
   using(var input=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.ReadWrite)) {
    if(input.Length>65536)throw new InvalidDataException("Oversized installed-app outcome.");
    bytes=new byte[checked((int)input.Length)];input.ReadExactly(bytes);
   }
   using var document=JsonDocument.Parse(bytes);
   var value=document.RootElement;
   bool failed=value.TryGetProperty("State",out var state) && state.GetString()=="Failed" ||
    value.TryGetProperty("Passed",out var passed) && passed.ValueKind==JsonValueKind.False;
   if(!failed)continue;
   string key=Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new{entry.SettingsSha256,relative,Hash=Convert.ToHexStringLower(SHA256.HashData(bytes))})));
   yield return new(key,entry.Profile,settings.Release.Repository,settings.Release.Tag,"AttentionRequired","AppTests",
    value.TryGetProperty("Detail",out var detail) && detail.ValueKind==JsonValueKind.String && detail.GetString() is {Length:>0 and <=2000} description
     ? description : "Installed-app test failed. Inspect the retained test output and restoration result; the workflow has not continued.",
    new DateTimeOffset(File.GetLastWriteTimeUtc(file)),run);
   }
  }
 }
}
