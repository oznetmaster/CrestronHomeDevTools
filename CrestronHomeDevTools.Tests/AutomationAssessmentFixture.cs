// Copyright (c) 2026 Neil Colvin. MIT licensed.
using CrestronHomeDevTools.Automation;
namespace CrestronHomeDevTools.Tests;
internal static class AutomationAssessmentFixture
{
 internal static SubmissionAutomationSettings Configure(string root,SubmissionAutomationSettings settings,SubmissionWorkflowStepContext context) {
  string P(string name)=>Path.Combine(root,name);string Hash(string name)=>AutomationFiles.Hash(P(name));
  AutomationReview.WriteDocument(P("policy.json"),new SubmissionEvidencePolicy(1,[new("functional",TimeSpan.Zero),new("endurance",TimeSpan.Zero)]));
  File.WriteAllText(P("trace.txt"),"Synthetic policy evidence; no hardware actions.");
  var policy=new SubmissionAutomationInput(P("policy.json"),Hash("policy.json"));
  var unused=new SubmissionAutomationInput(P("unused-template"),new('a',64));
  settings=settings with{Review=new(policy,unused,unused,unused,new(P("old-console"),[new("old.exe",new('a',64))]),"Fixture","Author",["observations.json"])};
  var identity=new SubmissionEvidenceIdentity(settings.Release.PackageSha256,settings.Release.SourceCommit,policy.Sha256,unused.Sha256);
  var now=DateTimeOffset.UtcNow.AddMinutes(-1);
  AutomationReview.WriteDocument(P("observations.json"),new SubmissionEvidenceDocument(1,[new("functional",identity,SubmissionEvidenceOutcome.Passed,now,now,[new("trace.txt",Hash("trace.txt"))])]));
  AutomationFiles.Write(P("windows-tests.json"),new{context.Checkpoint.InputSha256,Files=new[]{new SubmissionWorkflowReceipt("observations.json",Hash("observations.json")),new("trace.txt",Hash("trace.txt"))}});
  context.Checkpoint.CompletedStages[SubmissionWorkflowStage.WindowsTests]=new("windows-tests.json",Hash("windows-tests.json"));
  Directory.CreateDirectory(P("endurance/observations"));File.WriteAllText(P("endurance/observations/sample.txt"),"Synthetic sample.");
  File.Delete(P("endurance-evidence.json"));
  AutomationFiles.Write(P("endurance-evidence.json"),new{EvidenceDirectory="endurance/observations",Observation=new SubmissionObservation("endurance",identity,SubmissionEvidenceOutcome.Passed,now,now,[new("sample.txt",Hash("endurance/observations/sample.txt"))])});
  return settings;
 }
}
