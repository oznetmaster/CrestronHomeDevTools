// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CrestronHomeDevTools;

[JsonConverter (typeof (JsonStringEnumConverter<IoPortConnectionFormat>))]
public enum IoPortConnectionFormat { Unknown, Legacy, Typed }

/// <summary>A decoded connection. Legacy values contain no port type.</summary>
public sealed record IoPortConnection (int DeviceId, string? PortType, uint PortNumber);

public static class IoPortConnections
    {
    /// <summary>Selects only a known protocol family; missing or future versions remain unknown.</summary>
    public static IoPortConnectionFormat FormatForVersion (string? homeVersion)
        => Version.TryParse (homeVersion, out var v) && v.Major == 4 ? v.Minor switch
            {
            >= 7 and <= 12 => IoPortConnectionFormat.Legacy,
            13 => IoPortConnectionFormat.Typed,
            _ => IoPortConnectionFormat.Unknown
            } : IoPortConnectionFormat.Unknown;

    /// <summary>Reads either wire format; future type identifiers are preserved without authorizing writes.</summary>
    public static IoPortConnection Parse (string value)
        {
        ArgumentNullException.ThrowIfNull (value);
        var parts = value.Split ('|');
        if (parts.Length is not (2 or 3) || !int.TryParse (parts[0], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var device)
            || !uint.TryParse (parts[^1], NumberStyles.None, CultureInfo.InvariantCulture, out var port)
            || (parts.Length == 3 && !ValidType (parts[1])))
            throw new FormatException ("An I/O connection must contain a device ID, optional port type and unsigned port number.");
        return new (device, parts.Length == 3 ? CanonicalType (parts[1]) : null, port);
        }

    /// <summary>Formats an explicitly selected port. Use PrepareValueAsync to verify it against live metadata.</summary>
    public static string Format (IoPortConnection connection, IoPortConnectionFormat format)
        {
        ArgumentNullException.ThrowIfNull (connection);
        if (connection.PortType != null && !KnownType (CanonicalType (connection.PortType)))
            throw new InvalidOperationException ("The selected port type is not supported for writing.");
        string id = connection.DeviceId.ToString (CultureInfo.InvariantCulture), port = connection.PortNumber.ToString (CultureInfo.InvariantCulture);
        return format switch
            {
            IoPortConnectionFormat.Legacy => id + "|" + port,
            IoPortConnectionFormat.Typed when connection.PortType != null && KnownType (CanonicalType (connection.PortType))
                => id + "|" + CanonicalType (connection.PortType) + "|" + port,
            _ => throw new InvalidOperationException ("The port type or processor's connection format is not confirmed.")
            };
        }

    /// <summary>Prepares one configuration value using current identity, item usage and advertised port metadata.</summary>
    /// <remarks>Read-only: never starts a wizard, applies configuration or rewrites unrelated string values.</remarks>
    public static async Task<string> PrepareValueAsync (ConfigurationClient client, DriverInstanceReady target,
        string itemId, IoPortConnection selection, CancellationToken cancellationToken = default)
        {
        ArgumentNullException.ThrowIfNull (client);
        ArgumentNullException.ThrowIfNull (target);
        ArgumentNullException.ThrowIfNull (selection);
        ArgumentException.ThrowIfNullOrWhiteSpace (itemId);
        if (target.DeviceId <= 0 || string.IsNullOrWhiteSpace (target.Model) || !Version.TryParse (target.Version, out _))
            throw new ArgumentException ("The expected driver identity is required.");
        var report = await client.GetCapabilitiesAsync (cancellationToken).ConfigureAwait (false);
        if (report.PortConnectionFormat == IoPortConnectionFormat.Unknown)
            throw new InvalidOperationException ("The processor version has no confirmed port connection format.");
        var device = await client.GetDeviceAsync (target.DeviceId, cancellationToken).ConfigureAwait (false);
        if (device == null || device.Id != target.DeviceId || device.Model != target.Model
            || !device.PropertyValues.TryGetValue ("cp.driverInformation:version", out var version)
            || version.ValueKind != JsonValueKind.String || !DriverVersions.Equal (version.GetString (), target.Version))
            throw new InvalidOperationException ("The configuration target identity changed.");
        if (!device.PropertyValues.TryGetValue ("cp.driverConfiguration:configurationItems", out var items) || items.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException ("Configuration item metadata is unavailable. No wizard was started.");
        var matches = items.EnumerateArray ().Where (i => String (i, "Id") == itemId).ToArray ();
        if (matches.Length != 1 || !Object (matches[0], "Value", out var metadata)
            || !metadata.TryGetProperty ("ReadOnly", out var readOnly) || readOnly.ValueKind != JsonValueKind.False)
            throw new InvalidOperationException ("The configuration item is unknown, ambiguous or not confirmed writable.");
        string? type = selection.PortType == null ? null : CanonicalType (selection.PortType);
        if (type == null || !KnownType (type) || !MatchesUsage (String (matches[0], "UsageContext"), type))
            throw new InvalidOperationException ("Select a known port type matching the item's usage; CEC direction must be explicit.");
        var providers = report.Devices.Where (d => d.DeviceId == selection.DeviceId).ToArray ();
        if (providers.Length != 1 || providers[0].IoPorts?.Count (p => p.Type == type && p.PortNumber == selection.PortNumber) != 1)
            throw new InvalidOperationException ("The selected port is not uniquely advertised by its provider.");
        if (report.PortConnectionFormat == IoPortConnectionFormat.Legacy
            && String (matches[0], "UsageContext") == "cp.driverConfigurationUsageContext:cec.port"
            && providers[0].IoPorts!.Count (p => p.PortNumber == selection.PortNumber && p.Type is "CecInput" or "CecOutput") != 1)
            throw new InvalidOperationException ("The legacy connection cannot preserve this ambiguous CEC direction.");
        return Format (selection with { PortType = type }, report.PortConnectionFormat);
        }

    internal static string CanonicalType (string type) => type.Equals ("Com", StringComparison.OrdinalIgnoreCase) ? "Serial" : type;
    private static bool ValidType (string type) => type.Length is > 0 and <= 64 && char.IsAsciiLetter (type[0]) && type.All (char.IsAsciiLetterOrDigit);
    private static bool KnownType (string type) => type is "IrOutput" or "Serial" or "Relay" or "Versiport" or "CecInput" or "CecOutput";
    private static bool MatchesUsage (string? usage, string type) => usage switch
        {
        "cp.driverConfigurationUsageContext:ir.port" => type == "IrOutput",
        "cp.driverConfigurationUsageContext:com.port" => type == "Serial",
        "cp.driverConfigurationUsageContext:cecInput.port" => type == "CecInput",
        "cp.driverConfigurationUsageContext:cecOutput.port" => type == "CecOutput",
        "cp.driverConfigurationUsageContext:cec.port" => type is "CecInput" or "CecOutput",
        _ => false
        };
    private static bool Object (JsonElement element, string key, out JsonElement value)
        {
        value = default;
        return element.ValueKind == JsonValueKind.Object && element.TryGetProperty (key, out value) && value.ValueKind == JsonValueKind.Object;
        }
    private static string? String (JsonElement element, string key)
        => element.ValueKind == JsonValueKind.Object && element.TryGetProperty (key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString () : null;
    }
