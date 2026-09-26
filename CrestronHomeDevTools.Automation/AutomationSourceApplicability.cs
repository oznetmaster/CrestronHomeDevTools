// Copyright (c) 2026 Neil Colvin. MIT licensed.
namespace CrestronHomeDevTools.Automation;

/// <summary>A reviewed source-based N/A decision, never an executed test result.</summary>
public sealed record SubmissionSourceApplicabilityDecision(string RequirementId,string Rationale);
/// <summary>Exact source bytes condition reuse on a new release. Runtime-dependent decisions belong in test evidence.</summary>
public sealed record SubmissionSourceApplicabilityPlan(int SchemaVersion,string ReviewedBy,DateTimeOffset ReviewedUtc,
 SubmissionEvidenceFile[] SourceFiles,SubmissionSourceApplicabilityDecision[] Decisions);

internal static class AutomationSourceApplicability
{
 private sealed record Receipt(SubmissionEvidenceIdentity Identity,string PlanSha256,string ObservationSha256);
 internal static SubmissionEvidenceFile Prepare(string root,SubmissionAutomationSettings settings,CancellationToken token) {
  var review=settings.Review??throw new InvalidDataException("Review settings required.");
  var input=review.SourceApplicability??throw new InvalidDataException("Source applicability plan required.");
  if(!Path.IsPathFullyQualified(input.Path) || AutomationFiles.Hash(input.Path)!=input.Sha256 ||
   AutomationFiles.Hash(review.Policy.Path)!=review.Policy.Sha256)
   throw new InvalidDataException("Reviewed source applicability or policy changed.");
  var plan=AutomationFiles.Read<SubmissionSourceApplicabilityPlan>(input.Path);
  var now=DateTimeOffset.UtcNow;
  if(plan.SchemaVersion!=1 || string.IsNullOrWhiteSpace(plan.ReviewedBy) || plan.ReviewedUtc==default || plan.ReviewedUtc>now ||
   plan.SourceFiles is not {Length: >0 and <=512} || plan.Decisions is not {Length: >0 and <=512} ||
   plan.Decisions.Any(d=>d==null || string.IsNullOrWhiteSpace(d.RequirementId) || string.IsNullOrWhiteSpace(d.Rationale)) ||
   plan.Decisions.Select(d=>d.RequirementId).Distinct(StringComparer.Ordinal).Count()!=plan.Decisions.Length)
   throw new InvalidDataException("Source applicability requires a named, dated review, pinned source files and distinct reasons.");
  var policy=AutomationFiles.Read<SubmissionEvidencePolicy>(review.Policy.Path);
  if(policy.SchemaVersion!=1)throw new InvalidDataException("Unsupported source applicability policy.");
  foreach(var decision in plan.Decisions) {
   var requirement=policy.Requirements.SingleOrDefault(r=>r.Id==decision.RequirementId);
   if(requirement is not {AllowNotApplicable:true,Execution:{Method:"absence",RequiredOutcome:SubmissionEvidenceOutcome.NotApplicable,
    ResponseLimitSeconds:null,Restore:false,MaximumSampleGapSeconds:null}})
    throw new InvalidDataException("A source decision requires an explicit N/A absence policy without runtime measurements.");
  }
  var identity=new SubmissionEvidenceIdentity(settings.Release.PackageSha256,settings.Release.SourceCommit,review.Policy.Sha256,review.Template.Sha256);
  // Candidate() verifies the release checkout before this call. During review, retained
  // copies are rechecked; this never rewrites an older observation to claim a fresh test.
  var finish=AutomationReviewedFiles.Retain(root,"source-applicability/",new{identity,input.Sha256},
   settings.SourceRepository,plan.SourceFiles,token);
  AutomationFiles.Write(Path.Combine(root,"source-applicability-plan.json"),plan);
  const string relative="source-applicability-observations.json";
  string path=Path.Combine(root,relative),receiptPath=Path.Combine(root,"source-applicability-decisions-receipt.json");
  if(File.Exists(receiptPath)) {
   var receipt=AutomationFiles.Read<Receipt>(receiptPath);
   if(receipt.Identity!=identity || receipt.PlanSha256!=input.Sha256 || !File.Exists(path) || AutomationFiles.Hash(path)!=receipt.ObservationSha256)
    throw new InvalidDataException("Retained source applicability decisions changed or are missing.");
  } else {
   // An incomplete write is retained for inspection; do not issue replacement decisions.
   if(File.Exists(path))throw new InvalidDataException("Inspect incomplete source applicability preparation.");
   var files=plan.SourceFiles.Select(f=>f with{RelativePath="source-applicability/"+f.RelativePath})
    .Append(new("source-applicability-plan.json",AutomationFiles.Hash(Path.Combine(root,"source-applicability-plan.json")))).ToArray();
   var observations=plan.Decisions.Select(d=> {
    var requirement=policy.Requirements.Single(r=>r.Id==d.RequirementId);
    return new SubmissionObservation(d.RequirementId,identity,SubmissionEvidenceOutcome.NotApplicable,now,now,files,
     $"Source applicability reviewed by {plan.ReviewedBy} at {plan.ReviewedUtc:O}. Supporting source bytes match this release. {d.Rationale} No runtime execution is claimed.",
     new SubmissionExecutionObservation(requirement.Execution!.Target,"absence"));
   }).ToArray();
   var requirements=policy.Requirements.Where(r=>plan.Decisions.Any(d=>d.RequirementId==r.Id)).ToArray();
   if(!SubmissionEvidence.Evaluate(identity,requirements,observations,root,now,token).EvidenceChecksPassed)
    throw new InvalidDataException("Source applicability evidence did not validate.");
   AutomationReview.WriteDocument(path,new SubmissionEvidenceDocument(1,observations));
   finish();
   AutomationFiles.Write(receiptPath,new Receipt(identity,input.Sha256,AutomationFiles.Hash(path)));
  }
  return new(relative,AutomationFiles.Hash(path));
 }
}
