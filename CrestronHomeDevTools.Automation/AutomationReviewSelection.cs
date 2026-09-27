// Copyright (c) 2026 Neil Colvin. MIT licensed.
using System.IO.Compression;
using System.Text.Json;

namespace CrestronHomeDevTools.Automation;

/// <summary>Chooses an independently reviewed additive supplement without rewriting completed stage evidence.</summary>
internal static class AutomationReviewSelection
{
 internal static string Resolve(SubmissionWorkflowStepContext context,SubmissionAutomationSettings settings,CancellationToken token)
 {
  string root=context.RunDirectory,original=Path.Combine(root,"review");
  if(settings.Protected?.ReviewRevision is not {} revision)return original;
  string relative=revision.RelativeDirectory;
  if(!relative.StartsWith("review-revisions/",StringComparison.Ordinal) ||
   !SubmissionEvidence.SafeEvidencePath(root,relative,out var selected) || !Directory.Exists(selected))
   throw new InvalidDataException("A reviewed supplement must be retained beneath review-revisions in this run.");
  using var before=ReadReceipt(original,revision.OriginalReviewSha256);
  using var after=ReadReceipt(selected,revision.ReviewSha256);
  foreach(string field in new[]{"sourceCommit","candidateSha256","inventorySha256","mappingSha256"})
   if(before.RootElement.GetProperty(field).GetString()!=after.RootElement.GetProperty(field).GetString())
    throw new InvalidDataException("Supplement changed the candidate, policy, form inventory or mapping.");
  if(after.RootElement.GetProperty("sourceCommit").GetString()!=context.Checkpoint.Release.SourceCommit)
   throw new InvalidDataException("Supplement belongs to another release.");
  string oldBundle=Path.Combine(original,"evidence.zip"),newBundle=Path.Combine(selected,"evidence.zip");
  foreach(var pair in new[]{(oldBundle,before.RootElement),(newBundle,after.RootElement)})
   if(AutomationFiles.Hash(pair.Item1)!=pair.Item2.GetProperty("bundleSha256").GetString())
    throw new InvalidDataException("Review bundle changed.");
  // Domain validation checks bounded archives, original file hashes and all remaining declared gaps.
  var receipt=after.RootElement;string candidate=receipt.GetProperty("candidateSha256").GetString()!;
  SubmissionValidationReport validation;
  if(receipt.TryGetProperty("reviewMode",out var mode) && mode.GetString()=="DeclaredGaps") {
   var checkedBundle=SubmissionBundle.CheckReview(newBundle,receipt.GetProperty("bundleSha256").GetString()!,candidate,root,
    receipt.GetProperty("declarationsSha256").GetString()!,SubmissionReviewMode.DeclaredGaps,DateTimeOffset.UtcNow,token);
   if(!checkedBundle.ReadyForReview)throw new InvalidDataException("Supplement failed full review validation.");
   validation=checkedBundle.Review.Validation;
  } else {
   var checkedBundle=SubmissionBundle.Check(newBundle,receipt.GetProperty("bundleSha256").GetString()!,candidate,root,DateTimeOffset.UtcNow,token);
   if(!checkedBundle.ValidationChecksPassed)throw new InvalidDataException("Supplement failed full review validation.");
   validation=checkedBundle.Validation;
  }
  if(validation.Package?.Sha256!=context.Checkpoint.Release.PackageSha256)
   throw new InvalidDataException("Supplement contains another package.");
  using var oldArchive=ZipFile.OpenRead(oldBundle);using var newArchive=ZipFile.OpenRead(newBundle);
  using var oldObservations=ReadObservations(oldArchive);using var newObservations=ReadObservations(newArchive);
  RequireAdditive(oldObservations.RootElement,newObservations.RootElement);
  return selected;
 }
 private static JsonDocument ReadReceipt(string directory,string digest)
 {
  if(digest.Length!=64 || digest.Any(c=>!char.IsAsciiHexDigit(c)) ||
   !SubmissionEvidence.SafeEvidencePath(directory,"review-receipt.json",out var path) || AutomationFiles.Hash(path)!=digest ||
   !SubmissionEvidence.SafeEvidencePath(directory,"COMPLETE",out var complete) || File.ReadAllText(complete).Trim()!=digest)
   throw new InvalidDataException("Review supplement differs from its independent pin or is incomplete.");
  if(new FileInfo(path).Length>1024*1024)throw new InvalidDataException("Review receipt exceeds its bound.");
  var document=JsonDocument.Parse(File.ReadAllBytes(path));
  try {
   var r=document.RootElement;
   if(r.GetProperty("state").GetString() is not ("UnsignedReviewPrepared" or "UnsignedReviewWithDeclaredGapsPrepared") ||
    !r.GetProperty("signingCopy").GetBoolean() || r.GetProperty("submissionReady").GetBoolean() || r.GetProperty("deliveryAttempted").GetBoolean())
    throw new InvalidDataException("Select an unsigned signing review only.");
   foreach(var pair in new[]{("self-test.review.pdf","formSha256"),("form-report.json","formReportSha256"),
    ("inventory.json","inventorySha256"),("mapping.json","mappingSha256"),("evidence.zip","bundleSha256")})
    if(!SubmissionEvidence.SafeEvidencePath(directory,pair.Item1,out var file)||AutomationFiles.Hash(file)!=r.GetProperty(pair.Item2).GetString())
     throw new InvalidDataException("Review artifact changed.");
   return document;
  } catch {document.Dispose();throw;}
 }
 private static JsonDocument ReadObservations(ZipArchive archive)
 {
  var entries=archive.Entries.Where(e=>e.FullName=="observations.json").ToArray();
  if(entries.Length!=1 || entries[0].Length>16*1024*1024)throw new InvalidDataException("Invalid observation document in review bundle.");
  using var stream=entries[0].Open();return JsonDocument.Parse(stream);
 }
 internal static void RequireAdditive(JsonElement original,JsonElement revised)
 {
  var previous=original.GetProperty("observations").EnumerateArray().ToArray();
  var current=revised.GetProperty("observations").EnumerateArray().ToArray();
  var byId=new Dictionary<string,JsonElement>(StringComparer.Ordinal);
  foreach(var observation in current)
   if(!byId.TryAdd(observation.GetProperty("requirementId").GetString()!,observation))
    throw new InvalidDataException("Supplement contains duplicate requirement observations.");
  foreach(var observation in previous)
   if(!byId.TryGetValue(observation.GetProperty("requirementId").GetString()!,out var retained) || !JsonElement.DeepEquals(observation,retained))
    throw new InvalidDataException("Supplement must retain every original observation unchanged, including failures and endurance measurements.");
  if(current.Length<=previous.Length)throw new InvalidDataException("An additive supplement must contain new requirement observations.");
 }
}
