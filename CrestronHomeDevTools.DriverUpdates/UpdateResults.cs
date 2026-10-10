// Copyright (c) 2026 Neil Colvin. Licensed under the MIT License.
using System.Text.Json;
namespace CrestronHomeDevTools.DriverUpdates;
internal static class UpdateResults
    {
    // Cleanup can fail after successful updates. Preserve their durable per-driver results.
    internal static async Task RestoreAsync (IEnumerable<UpdateRow> rows, string journal)
        {
        var steps = new Dictionary<string, DriverUpdateStep> (StringComparer.Ordinal);
        var submitted = new HashSet<string> (StringComparer.Ordinal);
        bool unreadable = false;
        try
            {
            if (Directory.Exists (journal))
                {
                foreach (var file in Directory.GetFiles (journal, "*-intent.json"))
                    {
                    var intent = JsonSerializer.Deserialize<AvailableDriverUpdate> (await File.ReadAllTextAsync (file));
                    if (intent != null) submitted.Add (intent.DriverId);
                    }
                foreach (var file in Directory.GetFiles (journal, "*-completed.json"))
                    {
                    var step = JsonSerializer.Deserialize<DriverUpdateStep> (await File.ReadAllTextAsync (file));
                    if (step != null) steps[step.DriverId] = step;
                    }
                var result = Path.Combine (journal, "result.json");
                if (File.Exists (result))
                    foreach (var step in JsonSerializer.Deserialize<DriverUpdateBatchResult> (await File.ReadAllTextAsync (result))?.Steps ?? [])
                        steps[step.DriverId] = step;
                }
            }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { unreadable = true; }
        foreach (var row in rows)
            {
            steps.TryGetValue (row.DriverId, out var step);
            if (step == null && row.Result == "Updated") step = new (row.DriverId, "Updated", null, "Completion verified before the later error.");
            if (step == null && (submitted.Contains (row.DriverId) || row.Result is "Updating" or "Restarting" or "Verifying"))
                step = new (row.DriverId, "Unconfirmed", null, "Update may have started. Inspect this run before retrying.");
            row.Finish (step, unreadable);
            }
        }
    }
