// Copyright (c) 2026 Neil Colvin. MIT licensed.
using System.Diagnostics;
using CrestronHomeDevTools.Automation;
using CrestronHomeNUnit.Workflow;
using NUnit.Framework;
namespace CrestronHomeDevTools.Tests;

[TestFixture]
public sealed class PhaseThreeCommandTests
{
 [TestCase(false,false)][TestCase(true,false)][TestCase(false,true)][TestCase(true,true)]
 public async Task CommandRefusesIncompleteTestsWithoutWritingRunState(bool registry,bool finalStage) {
  string root=Path.Combine(TestContext.CurrentContext.WorkDirectory,"phase-three-command-"+Guid.NewGuid().ToString("N"));
  Directory.CreateDirectory(root);
  try {
   var release=new SubmissionWorkflowRelease("fixture/driver",3,"v1",new('a',40),new('b',64),new('c',64),new('d',64));
   var settings=new SubmissionAutomationSettings(1,root,release,root,new(Guid.NewGuid().ToString(),"1.0.0.0",PortalSubmissionKind.NewDriver,"Fixture","fixture@example.org"),"unused",
    new WorkflowPlan{Host="unused",CertificateSha256=new('e',64),SshFingerprint="unused",SourceRoots=[root],LocalTests=[],TestPackage=new("unused.csproj","unused.pkg","fixture",1),ProcessorSuites=[]});
   string settingsPath=Path.Combine(root,"settings.json"),registryPath=Path.Combine(root,"registry.json");
   AutomationFiles.Write(settingsPath,settings);string digest=AutomationFiles.Hash(settingsPath);
   AutomationFiles.Write(registryPath,new SubmissionAutomationRegistry(1,[new("fixture",3,SubmissionAutomationMode.Rehearsal,settingsPath,digest)]));
   SubmissionWorkflow.Open(root,release);
   if(finalStage)foreach(var stage in new[]{SubmissionWorkflowStage.ValidateCandidate,SubmissionWorkflowStage.WindowsTests,
     SubmissionWorkflowStage.ProcessorTests,SubmissionWorkflowStage.AppTests,SubmissionWorkflowStage.Endurance})
    await SubmissionWorkflow.AdvanceStageAsync(root,release,new SyntheticSteps(),stage);
   string run=Path.Combine(root,SubmissionWorkflow.RunKey(release)),state=Path.Combine(run,"state.json"),before=AutomationFiles.Hash(state);
   var start=new ProcessStartInfo("dotnet") {UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,WorkingDirectory=root};
   foreach(var arg in new[]{"exec","--runtimeconfig",Path.ChangeExtension(typeof(PhaseThreeCommandTests).Assembly.Location,"runtimeconfig.json"),
     typeof(SubmissionAutomationStages).Assembly.Location,"--phase-three"})start.ArgumentList.Add(arg);
   foreach(var arg in registry?new[]{"--registry",registryPath,"--profile","fixture","--release-id","3","--mode","rehearsal"}
     :new[]{"--settings",settingsPath,"--settings-sha256",digest})start.ArgumentList.Add(arg);
   using var process=Process.Start(start)!;
   var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();
   using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(30));
   try {await process.WaitForExitAsync(deadline.Token);}catch(OperationCanceledException){process.Kill(entireProcessTree:true);throw;}
   string output=await stdout,error=await stderr;
   Assert.That(process.ExitCode,Is.EqualTo(2),output+error);
   Assert.That(error,Does.Contain("InvalidOperationException"),output+error);
   Assert.That(AutomationFiles.Hash(state),Is.EqualTo(before));
   Assert.That(File.Exists(Path.Combine(run,"automation-binding.json")),Is.False);
  } finally {Directory.Delete(root,true);}
 }
 private sealed class SyntheticSteps:ISubmissionWorkflowSteps {
  public Task<SubmissionWorkflowStepResult> ExecuteAsync(SubmissionWorkflowStepContext c,CancellationToken t) {
   string name=c.Checkpoint.Stage+".json",path=Path.Combine(c.RunDirectory,name);File.WriteAllText(path,"{}");
   return Task.FromResult(new SubmissionWorkflowStepResult(SubmissionWorkflowStatus.Completed,new(name,AutomationFiles.Hash(path))));
  }
  public Task<SubmissionWorkflowStepResult> RecoverAsync(SubmissionWorkflowStepContext c,CancellationToken t)=>throw new InvalidOperationException("No recovery expected");
 }
}
