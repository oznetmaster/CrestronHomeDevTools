// Copyright (c) 2026 Neil Colvin. MIT licensed.
using System.Text.Json;
using CrestronHomeDevTools;
using CrestronHomeDevTools.Automation;

if(args is ["--help"])
{
 Console.WriteLine("submission automation: --settings PRIVATE_JSON --settings-sha256 PIN. Runs ready stages; returns while endurance waits. Missing review/app bindings stop explicitly.");return 0;
}
try
{
 if(args is not ["--settings",var path,"--settings-sha256",var digest] || !Path.IsPathFullyQualified(path) || AutomationFiles.Hash(path)!=digest)
  throw new InvalidDataException("Select the reviewed settings and exact digest.");
 var settings=AutomationFiles.Read<SubmissionAutomationSettings>(path);
 using var deadline=new CancellationTokenSource(TimeSpan.FromHours(6));
 Console.CancelKeyPress+=(_,e)=>{e.Cancel=true;deadline.Cancel();};
 var state=await SubmissionWorkflow.AdvanceAsync(settings.PrivateRoot,settings.Release,new SubmissionAutomationStages(settings,digest),deadline.Token);
 Console.WriteLine(JsonSerializer.Serialize(new{state.Stage,state.Status,state.ReasonCode,state.UpdatedUtc},AutomationFiles.Json));
 return state.Status switch { SubmissionWorkflowStatus.Completed=>0,SubmissionWorkflowStatus.Waiting=>4,_=>3 };
}
catch(Exception e) when(e is not OutOfMemoryException)
{
 Console.Error.WriteLine("Automation stopped; inspect its retained stage/operation records before resuming. No operation is automatically reset. Error type: "+e.GetType().Name);
 return 2;
}
