// Copyright (c) 2026 Neil Colvin. MIT licensed.
using System.Text.Json;

namespace CrestronHomeDevTools.Automation;

/// <summary>Reviewed original evidence and change-impact decisions, selected explicitly in the frozen profile.
/// The directory is a source, not an executable producer. Files retain their bytes under prior-evidence/.</summary>
public sealed record SubmissionAutomationPriorEvidence(string Directory,SubmissionEvidenceFile[] Files);

internal static class AutomationPriorEvidence
{
 private const string Prefix="prior-evidence/";
 private sealed record Receipt(string IdentitySha256,SubmissionEvidenceFile[] Files);
 internal static SubmissionEvidenceDocument Prepare(string root,SubmissionEvidenceIdentity identity,
  SubmissionAutomationReviewPlan review,CancellationToken token) {
  var plan=review.PriorEvidence??throw new InvalidDataException("Reviewed prior-evidence inputs are required.");
  if(!Path.IsPathFullyQualified(plan.Directory) || plan.Files.Length is <1 or >4096 ||
   plan.Files.Any(f=>f==null || f.Sha256.Length!=64 || !f.Sha256.All(char.IsAsciiHexDigit) ||
    string.IsNullOrWhiteSpace(f.RelativePath) || f.RelativePath.Contains('\\')) ||
   plan.Files.Select(f=>f.RelativePath).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=plan.Files.Length)
   throw new InvalidDataException("Prior evidence requires a bounded, unique pinned file inventory.");
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
  string output=Path.Combine(root,"prior-evidence"),receiptPath=Path.Combine(root,"prior-evidence-receipt.json");
  if(Path.GetFullPath(plan.Directory).Equals(Path.GetFullPath(output),StringComparison.OrdinalIgnoreCase) ||
   Path.GetFullPath(plan.Directory).StartsWith(Path.GetFullPath(output)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))
   throw new InvalidDataException("Original evidence must be outside the retained destination.");
  string binding=Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new{identity,plan},AutomationFiles.Json)));
  bool retained=File.Exists(receiptPath);
  if(retained && AutomationFiles.Read<Receipt>(receiptPath).IdentitySha256!=binding)
   throw new InvalidDataException("Retained prior-evidence binding changed.");
  if(!retained)Directory.CreateDirectory(output);
  if(!Directory.Exists(output) || (File.GetAttributes(output)&FileAttributes.ReparsePoint)!=0)
   throw new InvalidDataException("Prior evidence output is missing or redirected.");
  long bytes=0;
  foreach(var file in plan.Files) {
   token.ThrowIfCancellationRequested();
   string target=Path.Combine(output,file.RelativePath.Replace('/',Path.DirectorySeparatorChar));
   if(!retained) {
    if(!SubmissionEvidence.SafeEvidencePath(plan.Directory,file.RelativePath,out var source))
     throw new InvalidDataException("Invalid prior-evidence source path.");
    // SafeEvidencePath has checked traversal and links before any destination is created.
    string parent=output;
    foreach(string part in file.RelativePath.Split('/')[..^1]) {
     parent=Path.Combine(parent,part);Directory.CreateDirectory(parent);
     if((File.GetAttributes(parent)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("Redirected prior-evidence output.");
    }
    using var input=new FileStream(source,FileMode.Open,FileAccess.Read,FileShare.Read);
    if(input.Length>512L*1024*1024 || bytes+input.Length>512L*1024*1024)throw new InvalidDataException("Prior evidence exceeds 512 MiB.");
    if(!Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(input)).Equals(file.Sha256,StringComparison.OrdinalIgnoreCase))
     throw new InvalidDataException("Pinned original evidence changed.");
    if(!File.Exists(target)) {input.Position=0;using var copy=new FileStream(target,FileMode.CreateNew,FileAccess.Write,FileShare.None);input.CopyTo(copy);copy.Flush(true);}
   }
   if(!SubmissionEvidence.SafeEvidencePath(root,Prefix+file.RelativePath,out target) ||
    !AutomationFiles.Hash(target).Equals(file.Sha256,StringComparison.OrdinalIgnoreCase))
    throw new InvalidDataException("Retained original evidence changed or is missing.");
   bytes+=new FileInfo(target).Length;
   if(bytes>512L*1024*1024)throw new InvalidDataException("Prior evidence exceeds 512 MiB.");
  }
  var pending=new Queue<string>();pending.Enqueue(output);int count=0,entries=0;
  while(pending.TryDequeue(out var directory))foreach(string entry in Directory.EnumerateFileSystemEntries(directory)) {
   var attributes=File.GetAttributes(entry);
   if(++entries>8192 || (attributes&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("Unexpected or redirected prior-evidence tree.");
   if((attributes&FileAttributes.Directory)!=0)pending.Enqueue(entry);else count++;
  }
  if(count!=plan.Files.Length)
   throw new InvalidDataException("Prior evidence contains unreviewed extra files.");
  // Existing public validation rejects failed originals, mismatched scopes and chained prior reviews.
  // It also retains unrelated original failures and the scoped change-impact review.
  var imported=SubmissionPriorEvidence.Import(identity,policy,root,DateTimeOffset.UtcNow,token);
  AutomationFiles.Write(receiptPath,new Receipt(binding,plan.Files));
  return imported;
 }
}
