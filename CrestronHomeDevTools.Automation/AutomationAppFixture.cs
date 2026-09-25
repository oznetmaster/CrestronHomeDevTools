// Copyright (c) 2026 Neil Colvin. MIT licensed.
using System.Text.Json;

namespace CrestronHomeDevTools.Automation;

internal static class AutomationAppFixture
{
 internal const string FileName="app-fixture-settings.json";
 internal static void Validate(SubmissionAutomationSettings settings) {
  if(settings.InstalledAppFixtureSettings is not {} fixture)return;
  if(fixture.ValueKind!=JsonValueKind.Object ||
   (settings.InstalledAppTests==null && settings.NUnit.AndroidTests==null))
   throw new InvalidDataException("App fixture settings require an object and a selected app-test route.");
 }
 internal static void Check(string root,SubmissionAutomationSettings settings,bool create) {
  Validate(settings);
  string path=Path.Combine(root,FileName);
  if(settings.InstalledAppFixtureSettings is not {} fixture) {
   if(File.Exists(path))throw new InvalidDataException("Unexpected app fixture settings.");
   return;
  }
  if(File.Exists(path)) {
   if(!SubmissionEvidence.SafeEvidencePath(root,FileName,out _) ||
    !File.ReadAllBytes(path).AsSpan().SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(fixture,AutomationFiles.Json)))
    throw new InvalidDataException("App fixture settings changed or are unsafe.");
  } else if(create)AutomationFiles.Write(path,fixture);
  else throw new InvalidDataException("App fixture settings disappeared.");
 }
}
