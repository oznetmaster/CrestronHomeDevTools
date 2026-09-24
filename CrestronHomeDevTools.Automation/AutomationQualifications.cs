// Copyright (c) 2026 Neil Colvin. MIT licensed.
namespace CrestronHomeDevTools.Automation;

/// <summary>Pinned records of incomplete/failed observations and reviewed limitations.
/// This handoff cannot import a pass or itself accept a checklist interpretation.</summary>
public sealed record SubmissionAutomationQualifications(string Directory,SubmissionEvidenceFile[] Files,string Observations);

internal static class AutomationQualifications
{
 internal static SubmissionEvidenceFile Prepare(string root,SubmissionEvidenceIdentity identity,
  SubmissionAutomationReviewPlan review,CancellationToken token) {
  var plan=review.Qualifications??throw new InvalidDataException("Qualification inputs are required.");
  if(!plan.Files.Any(f=>f.RelativePath==plan.Observations))
   throw new InvalidDataException("Qualification observations must be included in the pinned inventory.");
  var complete=AutomationReviewedFiles.Retain(root,"qualifications/",new{identity,plan.Observations},plan.Directory,plan.Files,token);
  string relative="qualifications/"+plan.Observations;
  if(!SubmissionEvidence.SafeEvidencePath(root,relative,out var path))throw new InvalidDataException("Invalid qualification path.");
  var document=AutomationFiles.Read<SubmissionEvidenceDocument>(path);
  if(document.SchemaVersion!=1 || document.Observations.Count is <1 or >512 ||
   document.Observations.Any(o=>o.Outcome is not (SubmissionEvidenceOutcome.Partial or SubmissionEvidenceOutcome.Inconclusive or
    SubmissionEvidenceOutcome.Failed or SubmissionEvidenceOutcome.NotTested) || string.IsNullOrWhiteSpace(o.Rationale) || o.Files.Count==0 ||
    o.Files.Any(f=>!f.RelativePath.StartsWith("qualifications/",StringComparison.Ordinal))))
   throw new InvalidDataException("Qualifications require retained nonpassing observations and reasons; passes and N/A are not accepted.");
  if(AutomationFiles.Hash(review.Policy.Path)!=identity.PolicySha256)throw new InvalidDataException("Qualification policy changed.");
  var policy=AutomationFiles.Read<SubmissionEvidencePolicy>(review.Policy.Path);
  var requirements=policy.Requirements.Where(r=>document.Observations.Any(o=>o.RequirementId==r.Id)).ToArray();
  if(requirements.Length==0 || !SubmissionReviewAssessment.Assess(identity,requirements,document.Observations,root,
   SubmissionReviewMode.DeclaredGaps,document.Observations.Select(o=>new SubmissionGapDeclaration(o.RequirementId,o.Rationale)).ToArray(),
   DateTimeOffset.UtcNow,token).ReadyForReview)
   throw new InvalidDataException("Qualification records have invalid identity or source evidence.");
  complete();return new(relative,AutomationFiles.Hash(path));
 }
}
