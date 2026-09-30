// Copyright (c) 2026 Neil Colvin. MIT licensed.
using System.Text.Json;
using CrestronHomeDevTools;
using CrestronHomeDevTools.Automation;

if(args is ["--help"])
{
 Console.WriteLine("Explicit failed-step replacement: --inspect-app-step --settings FILE --settings-sha256 PIN --phase main|pre-endurance|post-endurance --step INDEX. Run a reviewed replacement with --recover-app-step and the same arguments plus --recovery-plan FILE --recovery-plan-sha256 PIN. Original failures remain retained; restoration must be reconciled; attempts never automatically replay.");
 Console.WriteLine("Inspect failed separate initial preparation: --inspect-app-preparation --settings FILE --settings-sha256 PIN --step INDEX. Explicit recovery: --recover-app-preparation --settings FILE --settings-sha256 PIN --step INDEX --catalogue-id EXACT_ID --state-sha256 INSPECTED_PIN --original-evidence-sha256 INSPECTED_PIN. Only same-candidate catalogue corrections before any fixture/control activity qualify; failures and passed checks remain retained. A replacement attempt is never automatically replayed.");
 Console.WriteLine("Read-only stage-binding check: --check-settings PRIVATE_JSON --settings-sha256 PIN. Exit zero means all stage bindings are present, not that tests passed or credentials/equipment were validated.");
 Console.WriteLine("Finite registry watcher: append --exit-when-finished after --poll-seconds N, before protected-worker/role options. Exits zero only when every registered run is fully retained or every rehearsal has reached unsigned review. Cannot be combined with --release-profiles. Confirmed terminal failures exit 2 after retaining their final notice; uncertain, active or approval-waiting runs remain open.");
 Console.WriteLine("Prepare from encrypted setup: --prepare-rehearsal or --prepare-submission, followed by --store PRIVATE_DIRECTORY --snapshot NAME. Submit requires a submission-purpose snapshot. Creates fresh private inputs only; no worker starts or operation is approved. Exit 3 lists missing stage bindings.");
 Console.WriteLine("submission automation: --settings PRIVATE_JSON --settings-sha256 PIN; or --registry PRIVATE_JSON --profile NAME --release-id ID --mode rehearsal|submit. Background: --watch-registry PRIVATE_JSON --status-directory PRIVATE_DIRECTORY --poll-seconds 60 [--release-profiles PRIVATE_JSON]. One-time intake: --intake-releases PRIVATE_PROFILES --registry PRIVATE_JSON. Default role is evidence. For the protected role append --protected-worker PRIVATE_JSON --protected-worker-sha256 INDEPENDENT_PIN --role protected. Rehearsal stops before signing/delivery. Submit requires exact authorizations. The ordinary background worker resumes waits without AI prompts.");return 0;
}
try
{
 if(args is ["--inspect-app-step","--settings",var inspectStepSettings,"--settings-sha256",var inspectStepPin,"--phase",var inspectPhase,"--step",var inspectedStep]) {
  if(!int.TryParse(inspectedStep,out int index))throw new InvalidDataException("Invalid step index.");
  return await AutomationAppStepRecovery.Command(AutomationRequest.ReadForCheck(inspectStepSettings,inspectStepPin),inspectPhase,index,null,null,CancellationToken.None);
 }
 if(args is ["--recover-app-step","--settings",var recoverStepSettings,"--settings-sha256",var recoverStepPin,"--phase",var recoveryPhase,"--step",var recoveredStep,"--recovery-plan",var recoveryPlan,"--recovery-plan-sha256",var recoveryPlanPin]) {
  if(!int.TryParse(recoveredStep,out int index))throw new InvalidDataException("Invalid step index.");
  using var stop=new CancellationTokenSource();Console.CancelKeyPress+=(_,e)=>{e.Cancel=true;stop.Cancel();};
  return await AutomationAppStepRecovery.Command(AutomationRequest.ReadForCheck(recoverStepSettings,recoverStepPin),recoveryPhase,index,recoveryPlan,recoveryPlanPin,stop.Token);
 }
 if(args is ["--inspect-app-preparation","--settings",var inspectSettings,"--settings-sha256",var inspectPin,"--step",var inspectStep]) {
  if(!int.TryParse(inspectStep,out int index))throw new InvalidDataException("Step must be a nonnegative integer.");
  return await AutomationPreparationRecovery.Command(AutomationRequest.ReadForCheck(inspectSettings,inspectPin),index,null,null,null,CancellationToken.None);
 }
 if(args is ["--recover-app-preparation","--settings",var repairSettings,"--settings-sha256",var repairPin,"--step",var repairStep,
  "--catalogue-id",var catalogue,"--state-sha256",var statePin,"--original-evidence-sha256",var evidencePin]) {
  if(!int.TryParse(repairStep,out int index))throw new InvalidDataException("Step must be a nonnegative integer.");
  using var stop=new CancellationTokenSource();Console.CancelKeyPress+=(_,e)=>{e.Cancel=true;stop.Cancel();};
  return await AutomationPreparationRecovery.Command(AutomationRequest.ReadForCheck(repairSettings,repairPin),index,catalogue,statePin,evidencePin,stop.Token);
 }
 if(args is ["--prepare-rehearsal" or "--prepare-submission","--store",var setupStore,"--snapshot",var snapshot]) {
  if(!OperatingSystem.IsWindows())throw new PlatformNotSupportedException("Encrypted setup requires Windows.");
  var store=DevToolsPrivateStore.Open(setupStore);
  var prepared=args[0]=="--prepare-submission"?SubmissionAutomationSetup.PrepareSubmission(store,snapshot):SubmissionAutomationSetup.PrepareRehearsal(store,snapshot);
  Console.WriteLine(JsonSerializer.Serialize(prepared,AutomationFiles.Json));
  return prepared.Configuration.AllStageBindingsPresent?0:3;
 }
 if(args is ["--check-settings",var checkPath,"--settings-sha256",var checkDigest]) {
  var check=AutomationRequest.ReadForCheck(checkPath,checkDigest);
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
  using var limit=new CancellationTokenSource(TimeSpan.FromMinutes(30));
  var result=await AutomationReleaseDiscovery.Tick(profiles,intakeRegistry,null,limit.Token);
  Console.WriteLine(JsonSerializer.Serialize(result,AutomationFiles.Json));
  return result.Any(r=>r.State=="AttentionRequired")?3:0;
 }
 string? releaseProfiles=null;
 if(args.Length>=2 && args[^2]=="--release-profiles") {releaseProfiles=args[^1];args=args[..^2];}
 bool exitWhenFinished=args.Length>0 && args[^1]=="--exit-when-finished";
 if(exitWhenFinished)args=args[..^1];
 if(args is ["--watch-registry",var registry,"--status-directory",var status,"--poll-seconds",var seconds]) {
  if(!int.TryParse(seconds,out int interval))throw new InvalidDataException("Invalid polling interval.");
  using var stop=new CancellationTokenSource();Console.CancelKeyPress+=(_,e)=>{e.Cancel=true;stop.Cancel();};
  try {return await AutomationWorker.Watch(registry,status,role,TimeSpan.FromSeconds(interval),stop.Token,releaseProfiles,protection,exitWhenFinished);}
  catch(OperationCanceledException) when(stop.IsCancellationRequested){return 0;}
 }
 if(releaseProfiles!=null)throw new InvalidDataException("Release discovery must be attached to a background evidence worker.");
 if(exitWhenFinished)throw new InvalidDataException("Exit-when-finished applies only to a registry watcher.");
 var request=AutomationRequest.Load(args);
 var settings=request.Settings;
 using var cancellation=new CancellationTokenSource();
 Console.CancelKeyPress+=(_,e)=>{e.Cancel=true;cancellation.Cancel();};
 await using var deadline=new CrestronHomeNUnit.Workflow.WorkflowActiveDeadline(TimeSpan.FromHours(6),cancellation.Token,()=>AutomationAppSteps.PreparedWaitPending(settings));
 var state=await SubmissionWorkflow.AdvanceAsync(settings.PrivateRoot,settings.Release,new SubmissionAutomationStages(settings,request.Sha256,role,protection),deadline.Token);
 deadline.ThrowIfFaulted();
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
