// Copyright (c) 2026 Neil Colvin. MIT licensed.
using CrestronHomeDevTools.Automation;
using NUnit.Framework;
namespace CrestronHomeDevTools.Tests;
public sealed partial class AutomationInstalledAppTests
{
 private void PrepareResponseFinalization() {
  context=context with{Checkpoint=context.Checkpoint with{SchemaVersion=2,Stage=SubmissionWorkflowStage.FinalizeTests}};
  foreach(var stage in new[]{SubmissionWorkflowStage.ValidateCandidate,SubmissionWorkflowStage.WindowsTests,SubmissionWorkflowStage.ProcessorTests})
   if(!context.Checkpoint.CompletedStages.ContainsKey(stage)) {
    var result=AutomationFiles.Complete(context,stage+".json",new{Synthetic=true});context.Checkpoint.CompletedStages.Add(stage,result.Receipt!);
   }
 }
 private Task<SubmissionWorkflowStepResult> FinalizeResponse(bool recover=false)=>AutomationFinalTests.Advance(context,settings,new('a',64),recover,
  (_,_,_,_)=>throw new InvalidOperationException("No equipment/test replay"),_=>throw new InvalidOperationException("No credentials expected"),default);
 [TestCase(false)][TestCase(true)] public async Task FailedComparisonBlocksFinalizationEvenWithAGapDeclaration(bool declared) {
  await ConfigureComparison(800);PrepareResponseFinalization();
  if(declared)settings=settings with{Review=settings.Review! with{PlannedGaps=[new("system.response","Synthetic declared gap cannot turn a measured failure into success.")]}};
  int before=calls;var result=await FinalizeResponse();
  Assert.That(result.Status,Is.EqualTo(SubmissionWorkflowStatus.Failed));Assert.That(result.ReasonCode,Is.EqualTo("response-comparison-failed"));
  Assert.That(File.Exists(Path.Combine(context.RunDirectory,AutomationFinalTests.ReceiptName)),Is.False);
  Assert.That(Directory.Exists(Path.Combine(context.RunDirectory,"removal")),Is.False);Assert.That(calls,Is.EqualTo(before));
  Assert.That(CompareResponses().Outcome,Is.EqualTo(SubmissionEvidenceOutcome.Failed));
 }
 [TestCase(true)][TestCase(false)] public async Task PassedOrExplicitlyPartialComparisonRetainsItsActualOutcome(bool limits) {
  await ConfigureComparison(limits:limits);PrepareResponseFinalization();int before=calls;
  CompareResponses();
  Assert.That(AutomationResponseComparison.AcceptableForFinalization(context,settings),Is.True);
  Assert.That(CompareResponses().Outcome,Is.EqualTo(limits?SubmissionEvidenceOutcome.Passed:SubmissionEvidenceOutcome.Partial));Assert.That(calls,Is.EqualTo(before));
 }
 [Test] public async Task PreviouslyFinalizedFailedComparisonBlocksReuseAndDocumentPreparationWithoutRewritingReceipt() {
  await ConfigureComparison(800);PrepareResponseFinalization();CompareResponses();
  var files=new[]{"endurance-evidence.json","post-endurance/installed-app-tests.json","response-comparison/observations.json"}.Order(StringComparer.Ordinal).Select(p=>new SubmissionWorkflowReceipt(p,AutomationFiles.Hash(Path.Combine(context.RunDirectory,p)))).ToArray();
  var result=AutomationFiles.Complete(context,AutomationFinalTests.ReceiptName,new{context.Checkpoint.InputSha256,SettingsSha256=new string('a',64),InitialStages=context.Checkpoint.CompletedStages.ToDictionary(x=>x.Key,x=>x.Value),Files=files});
  context.Checkpoint.CompletedStages.Add(SubmissionWorkflowStage.FinalizeTests,result.Receipt!);
  var original=File.ReadAllBytes(Path.Combine(context.RunDirectory,AutomationFinalTests.ReceiptName));int before=calls;
  Assert.Throws<InvalidDataException>(()=>AutomationFinalTests.VerifyRetained(context,settings,new('a',64)));
  await Assert.ThrowsAsync<InvalidDataException>(async()=>await FinalizeResponse(true));
  Assert.That(File.ReadAllBytes(Path.Combine(context.RunDirectory,AutomationFinalTests.ReceiptName)),Is.EqualTo(original));Assert.That(calls,Is.EqualTo(before));
  Assert.That(Directory.Exists(Path.Combine(context.RunDirectory,"review")),Is.False);
 }

 [Test] public async Task QualitativeAssessmentWaitsAfterCompletedCaptureAndResumesWithoutDeviceReplay() {
  await ConfigureComparison(limits:false,qualitative:true);PrepareResponseFinalization();
  string assessment=Path.Combine(context.RunDirectory,"performance-assessment/assessment.json");
  string post=Path.Combine(context.RunDirectory,"post-endurance/installed-app-tests.json");
  string initial=Path.Combine(context.RunDirectory,"installed-app-tests.json");
  string postHash=AutomationFiles.Hash(post),initialHash=AutomationFiles.Hash(initial);int originalCalls=calls;
  File.Move(assessment,assessment+".pending");
  var pending=await FinalizeResponse();
  Assert.That(pending.Status,Is.EqualTo(SubmissionWorkflowStatus.Waiting));
  Assert.That(pending.ReasonCode,Is.EqualTo("performance-assessment-required"));
  Assert.That(File.Exists(Path.Combine(context.RunDirectory,AutomationFinalTests.ReceiptName)),Is.False);
  File.Move(assessment+".pending",assessment);
  Assert.That(AutomationResponseComparison.AwaitingAssessment(context,settings),Is.False);
  Assert.That(CompareResponses().Outcome,Is.EqualTo(SubmissionEvidenceOutcome.Passed));
  Assert.That(AutomationFiles.Hash(post),Is.EqualTo(postHash));Assert.That(AutomationFiles.Hash(initial),Is.EqualTo(initialHash));
  Assert.That(calls,Is.EqualTo(originalCalls));
  File.AppendAllText(assessment," ");
  Assert.Throws<InvalidDataException>(()=>CompareResponses());
 }
 [Test] public async Task QualitativeAssessmentCannotCiteEvidenceAddedAfterCapture() {
  await ConfigureComparison(limits:false,qualitative:true);
  string extra=Path.Combine(context.RunDirectory,"installed-app/AndroidUI/extra.json");
  File.WriteAllText(extra,"new unretained observation");
  string path=Path.Combine(context.RunDirectory,"performance-assessment/assessment.json");
  var value=AutomationFiles.Read<SubmissionPerformanceAssessment>(path);
  File.WriteAllBytes(path,System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(value with {Findings=[
   value.Findings[0] with {BeforeEvidence=[new("installed-app/AndroidUI/extra.json",AutomationFiles.Hash(extra))]}]},AutomationFiles.Json));
  Assert.Throws<InvalidDataException>(()=>CompareResponses());
 }
}
