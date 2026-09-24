// Copyright (c) 2026 Neil Colvin. MIT licensed.
namespace CrestronHomeDevTools.Automation;

/// <summary>Stored only on the trusted worker. Dispatch supplies selectors, never executable commands or private file paths.</summary>
public sealed record SubmissionAutomationRegistry(int SchemaVersion, SubmissionAutomationRegistration[] Entries);
public sealed record SubmissionAutomationRegistration(string Profile, long ReleaseId, SubmissionAutomationMode Mode,
 string SettingsPath, string SettingsSha256);

internal sealed record AutomationRequest(SubmissionAutomationSettings Settings,string Sha256)
{
 internal static AutomationRequest Load(string[] args)
 {
  if(args is ["--settings",var path,"--settings-sha256",var digest]) return Read(path,digest);
  if(args is not ["--registry",var registryPath,"--profile",var profile,"--release-id",var id,"--mode",var mode] ||
   !Path.IsPathFullyQualified(registryPath) || profile.Length is <1 or >64 ||
   profile.Any(c=>!(char.IsAsciiLetterOrDigit(c)||c is '-' or '_')) ||
   !long.TryParse(id,System.Globalization.NumberStyles.None,System.Globalization.CultureInfo.InvariantCulture,out var releaseId) || releaseId<=0)
   throw new InvalidDataException("Select a registered release, profile and mode.");
  var selectedMode=mode switch { "rehearsal"=>SubmissionAutomationMode.Rehearsal,"submit"=>SubmissionAutomationMode.Submit,
   _=>throw new InvalidDataException("Select rehearsal or submit.") };
  var registry=AutomationFiles.Read<SubmissionAutomationRegistry>(registryPath);
  if(registry.SchemaVersion!=1)throw new InvalidDataException("Unknown automation registry version.");
  var entries=registry.Entries.Where(e=>e.Profile==profile && e.ReleaseId==releaseId && e.Mode==selectedMode).ToArray();
  if(entries.Length!=1)throw new InvalidDataException("The requested release and mode must have exactly one trusted registration.");
  var request=Read(entries[0].SettingsPath,entries[0].SettingsSha256);
  if(request.Settings.Release.ReleaseId!=releaseId || request.Settings.Mode!=selectedMode)
   throw new InvalidDataException("The dispatch differs from the frozen release or mode.");
  return request;
 }
 internal static AutomationRequest ReadForCheck(string path,string digest)=>Read(path,digest,validateReservation:false);
 private static AutomationRequest Read(string path,string digest,bool validateReservation=true)
 {
  if(!Path.IsPathFullyQualified(path) || digest.Length!=64 || digest.Any(c=>!(char.IsAsciiDigit(c)||c is >= 'a' and <= 'f')))
   throw new InvalidDataException("Select absolute private settings and their recorded lowercase digest.");
  // Read the pinned bytes once; never check one version and deserialize a replacement.
  if(new FileInfo(path).Length>16*1024*1024)throw new InvalidDataException("Settings exceed their size limit.");
  byte[] bytes=File.ReadAllBytes(path);
  if(Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes))!=digest)
   throw new InvalidDataException("Frozen automation settings changed.");
  var settings=System.Text.Json.JsonSerializer.Deserialize<SubmissionAutomationSettings>(bytes,AutomationFiles.Json)
   ??throw new InvalidDataException("Empty automation settings.");
  if(settings.SchemaVersion!=1 || !Enum.IsDefined(settings.Mode))throw new InvalidDataException("Invalid automation mode or schema.");
  if(validateReservation) {
   AutomationEndurance.ValidateReservation(settings.Endurance);
   AutomationDeploymentEndurance.ValidateConfiguration(settings);
  }
  return new(settings,digest);
 }
}
