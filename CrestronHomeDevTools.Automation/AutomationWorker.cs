// Copyright (c) 2026 Neil Colvin. MIT licensed.
using System.Text.Json;
namespace CrestronHomeDevTools.Automation;

/// <summary>Ordinary background process for Task Scheduler. No AI heartbeat or per-poll history files.</summary>
internal static class AutomationWorker
{
 internal sealed record Status(string Profile,long ReleaseId,SubmissionAutomationMode Mode,string State,string? Stage,string? Reason);
 internal static bool Owns(SubmissionWorkflowStage stage,SubmissionAutomationWorkerRole role)=>
  (stage>=SubmissionWorkflowStage.SignReview)==(role==SubmissionAutomationWorkerRole.Protected);

 internal static async Task<Status[]> Tick(string registryPath,SubmissionAutomationWorkerRole role,CancellationToken token,
  Func<AutomationRequest,CancellationToken,Task<SubmissionWorkflowCheckpoint>>? advance=null,AutomationProtectedWorker? protection=null)
 {
  var registry=AutomationFiles.Read<SubmissionAutomationRegistry>(registryPath);
  if(registry.SchemaVersion!=1 || registry.Entries.Length>1000 || !Enum.IsDefined(role))throw new InvalidDataException("Invalid worker registry.");
  var statuses=new List<Status>();
  foreach(var entry in registry.Entries) {
   token.ThrowIfCancellationRequested();
   AutomationRequest? currentRequest=null;
   try {
    var request=AutomationRequest.Load(["--registry",registryPath,"--profile",entry.Profile,"--release-id",entry.ReleaseId.ToString(System.Globalization.CultureInfo.InvariantCulture),"--mode",entry.Mode==SubmissionAutomationMode.Rehearsal?"rehearsal":"submit"]);
    currentRequest=request;
    var s=request.Settings;
    var checkpoint=SubmissionWorkflow.Read(s.PrivateRoot,s.Release);
    if(Owns(checkpoint.Stage,role) && checkpoint.Status is SubmissionWorkflowStatus.Ready or SubmissionWorkflowStatus.Running or SubmissionWorkflowStatus.Waiting) {
     using var deadline=CancellationTokenSource.CreateLinkedTokenSource(token);deadline.CancelAfter(TimeSpan.FromHours(6));
     checkpoint=await (advance??((r,t)=>SubmissionWorkflow.AdvanceAsync(r.Settings.PrivateRoot,r.Settings.Release,new SubmissionAutomationStages(r.Settings,r.Sha256,role,protection),t)))(request,deadline.Token);
    }
    statuses.Add(new(entry.Profile,entry.ReleaseId,entry.Mode,checkpoint.Status.ToString(),checkpoint.Stage.ToString(),checkpoint.ReasonCode));
   } catch(OperationCanceledException) when(token.IsCancellationRequested) {throw;}
   catch(Exception error) when(error is not OutOfMemoryException) {
    // Another process holding the workflow lock is not a failed device test. Preserve all domain journals.
    bool busy=error is IOException && (error.HResult&0xffff) is 32 or 33;
    string? stage=null;
    if(!busy && currentRequest is {} failed) {
     try {stage=SubmissionWorkflow.Read(failed.Settings.PrivateRoot,failed.Settings.Release).Stage.ToString();}
     catch(Exception inspectionError) when(inspectionError is not OutOfMemoryException) { }
    }
    statuses.Add(new(entry.Profile,entry.ReleaseId,entry.Mode,busy?"Busy":"AttentionRequired",stage,busy?"workflow-in-use":error.GetType().Name));
   }
  }
  return statuses.ToArray();
 }
 internal static bool SaveStatus(string directory,Status[] statuses) {
  Directory.CreateDirectory(directory);string file=Path.Combine(directory,"worker-status.json");
  byte[] bytes=JsonSerializer.SerializeToUtf8Bytes(statuses,AutomationFiles.Json);
  if(File.Exists(file)&&File.ReadAllBytes(file).AsSpan().SequenceEqual(bytes))return false;
  string pending=file+".tmp";using(var stream=new FileStream(pending,FileMode.Create,FileAccess.Write,FileShare.None)){stream.Write(bytes);stream.Flush(true);}
  File.Move(pending,file,true);
  string history=Path.Combine(directory,"worker-history.jsonl");
  if(File.Exists(history)&&new FileInfo(history).Length>=1024*1024)File.Move(history,history+".1",true);
  File.AppendAllText(history,JsonSerializer.Serialize(new{ObservedUtc=DateTimeOffset.UtcNow,Statuses=statuses})+Environment.NewLine);
  return true;
 }
 internal static async Task<DateTimeOffset> Cycle(string registryPath,string statusDirectory,SubmissionAutomationWorkerRole role,
  DateTimeOffset nextDiscovery,CancellationToken token,string? profilesPath=null,AutomationProtectedWorker? protection=null,
  Func<CancellationToken,Task<AutomationReleaseDiscovery.Status[]>>? discover=null,
  Func<AutomationRequest,CancellationToken,Task<SubmissionWorkflowCheckpoint>>? advance=null,DateTimeOffset? observedUtc=null) {
  var now=observedUtc??DateTimeOffset.UtcNow;
  if(profilesPath!=null && role!=SubmissionAutomationWorkerRole.Evidence)throw new InvalidDataException("Only the evidence worker can discover releases.");
  if(profilesPath!=null && now>=nextDiscovery) {
   using var limit=CancellationTokenSource.CreateLinkedTokenSource(token);limit.CancelAfter(TimeSpan.FromMinutes(30));
   using var http=new HttpClient();
   var discovered=await (discover??(t=>AutomationReleaseDiscovery.Tick(profilesPath,registryPath,new GitHubSubmissionRelease(http),t)))(limit.Token);
   SaveStatus(Path.Combine(statusDirectory,"release-discovery"),discovered.Select(s=>new Status(s.Profile,s.ReleaseId??0,s.Mode,s.State,"ReleaseIntake",s.Reason)).ToArray());
   nextDiscovery=(observedUtc??DateTimeOffset.UtcNow).AddMinutes(15);
  }
  var states=await Tick(registryPath,role,token,advance,protection);
  if(SaveStatus(statusDirectory,states))Console.WriteLine("Submission worker status changed; inspect the private worker status.");
  return nextDiscovery;
 }
 internal static async Task<int> Watch(string registryPath,string statusDirectory,SubmissionAutomationWorkerRole role,TimeSpan interval,CancellationToken token,string? profilesPath=null,AutomationProtectedWorker? protection=null) {
  if(!Path.IsPathFullyQualified(registryPath)||!Path.IsPathFullyQualified(statusDirectory)||interval<TimeSpan.FromSeconds(30)||interval>TimeSpan.FromMinutes(15))
   throw new InvalidDataException("Use absolute private paths and a 30-second to 15-minute polling interval.");
  if(profilesPath!=null && (!Path.IsPathFullyQualified(profilesPath)||role!=SubmissionAutomationWorkerRole.Evidence))throw new InvalidDataException("Only an evidence worker can monitor absolute private release profiles.");
  Directory.CreateDirectory(statusDirectory);
  // Keep one watcher per installed role/status directory; run.lock protects dispatches sharing a run.
  using var gate=new FileStream(Path.Combine(statusDirectory,"worker.lock"),FileMode.OpenOrCreate,FileAccess.Write,FileShare.None);
  DateTimeOffset nextDiscovery=DateTimeOffset.MinValue;
  while(!token.IsCancellationRequested) {
   nextDiscovery=await Cycle(registryPath,statusDirectory,role,nextDiscovery,token,profilesPath,protection);
   await Task.Delay(interval,token);
  }
  return 0;
 }
}
