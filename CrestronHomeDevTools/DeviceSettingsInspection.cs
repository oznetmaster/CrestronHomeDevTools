// Copyright (c) 2026 Neil Colvin. MIT license.
using System.Text.Json;

namespace CrestronHomeDevTools;

public sealed record DeviceSettingGroup (string Id, string? Name, string? Parent);

/// <summary>Inspection only. Value inclusion is explicit; metadata does not authorize calling Command.</summary>
public sealed record DeviceSetting (string Property, string? Parent, string? Description, string? Command,
    bool CommandAdvertised, bool? IsAvailable, bool? ReadOnly, string? RenderHint,
    bool HasValue, string? ObservedValueKind, JsonElement? Value);

/// <summary>Missing/null metadata is distinct from an advertised empty list. Values can contain private information.</summary>
public sealed record DeviceSettingsSnapshot (int DeviceId, bool GroupsAdvertised, bool GroupsAvailable,
    bool ItemsAdvertised, bool ItemsAvailable, bool ValuesIncluded,
    IReadOnlyList<DeviceSettingGroup> Groups, IReadOnlyList<DeviceSetting> Items);

public static class DeviceSettingsInspection
    {
    public static async Task<DeviceSettingsSnapshot> GetAsync (ConfigurationClient client, int deviceId,
        bool includeValues = false, CancellationToken cancellationToken = default)
        {
        ArgumentNullException.ThrowIfNull (client);
        var device = await client.GetDeviceAsync (deviceId, cancellationToken).ConfigureAwait (false)
            ?? throw new InvalidOperationException ("The settings target was not found.");
        if (device.Id != deviceId) throw new InvalidOperationException ("The settings target identity changed.");
        return FromDevice (device, includeValues);
        }

    internal static DeviceSettingsSnapshot FromDevice (DeviceInfo device, bool includeValues)
        {
        var groups = InspectionMetadata.Array (device, "deviceSettings:groups", out bool groupsAdvertised);
        var items = InspectionMetadata.Array (device, "deviceSettings:items", out bool itemsAdvertised);
        try
            {
            var parsedGroups = groups?.Deserialize<Group[]> (InspectionMetadata.Json) ?? [];
            var parsedItems = items?.Deserialize<Item[]> (InspectionMetadata.Json) ?? [];
            var groupIds = new HashSet<string> (StringComparer.Ordinal);
            if (parsedGroups.Any (g => g == null || string.IsNullOrWhiteSpace (g.Id) || g.Id.Length > 2048 || !groupIds.Add (g.Id))) throw new InvalidDataException ();
            var itemIds = new HashSet<(string?, string)> ();
            var result = new List<DeviceSetting> ();
            foreach (var item in parsedItems)
                {
                if (item == null || string.IsNullOrWhiteSpace (item.Property) || item.Property.Length > 2048 || !itemIds.Add ((item.Parent, item.Property))) throw new InvalidDataException ();
                bool hasValue = device.PropertyValues.TryGetValue (item.Property, out var value);
                result.Add (new (item.Property, item.Parent, item.Description, item.Command,
                    item.Command != null && device.Commands.Contains (item.Command, StringComparer.Ordinal),
                    item.IsAvailable, item.ReadOnly, RenderHint (item.RenderHint), hasValue,
                    hasValue ? value.ValueKind.ToString () : null, includeValues && hasValue ? value.Clone () : null));
                }
            return new (device.Id, groupsAdvertised, groups != null, itemsAdvertised, items != null, includeValues,
                System.Array.AsReadOnly (parsedGroups.Select (g => new DeviceSettingGroup (g.Id!, g.Name, g.Parent)).ToArray ()), result.AsReadOnly ());
            }
        catch (Exception error) when (error is JsonException or InvalidDataException or InvalidOperationException)
            { throw new InvalidDataException ("Dynamic settings metadata is invalid or ambiguous; private values are withheld."); }
        }

    private static string? RenderHint (JsonElement value)
        => value.ValueKind switch
            {
            JsonValueKind.Undefined or JsonValueKind.Null => null,
            JsonValueKind.String => value.GetString (),
            JsonValueKind.Number => value.GetRawText (), // Preserve unknown enum values; do not infer type/range from a render hint.
            _ => throw new InvalidDataException ()
            };
    private sealed record Group (string? Id, string? Name, string? Parent);
    private sealed record Item (string? Property, string? Parent, string? Description, string? Command,
        bool? IsAvailable, bool? ReadOnly, JsonElement RenderHint);
    }
