// Copyright (c) 2026 Neil Colvin. MIT licensed.
using System.Text.Json;

namespace CrestronHomeDevTools.Automation;

/// <summary>Reviewed original evidence and change-impact decisions, selected explicitly in the frozen profile.
/// The directory is a source, not an executable producer. Files retain their bytes under prior-evidence/.</summary>
public sealed record SubmissionAutomationPriorEvidence(string Directory,SubmissionEvidenceFile[] Files);

internal static class AutomationPriorEvidence
{
 private const string Prefix="prior-evidence/";
 internal static SubmissionEvidenceDocument Prepare(string root,SubmissionEvidenceIdentity identity,
  SubmissionAutomationReviewPlan review,CancellationToken token) {
  var plan=review.PriorEvidence??throw new InvalidDataException("Reviewed prior-evidence inputs are required.");
  using var policyInput=File.OpenRead(review.Policy.Path);
  if(policyInput.Length>16*1024*1024)throw new InvalidDataException("Reviewed policy exceeds its size limit.");
  byte[] policyBytes=new byte[checked((int)policyInput.Length)];policyInput.ReadExactly(policyBytes);
  if(Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(policyBytes))!=identity.PolicySha256)
   throw new InvalidDataException("Reviewed prior-evidence policy changed.");
  var policy=JsonSerializer.Deserialize<SubmissionEvidencePolicy>(policyBytes,AutomationFiles.Json)
   ??throw new InvalidDataException("Empty reviewed policy.");
  var scopes=policy.Requirements.Where(r=>r.PriorEvidence!=null).ToArray();
  if(scopes.Length==0 || scopes.Any(r=>new[]{r.PriorEvidence!.Policy.RelativePath,r.PriorEvidence.Observations.RelativePath,
   r.PriorEvidence.ChangeReview.RelativePath,r.PriorEvidence.EvidenceDirectory+"/"}.Any(p=>!p.StartsWith(Prefix,StringComparison.Ordinal))))
   throw new InvalidDataException("The reviewed policy must bind original evidence beneath prior-evidence/.");
  var complete=AutomationReviewedFiles.Retain(root,Prefix,identity,plan.Directory,plan.Files,token);
  // Existing public validation rejects failed originals, mismatched scopes and chained prior reviews.
  // It also retains unrelated original failures and the scoped change-impact review.
  var imported=SubmissionPriorEvidence.Import(identity,policy,root,DateTimeOffset.UtcNow,token);
  complete();
  return imported;
 }
}
