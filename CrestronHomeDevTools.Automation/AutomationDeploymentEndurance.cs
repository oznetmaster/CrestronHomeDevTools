// Copyright (c) 2026 Neil Colvin. MIT licensed.
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CrestronHomeDevTools.Automation;

internal static class AutomationDeploymentEndurance
{
 private sealed record Binding(string InputSha256,string NUnitReceiptSha256,SubmissionEnduranceWorkerPlan Worker);
 internal static void ValidateConfiguration(SubmissionAutomationSettings settings) {
  if(settings.EnduranceFromDeployment && (settings.Endurance==null || settings.NUnit.ActualDriver==null ||
   settings.NUnit.ReleaseCandidate==null || settings.EnduranceProbeSettingsTemplate==null))
   throw new InvalidDataException("Deployment-bound endurance requires an actual release deployment and a probe settings template.");
 }
 internal static SubmissionEnduranceWorkerPlan? Resolve(SubmissionWorkflowStepContext c,SubmissionAutomationSettings settings) {
  if(!settings.EnduranceFromDeployment)return settings.Endurance;
  ValidateConfiguration(settings);
  if(settings.Release!=c.Checkpoint.Release ||
   !c.Checkpoint.CompletedStages.ContainsKey(SubmissionWorkflowStage.ProcessorTests) ||
   !c.Checkpoint.CompletedStages.ContainsKey(SubmissionWorkflowStage.AppTests) ||
   !c.Checkpoint.CompletedStages.TryGetValue(SubmissionWorkflowStage.WindowsTests,out var receipt) || receipt.RelativePath!="windows-tests.json")
   throw new InvalidDataException("Deployment-bound endurance requires the completed candidate test stages.");
  string P(string name)=>Path.Combine(c.RunDirectory,name);
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

  var worker=settings.Endurance!;var source=worker.Probe;
  SubmissionEnduranceProcessProbe.Validate(source,worker.Plan);
  if(source.SettingsFile==null || new FileInfo(source.SettingsFile).Length>65536)
   throw new InvalidDataException("Missing retained deployment probe template.");
  var node=JsonNode.Parse(File.ReadAllBytes(source.SettingsFile)) as JsonObject??throw new InvalidDataException("Probe settings must be an object.");
  byte[] generated=RenderTemplate(node,installed.DeviceId,imported.CatalogueId);
  string output=P("endurance-producer"),name="settings.generated.json";
  if(Path.GetFullPath(source.Directory).Equals(Path.GetFullPath(output),StringComparison.OrdinalIgnoreCase))
   throw new InvalidDataException("Deployment template and final producer must be separate.");
  string templateName=Path.GetRelativePath(source.Directory,source.SettingsFile).Replace('\\','/');
  var binaries=source with{Files=source.Files.Where(f=>f.RelativePath!=templateName).ToArray(),SettingsFile=null};
  if(binaries.Files.Any(f=>f.RelativePath.Equals(name,StringComparison.OrdinalIgnoreCase)))throw new InvalidDataException("Reserved generated settings name.");
  var probe=binaries with{Directory=output,SettingsFile=Path.Combine(output,name),Files=[..binaries.Files,new(name,Convert.ToHexStringLower(SHA256.HashData(generated)))]};
  var resolved=worker with{Probe=probe,Plan=worker.Plan with{ProducerId=SubmissionEnduranceProcessProbe.GetProducerId(probe)}};
  string binding=P("endurance-deployment-binding.json");
  if(!File.Exists(binding)) {
   if(Directory.Exists(P("endurance")))throw new InvalidDataException("An existing endurance run cannot acquire a new deployment binding.");
   _=AutomationProbePreparation.Publish(worker with{Probe=binaries},output,generated);
  }
  // Once retained, missing/changed files are errors, never silently reconstructed.
  AutomationFiles.Write(binding,new Binding(c.Checkpoint.InputSha256,receipt.Sha256,resolved));
  SubmissionEnduranceProcessProbe.Validate(resolved.Probe,resolved.Plan);
  return resolved;
 }
 internal static byte[] RenderTemplate(JsonObject node,int deviceId,string catalogueId) {
  bool deviceBound=false,catalogueBound=false;
  JsonNode? Replace(JsonNode? value) {
   if(value is JsonValue v && v.TryGetValue<string>(out var text)) {
    if(text=="${deployedDeviceId}"){deviceBound=true;return JsonValue.Create(deviceId);}
    if(text.Contains("${deployedCatalogueId}",StringComparison.Ordinal)) {catalogueBound=true;text=text.Replace("${deployedCatalogueId}",catalogueId,StringComparison.Ordinal);}
    if(text.Contains("${",StringComparison.Ordinal))throw new InvalidDataException("Unknown or embedded device-ID deployment placeholder.");
    return JsonValue.Create(text);
   }
   if(value is JsonObject o)foreach(var key in o.Select(p=>p.Key).ToArray())o[key]=Replace(o[key]?.DeepClone());
   if(value is JsonArray a)for(int i=0;i<a.Count;i++)a[i]=Replace(a[i]?.DeepClone());
   return value;
  }
  byte[] generated=JsonSerializer.SerializeToUtf8Bytes(Replace(node),AutomationFiles.Json);
  if(!deviceBound || !catalogueBound)throw new InvalidDataException("The probe template must bind both deployedDeviceId and deployedCatalogueId.");
  return generated;
 }
}
