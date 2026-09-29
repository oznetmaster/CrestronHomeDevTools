// Copyright (c) 2026 Neil Colvin. MIT licensed.
namespace CrestronHomeDevTools;

public static partial class ManagedDeviceCommissioning
{
    /// <summary>Applies initial configuration once to a receipt-owned child and verifies readiness,
    /// including conversion into a native light wrapper and load.</summary>
    /// <remarks>Hold the processor lease and retain configuration intent before calling. An uncertain
    /// outcome must be inspected, never automatically replayed. The commissioning journal is unchanged.</remarks>
    public static async Task<DriverConfigurationResult> ConfigureCreatedAsync(ConfigurationClient client,
        string commissioningJournal, DriverConfiguration.Inputs inputs, TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(inputs);
        if(timeout <= TimeSpan.Zero || timeout > TimeSpan.FromHours(1))
            throw new ArgumentOutOfRangeException(nameof(timeout));
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        var token = deadline.Token;
        try
        {
            var request = ReadRequest(Path.Combine(commissioningJournal, "request.json"));
            // Validate the retained commission response and all current parent/child identities before writing.
            var original = await ObserveCreatedAsync(client, commissioningJournal, token).ConfigureAwait(false);
            var child = await client.GetDeviceAsync(original.DeviceId, token).ConfigureAwait(false)
                ?? throw new InvalidDataException("The commissioned child disappeared before configuration.");
            bool changed = false;
            if(child.LocationId != null)
                changed = await DriverConfiguration.ApplyAsync(client,
                    new(original.DeviceId, request.ChildModel, request.ParentVersion, "Installed"), inputs, token).ConfigureAwait(false);
            while(true)
            {
                var ready = await ObserveCreatedAsync(client, commissioningJournal, token).ConfigureAwait(false);
                if(ready.DeviceId != original.DeviceId)
                    throw new InvalidDataException("The configured child identity changed.");
                if(ready.State == "Ready")
                {
                    // Native conversion removes the wrapper's generic isConfigured/version fields.
                    // ObserveCreatedAsync instead verifies its parent, managed identity, native load,
                    // room, loading status and usable controls. A missing field alone is never success.
                    if(ready.NativeLoadId != null)
                        return new(original.DeviceId, changed, true);
                    child = await client.GetDeviceAsync(original.DeviceId, token).ConfigureAwait(false);
                    if(child != null && child.ParentDeviceId == request.ParentId && child.Model == request.ChildModel
                        && child.Name == request.Name && child.LocationId == request.LocationId
                        && VersionMatches(child, request.ParentVersion) && IsTrue(child, "cp.driverConfiguration:isConfigured"))
                        return new(original.DeviceId, changed, true);
                }
                await Task.Delay(250, token).ConfigureAwait(false);
            }
        }
        catch(OperationCanceledException) when(!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("Managed-child configuration readiness was not confirmed before the deadline. No command was retried.");
        }
    }
}
