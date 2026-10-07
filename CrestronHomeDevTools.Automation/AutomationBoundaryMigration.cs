// Copyright (c) 2026 Neil Colvin. MIT licensed.
using System.Text.Json;
namespace CrestronHomeDevTools.Automation;

// Explicit maintenance operation only. Never called by a worker tick or test selection.
internal static class AutomationBoundaryMigration
{
 internal static int Command(AutomationRequest request,string checkpointPin) {
  var s=request.Settings;
  var state=SubmissionWorkflow.MigrateTestBoundary(s.PrivateRoot,s.Release,checkpointPin,c=>Verify(c,s));
  Console.WriteLine(JsonSerializer.Serialize(new{state.SchemaVersion,state.Stage,state.Status,state.OperationId,
   state.ReasonCode,OriginalCheckpointSha256=checkpointPin},AutomationFiles.Json));
  return 0;
 }
 internal static void Verify(SubmissionWorkflowStepContext context,SubmissionAutomationSettings settings) {
  // The workflow gate is held by the caller. Existing recovery commands use the same gate.
  // Any document/provider activity requires reconciliation with its pinned original worker.
  foreach(string entry in Directory.EnumerateFileSystemEntries(context.RunDirectory)) {
   string name=Path.GetFileName(entry);
   if(new[]{"review","sign","deliver","retain"}.Any(prefix=>name.StartsWith(prefix,StringComparison.OrdinalIgnoreCase)))
    throw new InvalidDataException("Document or delivery work already exists; retain the original worker boundary.");
  }
  // A completed replacement is reused only after the producer verifies its whole retained inventory,
  // including original failures, restoration, reservation release and the endurance binding.
  if(settings.PostEnduranceTests!=null)AutomationPostEndurance.VerifyRetained(context);
 }
}
