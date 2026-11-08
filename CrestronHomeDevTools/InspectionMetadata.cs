// Copyright (c) 2026 Neil Colvin. MIT license.
using System.Collections.ObjectModel;
using System.Text.Json;

namespace CrestronHomeDevTools;

internal static class InspectionMetadata
    {
    internal static readonly JsonSerializerOptions Json = new () { PropertyNameCaseInsensitive = true };

    // Decode only known metadata containers, never arbitrary string-valued device properties.
    internal static JsonElement? Array (DeviceInfo device, string property, out bool advertised)
        {
        advertised = device.PropertyValues.TryGetValue (property, out var value);
        if (!advertised || value.ValueKind == JsonValueKind.Null) return null;
        try
            {
            if (value.ValueKind == JsonValueKind.String)
                {
                string text = value.GetString ()!;
                if (text.Length > 1024 * 1024) throw new InvalidDataException ();
                using var document = JsonDocument.Parse (text, new () { MaxDepth = 64 });
                value = document.RootElement.Clone ();
                if (value.ValueKind == JsonValueKind.Null) return null;
                }
            if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength () > 4096 || value.GetRawText ().Length > 1024 * 1024) throw new InvalidDataException ();
            return value;
            }
        catch (Exception error) when (error is JsonException or InvalidDataException)
            { throw new InvalidDataException ("Advertised inspection metadata has an unsupported layout; values are withheld."); }
        }

    internal static IReadOnlyDictionary<string, JsonElement> Properties (JsonElement value)
        {
        if (value.ValueKind != JsonValueKind.Object) throw new InvalidDataException ("Managed property values must be an object or null.");
        var result = new Dictionary<string, JsonElement> (StringComparer.Ordinal);
        foreach (var p in value.EnumerateObject ())
            if (string.IsNullOrWhiteSpace (p.Name) || p.Name.Length > 2048 || result.Count >= 4096 || !result.TryAdd (p.Name, p.Value.Clone ()))
                throw new InvalidDataException ("Managed property identifiers are invalid, duplicated or exceed the supported bound.");
        return new ReadOnlyDictionary<string, JsonElement> (result);
        }

    internal static ManagedPropertySet PropertySet (JsonElement value, bool includeValues)
        {
        if (value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) return new (false, System.Array.Empty<string> (), null);
        var properties = Properties (value);
        return new (true, System.Array.AsReadOnly (properties.Keys.ToArray ()), includeValues ? properties : null);
        }
    }
