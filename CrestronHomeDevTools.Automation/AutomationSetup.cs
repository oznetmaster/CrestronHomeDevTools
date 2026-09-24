// Copyright (c) 2026 Neil Colvin. MIT licensed.
using System.Globalization;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CrestronHomeDevTools.Automation;

public sealed record SubmissionAutomationPreparedSetup(string ProfilesPath,string RegistryPath,
 string ProvenancePath,SubmissionAutomationConfigurationReport Configuration);

/// <summary>Connect saved factual setup to a reviewed executable template. Does not start a worker,
/// provision secrets, authorize operations, or assert that evidence exists.</summary>
[SupportedOSPlatform("windows")]
public static class SubmissionAutomationSetup
{
 public static SubmissionAutomationPreparedSetup PrepareRehearsal(DevToolsPrivateStore store,string snapshotName) {
  ArgumentNullException.ThrowIfNull(store);
  var saved=store.LoadSetupProfile<SubmissionSetupSnapshot>(snapshotName);
  var snapshot=saved.Value;var run=snapshot.Run.Value;
  if(!Uri.TryCreate(snapshot.Driver.Value.RepositoryUrl,UriKind.Absolute,out var url) ||
   url.Scheme!="https" || url.Host!="github.com" || !url.IsDefaultPort || url.UserInfo.Length!=0 || url.Query.Length!=0 || url.Fragment.Length!=0 ||
   !Regex.IsMatch(url.AbsolutePath,"\\A/[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+/?\\z"))
   throw new InvalidDataException("Select a GitHub repository URL without query or credentials.");
  string repository=url.AbsolutePath.Trim('/');
  if(repository.EndsWith(".git",StringComparison.Ordinal))repository=repository[..^4];
  if(!run.AutomationNotBeforeUtc.EndsWith('Z') || !DateTimeOffset.TryParse(run.AutomationNotBeforeUtc,
   CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal,out var cutoff))
   throw new InvalidDataException("An explicit UTC release publication cutoff ending in Z is required.");
  byte[] templateBytes=ReadInput(run.AutomationSettingsTemplate),toolingBytes=ReadInput(run.AutomationToolingManifest);
  var settings=JsonSerializer.Deserialize<SubmissionAutomationSettings>(templateBytes,AutomationFiles.Json)
   ??throw new InvalidDataException("Empty settings template.");
  if(settings.SchemaVersion!=1 || settings.SourceRepository!="${source}" ||
   !string.Equals(settings.NUnit.Host,run.ProcessorHost,StringComparison.OrdinalIgnoreCase) ||
   (settings.InstalledAppTests is {} app && !string.Equals(app.Host,run.ProcessorHost,StringComparison.OrdinalIgnoreCase)))
   throw new InvalidDataException("Template source or processor differs from the saved setup. Review its equipment bindings; preparation never retargets hardware.");
  if(string.IsNullOrWhiteSpace(settings.CredentialBindings))
   throw new InvalidDataException("Select the evidence worker's provisioned credential bindings in the template.");
  // Never substitute the setup store/snapshot here: it can also contain mail and signing secrets.
  // A rehearsal carries no protected-stage configuration, even when its source template did.
  settings=settings with {PrivateRoot=run.PrivateWorkspace,Mode=SubmissionAutomationMode.Rehearsal,Protected=null,
   Review=settings.Review is {} review?review with {Title=snapshot.Driver.Value.DriverName+" ${version} - Crestron Home driver",Author=snapshot.Developer.Value.DeveloperName}:null};
  string directory=Path.Combine(store.DirectoryPath,"rehearsal-"+snapshotName+"-"+Guid.NewGuid().ToString("N"));
  string template=Path.Combine(directory,"settings-template.json"),tooling=Path.Combine(directory,"tooling.json");
  var profile=new SubmissionAutomationReleaseProfile(snapshotName,repository,cutoff,run.PrivateWorkspace,
   run.AutomationPackageName,new(template,"pending"),new(tooling,"pending"));
  AutomationReleaseDiscovery.Validate(profile);
  // Parse before creating outputs; do not accept arbitrary non-JSON as a tooling manifest.
  using var toolingDocument=JsonDocument.Parse(toolingBytes);
  if(toolingDocument.RootElement.ValueKind!=JsonValueKind.Object)throw new InvalidDataException("Tooling manifest must be an object.");
  var check=SubmissionAutomationConfiguration.Check(settings);
  Directory.CreateDirectory(directory); // inherits the private store's restricted permissions
  AutomationFiles.Write(template,settings);File.WriteAllBytes(tooling,toolingBytes);
  profile=profile with {SettingsTemplate=new(template,AutomationFiles.Hash(template)),ToolingManifest=new(tooling,AutomationFiles.Hash(tooling))};
  string profiles=Path.Combine(directory,"release-profiles.json"),registry=Path.Combine(directory,"registry.json"),provenance=Path.Combine(directory,"setup-provenance.json");
  AutomationFiles.Write(provenance,new {SchemaVersion=1,PreparedUtc=DateTimeOffset.UtcNow,Snapshot=saved.Name,
   SnapshotSha256=AutomationFiles.Hash(store.GetSubmissionSetupSnapshotPath(snapshotName)),
   DeveloperRevision=snapshot.Developer.Revision,DriverRevision=snapshot.Driver.Revision,RunRevision=snapshot.Run.Revision,
   SourceTemplateSha256=Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(templateBytes)),
   profile.SettingsTemplate,profile.ToolingManifest,Configuration=check});
  AutomationFiles.Write(registry,new SubmissionAutomationRegistry(1,[]));
  AutomationFiles.Write(profiles,new SubmissionAutomationReleaseProfiles(1,[profile]));
  return new(profiles,registry,provenance,check);
 }
 private static byte[] ReadInput(string path) {
  if(!Path.IsPathFullyQualified(path) || new FileInfo(path).Length>16*1024*1024)
   throw new InvalidDataException("Select an absolute, bounded local input file.");
  return File.ReadAllBytes(path);
 }
}
