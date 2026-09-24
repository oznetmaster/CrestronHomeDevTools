// Copyright (c) 2026 Neil Colvin. MIT licensed.
namespace CrestronHomeDevTools.Automation;

/// <summary>Candidate-specific reviewed non-applicability decisions and their source evidence.
/// Observation paths and supporting file references use the retained applicability/ namespace.</summary>
public sealed record SubmissionAutomationApplicability(string Directory,SubmissionEvidenceFile[] Files,
 string Observations);

internal static class AutomationApplicability
{
 internal static SubmissionEvidenceFile Prepare(string root,SubmissionEvidenceIdentity identity,
  SubmissionAutomationReviewPlan review,CancellationToken token) {
  var plan=review.Applicability??throw new InvalidDataException("Applicability inputs are required.");
  if(!plan.Files.Any(f=>f.RelativePath==plan.Observations))
   throw new InvalidDataException("Applicability observations must be included in the pinned inventory.");
  var complete=AutomationReviewedFiles.Retain(root,"applicability/",new{identity,plan.Observations},plan.Directory,plan.Files,token);
  string relative="applicability/"+plan.Observations;
  if(!SubmissionEvidence.SafeEvidencePath(root,relative,out var path) || new FileInfo(path).Length>16*1024*1024)
   throw new InvalidDataException("Invalid applicability observation document.");
  var document=AutomationFiles.Read<SubmissionEvidenceDocument>(path);
  if(document.SchemaVersion!=1 || document.Observations.Count is <1 or >512 ||
   document.Observations.Any(o=>o.Outcome!=SubmissionEvidenceOutcome.NotApplicable || o.Files.Count==0 ||
    o.Files.Any(f=>!f.RelativePath.StartsWith("applicability/",StringComparison.Ordinal))))
   throw new InvalidDataException("Reviewed applicability accepts only documented non-applicability, never test passes.");
  if(AutomationFiles.Hash(review.Policy.Path)!=identity.PolicySha256)
   throw new InvalidDataException("Applicability policy changed.");
  var policy=AutomationFiles.Read<SubmissionEvidencePolicy>(review.Policy.Path);
  var requirements=policy.Requirements.Where(r=>document.Observations.Any(o=>o.RequirementId==r.Id)).ToArray();
  if(requirements.Length==0 || !SubmissionEvidence.Evaluate(identity,requirements,document.Observations,root,DateTimeOffset.UtcNow,token).EvidenceChecksPassed)
   throw new InvalidDataException("Applicability decisions do not match the candidate, policy or retained source evidence.");
  complete();return new(relative,AutomationFiles.Hash(path));
 }
}
