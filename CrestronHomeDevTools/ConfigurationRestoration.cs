// Copyright (c) 2026 Neil Colvin. Licensed under the MIT License. See LICENSE.
using System.Text.Json;

namespace CrestronHomeDevTools;

public sealed record ConfigurationRestorationWrite(string ResourcePath, string Command, JsonElement Parameters,
    string VerificationCommand, JsonElement VerificationParameters, JsonElement Expected);

/// <summary>Plans and verifies the supported scene and programming reference repairs.</summary>
public static class ConfigurationRestoration
{
    /// <summary>Creates writes for changed references and rejects unsupported references before removal.</summary>
    /// <remarks>This is deliberately narrower than a whole-system restore. Other configuration resources
    /// must have no references to the replaced identifiers. Pass the same captured graph to the later restore.</remarks>
    public static IReadOnlyList<ConfigurationRestorationWrite> Plan(ValidatedConfigurationCapture capture, IReadOnlyDictionary<int,int> deviceIds)
    {
        ArgumentNullException.ThrowIfNull(capture);
        var writes = new List<ConfigurationRestorationWrite>();
        foreach (var entry in capture.Values)
        {
            var source = entry.Entry;
            bool scene = source.Command == "cp.scene:getCurrentTargets";
            bool programming = source.Command == "cp.programming:copyProgramming";
            // Inventory and enumeration contain identity declarations, not restoration definitions.
            bool ancillary = source.Command == null && source.ResourcePath is "v2/Groups" or "v2/QuickActions" or "v2/Media";
            bool sceneDetails = source.Command == "cp.scene:getSceneDetails";
            if (!scene && !programming && !ancillary && !sceneDetails) continue;
            var remap = ConfigurationReferenceRemapper.Remap(entry.Value, deviceIds);
            if (remap.UnsupportedReferences.Count != 0)
                throw new InvalidDataException("A configuration reference cannot be safely remapped: " + source.File);
            if (remap.Changes.Count == 0) continue;
            if (ancillary || sceneDetails) throw new InvalidDataException("An affected configuration resource has no validated restoration operation: " + source.File);
            if (scene)
            {
                if (!remap.Value.TryGetProperty("Targets", out var targets) || targets.ValueKind != JsonValueKind.Array)
                    throw new InvalidDataException("Scene target layout is unsupported.");
                writes.Add(new(source.ResourcePath, "cp.scene:updateSceneTargets", JsonSerializer.SerializeToElement(new { targets }),
                    source.Command!, source.Parameters ?? JsonSerializer.SerializeToElement(new { }), remap.Value));
            }
            else
            {
                ConfigurationProgramming.Validate(entry.Value);
                if (source.Parameters is not JsonElement context) throw new InvalidDataException("Programming context is missing.");
                var mappedContext = ConfigurationReferenceRemapper.Remap(context, deviceIds);
                if (mappedContext.UnsupportedReferences.Count != 0) throw new InvalidDataException("Programming context contains an unsupported reference.");
                var entity = mappedContext.Value.GetProperty("sourceEntity");
                var eventInfo = mappedContext.Value.GetProperty("sourceEvent");
                var gesture = mappedContext.Value.GetProperty("sourceGesture");
                var modes = capture.Values.Single(v => v.Entry.Command == "cp.programming:getAvailableModes" &&
                    v.Entry.Parameters is JsonElement p && JsonElement.DeepEquals(p.GetProperty("programmableEntity"),context.GetProperty("sourceEntity")) &&
                    JsonElement.DeepEquals(p.GetProperty("eventInfo"),context.GetProperty("sourceEvent"))).Value;
                if (modes.ValueKind != JsonValueKind.Array || modes.EnumerateArray().Count(m => m.GetProperty("Type").GetString() == "None") != 1)
                    throw new InvalidDataException("The affected event cannot be cleared before restoring its sequence.");
                writes.Add(new("v2/Programming", "cp.programming:pasteProgramming", JsonSerializer.SerializeToElement(new {
                    targetEntity = entity, targetEvent = eventInfo, targetGesture = gesture, copyPasteDto = remap.Value, pasteOptions = Array.Empty<string>() }),
                    "cp.programming:copyProgramming", mappedContext.Value, remap.Value));
            }
        }
        return writes;
    }

