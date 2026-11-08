// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Text.Json;

namespace CrestronHomeDevTools;

/// <summary>Identification read from the authenticated processor's API root.</summary>
public sealed record ProcessorIdentity
    {
    public string? SystemType { get; init; }
    public string? FirmwareVersion { get; init; }
    public string? ProgramVersion { get; init; }
    public Dictionary<string, int>? ConfigurationSchema { get; init; }
    }

/// <summary>Optional connection capability; existing custom connections need not implement it.</summary>
public interface IProcessorIdentitySource
    {
    Task<ProcessorIdentity> ReadProcessorIdentityAsync (CancellationToken cancellationToken = default);
    }

/// <summary>A port advertised by a device, without its private configuration.</summary>
public sealed record IoPortDescriptor (string Type, uint PortNumber, string? Name);

/// <summary>Advertised identifiers only; property values and command parameters are excluded.</summary>
/// <param name="IoPorts">Null means unavailable or malformed metadata; an empty list means an advertised empty array.</param>
public sealed record DeviceCapabilities (int DeviceId, IReadOnlyList<string> Commands,
    IReadOnlyList<string> Properties, IReadOnlyList<IoPortDescriptor>? IoPorts);

/// <summary>A fresh read-only observation, not proof that an advertised command will succeed.</summary>
public sealed record ProcessorCapabilityReport (ProcessorIdentity? Identity, string? ReleaseLevel,
    IoPortConnectionFormat PortConnectionFormat, IReadOnlyList<DeviceCapabilities> Devices);

internal static class ProcessorCapabilities
    {
    internal static async Task<ProcessorCapabilityReport> ReadAsync (ConfigurationClient client,
        IProcessorIdentitySource? identitySource, CancellationToken token)
        {
        var identity = identitySource == null ? null : await identitySource.ReadProcessorIdentityAsync (token).ConfigureAwait (false);
        var devices = await client.GetDevicesAsync (token).ConfigureAwait (false);
        string? releaseLevel = null;
        var levels = devices.Select (d => Text (d, "cp.processorSoftwareUpdate:properties", "ReleaseLevel"))
            .Where (s => s != null).Distinct (StringComparer.Ordinal).ToArray ();
        if (levels.Length == 1) releaseLevel = levels[0];
        return new (identity, releaseLevel, IoPortConnections.FormatForVersion (identity?.ProgramVersion),
            devices.OrderBy (d => d.Id).Select (d => new DeviceCapabilities (d.Id,
                d.Commands.Distinct (StringComparer.Ordinal).Order (StringComparer.Ordinal).ToArray (),
                d.PropertyValues.Keys.Order (StringComparer.Ordinal).ToArray (), ReadPorts (d))).ToArray ());
        }

    internal static IReadOnlyList<IoPortDescriptor>? ReadPorts (DeviceInfo device)
        {
        if (!device.PropertyValues.TryGetValue ("cp.ioPorts", out var value) || value.ValueKind != JsonValueKind.Array)
            return null;
        try
            {
            var ports = value.Deserialize<AdvertisedPort[]> ();
            if (ports == null || ports.Any (p => p == null || string.IsNullOrWhiteSpace (p.Type) || p.PortNumber == null))
                return null;
            return ports.Select (p => new IoPortDescriptor (IoPortConnections.CanonicalType (p.Type!), p.PortNumber!.Value, p.Name)).ToArray ();
            }
        catch (JsonException) { return null; }
        }

    private sealed record AdvertisedPort (string? Type, uint? PortNumber, string? Name);
    private static string? Text (DeviceInfo device, string key, string field)
        => device.PropertyValues.TryGetValue (key, out var property) && property.ValueKind == JsonValueKind.Object
            && property.TryGetProperty (field, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString () : null;
    }
