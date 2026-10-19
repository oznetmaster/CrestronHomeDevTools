// Copyright (c) 2026 Neil Colvin. MIT licensed.
namespace CrestronHomeDevTools;

public sealed record NamedManagedDeviceRequest(int ParentId, string ParentModel, string ParentVersion,
    string ManagedDeviceId, string Name, string ChildModel, string RoomName);
public sealed record NamedManagedDeviceTestTarget(string Alias, NamedManagedDeviceRequest Request);
public sealed record NamedManagedDeviceTestBinding(string Alias, int DeviceId, NamedManagedDeviceRequest Request);
public sealed record NamedManagedDeviceValidationResult(bool Passed, bool RestorationConfirmed, bool CleanupConfirmed,
    IReadOnlyList<NamedManagedDeviceTestBinding> Bindings);

public static partial class ManagedDeviceValidation
{
    /// <summary>Resolve every named room before commissioning any child. Native IDs stay in processor-private journals.</summary>
    public static async Task<NamedManagedDeviceValidationResult> RunInRoomsAsync(
        Func<CancellationToken, Task<ConfigurationClient>> openConnection,
        IReadOnlyList<NamedManagedDeviceTestTarget> targets, string journalDirectory, TimeSpan operationTimeout,
        Func<CancellationToken, Task> verifyOwnership,
        Func<IReadOnlyList<NamedManagedDeviceTestBinding>, CancellationToken, Task<ManagedDeviceTestOutcome>> runTests,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(targets);
        targets = targets.ToArray();
        if (targets.Count == 0 || targets.Any(target => target == null || target.Request == null ||
            string.IsNullOrWhiteSpace(target.Alias) || string.IsNullOrWhiteSpace(target.Request.RoomName)) ||
            targets.Select(target => target.Alias).Distinct(StringComparer.OrdinalIgnoreCase).Count() != targets.Count)
            throw new ArgumentException("Distinct named-room child targets are required.", nameof(targets));
        await verifyOwnership(cancellationToken).ConfigureAwait(false);
        ManagedDeviceTestTarget[] resolved;
        await using (var client = await openConnection(cancellationToken).ConfigureAwait(false))
        {
            var locations = await client.GetLocationsAsync(cancellationToken).ConfigureAwait(false);
            resolved = targets.Select(target => new ManagedDeviceTestTarget(target.Alias,
                new(target.Request.ParentId, target.Request.ParentModel, target.Request.ParentVersion,
                    target.Request.ManagedDeviceId, target.Request.Name, target.Request.ChildModel,
                    ProcessorTestRooms.Resolve(locations, target.Request.RoomName)))).ToArray();
        }
        NamedManagedDeviceTestBinding[] Describe(IReadOnlyList<ManagedDeviceTestBinding> bindings) =>
            bindings.Select(binding => new NamedManagedDeviceTestBinding(binding.Alias, binding.DeviceId,
                targets.Single(target => target.Alias == binding.Alias).Request)).ToArray();
        var result = await RunAsync(openConnection, resolved, journalDirectory, operationTimeout, verifyOwnership,
            (bindings, token) => runTests(Describe(bindings), token), cancellationToken).ConfigureAwait(false);
        return new(result.Passed, result.RestorationConfirmed, result.CleanupConfirmed, Describe(result.Bindings));
    }
}
