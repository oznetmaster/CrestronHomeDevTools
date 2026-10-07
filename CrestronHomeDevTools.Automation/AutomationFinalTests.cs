// Copyright (c) 2026 Neil Colvin. MIT licensed.
using System.Net;
using CrestronHomeNUnit.Workflow;

namespace CrestronHomeDevTools.Automation;

// This is a durable phase boundary, not a replacement for NUnit/TRX results.
// Every original producer result remains authoritative and is verified again at review.
// Review input composition, document preparation, signing, and delivery belong to phase three.
internal static class AutomationFinalTests
{
 internal const string ReceiptName="tests-finalized.json";
 private static readonly SubmissionWorkflowStage[] Initial=[SubmissionWorkflowStage.ValidateCandidate,
  SubmissionWorkflowStage.WindowsTests,SubmissionWorkflowStage.ProcessorTests,SubmissionWorkflowStage.AppTests,SubmissionWorkflowStage.Endurance];
 private sealed record Receipt(string InputSha256,string SettingsSha256,
  Dictionary<SubmissionWorkflowStage,SubmissionWorkflowReceipt> InitialStages,SubmissionWorkflowReceipt[] Files);
 internal static async Task<SubmissionWorkflowStepResult> Advance(SubmissionWorkflowStepContext context,SubmissionAutomationSettings settings,
  string settingsSha256,bool recover,Func<InstalledDriverTestPlan,NetworkCredential,string,CancellationToken,Task<InstalledDriverTestResult>> run,
  Func<string,NetworkCredential> credentials,CancellationToken token) {
  if(context.Checkpoint.Stage!=SubmissionWorkflowStage.FinalizeTests)throw new InvalidDataException("Final tests require their own test-stage operation.");
  _=InitialReceipts(context);
  // A completed boundary is reused only after verification; no equipment callback is repeated.
  if(File.Exists(Path.Combine(context.RunDirectory,ReceiptName))) {
   Verify(context,settings,settingsSha256);
   return Finish(context,settings,new(SubmissionWorkflowStatus.Completed,new(ReceiptName,AutomationFiles.Hash(Path.Combine(context.RunDirectory,ReceiptName)))));
  }
  if(settings.PostEnduranceTests!=null) {
   var post=await AutomationPostEndurance.Advance(context,settings,recover,run,credentials,token);
   if(post.Status!=SubmissionWorkflowStatus.Completed)return post;
  }
  if(settings.ResponseComparison!=null) {
   if(AutomationResponseComparison.AwaitingAssessment(context,settings))
    return new(SubmissionWorkflowStatus.Waiting,ReasonCode:"performance-assessment-required");
   AutomationResponseComparison.Prepare(context,settings,token);
   if(!AutomationResponseComparison.AcceptableForFinalization(context,settings))
    return new(SubmissionWorkflowStatus.Failed,ReasonCode:"response-comparison-failed");
  }
  if(settings.Review==null)return new(SubmissionWorkflowStatus.NeedsInput,ReasonCode:"test-policy-required");
  if(settings.Removal!=null && !AutomationTestAssessment.Prepare(context,settings,token,beforeRemoval:true))
   return new(SubmissionWorkflowStatus.Failed,ReasonCode:"test-requirements-failed");
  if(settings.Removal!=null) {
   var removal=await AutomationRemoval.Advance(context,settings,credentials,token);
   if(removal.Status!=SubmissionWorkflowStatus.Completed)return removal;
  }
  if(!AutomationTestAssessment.Prepare(context,settings,token))
   return new(SubmissionWorkflowStatus.Failed,ReasonCode:"test-requirements-failed");
  var result=AutomationFiles.Complete(context,ReceiptName,new Receipt(context.Checkpoint.InputSha256,settingsSha256,InitialReceipts(context),Files(context,settings)));
  return Finish(context,settings,result);
 }
 private static SubmissionWorkflowStepResult Finish(SubmissionWorkflowStepContext context,SubmissionAutomationSettings settings,SubmissionWorkflowStepResult result) {
  // Closing the operator inbox can be retried after its result was durably retained.
  if(settings.OperatorInbox is {} inbox)SubmissionOperatorInboxLifecycle.Close(inbox);
  return result;
 }
 internal static void VerifyRetained(SubmissionWorkflowStepContext context,SubmissionAutomationSettings settings,string settingsSha256) {
  if(!context.Checkpoint.CompletedStages.TryGetValue(SubmissionWorkflowStage.FinalizeTests,out var completed) || completed.RelativePath!=ReceiptName)
   throw new InvalidDataException("Document preparation requires completed final tests; it never runs missing tests.");
  Check(context.RunDirectory,completed);
  Verify(context,settings,settingsSha256);
 }
 private static void Verify(SubmissionWorkflowStepContext context,SubmissionAutomationSettings settings,string settingsSha256) {
  var saved=AutomationFiles.Read<Receipt>(Path.Combine(context.RunDirectory,ReceiptName));
  if(saved.InputSha256!=context.Checkpoint.InputSha256 || saved.SettingsSha256!=settingsSha256 ||
   saved.InitialStages.Count!=Initial.Length || InitialReceipts(context).Any(p=>!saved.InitialStages.TryGetValue(p.Key,out var r)||r!=p.Value) ||
   !saved.Files.SequenceEqual(Files(context,settings)))throw new InvalidDataException("Final test boundary or producer identity changed.");
 }
 private static Dictionary<SubmissionWorkflowStage,SubmissionWorkflowReceipt> InitialReceipts(SubmissionWorkflowStepContext context) {
  var result=new Dictionary<SubmissionWorkflowStage,SubmissionWorkflowReceipt>();
  foreach(var stage in Initial) {
   if(!context.Checkpoint.CompletedStages.TryGetValue(stage,out var receipt))throw new InvalidDataException("Final tests require every preceding test stage to complete.");
   Check(context.RunDirectory,receipt);result.Add(stage,receipt);
  }
  return result;
 }
 private static SubmissionWorkflowReceipt[] Files(SubmissionWorkflowStepContext context,SubmissionAutomationSettings settings) {
  var paths=new List<string>{"endurance-evidence.json"};
  if(settings.PostEnduranceTests!=null) {
   AutomationPostEndurance.VerifyRetained(context);paths.Add("post-endurance/installed-app-tests.json");
  }
  if(settings.ResponseComparison!=null) {
   if(!AutomationResponseComparison.AcceptableForFinalization(context,settings))
    throw new InvalidDataException("Final test boundary includes a failed response comparison; document preparation is blocked.");
   paths.Add(AutomationResponseComparison.VerifyRetained(context).RelativePath);
  }
  if(settings.Removal!=null)paths.Add(AutomationRemoval.VerifyRetained(context).RelativePath);
  AutomationTestAssessment.VerifyRetained(context,settings);paths.Add(AutomationTestAssessment.ReceiptName);
  return paths.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).Select(p=>{
   if(!SubmissionEvidence.SafeEvidencePath(context.RunDirectory,p,out var full))throw new InvalidDataException("Unsafe final-test evidence path.");
   return new SubmissionWorkflowReceipt(p,AutomationFiles.Hash(full));
  }).ToArray();
 }
 private static void Check(string root,SubmissionWorkflowReceipt receipt) {
  if(!SubmissionEvidence.SafeEvidencePath(root,receipt.RelativePath.Replace('\\','/'),out var path)||AutomationFiles.Hash(path)!=receipt.Sha256)
   throw new InvalidDataException("Completed test receipt changed.");
 }
}
