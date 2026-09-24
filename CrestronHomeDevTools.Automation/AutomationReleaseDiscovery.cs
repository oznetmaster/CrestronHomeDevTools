// Copyright (c) 2026 Neil Colvin. MIT licensed.
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
namespace CrestronHomeDevTools.Automation;

/// <summary>Private opt-in settings. Release publication supplies identity only, never commands or secrets.</summary>
public sealed record SubmissionAutomationReleaseProfile(string Name,string Repository,DateTimeOffset NotBeforeUtc,
 string PrivateRoot,string PackageNameTemplate,SubmissionAutomationInput SettingsTemplate,
 SubmissionAutomationInput ToolingManifest,SubmissionAutomationMode Mode=SubmissionAutomationMode.Rehearsal,bool AllowPrerelease=false);
public sealed record SubmissionAutomationReleaseProfiles(int SchemaVersion,SubmissionAutomationReleaseProfile[] Profiles);

internal static class AutomationReleaseDiscovery
{
 internal sealed record Status(string Profile,long? ReleaseId,string State,string Reason,SubmissionAutomationMode Mode);
 internal static async Task<Status[]> Tick(string profilesPath,string registryPath,GitHubSubmissionRelease github,CancellationToken token,
  Func<string,string,string,CancellationToken,Task>? checkout=null)
 {
  var profiles=AutomationFiles.Read<SubmissionAutomationReleaseProfiles>(profilesPath);
  if(profiles.SchemaVersion!=1 || profiles.Profiles.Length>100 || profiles.Profiles.Select(p=>p.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=profiles.Profiles.Length)
   throw new InvalidDataException("Invalid release profiles.");
  var statuses=new List<Status>();
  foreach(var p in profiles.Profiles) {
   bool validProfile=false;
   try {
    Validate(p);validProfile=true;Verify(p.SettingsTemplate);Verify(p.ToolingManifest);
    var releases=await github.ListPublishedAsync(p.Repository,p.NotBeforeUtc,p.AllowPrerelease,token);
    foreach(var published in releases) {
     token.ThrowIfCancellationRequested();
     var registry=AutomationFiles.Read<SubmissionAutomationRegistry>(registryPath);
     var matching=registry.Entries.Where(e=>e.Profile==p.Name && e.ReleaseId==published.ReleaseId && e.Mode==p.Mode).ToArray();
     if(matching.Length>1)throw new InvalidDataException("Duplicate release registration.");
     if(matching.Length==1) {
      var saved=AutomationRequest.Load(["--registry",registryPath,"--profile",p.Name,"--release-id",published.ReleaseId.ToString(CultureInfo.InvariantCulture),"--mode",p.Mode==SubmissionAutomationMode.Rehearsal?"rehearsal":"submit"]);
      if(saved.Settings.Release.Repository!=p.Repository || saved.Settings.PrivateRoot!=p.PrivateRoot)throw new InvalidDataException("Registered profile identity changed.");
      continue; // Never redo an old submission because a release is still listed.
     }
     string version=Version(published.Tag),package=p.PackageNameTemplate.Replace("${version}",version,StringComparison.Ordinal);
     Directory.CreateDirectory(p.PrivateRoot);
     // Freeze the entire opted-in profile, including mode and equipment bindings, before intake.
     string profileDir=Path.Combine(p.PrivateRoot,"profiles");Directory.CreateDirectory(profileDir);
     byte[] profileBytes=JsonSerializer.SerializeToUtf8Bytes(p,AutomationFiles.Json);
     string profileHash=Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(profileBytes));
     string frozen=Path.Combine(profileDir,profileHash+".json");AutomationFiles.Write(frozen,p);
     var intake=await SubmissionReleaseIntake.PrepareAsync(new(1,p.Repository,published.ReleaseId,package,p.PrivateRoot,frozen,profileHash,
      p.ToolingManifest.Path,p.ToolingManifest.Sha256,p.AllowPrerelease),github,token);
     if(intake.Availability!=SubmissionReleaseAvailability.Ready) {statuses.Add(new(p.Name,published.ReleaseId,"Waiting",intake.ReasonCode,p.Mode));continue;}
     var release=intake.Checkpoint!.Release;string run=intake.RunDirectory!,source=Path.Combine(run,"source");
     using(var setupGate=new FileStream(Path.Combine(run,"registration.lock"),FileMode.OpenOrCreate,FileAccess.Write,FileShare.None)) {
      string settingsPath=Path.Combine(run,"automation-settings.json");
      if(!File.Exists(settingsPath)) {
       var settings=Expand(p,release,run,source,version);
       await (checkout??Checkout)(p.Repository,release.SourceCommit,source,token);
       AutomationFiles.Write(settingsPath,settings);
      }
      string digest=AutomationFiles.Hash(settingsPath);
      // Validate serialized settings before exposing the registration to a worker.
      var request=AutomationRequest.Load(["--settings",settingsPath,"--settings-sha256",digest]);
      if(request.Settings.Release!=release || request.Settings.Mode!=p.Mode || request.Settings.PrivateRoot!=p.PrivateRoot)
       throw new InvalidDataException("Retained settings differ from this release profile.");
      Register(registryPath,new(p.Name,published.ReleaseId,p.Mode,settingsPath,digest));
     }
     statuses.Add(new(p.Name,published.ReleaseId,"Registered","release-ready",p.Mode));
    }
   }catch(OperationCanceledException) when(token.IsCancellationRequested){throw;}
   catch(Exception e) when(e is not OutOfMemoryException){
    if(validProfile && Directory.Exists(p.PrivateRoot)) {
     string path=Path.Combine(p.PrivateRoot,"discovery-error.json");
     if(!File.Exists(path))AutomationFiles.Write(path,new{ObservedUtc=DateTimeOffset.UtcNow,p.Name,ErrorType=e.GetType().Name,
      Message=e.Message,JsonPath=(e as JsonException)?.Path,JsonLine=(e as JsonException)?.LineNumber});
    }
    statuses.Add(new(p.Name,null,"AttentionRequired",e.GetType().Name,p.Mode));
   }
  }
  return statuses.ToArray();
 }
 internal static SubmissionAutomationSettings Expand(SubmissionAutomationReleaseProfile p,SubmissionWorkflowRelease release,string run,string source,string version) {
  Verify(p.SettingsTemplate);
  var node=JsonNode.Parse(File.ReadAllText(p.SettingsTemplate.Path)) as JsonObject??throw new InvalidDataException("Settings template must be an object.");
  // Stable across intake retries, distinct across releases and independently frozen profiles.
  string reservationId=Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(
   JsonSerializer.SerializeToUtf8Bytes(new{Purpose="endurance-reservation-v1",Release=release},AutomationFiles.Json)))[..32];
  var replacements=new Dictionary<string,string> {
   ["${run}"]=run,["${source}"]=source,["${package}"]=Path.Combine(run,"candidate.pkg"),["${version}"]=version,
   ["${version4}"]=version.Split('.').Length==3?version+".0":version,["${commit}"]=release.SourceCommit,
   ["${packageSha256}"]=release.PackageSha256,["${releaseId}"]=release.ReleaseId.ToString(CultureInfo.InvariantCulture),
   ["${reservationId}"]=reservationId
  };
  JsonNode? Replace(JsonNode? value) {
   if(value is JsonValue v && v.TryGetValue<string>(out var text)) {
    foreach(var replacement in replacements)text=text.Replace(replacement.Key,replacement.Value,StringComparison.Ordinal);
    if(text.Contains("${",StringComparison.Ordinal))throw new InvalidDataException("Unknown settings placeholder.");
    return JsonValue.Create(text);
   }
   if(value is JsonObject o)foreach(var key in o.Select(k=>k.Key).ToArray())o[key]=Replace(o[key]?.DeepClone());
   if(value is JsonArray a)for(int i=0;i<a.Count;i++)a[i]=Replace(a[i]?.DeepClone());
   return value;
  }
  // These values come only from verified intake and the private profile, never from template defaults.
  foreach(var key in node.Select(v=>v.Key).Where(k=>new[]{"SchemaVersion","PrivateRoot","Release","Mode"}.Contains(k,StringComparer.OrdinalIgnoreCase)).ToArray())node.Remove(key);
  node["SchemaVersion"]=1;node["PrivateRoot"]=p.PrivateRoot;node["Release"]=JsonSerializer.SerializeToNode(release,AutomationFiles.Json);node["Mode"]=p.Mode.ToString();
  var settings=Replace(node)!.Deserialize<SubmissionAutomationSettings>(AutomationFiles.Json)??throw new InvalidDataException("Empty settings template.");
  if(!Path.GetFullPath(settings.SourceRepository).Equals(Path.GetFullPath(source),StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("The driver source must use ${source}.");
  AutomationEndurance.ValidateReservation(settings.Endurance);
  return AutomationProbePreparation.Prepare(settings,run,value=>Replace(value));
 }
 internal static void Register(string path,SubmissionAutomationRegistration entry) {
  using var gate=new FileStream(path+".lock",FileMode.OpenOrCreate,FileAccess.Write,FileShare.None);
  var registry=AutomationFiles.Read<SubmissionAutomationRegistry>(path);
  if(registry.SchemaVersion!=1 || registry.Entries.Length>=1000)throw new InvalidDataException("Invalid or full registry.");
  var matches=registry.Entries.Where(e=>e.Profile==entry.Profile && e.ReleaseId==entry.ReleaseId && e.Mode==entry.Mode).ToArray();
  if(matches.Length==1 && matches[0]==entry)return;
  if(matches.Length!=0)throw new InvalidDataException("Release registration cannot be replaced.");
  byte[] data=JsonSerializer.SerializeToUtf8Bytes(registry with{Entries=[..registry.Entries,entry]},AutomationFiles.Json);
  string temp=path+".tmp";using(var stream=new FileStream(temp,FileMode.Create,FileAccess.Write,FileShare.None)){stream.Write(data);stream.Flush(true);}
  File.Move(temp,path,true);
 }
 internal static void Validate(SubmissionAutomationReleaseProfile p) {
  if(!Regex.IsMatch(p.Name,"\\A[A-Za-z0-9_-]{1,64}\\z") || !Enum.IsDefined(p.Mode) || !Path.IsPathFullyQualified(p.PrivateRoot) ||
   p.NotBeforeUtc==default || !p.PackageNameTemplate.EndsWith(".pkg",StringComparison.Ordinal) || p.PackageNameTemplate.IndexOfAny(['/', '\\', ':'])>=0 || p.PackageNameTemplate.Any(char.IsControl) ||
   p.PackageNameTemplate.Replace("${version}","",StringComparison.Ordinal).Contains("${",StringComparison.Ordinal))throw new InvalidDataException("Invalid opted-in release profile.");
 }
 private static void Verify(SubmissionAutomationInput input) {
  if(!Path.IsPathFullyQualified(input.Path)||new FileInfo(input.Path).Length>16*1024*1024||AutomationFiles.Hash(input.Path)!=input.Sha256)throw new InvalidDataException("Pinned release input changed.");
 }
 internal static string Version(string tag) {
  if(!Regex.IsMatch(tag,"\\Av?[0-9]+\\.[0-9]+\\.[0-9]+(?:\\.[0-9]+)?\\z"))throw new InvalidDataException("Automatic version expansion requires a numeric three- or four-component release tag.");
  return tag.StartsWith('v')?tag[1..]:tag;
 }
 private static async Task Checkout(string repository,string commit,string directory,CancellationToken token) {
  Directory.CreateDirectory(directory);
  async Task<string> Git(params string[] args) {
   var start=new ProcessStartInfo("git"){WorkingDirectory=directory,UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,RedirectStandardInput=true};
   start.ArgumentList.Add("-c");start.ArgumentList.Add("core.longpaths=true");
   start.ArgumentList.Add("-c");start.ArgumentList.Add("core.hooksPath="+Path.Combine(directory,".disabled-hooks"));
   foreach(var arg in args)start.ArgumentList.Add(arg);
   start.Environment["GIT_TERMINAL_PROMPT"]="0";
   using var timeout=CancellationTokenSource.CreateLinkedTokenSource(token);timeout.CancelAfter(TimeSpan.FromMinutes(5));
   using var process=Process.Start(start)??throw new IOException("Could not start Git.");process.StandardInput.Close();
   try {
    var output=process.StandardOutput.ReadToEndAsync(timeout.Token);var error=process.StandardError.ReadToEndAsync(timeout.Token);
    await process.WaitForExitAsync(timeout.Token);string diagnostic=await error;
    if(process.ExitCode!=0) {
     // Preserve the first original Git failure outside the source tree, with a bounded diagnostic.
     string failure=Path.Combine(Path.GetDirectoryName(directory)!,"source-checkout-error.json");
     if(!File.Exists(failure))AutomationFiles.Write(failure,new{ObservedUtc=DateTimeOffset.UtcNow,process.ExitCode,Arguments=args,Error=diagnostic[..Math.Min(diagnostic.Length,65536)]});
     throw new IOException("Frozen source checkout failed; inspect source-checkout-error.json.");
    }
    return (await output).Trim();
   }finally{if(!process.HasExited)process.Kill(true);}
  }
  if(!Directory.Exists(Path.Combine(directory,".git"))) {
   if(Directory.EnumerateFileSystemEntries(directory).Any())throw new InvalidDataException("Source directory is not an owned checkout.");
   await Git("-c","init.templateDir=","init");
  }
  await Git("config","--local","core.longpaths","true");
  string url="https://github.com/"+repository+".git";
  // Fetch the immutable commit directly. Do not execute submodules, hooks or release-supplied commands.
  if(await Git("status","--porcelain")!="")throw new InvalidDataException("Release source checkout has changes.");
  await Git("fetch","--depth=1",url,commit);await Git("checkout","--detach",commit);
  if(await Git("rev-parse","HEAD")!=commit)throw new InvalidDataException("Source checkout does not match the release.");
 }
}
