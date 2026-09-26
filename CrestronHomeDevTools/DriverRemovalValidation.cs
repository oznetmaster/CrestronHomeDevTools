// Copyright (c) 2026 Neil Colvin. MIT licensed.
using System.Text.Json;

namespace CrestronHomeDevTools;

public sealed record DriverRemovalTarget(string Host, int DeviceId, int ParentDeviceId, string Name,
    string Model, string Version, int LocationId);
/// <summary>Inventory identity only; configuration property collections may contain secrets and are excluded.</summary>
public sealed record DriverRemovalDevice(int Id, int? ParentDeviceId, string? Name, string? Model,
    int? LocationId, string? Version, string? LoadingStatus);
public sealed record DriverRemovalUiOutcome(bool Passed, bool HomeRestored);
public sealed record DriverRemovalValidationResult(bool RemovalAttempted, bool RemovalConfirmed,
    bool OtherDevicesPreserved, bool UiAbsenceConfirmed, bool HomeRestored, ProcessorErrorLogInterval? LogInterval)
{
    public bool SafeToRelease => RemovalConfirmed && OtherDevicesPreserved && HomeRestored;
    public bool Passed => RemovalAttempted && SafeToRelease && UiAbsenceConfirmed && LogInterval?.NoNewErrorsOrExceptions == true;
}

/// <summary>Final, explicitly selected removal of the actual driver, its descendants, and corresponding UI checks.</summary>
public static class DriverRemovalValidation
{
    /// <remarks>
    /// Caller holds processor and Android reservations, verifies the candidate package, and restores physical
    /// loads before invoking. Both connection factories must target Target.Host. The UI callback receives the
    /// exact pre-removal tree, a removal phase flag, and a fresh private evidence directory. It must verify
    /// relevant room/end-user views, retain its evidence and independently confirm return to Home.
    /// This does not reinstall the driver. An existing journal or uncertain operation is never replayed.
    /// Exceptions require journal inspection before reservation release.
    /// </remarks>
    public static Task<DriverRemovalValidationResult> RunAsync(DriverRemovalTarget target, string journalDirectory,
        TimeSpan timeout, Func<CancellationToken, Task<ConfigurationClient>> openConnection,
        Func<CancellationToken, Task> verifyOwnership,
        Func<IReadOnlyList<DriverRemovalDevice>, bool, string, CancellationToken, Task<DriverRemovalUiOutcome>> observeUi,
        Func<CancellationToken, Task<ProcessorErrorLogSnapshot>> readLog, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(openConnection);
        return RunCoreAsync(target, journalDirectory, timeout, verifyOwnership, observeUi, readLog,
            async ct =>
            {
                await using var client = await openConnection(ct).ConfigureAwait(false);
                return (await client.GetDevicesAsync(ct).ConfigureAwait(false)).Select(Summarize).ToArray();
            },
            async ct =>
            {
                await using var client = await openConnection(ct).ConfigureAwait(false);
                // Retains the existing API's no-reboot and exact dependency-scope checks.
                await client.RemoveDriverInstanceAsync(target.DeviceId, target.Model, target.Version, timeout, ct).ConfigureAwait(false);
            }, token);
    }

