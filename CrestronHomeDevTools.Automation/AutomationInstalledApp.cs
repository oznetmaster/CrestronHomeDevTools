// Copyright (c) 2026 Neil Colvin. MIT licensed.
using System.Net;
using System.Text.Json;
using CrestronHomeNUnit.Workflow;

namespace CrestronHomeDevTools.Automation;

internal static class AutomationInstalledApp
{
 private sealed record Intent(string OperationId,string InputSha256,string SourceDigest,string ProfileSha256);
 private sealed record Receipt(string InputSha256,SubmissionWorkflowReceipt[] Files);

 internal static void Validate(SubmissionAutomationSettings settings) {
  var plan=settings.InstalledAppTests??throw new InvalidDataException("Missing installed-app plan.");
  plan.Validate();
  if(settings.NUnit.AndroidTests!=null || plan.Host!=settings.NUnit.Host ||
   plan.CertificateSha256!=settings.NUnit.CertificateSha256 || plan.SshFingerprint!=settings.NUnit.SshFingerprint ||
   !plan.PackageSha256.Equals(settings.Release.PackageSha256,StringComparison.OrdinalIgnoreCase) ||
   plan.PackageSourceCommit!=settings.Release.SourceCommit ||
   AutomationFiles.Hash(plan.PackagePath)!=settings.Release.PackageSha256)
   throw new InvalidDataException("Installed app tests require the same frozen candidate and processor, and cannot duplicate combined Android tests.");
 }

 internal static async Task<SubmissionWorkflowStepResult> Advance(SubmissionWorkflowStepContext context,
  SubmissionAutomationSettings settings,bool recover,
  Func<InstalledDriverTestPlan,NetworkCredential,string,CancellationToken,Task<InstalledDriverTestResult>> run,
  Func<string,NetworkCredential> credentials,CancellationToken token) {
  Validate(settings);
  if(string.IsNullOrWhiteSpace(context.Checkpoint.OperationId))throw new InvalidDataException("Missing app operation identity.");
  var plan=settings.InstalledAppTests!;
  string intentPath=Path.Combine(context.RunDirectory,"installed-app-intent.json");
  string folder=Path.Combine(context.RunDirectory,"installed-app");
  string resultPath=Path.Combine(folder,"InstalledDriverTests.json");
  // A recorded invocation is never repeated, even if the caller mistakenly uses execute rather than recover.
  if(File.Exists(intentPath)) {
   var intent=AutomationFiles.Read<Intent>(intentPath);
   if(intent.OperationId!=context.Checkpoint.OperationId || intent.InputSha256!=context.Checkpoint.InputSha256 ||
    intent.SourceDigest!=await WorkflowEvidence.SourceDigestAsync(plan.SourceRoots,token) ||
    intent.ProfileSha256!=AutomationFiles.Hash(plan.AndroidTests.ProfilePath))
    throw new InvalidDataException("Installed-app attempt or fixture source changed.");
   if(!File.Exists(resultPath) || new FileInfo(resultPath).Length==0)
    return new(SubmissionWorkflowStatus.OutcomeUnknown,ReasonCode:"inspect-installed-app-operation-and-leases");
  } else {
   if(recover) return new(SubmissionWorkflowStatus.OutcomeUnknown,ReasonCode:"installed-app-intent-missing");
   var intent=new Intent(context.Checkpoint.OperationId,context.Checkpoint.InputSha256,
    await WorkflowEvidence.SourceDigestAsync(plan.SourceRoots,token),AutomationFiles.Hash(plan.AndroidTests.ProfilePath));
   AutomationFiles.Write(intentPath,intent);
   await run(plan,credentials(plan.Host),folder,token);
   if(intent.SourceDigest!=await WorkflowEvidence.SourceDigestAsync(plan.SourceRoots,token) ||
    intent.ProfileSha256!=AutomationFiles.Hash(plan.AndroidTests.ProfilePath))
    throw new InvalidDataException("Installed-app fixture source changed during execution.");
  }
  using var document=JsonDocument.Parse(File.ReadAllBytes(resultPath));
  if(document.RootElement.TryGetProperty("State",out _))
   return new(SubmissionWorkflowStatus.OutcomeUnknown,ReasonCode:"inspect-installed-app-operation-and-leases");
  var result=JsonSerializer.Deserialize<InstalledDriverTestResult>(document.RootElement.GetRawText())
   ??throw new InvalidDataException("Missing installed-app result.");
  if(!result.Passed) return new(SubmissionWorkflowStatus.Failed,ReasonCode:"installed-app-tests-or-restoration-incomplete");
  return AutomationFiles.Complete(context,"installed-app-tests.json",new Receipt(context.Checkpoint.InputSha256,Inventory(context.RunDirectory)));
 }

 private static SubmissionWorkflowReceipt[] Inventory(string root) {
  string folder=Path.Combine(root,"installed-app");
  var entries=new List<FileSystemInfo>();var pending=new Stack<DirectoryInfo>();pending.Push(new(folder));
  while(pending.Count>0) {
   var directory=pending.Pop();
   if((directory.Attributes&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("Installed-app evidence contains a link.");
   foreach(var entry in directory.EnumerateFileSystemInfos()) {
    if(entries.Count>=4096 || (entry.Attributes&FileAttributes.ReparsePoint)!=0)
     throw new InvalidDataException("Installed-app evidence exceeds its bound or contains a link.");
    entries.Add(entry);if(entry is DirectoryInfo child)pending.Push(child);
   }
  }
  return entries.OfType<FileInfo>().OrderBy(f=>f.FullName,StringComparer.Ordinal)
   .Select(f=>new SubmissionWorkflowReceipt(Path.GetRelativePath(root,f.FullName),AutomationFiles.Hash(f.FullName))).ToArray();
 }
 internal static void VerifyRetained(string root) {
  var receipt=AutomationFiles.Read<Receipt>(Path.Combine(root,"installed-app-tests.json"));
  if(!receipt.Files.SequenceEqual(Inventory(root)))throw new InvalidDataException("Completed installed-app evidence changed.");
 }
}
