// Copyright (c) 2026 Neil Colvin. MIT licensed.
using System.Text.Json;
using System.Text.Json.Serialization;
using CrestronHomeDevTools;
using CrestronHomeNUnit.Android;

namespace WeatherLinkAndroidTests;

public sealed record FixtureSettings(string ProcessorHost,int DeviceId,string TileName,
 string CredentialBindings,SubmissionEvidenceIdentity Identity)
{
 public static readonly JsonSerializerOptions Json=new() {PropertyNameCaseInsensitive=true,WriteIndented=true,
  UnmappedMemberHandling=JsonUnmappedMemberHandling.Disallow,RespectRequiredConstructorParameters=true,
  RespectNullableAnnotations=true,AllowDuplicateProperties=false,Converters={new JsonStringEnumConverter()}};
 public static FixtureSettings Read(AndroidRunContext context) {
  string evidence=Path.GetFullPath(context.EvidenceDirectory);
  var parent=Directory.GetParent(evidence);
  if(Path.GetFileName(evidence)!="AndroidUI" || parent?.Name!="installed-app" || parent.Parent==null)
   throw new InvalidDataException("Use the public controller's separate installed-app stage.");
  string root=parent.Parent.FullName;
  if(!SubmissionEvidence.SafeEvidencePath(root,"app-fixture-settings.json",out var path) || new FileInfo(path).Length>65536)
   throw new InvalidDataException("Missing or unsafe pinned app fixture settings.");
  var settings=JsonSerializer.Deserialize<FixtureSettings>(File.ReadAllBytes(path),Json)
   ??throw new InvalidDataException("Missing app fixture settings.");
  settings.Validate(context);return settings;
 }
 public void Validate(AndroidRunContext context) {
  if(ProcessorHost!=context.ProcessorAddress || DeviceId!=context.InstalledDriverId || DeviceId<=0 ||
   string.IsNullOrWhiteSpace(TileName) || !Path.IsPathFullyQualified(CredentialBindings) ||
   !Identity.PackageSha256.Equals(context.PackageSha256,StringComparison.OrdinalIgnoreCase) ||
   Identity.SourceCommit!=context.ReleaseSourceCommit || !Hash(Identity.PolicySha256) || !Hash(Identity.TemplateSha256))
   throw new InvalidDataException("Fixture settings do not match the selected candidate and processor.");
 }
 private static bool Hash(string value)=>value.Length==64&&value.All(char.IsAsciiHexDigit);
}
