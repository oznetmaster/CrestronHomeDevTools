// Copyright (c) 2026 Neil Colvin. MIT licensed.
using System.Text.Json;

namespace CrestronHomeDevTools.Automation;

// Shared by endurance collection and its subsequent functional checks. Never
// reconstruct a catalogue identifier from a version string or a display name.
internal static class AutomationDeploymentEvidence
{
 internal sealed record Deployment(DriverDeploymentResult Imported,DriverInstanceReady Installed,SubmissionWorkflowReceipt Receipt);
 internal static Deployment Read(SubmissionWorkflowStepContext c,SubmissionAutomationSettings settings,bool beforeAppTests=false) {
  if(settings.NUnit.ActualDriver==null || settings.NUnit.ReleaseCandidate==null)
   throw new InvalidDataException("Actual candidate deployment is required.");
  if(settings.Release!=c.Checkpoint.Release ||
   !c.Checkpoint.CompletedStages.ContainsKey(SubmissionWorkflowStage.ProcessorTests) ||
   (beforeAppTests ? c.Checkpoint.Stage!=SubmissionWorkflowStage.AppTests : !c.Checkpoint.CompletedStages.ContainsKey(SubmissionWorkflowStage.AppTests)) ||
   !c.Checkpoint.CompletedStages.TryGetValue(SubmissionWorkflowStage.WindowsTests,out var receipt) || receipt.RelativePath!="windows-tests.json")
   throw new InvalidDataException("Deployment evidence requires the completed candidate test stages.");

  if(!SubmissionEvidence.SafeEvidencePath(c.RunDirectory,receipt.RelativePath,out var receiptPath) || AutomationFiles.Hash(receiptPath)!=receipt.Sha256)
   throw new InvalidDataException("The completed NUnit receipt changed.");
  using var inventory=JsonDocument.Parse(File.ReadAllBytes(receiptPath));
  if(inventory.RootElement.GetProperty("InputSha256").GetString()!=c.Checkpoint.InputSha256)
   throw new InvalidDataException("Deployment evidence belongs to another workflow.");
  var files=inventory.RootElement.GetProperty("Files").EnumerateArray().ToDictionary(
   f=>f.GetProperty("RelativePath").GetString()!.Replace('\\','/'),f=>f.GetProperty("Sha256").GetString()!,StringComparer.Ordinal);
  string Retained(string name) {
   string relative="nunit/"+name;
   if(!files.TryGetValue(relative,out var hash) || !SubmissionEvidence.SafeEvidencePath(c.RunDirectory,relative,out var path) || AutomationFiles.Hash(path)!=hash)
    throw new InvalidDataException("Missing or changed retained release deployment evidence.");
   return path;
  }
  var imported=AutomationFiles.Read<DriverDeploymentResult>(Retained("actual-import.json"));
  var installed=AutomationFiles.Read<DriverInstanceReady>(Retained("actual-activation.json"));
  using var release=JsonDocument.Parse(File.ReadAllBytes(Retained("ReleaseCandidate.json")));
  var r=release.RootElement;var candidate=settings.NUnit.ReleaseCandidate!;
  if(!imported.Available || string.IsNullOrWhiteSpace(imported.CatalogueId) || installed.DeviceId<=0 ||
   !imported.Sha256.Equals(settings.Release.PackageSha256,StringComparison.OrdinalIgnoreCase) ||
   !candidate.Sha256.Equals(settings.Release.PackageSha256,StringComparison.OrdinalIgnoreCase) || candidate.SourceCommit!=settings.Release.SourceCommit ||
   !Guid.TryParse(imported.Package.DriverId,out var driver) || !Guid.TryParse(candidate.DriverGuid,out var expected) || driver!=expected ||
   Version.Parse(imported.Package.Version)!=Version.Parse(candidate.DriverVersion) ||
   installed.Model!=imported.Package.Model || Version.Parse(installed.Version)!=Version.Parse(candidate.DriverVersion) ||
   !r.GetProperty("Sha256").GetString()!.Equals(settings.Release.PackageSha256,StringComparison.OrdinalIgnoreCase) ||
   r.GetProperty("SourceCommit").GetString()!=settings.Release.SourceCommit || !r.GetProperty("SourceInitiallyClean").GetBoolean() ||
   r.GetProperty("Mode").GetString()!="PrebuiltRelease")
   throw new InvalidDataException("Deployment receipts do not establish the selected release candidate.");

  return new(imported,installed,receipt);
 }
}
