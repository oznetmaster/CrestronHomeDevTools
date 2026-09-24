// Copyright (c) 2026 Neil Colvin. MIT licensed.
using System.Text.Json;
using CrestronHomeDevTools;
using CrestronHomeDevTools.Automation;

if(args is ["--help"])
{
 Console.WriteLine("submission automation: --settings PRIVATE_JSON --settings-sha256 PIN; or --registry PRIVATE_JSON --profile NAME --release-id ID --mode rehearsal|submit. Rehearsal stops before signing/delivery. Submit still requires exact authorizations. Returns while endurance waits; missing bindings stop explicitly.");return 0;
}
try
{
 var request=AutomationRequest.Load(args);
 var settings=request.Settings;
 using var deadline=new CancellationTokenSource(TimeSpan.FromHours(6));
 Console.CancelKeyPress+=(_,e)=>{e.Cancel=true;deadline.Cancel();};
 var state=await SubmissionWorkflow.AdvanceAsync(settings.PrivateRoot,settings.Release,new SubmissionAutomationStages(settings,request.Sha256),deadline.Token);
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
