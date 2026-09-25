// Copyright (c) 2026 Neil Colvin. MIT licensed.
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CrestronHomeDevTools.Automation;

/// <summary>Release intake prepares a private, immutable per-release probe before registration.
/// Only local file operations occur. Source publications and previous attempts are never edited.</summary>
internal static class AutomationProbePreparation
{
 internal static SubmissionAutomationSettings Prepare(SubmissionAutomationSettings settings,string run,
  Func<JsonNode?,JsonNode?> expand) {
  if(settings.EnduranceProbeSettingsTemplate is not {} input)return settings;
  var worker=settings.Endurance??throw new InvalidDataException("A probe settings template requires an endurance plan.");
  var source=worker.Probe;
  if(source.SettingsFile!=null)throw new InvalidDataException("Select either preconfigured probe settings or a settings template.");
  if(!Path.IsPathFullyQualified(input.Path) || new FileInfo(input.Path).Length>65536 || AutomationFiles.Hash(input.Path)!=input.Sha256)
   throw new InvalidDataException("Pinned probe settings template changed.");
  // The source plan's producer ID is replaced only after validating the complete source inventory.
  SubmissionEnduranceProcessProbe.Validate(source,worker.Plan with{ProducerId=SubmissionEnduranceProcessProbe.GetProducerId(source)});
  var node=JsonNode.Parse(File.ReadAllBytes(input.Path)) as JsonObject??throw new InvalidDataException("Probe settings template must be an object.");
  var expanded=expand(node) as JsonObject??throw new InvalidDataException("Probe settings must remain an object.");
  if(settings.EnduranceFromDeployment)_=AutomationDeploymentEndurance.RenderTemplate((JsonObject)expanded.DeepClone(),1,"validation-only");
  byte[] generated=JsonSerializer.SerializeToUtf8Bytes(expanded,AutomationFiles.Json);
  string output=Path.Combine(run,settings.EnduranceFromDeployment?"endurance-producer-template":"endurance-producer");
  if(Path.GetFullPath(source.Directory).StartsWith(Path.GetFullPath(run)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))
   throw new InvalidDataException("The published source producer must be outside this run.");
  return settings with{Endurance=Publish(worker,output,generated)};
 }
 internal static SubmissionEnduranceWorkerPlan Publish(SubmissionEnduranceWorkerPlan worker,string output,byte[] generated) {
  var source=worker.Probe;
  const string generatedName="settings.generated.json";
  if(source.Files.Any(f=>f.RelativePath.Equals(generatedName,StringComparison.OrdinalIgnoreCase)))
   throw new InvalidDataException("Published producer already contains the reserved generated settings name.");
  Directory.CreateDirectory(output);
  string Target(string relative) {
   string parent=output;
   if((File.GetAttributes(parent)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("Producer output must not be a link.");
   foreach(string part in relative.Split('/')[..^1]) {
    parent=Path.Combine(parent,part);Directory.CreateDirectory(parent);
    if((File.GetAttributes(parent)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("Producer output must not contain links.");
   }
   string path=Path.Combine(parent,relative.Split('/')[^1]);
   if((File.Exists(path)||Directory.Exists(path)) && !SubmissionEvidence.SafeEvidencePath(output,relative,out _))
    throw new InvalidDataException("Invalid retained producer output.");
   return path;
  }
  foreach(var pin in source.Files) {
   if(!SubmissionEvidence.SafeEvidencePath(source.Directory,pin.RelativePath,out var from))throw new InvalidDataException("Invalid producer file path.");
   string to=Target(pin.RelativePath);
   if(!File.Exists(to))File.Copy(from,to,false);
   if(AutomationFiles.Hash(to)!=pin.Sha256.ToLowerInvariant())throw new InvalidDataException("Retained producer copy differs from its source pin.");
  }
  string settingsPath=Target(generatedName);
  if(File.Exists(settingsPath)) {
   if(!File.ReadAllBytes(settingsPath).AsSpan().SequenceEqual(generated))throw new InvalidDataException("Retained generated probe settings changed.");
  } else {
   using var file=new FileStream(settingsPath,FileMode.CreateNew,FileAccess.Write,FileShare.None);file.Write(generated);file.Flush(true);
  }
  var probe=source with{Directory=output,SettingsFile=settingsPath,
   Files=[..source.Files,new SubmissionEvidenceFile(generatedName,AutomationFiles.Hash(settingsPath))]};
  worker=worker with{Probe=probe,Plan=worker.Plan with{ProducerId=SubmissionEnduranceProcessProbe.GetProducerId(probe)}};
  // Detect foreign files, links and incomplete/interrupted copies before registration.
  SubmissionEnduranceProcessProbe.Validate(worker.Probe,worker.Plan);
  return worker;
 }
}
