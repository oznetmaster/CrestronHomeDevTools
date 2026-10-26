// Copyright (c) 2026 Neil Colvin. Licensed under the MIT License. See LICENSE.
using System.Text.Json;

namespace CrestronHomeDevTools;

public sealed record ManagedChildReplacementPlan(ManagedDeviceRequest Request, int OldWrapperId, int OldLoadId,
    string CaptureSha256, int ReferenceWrites);
public sealed record ManagedChildReplacementResult(int OldWrapperId, int OldLoadId, int NewWrapperId, int NewLoadId,
    int RestoredDefinitions, string State);

/// <summary>Recommissions a reviewed native managed light while preserving supported user configuration.</summary>
/// <remarks>Recommissioning alone is not a missing-configuration repair. In the validated native-light case,
/// Home bypassed the generic wizard and created a new record without activation/store attributes. Old binding
/// records are not replayed. A separately validated missing-record repair and persistence verification are required.</remarks>
public static class ManagedChildReplacement
{
    public static ManagedChildReplacementPlan Plan(ValidatedConfigurationCapture capture, ManagedDeviceRequest request, int wrapperId)
    {
        ManagedDeviceCommissioning.Validate(request);
        var inventory = Inventory(capture);
        var wrapper = inventory.Single(d => d.Id == wrapperId);
        var load = ValidateExisting(inventory, request, wrapper);
        if (!inventory.Any(d => d.Id == capture.Manifest.Identity.ProcessorId && d.Model == capture.Manifest.Identity.Model))
            throw new InvalidDataException("The captured processor identity is missing.");
        // Default voice settings are the only settings validated by this first replacement workflow.
        // Reject customized settings before removal, rather than silently losing them.
        if (!load.PropertyValues.TryGetValue("cp.deviceVoiceControl:enabled", out var voice) || voice.ValueKind != JsonValueKind.True ||
            !load.PropertyValues.TryGetValue("cp.deviceVoiceControl:description", out var description) || description.GetString() != "")
            throw new InvalidDataException("Customized or unavailable voice settings require an additional restoration operation.");
        var placeholders = new Dictionary<int,int> { [wrapperId] = int.MaxValue, [load.Id] = int.MaxValue - 1 };
        if (inventory.Any(d => placeholders.Values.Contains(d.Id))) throw new InvalidDataException("Planning identifiers conflict with the inventory.");
        // Device properties can contain references outside scenes/programming. Do not ignore those.
        foreach (var device in inventory)
        {
            var remap = ConfigurationReferenceRemapper.Remap(JsonSerializer.SerializeToElement(device.PropertyValues), placeholders);
            if (remap.Changes.Count != 0 || remap.UnsupportedReferences.Count != 0)
                throw new InvalidDataException("A device property contains an unsupported replacement reference.");
        }
        var writes = ConfigurationRestoration.Plan(capture, placeholders);
        return new(request, wrapperId, load.Id, capture.ManifestSha256, writes.Count);
    }

