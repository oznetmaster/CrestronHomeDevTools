// Copyright (c) 2026 Neil Colvin. MIT licensed.
using System.Text.Json;

namespace CrestronHomeDevTools;

public sealed record DriverReadinessIssue(int DeviceId, string? Name, string Reason);
public sealed record DriverReadinessReport(DateTimeOffset ObservedUtc, int RootDeviceId,
    int[] InspectedDeviceIds, DriverReadinessIssue[] Issues)
{
    public bool Ready => Issues.Length == 0;
}

/// <summary>Read-only preflight of an installed driver and its installed descendants.
/// Discovery advertisements are not installations. No configuration values are returned or changed.</summary>
public static class DriverReadiness
{
    public static async Task<DriverReadinessReport> InspectAsync(ConfigurationClient client, int rootDeviceId,
        string expectedModel, string expectedVersion, IReadOnlyDictionary<int, string[]>? requiredCommands = null,
        CancellationToken cancellationToken = default)
        => Inspect(await client.GetDevicesAsync(cancellationToken).ConfigureAwait(false), rootDeviceId,
            expectedModel, expectedVersion, requiredCommands);

    public static DriverReadinessReport Inspect(IReadOnlyList<DeviceInfo> inventory, int rootDeviceId,
        string expectedModel, string expectedVersion, IReadOnlyDictionary<int, string[]>? requiredCommands = null)
    {
        if (rootDeviceId <= 0 || string.IsNullOrWhiteSpace(expectedModel) || !Version.TryParse(expectedVersion, out _))
            throw new ArgumentException("An exact installed root, model and version are required.");
        if (inventory.Select(d => d.Id).Distinct().Count() != inventory.Count)
            throw new InvalidDataException("Device inventory contains duplicate identities.");
        var issues = new List<DriverReadinessIssue>();
        var root = inventory.SingleOrDefault(d => d.Id == rootDeviceId);
        var ids = new HashSet<int> { rootDeviceId };
        int count;
        do { count = ids.Count; foreach (var d in inventory) if (d.ParentDeviceId is int parent && ids.Contains(parent)) ids.Add(d.Id); }
        while (count != ids.Count);
        void Issue(DeviceInfo d, string reason) => issues.Add(new(d.Id, d.Name, reason));
        if (root == null) issues.Add(new(rootDeviceId, null, "root-missing"));
        else if (root.Model != expectedModel) Issue(root, "root-model-changed");
        foreach (var d in inventory.Where(d => ids.Contains(d.Id)))
        {
            bool native = Text(d, "lightType:variant") == "load";
            bool wrapper = !native && d.LocationId == null && d.PropertyValues.ContainsKey("platform:managedDevices") && d.Id != rootDeviceId;
            if (wrapper && inventory.Count(child => child.ParentDeviceId == d.Id && Text(child, "lightType:variant") == "load") != 1)
                Issue(d, "native-light-load-missing-or-ambiguous");
            if (!native)
            {
                if (Text(d, "cp.driverConfiguration:driverLoadingStatus") != "Loaded") Issue(d, "driver-not-loaded");
                if (!True(d, "onlineIndicator:isOnline")) Issue(d, "device-offline-or-unconfirmed");
            }
            if (!native && !wrapper)
            {
                if (!True(d, "cp.driverConfiguration:isConfigured")) Issue(d, "configuration-required-or-unconfirmed");
                if (!True(d, "readyIndicator:isReady")) Issue(d, "device-not-ready");
            }
            if ((!native && !wrapper) || d.PropertyValues.ContainsKey("cp.driverInformation:version"))
                if (!DriverVersions.Equal(Text(d, "cp.driverInformation:version"), expectedVersion)) Issue(d, "driver-version-changed-or-unconfirmed");
            if (native)
            {
                if (!d.Commands.Contains("lightDimmer:setLevel")) Issue(d, "native-light-control-missing");
                if (!d.PropertyValues.TryGetValue("lightDimmer:level", out var level) || level.ValueKind != JsonValueKind.Number ||
                    !level.TryGetDouble(out double value) || !double.IsFinite(value) || value < 0 || value > 1)
                    Issue(d, "native-light-state-unconfirmed");
            }
            if (d.PropertyValues.TryGetValue("cp.driverConfiguration:configurationItems", out var items) && items.ValueKind == JsonValueKind.Array &&
                items.EnumerateArray().Any(i => i.ValueKind == JsonValueKind.Object &&
                    new[] { "IsNew", "IsReviewNeeded" }.Any(k => i.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.True)))
                Issue(d, "configuration-review-required");
        }
        foreach (var expected in requiredCommands ?? new Dictionary<int, string[]>())
        {
            var device = inventory.SingleOrDefault(d => d.Id == expected.Key && ids.Contains(d.Id));
            if (device == null) issues.Add(new(expected.Key, null, "expected-device-missing-from-tree"));
            else foreach (string command in expected.Value)
                if (!device.Commands.Contains(command)) Issue(device, "required-control-missing:" + command);
        }
        return new(DateTimeOffset.UtcNow, rootDeviceId, inventory.Where(d => ids.Contains(d.Id)).Select(d => d.Id).Order().ToArray(), issues.ToArray());
    }

    private static bool True(DeviceInfo device, string key) => device.PropertyValues.TryGetValue(key, out var v) && v.ValueKind == JsonValueKind.True;
    private static string? Text(DeviceInfo device, string key) => device.PropertyValues.TryGetValue(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
