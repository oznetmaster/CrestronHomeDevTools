// Copyright (c) 2026 Neil Colvin. Licensed under the MIT License. See LICENSE.
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CrestronHomeDevTools;

/// <summary>Preserves Home's copy/paste payload without reconstructing its programming model.</summary>
public static class ConfigurationProgramming
{
    /// <summary>Compares copy DTOs, ignoring only regenerated block and command-step identifiers.</summary>
    /// <remarks>Device identifiers, operations, arguments, mode settings and unknown fields remain significant.
    /// The initial implementation supports flat command sequences; other sequence layouts are rejected.</remarks>
    public static bool SemanticallyEqual(JsonElement expected, JsonElement actual)
        => JsonNode.DeepEquals(Normalize(expected), Normalize(actual));

    /// <summary>Checks the supported copy format before any existing device is removed.</summary>
    public static void Validate(JsonElement copy) => _ = Normalize(copy);

    private static JsonNode? Normalize(JsonElement copy)
    {
        if (copy.GetRawText().Length > 16 * 1024 * 1024) throw new InvalidDataException("Programming exceeds its limit.");
        var root = JsonNode.Parse(copy.GetRawText()) as JsonObject ?? throw new InvalidDataException("Programming copy is missing.");
        if (root["ProgrammableEvents"] is not JsonArray events || events.Count != 1)
            throw new InvalidDataException("Only a single copied event can be restored.");
        foreach (var item in events)
        {
            if (item?["Programming"] is not JsonObject programming ||
                programming["Mode"]?["Type"]?.GetValue<string>() != "Sequence" ||
                programming["TriggerProgrammingData"] is not JsonArray triggers || triggers.Count != 1)
                throw new InvalidDataException("Only captured sequence programming is supported for replacement.");
            foreach (var trigger in triggers)
            {
                if (trigger is not JsonObject entry || entry["TriggerGesture"] is not JsonObject ||
                    entry["ProgrammingData"] is not JsonValue data || !data.TryGetValue<string>(out var serialized))
                    throw new InvalidDataException("Programming trigger data is incomplete.");
                var nested = JsonNode.Parse(serialized) as JsonObject ?? throw new InvalidDataException("Invalid programming data.");
                if (nested["ModeActionItems"] is not JsonArray actions || actions.Count != 0 ||
                    nested["SequenceProgrammingBlockJson"] is not JsonValue blockData || !blockData.TryGetValue<string>(out var blockJson))
                    throw new InvalidDataException("Programming layout is unsupported.");
                var block = JsonNode.Parse(blockJson) as JsonObject ?? throw new InvalidDataException("Sequence block is missing.");
                if (block["Id"] is not JsonValue || block["ProgrammableEntity"] is not JsonObject ||
                    block["EventId"] is not JsonValue || block["IsNested"]?.GetValue<bool>() != false || block["Steps"] is not JsonArray steps)
                    throw new InvalidDataException("Sequence block layout is unsupported.");
                block.Remove("Id");
                foreach (var step in steps)
                {
                    if (step is not JsonObject command || command["Type"]?.GetValue<int>() != 0 ||
                        command["Id"] is not JsonValue || command["Target"] is not JsonObject ||
                        command["Operation"] is not JsonObject || command["Arguments"] is not JsonArray)
                        throw new InvalidDataException("Only flat command sequences are supported for replacement.");
                    command.Remove("Id");
                }
                // Replace encoded JSON by nodes for comparison: whitespace and object order are immaterial.
                nested["SequenceProgrammingBlockJson"] = block;
                entry["ProgrammingData"] = nested;
            }
        }
        return root;
    }
}