    /// <summary>Writes each planned definition once, retaining durable intent and readback before continuing.</summary>
    /// <remarks>The caller owns the processor lease and has revalidated identity and the captured graph.
    /// Existing journals are never replayed. Failure requires inspection; this method does not retry writes.</remarks>
    public static async Task ApplyAsync(ConfigurationClient client, IReadOnlyList<ConfigurationRestorationWrite> writes,
        string newPrivateJournal, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(client); ArgumentNullException.ThrowIfNull(writes);
        string folder = Path.GetFullPath(newPrivateJournal);
        if (Directory.Exists(folder) || File.Exists(folder)) throw new IOException("Restoration requires a new journal.");
        Directory.CreateDirectory(folder);
        void Record(string name, object? value)
        {
            using var stream = new FileStream(Path.Combine(folder, name + ".json"), FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            JsonSerializer.Serialize(stream, value); stream.Flush(true);
        }
        Record("request", writes);
        for (int i = 0; i < writes.Count; i++)
        {
            var write = writes[i]; string prefix = (i + 1).ToString("D4");
            if (write.Command is not ("cp.scene:updateSceneTargets" or "cp.programming:pasteProgramming") ||
                write.Command == "cp.scene:updateSceneTargets" && write.VerificationCommand != "cp.scene:getCurrentTargets" ||
                write.Command == "cp.programming:pasteProgramming" && write.VerificationCommand != "cp.programming:copyProgramming")
                throw new InvalidDataException("Unsupported restoration operation.");
            Record(prefix + "-intent", new { Utc = DateTimeOffset.UtcNow, Write = write });
            if (write.Command == "cp.programming:pasteProgramming")
            {
                // Home paste appends to an existing sequence. Clearing the destination first is
                // necessary even when removal left an invalid reference in that event.
                var context = new { programmableEntity = write.Parameters.GetProperty("targetEntity"), eventInfo = write.Parameters.GetProperty("targetEvent") };
                var modes = await client.ExecuteConfigurationCommandAsync("v2/Programming", "cp.programming:getAvailableModes", context, token).ConfigureAwait(false);
                if (modes is not { ValueKind: JsonValueKind.Array }) throw new InvalidDataException("Available programming modes are missing.");
                var none = modes.Value.EnumerateArray().Single(m => m.GetProperty("Type").GetString() == "None");
                Record(prefix + "-clear-intent", new { context.programmableEntity, context.eventInfo, mode = none, Utc = DateTimeOffset.UtcNow });
                var cleared = await client.ExecuteConfigurationCommandAsync("v2/Programming", "cp.programming:setMode", new { context.programmableEntity, context.eventInfo, mode = none }, token).ConfigureAwait(false);
                Record(prefix + "-clear-response", cleared);
                var mode = await client.ExecuteConfigurationCommandAsync("v2/Programming", "cp.programming:getMode", context, token).ConfigureAwait(false);
                Record(prefix + "-clear-readback", mode);
                if (mode?.GetProperty("Type").GetString() != "None") throw new InvalidDataException("The destination event was not cleared; paste was not attempted.");
            }
            var response = await client.ExecuteConfigurationCommandAsync(write.ResourcePath, write.Command, write.Parameters, token).ConfigureAwait(false);
            Record(prefix + "-response", response);
            var observed = await client.ExecuteConfigurationCommandAsync(write.ResourcePath, write.VerificationCommand, write.VerificationParameters, token).ConfigureAwait(false);
            Record(prefix + "-readback", observed);
            if (observed == null || !(write.Command == "cp.programming:pasteProgramming"
                ? ConfigurationProgramming.SemanticallyEqual(write.Expected, observed.Value) : JsonElement.DeepEquals(write.Expected, observed.Value)))
                throw new InvalidDataException("Configuration restoration readback differs from the saved definition; inspect the retained journal.");
        }
        Record("complete", new { Utc = DateTimeOffset.UtcNow, Writes = writes.Count });
    }
}
