// Copyright (c) 2026 Neil Colvin. MIT licensed.
namespace CrestronHomeDevTools.Automation;

// The test suite, not document generation, decides whether its entire requirement set is satisfied.
internal static class AutomationTestAssessment
{
 internal const string ReceiptName="test-assessment.json";
 private sealed record Receipt(string InputSha256,string PlanSha256,bool Eligible,SubmissionWorkflowReceipt[] Files);
 private static string PlanPin(SubmissionAutomationSettings settings)=>Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(
  System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new{settings.Mode,settings.Release,Policy=settings.Review?.Policy,TemplateSha256=settings.Review?.Template.Sha256,settings.Review?.ObservationSources,settings.Review?.PlannedGaps,settings.Review?.Declarations,settings.Review?.Applicability,settings.Review?.SourceApplicability,settings.Review?.Qualifications,settings.Review?.PriorEvidence,settings.Endurance,settings.Removal,settings.ResponseComparison},AutomationFiles.Json)));
 internal static bool Prepare(SubmissionWorkflowStepContext c,SubmissionAutomationSettings settings,CancellationToken token,bool beforeRemoval=false) {
  var plan=settings.Review??throw new InvalidDataException("Full test policy is required before finalization.");
  AutomationReview.ValidatePlannedGaps(plan);
  string folder=beforeRemoval?"test-assessment-before-removal":"test-assessment";
  string receiptPath=Path.Combine(c.RunDirectory,beforeRemoval?"test-assessment-before-removal.json":ReceiptName);
  if(File.Exists(receiptPath))return Verify(c,settings,beforeRemoval).Eligible;
  var report=AutomationTestEvidence.Collect(c,settings,plan,folder,token,!beforeRemoval);
  bool eligible=Eligible(settings,report,beforeRemoval);
  var files=Directory.GetFiles(Path.Combine(c.RunDirectory,folder)).Select(p=>new SubmissionWorkflowReceipt(
   Path.GetRelativePath(c.RunDirectory,p).Replace('\\','/'),AutomationFiles.Hash(p)))
   .Concat(report.Observations.Observations.SelectMany(o=>o.Files).Select(f=>new SubmissionWorkflowReceipt(f.RelativePath,f.Sha256)))
   .Distinct().OrderBy(f=>f.RelativePath,StringComparer.Ordinal).ToArray();
  AutomationFiles.Write(receiptPath,new Receipt(c.Checkpoint.InputSha256,PlanPin(settings),eligible,files));
  return Verify(c,settings,beforeRemoval).Eligible;
 }
 internal static bool Eligible(SubmissionAutomationSettings settings,SubmissionEvidenceCompositionReport report,bool beforeRemoval=false) {
  var gaps=ReadQualifications(settings,report);
  // A declaration cannot turn failed, unexecuted or malformed testing into a successful suite.
  if(report.Observations.Observations.Any(o=>o.Outcome is SubmissionEvidenceOutcome.Failed or SubmissionEvidenceOutcome.NotTested or SubmissionEvidenceOutcome.Inconclusive))return false;
  foreach(var issue in report.Evidence.Issues) {
   if(beforeRemoval && settings.Removal!=null && issue.Code=="missing-observation" && issue.RequirementId==AutomationRemoval.Validate(settings).Id)continue;
   if(!IsQualifiedIssue(settings,report,issue,gaps))return false;
  }
  return true;
 }
 internal static SubmissionGapDeclaration[] ReadQualifications(SubmissionAutomationSettings settings,SubmissionEvidenceCompositionReport report) {
  var plan=settings.Review??throw new InvalidDataException("Test policy missing.");
  var gaps=plan.PlannedGaps??Array.Empty<SubmissionGapDeclaration>();
  if(plan.Declarations is {} declaration) {
   if(AutomationFiles.Hash(declaration.Path)!=declaration.Sha256)throw new InvalidDataException("Frozen test qualification changed.");
   var document=AutomationFiles.Read<SubmissionGapDeclarations>(declaration.Path);
   var identity=new SubmissionEvidenceIdentity(settings.Release.PackageSha256,settings.Release.SourceCommit,plan.Policy.Sha256,plan.Template.Sha256);
   if(document.SchemaVersion!=1 || document.Identity!=identity || document.Mode!=SubmissionReviewMode.DeclaredGaps)
    throw new InvalidDataException("Test qualifications belong to another candidate or policy.");
   gaps=document.Declarations.ToArray();
  }
  if(gaps.Any(g=>g.InterpretationReview!=null || string.IsNullOrWhiteSpace(g.Reason)) || gaps.Select(g=>g.RequirementId).Distinct().Count()!=gaps.Length)
   throw new InvalidDataException("Test qualifications must be distinct scoped limitations, not substituted interpretations.");
  if(gaps.Any(g=>!report.Evidence.Issues.Any(i=>i.RequirementId==g.RequirementId)))
   throw new InvalidDataException("A test qualification no longer matches the actual evidence.");
  return gaps;
 }
 internal static bool IsQualifiedIssue(SubmissionAutomationSettings settings,SubmissionEvidenceCompositionReport report,SubmissionEvidenceIssue issue,SubmissionGapDeclaration[] gaps) {
   if(settings.Mode!=SubmissionAutomationMode.Rehearsal)return false;
   bool declared=gaps.Any(g=>g.RequirementId==issue.RequirementId);
   if(!declared)return false;
   if(issue.Code=="insufficient-duration" && settings.Endurance is {} endurance && issue.RequirementId==endurance.Plan.Requirement.Id &&
      report.Observations.Observations.SingleOrDefault(o=>o.RequirementId==issue.RequirementId) is {Outcome:SubmissionEvidenceOutcome.Passed} observed &&
      endurance.Plan.Requirement.MinimumDuration>TimeSpan.Zero && observed.FinishedUtc-observed.StartedUtc>=endurance.Plan.Requirement.MinimumDuration)return true;
   if(settings.ResponseComparison is {Limits:null} response && issue.RequirementId==response.RequirementId &&
      issue.Code is "not-passed" or "execution-outcome" &&
      report.Observations.Observations.SingleOrDefault(o=>o.RequirementId==issue.RequirementId)?.Outcome==SubmissionEvidenceOutcome.Partial)return true;
   return false;
 }
 internal static SubmissionEvidenceDocument ReadForReview(SubmissionWorkflowStepContext c,SubmissionAutomationSettings settings) {
  if(!Verify(c,settings,false).Eligible)throw new InvalidDataException("Phase-two requirement assessment failed; document preparation is blocked.");
  return AutomationFiles.Read<SubmissionEvidenceDocument>(Path.Combine(c.RunDirectory,"test-assessment","observations.json"));
 }
 internal static void VerifyRetained(SubmissionWorkflowStepContext c,SubmissionAutomationSettings settings)=>_=ReadForReview(c,settings);
 private static Receipt Verify(SubmissionWorkflowStepContext c,SubmissionAutomationSettings settings,bool beforeRemoval) {
  string name=beforeRemoval?"test-assessment-before-removal.json":ReceiptName;
  var receipt=AutomationFiles.Read<Receipt>(Path.Combine(c.RunDirectory,name));
  if(receipt.InputSha256!=c.Checkpoint.InputSha256 || receipt.PlanSha256!=PlanPin(settings))throw new InvalidDataException("Phase-two policy or run identity changed.");
  foreach(var file in receipt.Files)
   if(!SubmissionEvidence.SafeEvidencePath(c.RunDirectory,file.RelativePath,out var path)||!string.Equals(AutomationFiles.Hash(path),file.Sha256,StringComparison.OrdinalIgnoreCase))
    throw new InvalidDataException("Phase-two assessment evidence changed.");
  string folder=beforeRemoval?"test-assessment-before-removal":"test-assessment";
  var report=AutomationFiles.Read<SubmissionEvidenceCompositionReport>(Path.Combine(c.RunDirectory,folder,"composition-report.json"));
  if(receipt.Eligible!=Eligible(settings,report,beforeRemoval))throw new InvalidDataException("Phase-two assessment decision is inconsistent.");
  return receipt;
 }
}
