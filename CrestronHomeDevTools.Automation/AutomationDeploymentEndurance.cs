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
  var deployment=AutomationDeploymentEvidence.Read(c,settings);
  var imported=deployment.Imported;var installed=deployment.Installed;var receipt=deployment.Receipt;
  string P(string name)=>Path.Combine(c.RunDirectory,name);
  var worker=settings.Endurance!;var source=worker.Probe;
  SubmissionEnduranceProcessProbe.Validate(source,worker.Plan);
  if(source.SettingsFile==null || new FileInfo(source.SettingsFile).Length>65536)
   throw new InvalidDataException("Missing retained deployment probe template.");
  var node=JsonNode.Parse(File.ReadAllBytes(source.SettingsFile)) as JsonObject??throw new InvalidDataException("Probe settings must be an object.");
  if(settings.ManagedDevices!=null)node=JsonNode.Parse(AutomationManagedDevices.RenderInputs(JsonSerializer.SerializeToElement(node),
   AutomationManagedDevices.VerifyRetained(c)).GetRawText())!.AsObject();
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
 internal static byte[] RenderTemplate(JsonObject node,int deviceId,string catalogueId,SubmissionManagedDevicesPlan? deferredManaged=null) {
  bool deviceBound=false,catalogueBound=false;
  JsonNode? Replace(JsonNode? value) {
   if(value is JsonValue v && v.TryGetValue<string>(out var text)) {
    if(deferredManaged!=null && AutomationManagedDevices.IsReference(text) && deferredManaged.Children.Any(c=>c.Alias==text.Split(':')[1]))return JsonValue.Create(text);
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
