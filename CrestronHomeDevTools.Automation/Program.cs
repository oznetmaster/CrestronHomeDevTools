// Copyright (c) 2026 Neil Colvin. MIT licensed.
using System.Text.Json;
using CrestronHomeDevTools;
using CrestronHomeDevTools.Automation;

if(args is ["--help"])
{
 Console.WriteLine("Read-only stage-binding check: --check-settings PRIVATE_JSON --settings-sha256 PIN. Exit zero means all stage bindings are present, not that tests passed or credentials/equipment were validated.");
 Console.WriteLine("submission automation: --settings PRIVATE_JSON --settings-sha256 PIN; or --registry PRIVATE_JSON --profile NAME --release-id ID --mode rehearsal|submit. Background: --watch-registry PRIVATE_JSON --status-directory PRIVATE_DIRECTORY --poll-seconds 60 [--release-profiles PRIVATE_JSON]. One-time intake: --intake-releases PRIVATE_PROFILES --registry PRIVATE_JSON. Default role is evidence. For the protected role append --protected-worker PRIVATE_JSON --protected-worker-sha256 INDEPENDENT_PIN --role protected. Rehearsal stops before signing/delivery. Submit requires exact authorizations. The ordinary background worker resumes waits without AI prompts.");return 0;
}
try
{
 if(args is ["--check-settings",var checkPath,"--settings-sha256",var checkDigest]) {
  var check=AutomationRequest.Load(["--settings",checkPath,"--settings-sha256",checkDigest]);
  var report=SubmissionAutomationConfiguration.Check(check.Settings);
  Console.WriteLine(JsonSerializer.Serialize(report,AutomationFiles.Json));
  return report.AllStageBindingsPresent?0:3;
 }
 var role=SubmissionAutomationWorkerRole.Evidence;
 if(args.Length>=2 && args[^2]=="--role") {
  role=args[^1] switch{"evidence"=>SubmissionAutomationWorkerRole.Evidence,"protected"=>SubmissionAutomationWorkerRole.Protected,_=>throw new InvalidDataException("Select an installed worker role.")};args=args[..^2];
 }
 AutomationProtectedWorker? protection=null;
 if(args.Length>=4 && args[^4]=="--protected-worker" && args[^2]=="--protected-worker-sha256") {
  protection=AutomationProtectedWorker.Load(args[^3],args[^1]);args=args[..^4];
 }
 if((role==SubmissionAutomationWorkerRole.Protected)!=(protection!=null))throw new InvalidDataException("Protected role requires its independently pinned installed configuration; evidence role cannot use it.");
 if(args is ["--intake-releases",var profiles,"--registry",var intakeRegistry]) {
  if(role!=SubmissionAutomationWorkerRole.Evidence)throw new InvalidDataException("Release intake belongs to the evidence worker.");
  using var http=new HttpClient();using var limit=new CancellationTokenSource(TimeSpan.FromMinutes(30));
  var result=await AutomationReleaseDiscovery.Tick(profiles,intakeRegistry,new GitHubSubmissionRelease(http),limit.Token);
  Console.WriteLine(JsonSerializer.Serialize(result,AutomationFiles.Json));
  return result.Any(r=>r.State=="AttentionRequired")?3:0;
 }
 string? releaseProfiles=null;
 if(args.Length>=2 && args[^2]=="--release-profiles") {releaseProfiles=args[^1];args=args[..^2];}
 if(args is ["--watch-registry",var registry,"--status-directory",var status,"--poll-seconds",var seconds]) {
  if(!int.TryParse(seconds,out int interval))throw new InvalidDataException("Invalid polling interval.");
  using var stop=new CancellationTokenSource();Console.CancelKeyPress+=(_,e)=>{e.Cancel=true;stop.Cancel();};
  try {return await AutomationWorker.Watch(registry,status,role,TimeSpan.FromSeconds(interval),stop.Token,releaseProfiles,protection);}
  catch(OperationCanceledException) when(stop.IsCancellationRequested){return 0;}
 }
 if(releaseProfiles!=null)throw new InvalidDataException("Release discovery must be attached to a background evidence worker.");
 var request=AutomationRequest.Load(args);
 var settings=request.Settings;
 using var deadline=new CancellationTokenSource(TimeSpan.FromHours(6));
 Console.CancelKeyPress+=(_,e)=>{e.Cancel=true;deadline.Cancel();};
 var state=await SubmissionWorkflow.AdvanceAsync(settings.PrivateRoot,settings.Release,new SubmissionAutomationStages(settings,request.Sha256,role,protection),deadline.Token);
 bool rehearsed=settings.Mode==SubmissionAutomationMode.Rehearsal && state.Stage==SubmissionWorkflowStage.SignReview &&
  state.Status==SubmissionWorkflowStatus.NeedsInput && state.ReasonCode=="rehearsal-ready-for-review";
 Console.WriteLine(JsonSerializer.Serialize(new{settings.Mode,state.Stage,state.Status,state.ReasonCode,state.UpdatedUtc,
  Outcome=rehearsed?"RehearsalPrepared":state.Status.ToString()},AutomationFiles.Json));
 if(rehearsed)return 0;
 return state.Status switch { SubmissionWorkflowStatus.Completed=>0,SubmissionWorkflowStatus.Waiting=>4,_=>3 };
}
catch(Exception e) when(e is not OutOfMemoryException)
{
 Console.Error.WriteLine("Automation stopped; inspect its retained stage/operation records before resuming. No operation is automatically reset. Error type: "+e.GetType().Name);
 return 2;
}
