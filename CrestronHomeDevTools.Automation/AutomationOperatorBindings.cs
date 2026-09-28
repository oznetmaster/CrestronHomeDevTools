// Copyright (c) 2026 Neil Colvin. MIT licensed.
using System.Text.Json;

namespace CrestronHomeDevTools.Automation;

/// <summary>Validate the public inbox contract before any stage can publish physical-action requests.
/// Does not alter fixture inputs, create an inbox, or grant action/signing authority.</summary>
internal static class AutomationOperatorBindings
{
 internal static void Validate(SubmissionAutomationSettings settings,bool releaseTemplate=false)
 {
  var declared=settings.OperatorInbox;
  bool Key(string key)=>releaseTemplate && key=="${runKey}" || key==SubmissionWorkflow.RunKey(settings.Release);
  string DirectoryPath(string value) {
   if(string.IsNullOrWhiteSpace(value))throw new InvalidDataException("Operator inbox requires an absolute directory.");
   string expanded=releaseTemplate?value.Replace("${run}",Path.Combine(Path.GetTempPath(),"operator-binding-template"),StringComparison.Ordinal):value;
   if(expanded.Contains("${",StringComparison.Ordinal) || !Path.IsPathFullyQualified(expanded))
    throw new InvalidDataException("Operator inbox requires an absolute directory or the release template's ${run} path.");
   return Path.TrimEndingDirectorySeparator(Path.GetFullPath(expanded));
  }
  string? directory=null;
  if(declared!=null) {
   if(!Key(declared.RunKey))throw new InvalidDataException("Operator inbox must use this exact release's run key.");
   directory=DirectoryPath(declared.Directory);
  }
  static bool Has(JsonElement value,string name)=>value.ValueKind==JsonValueKind.Object && value.EnumerateObject().Any(p=>p.Name.Equals(name,StringComparison.OrdinalIgnoreCase));
  void Walk(JsonElement value,string name) {
   // OperatorInbox is reserved for the protocol. Nested Inbox objects with Directory/RunKey
   // use the same protocol; unrelated application-specific inbox settings are left alone.
   bool binding=name.Equals("OperatorInbox",StringComparison.OrdinalIgnoreCase) ||
    name.Equals("Inbox",StringComparison.OrdinalIgnoreCase) && (Has(value,"Directory") || Has(value,"RunKey"));
   if(binding && value.ValueKind!=JsonValueKind.Null) {
    SubmissionOperatorInbox inbox;
    try {inbox=value.Deserialize<SubmissionOperatorInbox>(AutomationFiles.Json) ?? throw new JsonException();}
    catch(JsonException) {throw new InvalidDataException("Malformed fixture operator inbox binding.");}
    if(declared==null || inbox.RunKey!=declared.RunKey || !string.Equals(DirectoryPath(inbox.Directory),directory,
     OperatingSystem.IsWindows()?StringComparison.OrdinalIgnoreCase:StringComparison.Ordinal))
     throw new InvalidDataException("Every fixture operator inbox must match the workflow's declared directory and run key.");
    return;
   }
   if(value.ValueKind==JsonValueKind.Object)foreach(var property in value.EnumerateObject())Walk(property.Value,property.Name);
   else if(value.ValueKind==JsonValueKind.Array)foreach(var item in value.EnumerateArray())Walk(item,"");
  }
  foreach(var fixture in new[]{settings.InstalledAppFixtureSettings,settings.PreEnduranceFixtureSettings,settings.PostEnduranceFixtureSettings})
   if(fixture is {} value)Walk(value,"");
 }
}