    /// <summary>Removes and recreates one reviewed child, then restores supported references with its new IDs.</summary>
    /// <remarks>Hold the processor lease. The original capture must be revalidated immediately before this call.
    /// All outcomes are journaled; an interrupted journal is never automatically replayed. Returned state
    /// still requires a caller-controlled tree reload and persistence verification before customer success.</remarks>
    public static async Task<ManagedChildReplacementResult> ReplaceAsync(ConfigurationClient client,
        ValidatedConfigurationCapture original, ManagedChildReplacementPlan reviewedPlan, string newPrivateJournal,
        DriverConfiguration.Inputs? initialConfiguration, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client); ArgumentNullException.ThrowIfNull(original); ArgumentNullException.ThrowIfNull(reviewedPlan);
        if (timeout <= TimeSpan.Zero || timeout > TimeSpan.FromMinutes(10)) throw new ArgumentOutOfRangeException(nameof(timeout));
        if (Plan(original, reviewedPlan.Request, reviewedPlan.OldWrapperId) != reviewedPlan)
            throw new InvalidDataException("The reviewed replacement plan differs from its pinned capture.");
        string folder = Path.GetFullPath(newPrivateJournal);
        if (Directory.Exists(folder) || File.Exists(folder)) throw new IOException("Replacement requires a new private journal.");
        Directory.CreateDirectory(folder);
        void Record(string name, object? value)
        {
            using var stream = new FileStream(Path.Combine(folder, name + ".json"), FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            JsonSerializer.Serialize(stream, value); stream.Flush(true);
        }
        Record("request", reviewedPlan);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); deadline.CancelAfter(timeout);
        var token = deadline.Token;
        try
        {
            // Capture again while holding the lease. External Setup edits are not covered by that lease.
            var freshPath = Path.Combine(folder, "preflight");
            await LiveConfigurationCapture.CaptureAsync(client, original.Manifest.Identity, freshPath, token).ConfigureAwait(false);
            string freshDigest = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(Path.Combine(freshPath, "capture.json"))));
            var fresh = LiveConfigurationCapture.ReadValidated(freshPath, freshDigest);
            AssertUnchanged(original, fresh);
            var before = await client.GetDevicesAsync(token).ConfigureAwait(false);
            ValidateExisting(before, reviewedPlan.Request, before.Single(d => d.Id == reviewedPlan.OldWrapperId));
            Record("remove-intent", new { CommandDeviceId = reviewedPlan.OldLoadId, ExpectedRemovedIds = new[] { reviewedPlan.OldWrapperId, reviewedPlan.OldLoadId }, Utc = DateTimeOffset.UtcNow });
            var response = await client.ExecuteDeviceCommandAsync(reviewedPlan.OldLoadId, "cp.deviceConfiguration:setLocation", new { locationId = (int?)null }, token).ConfigureAwait(false);
            Record("remove-response", response);
            var removed = new HashSet<int> { reviewedPlan.OldWrapperId, reviewedPlan.OldLoadId };
            IReadOnlyList<DeviceInfo> after;
            do
            {
                after = await client.GetDevicesAsync(token).ConfigureAwait(false);
                if (after.All(d => !removed.Contains(d.Id))) break;
                await Task.Delay(200, token).ConfigureAwait(false);
            } while (true);
            if (after.Count != before.Count - 2 || before.Where(d => !removed.Contains(d.Id)).Any(old => !after.Any(now => Structure(old, now))))
                throw new InvalidDataException("Removal did not preserve the other device identities.");
            Record("removed", new { Utc = DateTimeOffset.UtcNow });
            string commissioning = Path.Combine(folder, "commissioning");
            var created = await ManagedDeviceCommissioning.CommissionAsync(client, reviewedPlan.Request, commissioning, timeout, token).ConfigureAwait(false);
            if (created.State == "ConfigurationRequired")
            {
                if (initialConfiguration == null) throw new InvalidDataException("The new child requires initial configuration; inspect the retained commissioning journal.");
                Record("initial-configuration-intent", new { DeviceId = created.DeviceId, Utc = DateTimeOffset.UtcNow });
                await ManagedDeviceCommissioning.ConfigureCreatedAsync(client, commissioning, initialConfiguration, timeout, token).ConfigureAwait(false);
                created = await ManagedDeviceCommissioning.ObserveCreatedAsync(client, commissioning, token).ConfigureAwait(false);
            }
            if (created.State != "Ready" || created.NativeLoadId is not int newLoad || newLoad == reviewedPlan.OldLoadId || created.DeviceId == reviewedPlan.OldWrapperId)
                throw new InvalidDataException("Fresh native child creation and configuration were not confirmed.");
            var map = new Dictionary<int,int> { [reviewedPlan.OldWrapperId] = created.DeviceId, [reviewedPlan.OldLoadId] = newLoad };
            Record("new-identities", map);
            var writes = ConfigurationRestoration.Plan(original, map);
            await ConfigurationRestoration.ApplyAsync(client, writes, Path.Combine(folder, "restoration"), token).ConfigureAwait(false);
            var final = await client.GetDevicesAsync(token).ConfigureAwait(false);
            var newNative = ValidateExisting(final, reviewedPlan.Request, final.Single(d => d.Id == created.DeviceId));
            if (newNative.Id != newLoad || before.Where(d => !removed.Contains(d.Id)).Any(old => !final.Any(now => Structure(old, now))) || final.Count != before.Count)
                throw new InvalidDataException("Final device identities differ from the replacement plan.");
            var result = new ManagedChildReplacementResult(reviewedPlan.OldWrapperId, reviewedPlan.OldLoadId, created.DeviceId, newLoad, writes.Count, "RecommissionedAwaitingPersistenceVerification");
            Record("result", result);
            return result;
        }
        catch (Exception error)
        {
            Record("failure", new { Utc = DateTimeOffset.UtcNow, Type = error.GetType().FullName });
            throw;
        }
    }

    private static IReadOnlyList<DeviceInfo> Inventory(ValidatedConfigurationCapture capture)
        => capture.Values.Single(v => v.Entry.ResourcePath == "v2/Devices" && v.Entry.Command == null).Value
            .Deserialize<Dictionary<string,DeviceInfo>>()?.Values.ToArray() ?? throw new InvalidDataException("Device capture is missing.");

    private static DeviceInfo ValidateExisting(IReadOnlyList<DeviceInfo> inventory, ManagedDeviceRequest request, DeviceInfo wrapper)
    {
        var parent = inventory.Single(d => d.Id == request.ParentId);
        if (parent.Model != request.ParentModel || !parent.PropertyValues.TryGetValue("cp.driverInformation:version", out var version) ||
            !DriverVersions.Equal(version.GetString(), request.ParentVersion) || !parent.Commands.Contains("cp.platformController:commissionManagedDevice") ||
            !parent.PropertyValues.TryGetValue("platform:managedDevices", out var advertised) || advertised.ValueKind != JsonValueKind.Array ||
            advertised.EnumerateArray().Count(x => x.GetProperty("Id").GetString() == request.ManagedDeviceId) != 1 ||
            wrapper.ParentDeviceId != request.ParentId || wrapper.LocationId != null || wrapper.Name != request.Name || wrapper.Model != request.ChildModel ||
            !wrapper.PropertyValues.TryGetValue("platform:managedDevices", out var managed) || managed.ValueKind != JsonValueKind.Array ||
            managed.GetArrayLength() != 1 || managed[0].GetProperty("Id").GetString() != request.ManagedDeviceId)
            throw new InvalidDataException("The reviewed native managed-light identity is not confirmed.");
        var loads = inventory.Where(d => d.ParentDeviceId == wrapper.Id).ToArray();
        if (loads.Length != 1 || loads[0].Name != request.Name || loads[0].Model != request.ChildModel || loads[0].LocationId != request.LocationId ||
            !loads[0].Commands.Contains("cp.deviceConfiguration:setLocation") || inventory.Any(d => d.ParentDeviceId == loads[0].Id) ||
            !loads[0].PropertyValues.TryGetValue("lightType:variant", out var variant) || variant.GetString() != "load")
            throw new InvalidDataException("Exactly one reviewed native load is required.");
        return loads[0];
    }

    private static bool Structure(DeviceInfo a, DeviceInfo b) => a.Id == b.Id && a.Name == b.Name && a.Model == b.Model && a.ParentDeviceId == b.ParentDeviceId && a.LocationId == b.LocationId;

    private static void AssertUnchanged(ValidatedConfigurationCapture before, ValidatedConfigurationCapture after)
    {
        var oldDevices = Inventory(before); var newDevices = Inventory(after);
        if (oldDevices.Count != newDevices.Count || oldDevices.Any(d => !newDevices.Any(n => Structure(d,n)))) throw new InvalidDataException("Device structure changed after review.");
        var oldValues = before.Values.Where(v => v.Entry.ResourcePath != "v2/Devices").ToArray();
        var newValues = after.Values.Where(v => v.Entry.ResourcePath != "v2/Devices").ToArray();
        if (oldValues.Length != newValues.Length) throw new InvalidDataException("Configuration changed after review.");
        for (int i = 0; i < oldValues.Length; i++)
        {
            var a = oldValues[i]; var b = newValues[i];
            if (a.Entry.ResourcePath != b.Entry.ResourcePath || a.Entry.Command != b.Entry.Command ||
                a.Entry.Parameters.HasValue != b.Entry.Parameters.HasValue || a.Entry.Parameters is JsonElement p && !JsonElement.DeepEquals(p,b.Entry.Parameters!.Value) ||
                !(a.Entry.Command == "cp.programming:copyProgramming" ? ConfigurationProgramming.SemanticallyEqual(a.Value,b.Value) : JsonElement.DeepEquals(a.Value,b.Value)))
                throw new InvalidDataException("Captured configuration changed after review.");
        }
    }
}
