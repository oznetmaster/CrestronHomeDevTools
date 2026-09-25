// Copyright (c) 2026 Neil Colvin. MIT licensed.
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;

namespace CrestronHomeDevTools.Automation;

// The completed coordinator receipt is the trust anchor. Never mint review pins
// from a fixture's unverified output or replace independently supplied pins.
internal static class AutomationAndroidReview
{
 internal sealed record Binding(string PinsPath,JsonElement Evidence);
 internal static Binding Bind(SubmissionWorkflowStepContext c,SubmissionWorkflowRelease release,
  bool separate,string candidatePath,CancellationToken token)
 {
  string prefix=separate?"installed-app/AndroidUI/":"nunit/AndroidUI/";
  string receiptName=separate?"installed-app-tests.json":"windows-tests.json";
  var stage=separate?SubmissionWorkflowStage.AppTests:SubmissionWorkflowStage.WindowsTests;
  if(!c.Checkpoint.CompletedStages.TryGetValue(stage,out var completed) || completed.RelativePath!=receiptName ||
   AutomationFiles.Hash(Path.Combine(c.RunDirectory,receiptName))!=completed.Sha256)
   throw new InvalidDataException("Android review requires an unchanged completed coordinator receipt.");
  using var receipt=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(c.RunDirectory,receiptName)));
  if(receipt.RootElement.GetProperty("InputSha256").GetString()!=c.Checkpoint.InputSha256 || c.Checkpoint.Release!=release)
   throw new InvalidDataException("Android coordinator receipt belongs to another workflow.");
  var retained=receipt.RootElement.GetProperty("Files").EnumerateArray().ToDictionary(
   f=>f.GetProperty("RelativePath").GetString()!.Replace('\\','/'),f=>f.GetProperty("Sha256").GetString()!,StringComparer.Ordinal);
  foreach(var file in retained.Where(f=>f.Key.StartsWith(prefix,StringComparison.Ordinal))) {
   token.ThrowIfCancellationRequested();
   if(!SubmissionEvidence.SafeEvidencePath(c.RunDirectory,file.Key,out var path) || !Same(AutomationFiles.Hash(path),file.Value))
    throw new InvalidDataException("Retained Android coordinator evidence changed.");
  }
  string PathFor(string name) {
   if(!retained.ContainsKey(prefix+name) || !SubmissionEvidence.SafeEvidencePath(c.RunDirectory,prefix+name,out var path))
    throw new InvalidDataException("Android audit input is missing from the completed coordinator inventory.");
   return path;
  }
  using var pin=JsonDocument.Parse(File.ReadAllBytes(PathFor("producer-pin.json")));
  using var context=JsonDocument.Parse(File.ReadAllBytes(PathFor("context.json")));
  var p=pin.RootElement;var x=context.RootElement;
  string runId=p.GetProperty("RunId").GetString()!;
  int version=p.GetProperty("SchemaVersion").GetInt32();
  if(version is not (1 or 2) || runId.Length!=32 || !runId.All(ch=>char.IsAsciiHexDigit(ch) && !char.IsUpper(ch)) ||
   x.GetProperty("RunId").GetString()!=runId || !Same(p.GetProperty("PackageSha256").GetString(),release.PackageSha256) ||
   !Same(x.GetProperty("PackageSha256").GetString(),release.PackageSha256) || x.GetProperty("ReleaseSourceCommit").GetString()!=release.SourceCommit)
   throw new InvalidDataException("Android coordinator pins identify another release or run.");
  string Pinned(string file,string field) {
   string hash=AutomationFiles.Hash(PathFor(file));
   if(!Same(hash,p.GetProperty(field).GetString()))throw new InvalidDataException("Android pre-execution pin differs from retained evidence.");
   return hash;
  }
  string manifestHash=Pinned("producer-manifest.json","ProducerManifestSha256");
  string discoveryHash=Pinned("discovery.dump","DiscoverySha256");
  string? selectionHash=version==2?Pinned("selection.json","SelectionSha256"):null;
  using var reader=XmlReader.Create(PathFor("discovery.dump"),new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null});
  var assemblies=XDocument.Load(reader).Descendants("test-suite").Where(e=>(string?)e.Attribute("type")=="Assembly").ToArray();
  if(assemblies.Length!=1)throw new InvalidDataException("Android discovery requires exactly one assembly.");
  string assembly=(string?)assemblies[0].Attribute("name")??"";
  if(string.IsNullOrWhiteSpace(assembly) || Path.GetFileName(assembly)!=assembly || assembly.Contains('/') || assembly.Contains('\\'))
   throw new InvalidDataException("Unsafe Android assembly name.");
  string assemblyHash=AutomationFiles.Hash(PathFor("assembly/"+assembly));
  using var manifest=JsonDocument.Parse(File.ReadAllBytes(PathFor("producer-manifest.json")));
  var entries=manifest.RootElement.GetProperty("files").EnumerateArray().Where(e=>e.GetProperty("relativePath").GetString()==assembly).ToArray();
  if(entries.Length!=1 || !Same(entries[0].GetProperty("sha256").GetString(),assemblyHash))
   throw new InvalidDataException("Android assembly differs from the pre-execution producer inventory.");
  var run=new Dictionary<string,object>{["runId"]=runId,["assembly"]=assembly,["assemblySha256"]=assemblyHash,
   ["discoverySha256"]=discoveryHash,["producerManifestSha256"]=manifestHash};
  if(selectionHash!=null)run.Add("selectionSha256",selectionHash);
  string pins=Path.Combine(c.RunDirectory,"review-inputs","android-pins.json");
  AutomationReview.WriteDocument(pins,new{schemaVersion=1,candidateSha256=AutomationFiles.Hash(candidatePath),runs=new[]{run}});
  var evidence=JsonSerializer.SerializeToElement(new[]{new{runId,path=Path.GetDirectoryName(PathFor("context.json"))!}},AutomationReview.DocumentJson);
  return new(pins,evidence);
 }
 private static bool Same(string? a,string? b)=>a!=null && b!=null && a.Equals(b,StringComparison.OrdinalIgnoreCase);
}