    /// <summary>Retains identity and load status without copying configuration properties that may contain credentials.</summary>
    public static DriverRemovalDevice Summarize(DeviceInfo item)
    {
        string? Text(string key) => item.PropertyValues.TryGetValue(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        return new(item.Id, item.ParentDeviceId, item.Name, item.Model, item.LocationId,
            Text("cp.driverInformation:version"), Text("cp.driverConfiguration:driverLoadingStatus"));
    }

    internal static async Task<DriverRemovalValidationResult> RunCoreAsync(DriverRemovalTarget target,
        string journalDirectory, TimeSpan timeout, Func<CancellationToken, Task> verifyOwnership,
        Func<IReadOnlyList<DriverRemovalDevice>, bool, string, CancellationToken, Task<DriverRemovalUiOutcome>> observeUi,
        Func<CancellationToken, Task<ProcessorErrorLogSnapshot>> readLog,
        Func<CancellationToken, Task<DriverRemovalDevice[]>> readInventory,
        Func<CancellationToken, Task> remove, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(target); ArgumentNullException.ThrowIfNull(verifyOwnership);
        ArgumentNullException.ThrowIfNull(observeUi); ArgumentNullException.ThrowIfNull(readLog);
        if (string.IsNullOrWhiteSpace(target.Host) || target.DeviceId <= 0 || target.ParentDeviceId == 0 || target.LocationId <= 0 ||
            string.IsNullOrWhiteSpace(target.Model) || string.IsNullOrWhiteSpace(target.Name) || !Version.TryParse(target.Version, out _) ||
            timeout <= TimeSpan.Zero || timeout > TimeSpan.FromMinutes(15) || !Path.IsPathFullyQualified(journalDirectory))
            throw new ArgumentException("Provide the exact installed target, fresh absolute journal and a bounded timeout.");
        if (Directory.Exists(journalDirectory) || File.Exists(journalDirectory))
            throw new InvalidOperationException("Removal journal already exists; inspect it instead of replaying removal.");
        Directory.CreateDirectory(journalDirectory);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(timeout);
        token = deadline.Token;
        void Record(string name, object value)
        {
            using var file = new FileStream(Path.Combine(journalDirectory, name + ".json"), FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            JsonSerializer.Serialize(file, value); file.Flush(true);
        }
        async Task<DriverRemovalDevice[]> Inventory()
        {
            await verifyOwnership(token).ConfigureAwait(false);
            var data = await readInventory(token).ConfigureAwait(false);
            if (data.Length > 4096 || data.Select(d => d.Id).Distinct().Count() != data.Length)
                throw new InvalidDataException("Invalid or oversized device inventory.");
            return data.OrderBy(d => d.Id).ToArray();
        }
        bool attempted = false;
        try
        {
            Record("target", target);
            var before = await Inventory().ConfigureAwait(false);
            var root = before.SingleOrDefault(d => d.Id == target.DeviceId);
            if (root == null || root.ParentDeviceId != target.ParentDeviceId || root.Name != target.Name || root.Model != target.Model ||
                root.LocationId != target.LocationId || !Version.TryParse(root.Version, out var version) || version != Version.Parse(target.Version) || root.LoadingStatus != "Loaded")
                throw new InvalidDataException("The actual driver identity changed; no removal sent.");
            var scope = new HashSet<int> { target.DeviceId };
            bool changed;
            do
            {
                changed = false;
                foreach (var item in before)
                    if (item.ParentDeviceId is int parent && scope.Contains(parent) && scope.Add(item.Id)) changed = true;
            } while (changed);
            var selected = Array.AsReadOnly(before.Where(d => scope.Contains(d.Id)).ToArray());
            var preserved = before.Where(d => !scope.Contains(d.Id)).ToArray();
            Record("before-inventory", before); Record("removal-scope", selected);
            string firstUi = Path.Combine(journalDirectory, "ui-before"); Directory.CreateDirectory(firstUi);
            var beforeUi = await observeUi(selected, false, firstUi, token).ConfigureAwait(false);
            Record("before-ui", beforeUi);
            if (!beforeUi.Passed || !beforeUi.HomeRestored || !Directory.EnumerateFiles(firstUi, "*", SearchOption.AllDirectories).Any())
                throw new InvalidDataException("Pre-removal UI verification/restoration/evidence is incomplete; no removal sent.");
            var beforeLog = await readLog(token).ConfigureAwait(false);
            if (!beforeLog.Host.Equals(target.Host, StringComparison.OrdinalIgnoreCase) ||
                !ProcessorErrorLog.Compare(beforeLog, beforeLog with { RequestSentUtc = beforeLog.ObservedUtc }).Comparable)
                throw new InvalidDataException("The target's baseline error log is not suitable for interval comparison.");
            Record("before-log", beforeLog);
            var confirmedBefore = await Inventory().ConfigureAwait(false);
            if (!before.SequenceEqual(confirmedBefore))
                throw new InvalidDataException("Device inventory changed during preflight; no removal sent.");
            await verifyOwnership(token).ConfigureAwait(false);
            Record("removal-intent", new { Target = target, Utc = DateTimeOffset.UtcNow }); attempted = true;
            await remove(token).ConfigureAwait(false);
            DriverRemovalDevice[] after;
            while (true)
            {
                after = await Inventory().ConfigureAwait(false);
                if (preserved.Any(old => !after.Contains(old)))
                    throw new InvalidDataException("An unrelated device changed or disappeared; inspect retained inventory.");
                if (!after.Any(d => scope.Contains(d.Id) || d.ParentDeviceId is int parent && scope.Contains(parent))) break;
                await Task.Delay(250, token).ConfigureAwait(false);
            }
            Record("after-inventory", after);
            string finalUi = Path.Combine(journalDirectory, "ui-after"); Directory.CreateDirectory(finalUi);
            var afterUi = await observeUi(selected, true, finalUi, token).ConfigureAwait(false);
            Record("after-ui", afterUi);
            var finalInventory = await Inventory().ConfigureAwait(false);
            Record("final-inventory", finalInventory);
            if (preserved.Any(old => !finalInventory.Contains(old)) ||
                finalInventory.Any(d => scope.Contains(d.Id) || d.ParentDeviceId is int parent && scope.Contains(parent)))
                throw new InvalidDataException("Device removal or unrelated-device preservation changed during UI observation.");
            var afterLog = await readLog(token).ConfigureAwait(false);
            Record("after-log", afterLog);
            var interval = ProcessorErrorLog.Compare(beforeLog, afterLog); Record("log-interval", interval);
            await verifyOwnership(token).ConfigureAwait(false);
            var result = new DriverRemovalValidationResult(true, true, true,
                afterUi.Passed && Directory.EnumerateFiles(finalUi, "*", SearchOption.AllDirectories).Any(), afterUi.HomeRestored, interval);
            Record("result", result); return result;
        }
        catch (Exception error)
        {
            try { Record("stopped", new { RemovalAttempted = attempted, ErrorType = error.GetType().Name, Utc = DateTimeOffset.UtcNow, InspectBeforeRetry = true }); }
            catch { /* Preserve the original operation error. */ }
            throw;
        }
    }
}
