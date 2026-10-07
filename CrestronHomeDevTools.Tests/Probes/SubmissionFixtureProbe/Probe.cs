// Copyright (c) 2026 Neil Colvin. MIT licensed.
using System.Security.Cryptography;
using CrestronHomeDevTools;
using CrestronHomeDevTools.Automation;
using CrestronHomeDevTools.SubmissionTests;
using NUnit.Framework;

namespace SubmissionFixtureProbe;
// Software-only probe: real NUnit selection and real durable controller; no hardware adapter or credentials.
[TestFixture]
public sealed class DriverSubmission:SubmissionFixture
{
 protected override string SettingsEnvironment=>"SUBMISSION_FIXTURE_PROBE";
 protected override ISubmissionTestSession CreateSession()=>new ProbeSession(Environment.GetEnvironmentVariable(SettingsEnvironment)??throw new InvalidOperationException("Probe root required"));
 private sealed class ProbeSession(string root):ISubmissionTestSession,ISubmissionWorkflowSteps {
  private readonly SubmissionWorkflowRelease release=new("fixture/driver",1,"v1",new('a',40),new('b',64),new('c',64),new('d',64));
  public string RunDirectory=>Path.Combine(root,SubmissionWorkflow.RunKey(release));
  public async Task<SubmissionWorkflowCheckpoint> RunStageAsync(SubmissionWorkflowStage stage,CancellationToken token) {
   SubmissionWorkflow.Open(root,release);var state=SubmissionWorkflow.Read(root,release);
   if(!state.CompletedStages.ContainsKey(stage)&&state.Stage!=stage)throw new SubmissionTestPrerequisiteException(state.Stage);
   return await SubmissionWorkflow.AdvanceStageAsync(root,release,this,stage,token);
  }
  public Task<SubmissionWorkflowStepResult> ExecuteAsync(SubmissionWorkflowStepContext context,CancellationToken token) {
   File.AppendAllText(Path.Combine(root,"executed.txt"),context.Checkpoint.Stage+Environment.NewLine);
   string file=context.Checkpoint.Stage+".json",path=Path.Combine(RunDirectory,file);File.WriteAllText(path,"synthetic selection probe");
   return Task.FromResult(new SubmissionWorkflowStepResult(SubmissionWorkflowStatus.Completed,new(file,Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path))))));
  }
  public Task<SubmissionWorkflowStepResult> RecoverAsync(SubmissionWorkflowStepContext context,CancellationToken token)=>throw new InvalidOperationException("Probe does not start background work.");
 }
}
