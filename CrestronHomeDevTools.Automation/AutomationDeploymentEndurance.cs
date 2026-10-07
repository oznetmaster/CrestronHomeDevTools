// Copyright (c) 2026 Neil Colvin. MIT licensed.
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CrestronHomeDevTools.Automation;

internal static class AutomationDeploymentEndurance
{
 private sealed record Binding(string InputSha256,string NUnitReceiptSha256,SubmissionEnduranceWorkerPlan Worker,
  [property:System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)] int RuntimeStateSchema=0);
 internal static void ValidateConfiguration(SubmissionAutomationSettings settings) {
  if(settings.EnduranceFromDeployment && (settings.Endurance==null || settings.NUnit.ActualDriver==null ||
   settings.NUnit.ReleaseCandidate==null || settings.EnduranceProbeSettingsTemplate==null))
   throw new InvalidDataException("Deployment-bound endurance requires an actual release deployment and a probe settings template.");
 }
 internal static SubmissionEnduranceWorkerPlan? Resolve(SubmissionWorkflowStepContext c,SubmissionAutomationSettings settings,bool verifyOnly=false,bool inspectStaleIdentity=false) {
  if(!settings.EnduranceFromDeployment)return settings.Endurance;
  ValidateConfiguration(settings);
  var deployment=AutomationDeploymentEvidence.Read(c,settings);
  var imported=deployment.Imported;var installed=deployment.Installed;var receipt=deployment.Receipt;
  string P(string name)=>Path.Combine(c.RunDirectory,name);
  string binding=P("endurance-deployment-binding.json");
  if(verifyOnly && !File.Exists(binding))throw new InvalidDataException("Completed endurance deployment binding is missing; verification cannot prepare a producer.");
  int runtimeSchema=File.Exists(binding)?AutomationFiles.Read<Binding>(binding).RuntimeStateSchema:1;
  if(runtimeSchema is not (0 or 1))throw new InvalidDataException("Unsupported endurance runtime-state binding.");
  var worker=settings.Endurance!;var source=worker.Probe;
  SubmissionEnduranceProcessProbe.Validate(source,worker.Plan);
  if(source.SettingsFile==null || new FileInfo(source.SettingsFile).Length>65536)
   throw new InvalidDataException("Missing retained deployment probe template.");
  var node=JsonNode.Parse(File.ReadAllBytes(source.SettingsFile)) as JsonObject??throw new InvalidDataException("Probe settings must be an object.");
  if(settings.ManagedDevices!=null)node=JsonNode.Parse(AutomationManagedDevices.RenderInputs(JsonSerializer.SerializeToElement(node),
   AutomationManagedDevices.VerifyRetained(c)).GetRawText())!.AsObject();
  if(inspectStaleIdentity && !verifyOnly)throw new InvalidDataException("Stale identity inspection must be read-only.");
  if(!inspectStaleIdentity)ValidateSettingsBinding(node,worker.Plan);
  if(runtimeSchema==1)BindRuntimeBaseline(node,c.RunDirectory,!File.Exists(binding));
  byte[] generated=RenderTemplate(node,installed.DeviceId,imported.CatalogueId);
  string output=P("endurance-producer"),name="settings.generated.json";
  if(Path.GetFullPath(source.Directory).Equals(Path.GetFullPath(output),StringComparison.OrdinalIgnoreCase))
   throw new InvalidDataException("Deployment template and final producer must be separate.");
  string templateName=Path.GetRelativePath(source.Directory,source.SettingsFile).Replace('\\','/');
  var binaries=source with{Files=source.Files.Where(f=>f.RelativePath!=templateName).ToArray(),SettingsFile=null};
  if(binaries.Files.Any(f=>f.RelativePath.Equals(name,StringComparison.OrdinalIgnoreCase)))throw new InvalidDataException("Reserved generated settings name.");
  var probe=binaries with{Directory=output,SettingsFile=Path.Combine(output,name),Files=[..binaries.Files,new(name,Convert.ToHexStringLower(SHA256.HashData(generated)))]};
  var resolved=worker with{Probe=probe,Plan=worker.Plan with{ProducerId=SubmissionEnduranceProcessProbe.GetProducerId(probe)}};
  if(!File.Exists(binding)) {
   if(Directory.Exists(P("endurance")))throw new InvalidDataException("An existing endurance run cannot acquire a new deployment binding.");
   _=AutomationProbePreparation.Publish(worker with{Probe=binaries},output,generated);
  }
  // Once retained, missing/changed files are errors, never silently reconstructed.
  var expected=new Binding(c.Checkpoint.InputSha256,receipt.Sha256,resolved,runtimeSchema);
  if(verifyOnly) {
   if(!File.ReadAllBytes(binding).AsSpan().SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(expected,AutomationFiles.Json)))
    throw new InvalidDataException("Completed endurance deployment binding changed.");
  } else AutomationFiles.Write(binding,expected);
  SubmissionEnduranceProcessProbe.Validate(resolved.Probe,resolved.Plan);
  return resolved;
 }
 internal static void BindRuntimeBaseline(JsonObject node,string run,bool creating) {
  var fields=node.Where(p=>p.Key.Equals("BaselineFile",StringComparison.OrdinalIgnoreCase)).ToArray();
  if(fields.Length==0)return; // Custom producers without a lifetime baseline remain supported.
  if(fields.Length!=1||fields[0].Value is not JsonValue value||!value.TryGetValue<string>(out var configured)||!Path.IsPathFullyQualified(configured))
   throw new InvalidDataException("A single absolute producer baseline destination is required.");
  string path=Path.Combine(run,"endurance-lifetime.json");
  if(creating&&(File.Exists(path)||Directory.Exists(path)))throw new InvalidDataException("Fresh endurance runtime state already exists; inspect it before starting.");
  node[fields[0].Key]=path;
 }
 // The shared Identity convention is optional for custom producers. When present,
 // reject stale template bindings before publishing files or reserving a processor.
 internal static void ValidateSettingsBinding(JsonObject node,SubmissionEndurancePlan plan) {
  JsonNode? Field(string name) {
   var matches=node.Where(p=>p.Key.Equals(name,StringComparison.OrdinalIgnoreCase)).ToArray();
   if(matches.Length>1)throw new InvalidDataException("Ambiguous endurance producer binding: "+name);
   return matches.Length==0?null:matches[0].Value;
  }
  if(Field("Identity") is not {} identity)return;
  var options=new JsonSerializerOptions(AutomationFiles.Json){PropertyNameCaseInsensitive=true};
  if(identity.Deserialize<SubmissionEvidenceIdentity>(options)!=plan.Identity)
   throw new InvalidDataException("Endurance producer identity differs from the frozen plan.");
  foreach(var field in new[]{("InstallationIdentity",plan.InstallationIdentity),("RequirementId",plan.Requirement.Id),
   ("ProcessorHost",plan.ProcessorIdentity.StartsWith("processor:",StringComparison.Ordinal)?plan.ProcessorIdentity[10..]:null)})
   if(field.Item2!=null && Field(field.Item1) is {} value && value.GetValue<string>()!=field.Item2)
    throw new InvalidDataException("Endurance producer binding differs from the frozen plan: "+field.Item1);
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
