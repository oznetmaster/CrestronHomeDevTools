// Copyright (c) 2026 Neil Colvin. MIT licensed.
namespace CrestronHomeDevTools.Automation;

/// <summary>Read-only discovery for a persistent signed-in operator desktop.
/// Uses the worker's trusted registry and pinned settings; never opens credentials or advances stages.</summary>
public static class SubmissionOperatorDiscovery
{
 public static void ValidateProfiles(IReadOnlyList<string> profiles) {
  ArgumentNullException.ThrowIfNull(profiles);
  if(profiles.Count is <1 or >100 || profiles.Any(p=>p is not {Length:>=1 and <=64} ||
    p.Any(c=>!char.IsAsciiLetterOrDigit(c)&&c is not '-' and not '_')) ||
    profiles.Distinct(StringComparer.Ordinal).Count()!=profiles.Count)
   throw new ArgumentException("Select distinct trusted profile names, using letters, digits, hyphens and underscores.",nameof(profiles));
 }
 public static IReadOnlyList<SubmissionOperatorInbox> Read(string registryPath,IReadOnlyList<string> profiles) {
  var result=new Dictionary<string,SubmissionOperatorInbox>(StringComparer.Ordinal);
  foreach(var (_,settings) in ReadSettings(registryPath,profiles)) {
   if(settings.OperatorInbox is not {} inbox)continue;
   inbox=inbox with {Directory=Path.TrimEndingDirectorySeparator(Path.GetFullPath(inbox.Directory))};
   if(result.TryGetValue(inbox.RunKey,out var previous) && !string.Equals(previous.Directory,inbox.Directory,
     OperatingSystem.IsWindows()?StringComparison.OrdinalIgnoreCase:StringComparison.Ordinal))
    throw new InvalidDataException("One release run cannot select multiple operator inboxes.");
   result[inbox.RunKey]=inbox;
  }
  return result.Values.ToArray();
 }
 internal static IReadOnlyList<(SubmissionAutomationRegistration Entry,SubmissionAutomationSettings Settings)> ReadSettings(string registryPath,IReadOnlyList<string> profiles) {
  ValidateProfiles(profiles);
  if(!Path.IsPathFullyQualified(registryPath))throw new ArgumentException("Select an absolute private worker registry.",nameof(registryPath));
  var registry=AutomationFiles.Read<SubmissionAutomationRegistry>(registryPath);
  if(registry.SchemaVersion!=1 || registry.Entries.Length>1000 || registry.Entries.Any(e=>e is null))throw new InvalidDataException("Invalid operator discovery registry.");
  var selected=registry.Entries.Where(e=>profiles.Contains(e.Profile,StringComparer.Ordinal)).ToArray();
  if(selected.GroupBy(e=>(e.Profile,e.ReleaseId,e.Mode)).Any(g=>g.Count()!=1))
   throw new InvalidDataException("Operator discovery requires unique release registrations.");
  var result=new List<(SubmissionAutomationRegistration,SubmissionAutomationSettings)>();
  foreach(var entry in selected) {
   if(entry.ReleaseId<=0 || !Enum.IsDefined(entry.Mode))throw new InvalidDataException("Invalid registered release selector.");
   // The same pinned-byte reader used by the worker; no mutable settings re-read.
   var settings=AutomationRequest.ReadForCheck(entry.SettingsPath,entry.SettingsSha256).Settings;
   if(settings.Release.ReleaseId!=entry.ReleaseId || settings.Mode!=entry.Mode)
    throw new InvalidDataException("Registered release differs from its pinned settings.");
   AutomationOperatorBindings.Validate(settings);
   result.Add((entry,settings));
  }
  return result;
 }
}
