// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.
using System.Text.Json;

namespace CrestronHomeDevTools;

public sealed record DriverUpdateTarget (string Host, int HttpsPort, int WebSocketPort, string CertificateSha256);
public sealed record DriverUpdateDevice (int Id, string? Name, string? Model, int? RoomId, int? ParentDeviceId, string? Version);
public sealed record AvailableDriverUpdate (string DriverId, string? Model, string? Manufacturer, string? Developer,
    string? CatalogueVersion, string? AvailabilityState, string Status, string Detail,
    DriverUpdateEligibility? Eligibility, DriverUpdateDevice[] Devices);
public sealed record DriverUpdateReport (int SchemaVersion, DriverUpdateTarget Target, DateTimeOffset ObservedUtc,
    AvailableDriverUpdate[] Drivers, DriverUpdateDevice[] UnresolvedDevices);
public sealed record DriverUpdateStep (string DriverId, string State, string? OperationId, string Detail);
public sealed record DriverUpdateBatchResult (string State, bool SafeToReleaseReservation, DriverUpdateStep[] Steps, string? Reason = null);

/// <summary>Read-only catalogue assessment and sequential, explicitly selected driver updates.
/// The caller holds the processor operation lease throughout ApplyAsync, including restart recovery.</summary>
public static class DriverUpdateManager
    {
    private static readonly JsonSerializerOptions Json = new () { WriteIndented = true, PropertyNameCaseInsensitive = true };

    public static async Task<DriverUpdateReport> InspectAsync (ConfigurationClient client, DriverUpdateTarget target,
        CancellationToken cancellationToken = default)
        {
        ValidateTarget (target);
        var devices = (await client.GetDevicesAsync (cancellationToken).ConfigureAwait (false))
            .Where (d => d.PropertyValues.ContainsKey ("cp.driverInformation:version")).Select (Snapshot).ToArray ();
        // Model matching only narrows catalogue queries. Home's eligibility IDs establish the update scope.
        var models = devices.Select (d => d.Model).Where (m => !string.IsNullOrWhiteSpace (m)).ToHashSet (StringComparer.Ordinal);
        var catalogue = await client.GetDriversAsync (cancellationToken: cancellationToken).ConfigureAwait (false);
        var rows = new List<AvailableDriverUpdate> ();
        foreach (var driver in catalogue.Where (d => models.Contains (d.Model)).DistinctBy (d => d.Id).OrderBy (d => d.Id, StringComparer.Ordinal))
            {
            cancellationToken.ThrowIfCancellationRequested ();
            DriverUpdateEligibility? raw;
            try { raw = await client.GetDriverUpdateEligibilityAsync (driver.Id, cancellationToken).ConfigureAwait (false); }
            catch (ProcessorApiException)
                {
                rows.Add (new (driver.Id, driver.Model, driver.Manufacturer, driver.Developer, driver.Version,
                    driver.AvailabilityState, "Unknown", "Processor did not provide update eligibility.", null, []));
                continue;
                }
            // Never export raw extension/configuration properties: they may contain private information.
            var eligibility = raw == null ? null : new DriverUpdateEligibility
                {
                InstalledDriverVersion = raw.InstalledDriverVersion, AvailableDriverVersion = raw.AvailableDriverVersion,
                IsSupportsSwapDriver = raw.IsSupportsSwapDriver, IsSwapDriverRequiresReboot = raw.IsSwapDriverRequiresReboot,
                EligibleDeviceIds = raw.EligibleDeviceIds?.ToArray ()
                };
            var ids = eligibility?.EligibleDeviceIds ?? [];
            var affected = devices.Where (d => ids.Contains (d.Id)).OrderBy (d => d.Id).ToArray ();
            var (status, detail) = Classify (driver, eligibility, affected);
            rows.Add (new (driver.Id, driver.Model, driver.Manufacturer, driver.Developer, driver.Version,
                driver.AvailabilityState, status, detail, eligibility, affected));
            }
        // Multiple catalogue versions for one affected scope are alternatives, not separate updates.
        foreach (var group in rows.Where (Actionable).GroupBy (r => string.Join (",", r.Devices.Select (d => d.Id))))
            {
            var candidates = group.ToArray ();
            if (candidates.Length < 2) continue;
            var identities = candidates.Select (r => (r.Model, r.Manufacturer, r.Developer)).Distinct ().Count ();
            var latest = candidates.OrderByDescending (r => Version.Parse (r.Eligibility!.AvailableDriverVersion!)).ToArray ();
            bool ambiguous = identities != 1 || DriverVersions.Equal (latest[0].Eligibility!.AvailableDriverVersion, latest[1].Eligibility!.AvailableDriverVersion);
            foreach (var row in candidates.Where (r => ambiguous || r != latest[0]))
                rows[rows.IndexOf (row)] = row with { Status = ambiguous ? "Conflict" : "Superseded", Detail = ambiguous
                    ? "Multiple catalogue entries claim this scope; select and resolve the driver identity manually."
                    : "A newer eligible catalogue version is listed for the same driver and devices." };
            }
        var overlapping = rows.Where (Actionable).SelectMany (r => r.Devices.Select (d => (d.Id, r.DriverId)))
            .GroupBy (x => x.Id).Where (g => g.Count () > 1).SelectMany (g => g.Select (x => x.DriverId)).ToHashSet ();
        for (int i = 0; i < rows.Count; i++)
            if (overlapping.Contains (rows[i].DriverId)) rows[i] = rows[i] with { Status = "Conflict", Detail = "Update scopes overlap; review the catalogue before applying either update." };
        var resolved = rows.Where (r => r.Eligibility != null).SelectMany (r => r.Devices).Select (d => d.Id).ToHashSet ();
        return new (1, target, DateTimeOffset.UtcNow, rows.ToArray (), devices.Where (d => !resolved.Contains (d.Id)).ToArray ());
        }

    public static DriverUpdateReport ReadReport (string path)
        {
        if (new FileInfo (path).Length > 16 * 1024 * 1024) throw new ArgumentException ("Update report exceeds 16 MiB.");
        var report = JsonSerializer.Deserialize<DriverUpdateReport> (File.ReadAllText (path), Json)
            ?? throw new ArgumentException ("Update report is empty.");
        ValidateReport (report);
        return report;
        }

    public static AvailableDriverUpdate[] SelectUpdates (DriverUpdateReport report, bool all, string[] driverIds, bool allowReboot)
        {
        ValidateReport (report);
        if (all == (driverIds.Length != 0) || driverIds.Any (string.IsNullOrWhiteSpace) || driverIds.Distinct (StringComparer.Ordinal).Count () != driverIds.Length)
            throw new ArgumentException ("Choose --all true or unique --drivers catalogue IDs, not both.");
        var selected = all ? report.Drivers.Where (r => r.Status == "UpdateAvailable" || allowReboot && r.Status == "RebootRequired").ToArray ()
            : driverIds.Select (id => report.Drivers.SingleOrDefault (r => r.DriverId == id)
                ?? throw new ArgumentException ("Selected driver is absent from the reviewed report.")).ToArray ();
        foreach (var row in selected)
            {
            var (status, _) = Classify (new DriverInfo { Id = row.DriverId, Model = row.Model, Version = row.CatalogueVersion }, row.Eligibility, row.Devices);
            if (!Actionable (row) || status != row.Status || status == "RebootRequired" && !allowReboot)
                throw new ArgumentException ("Selected driver is not a confirmed eligible upgrade under the requested reboot policy.");
            }
        if (selected.SelectMany (r => r.Devices).GroupBy (d => d.Id).Any (g => g.Count () > 1))
            throw new ArgumentException ("Selected driver updates overlap.");
        return selected;
        }

    public static async Task<DriverUpdateBatchResult> ApplyAsync (ConfigurationClient client, DriverUpdateTarget target,
        DriverUpdateReport report, string[] selectedDriverIds, string journalDirectory, TimeSpan timeout,
        DriverRebootHandler? reboot = null, CancellationToken cancellationToken = default)
        {
        ValidateTarget (target);
        ValidateReport (report);
        if (report.Target != target) throw new ArgumentException ("Update report belongs to a different processor connection or certificate.");
        if (timeout <= TimeSpan.Zero || timeout > TimeSpan.FromHours (1)) throw new ArgumentOutOfRangeException (nameof (timeout));
        var selected = SelectUpdates (report, false, selectedDriverIds, reboot != null);
        if (selected.Length == 0) throw new ArgumentException ("No driver updates were selected.");
        Directory.CreateDirectory (journalDirectory);
        var intentPath = Path.Combine (journalDirectory, "intent.json");
        // One durable intent per directory. An interrupted or completed directory is never reused.
        await SaveNewAsync (intentPath, new { Report = report, SelectedDriverIds = selectedDriverIds, CreatedUtc = DateTimeOffset.UtcNow }, cancellationToken).ConfigureAwait (false);
        var steps = new List<DriverUpdateStep> ();
        bool uncertain = false;
        string phase = "selection-preflight";
        var active = client;
        try
            {
            // Check the entire reviewed selection before changing its first driver.
            foreach (var row in selected) await RecheckAsync (active, row, cancellationToken).ConfigureAwait (false);
            foreach (var row in selected)
                {
                phase = "driver-preflight";
                cancellationToken.ThrowIfCancellationRequested ();
                await RecheckAsync (active, row, cancellationToken).ConfigureAwait (false);
                var request = row.Status == "RebootRequired" ? new DriverRebootRequest ("Update", null,
                    row.Model!, row.Eligibility!.AvailableDriverVersion!, DriverRebootMode.ExplicitAfterOperation) : null;
                if (request != null) await reboot!.BeforeSubmitAsync (request, cancellationToken).ConfigureAwait (false);
                int index = steps.Count;
                await SaveNewAsync (Path.Combine (journalDirectory, $"{index:D3}-intent.json"), row, cancellationToken).ConfigureAwait (false);
                phase = "update-submission";
                uncertain = true;
                steps.Add (new (row.DriverId, "Unconfirmed", null, "Update submission may have reached the processor; inspect before retrying."));
                var operation = await active.BeginDriverUpdateAsync (new (row.DriverId, row.Eligibility!), cancellationToken, reboot != null).ConfigureAwait (false);
                steps[index] = steps[index] with { OperationId = operation };
                await SaveNewAsync (Path.Combine (journalDirectory, $"{index:D3}-submitted.json"), steps[index], CancellationToken.None).ConfigureAwait (false);
                phase = "update-completion";
                if (request != null)
                    {
                    var swap = await active.WaitForDriverSwapAsync (operation, row.DriverId, timeout, cancellationToken).ConfigureAwait (false);
                    if (!swap.IsRebootRequired || swap.DeviceIdsRequiringReconfiguration.Length != 0)
                        throw new InvalidOperationException ("Driver swap requires inspection before restart.");
                    phase = "restart-recovery";
                    var previous = active;
                    active = await reboot!.RecoverAsync (request with { SwapCompletion = swap }, previous, cancellationToken).ConfigureAwait (false);
                    if (!ReferenceEquals (previous, client) && !ReferenceEquals (previous, active))
                        await previous.DisposeAsync ().ConfigureAwait (false);
                    }
                else
                    {
                    var result = await active.WaitForOperationAsync (operation, timeout, cancellationToken).ConfigureAwait (false);
                    if (!result.Succeeded)
                        {
                        uncertain = result.Status != "Failed";
                        steps[index] = steps[index] with { State = uncertain ? "Unconfirmed" : "Failed", Detail = "Processor did not confirm successful completion; remaining updates were not submitted." };
                        return await FinishAsync (journalDirectory, new ("Stopped", !uncertain, steps.ToArray ())).ConfigureAwait (false);
                        }
                    }
                phase = "loaded-instance-verification";
                await active.WaitForDriverVersionAsync (row.Devices.Select (d => d.Id).ToArray (), row.Eligibility!.AvailableDriverVersion!, timeout, cancellationToken).ConfigureAwait (false);
                var after = await active.GetDevicesAsync (cancellationToken).ConfigureAwait (false);
                foreach (var before in row.Devices)
                    {
                    var current = after.SingleOrDefault (d => d.Id == before.Id);
                    if (current == null || (Snapshot (current) with { Version = before.Version }) != before)
                        throw new InvalidOperationException ("Updated instance identity or room changed; inspect before continuing.");
                    }
                steps[index] = steps[index] with { State = "Updated", Detail = "Version, loaded state and instance identity verified." };
                await SaveNewAsync (Path.Combine (journalDirectory, $"{index:D3}-completed.json"), steps[index], CancellationToken.None).ConfigureAwait (false);
                uncertain = false;
                }
            return await FinishAsync (journalDirectory, new ("Completed", true, steps.ToArray ())).ConfigureAwait (false);
            }
        catch (Exception exception)
            {
            // Keep raw processor replies/configuration out of the portable result.
            await SaveNewAsync (Path.Combine (journalDirectory, "failure.json"), new { Phase = phase, Type = exception.GetType ().Name, UnconfirmedOperation = uncertain }, CancellationToken.None).ConfigureAwait (false);
            return await FinishAsync (journalDirectory, new ("Stopped", !uncertain, steps.ToArray (), phase + ": " + exception.GetType ().Name)).ConfigureAwait (false);
            }
        finally { if (!ReferenceEquals (active, client)) await active.DisposeAsync ().ConfigureAwait (false); }
        }

    private static async Task RecheckAsync (ConfigurationClient client, AvailableDriverUpdate row, CancellationToken token)
        {
        var driver = await client.GetDriverAsync (row.DriverId, token).ConfigureAwait (false);
        if (driver == null || driver.Model != row.Model || driver.Manufacturer != row.Manufacturer || driver.Developer != row.Developer
            || !DriverVersions.Equal (driver.Version, row.CatalogueVersion)) throw new InvalidOperationException ("Catalogue identity changed.");
        var eligibility = await client.GetDriverUpdateEligibilityAsync (row.DriverId, token).ConfigureAwait (false);
        var original = row.Eligibility!;
        if (eligibility == null || !DriverVersions.Equal (eligibility.InstalledDriverVersion, original.InstalledDriverVersion)
            || !DriverVersions.Equal (eligibility.AvailableDriverVersion, original.AvailableDriverVersion)
            || eligibility.IsSupportsSwapDriver != true || eligibility.IsSwapDriverRequiresReboot != original.IsSwapDriverRequiresReboot
            || eligibility.EligibleDeviceIds == null || !eligibility.EligibleDeviceIds.Order ().SequenceEqual (original.EligibleDeviceIds!.Order ()))
            throw new InvalidOperationException ("Reviewed update eligibility changed.");
        var devices = await client.GetDevicesAsync (token).ConfigureAwait (false);
        foreach (var expected in row.Devices)
            {
            var device = devices.SingleOrDefault (d => d.Id == expected.Id);
            if (device == null || Snapshot (device) != expected) throw new InvalidOperationException ("Reviewed device identity changed.");
            }
        }

    private static (string, string) Classify (DriverInfo driver, DriverUpdateEligibility? eligibility, DriverUpdateDevice[] devices)
        {
        if (eligibility == null) return ("Unknown", "No update eligibility returned; this is not proof that the driver is current.");
        if (!Version.TryParse (eligibility.InstalledDriverVersion, out var installed) || !Version.TryParse (eligibility.AvailableDriverVersion, out var available))
            return ("Unknown", "Both installed and available versions are required.");
        if (available == installed) return ("Current", "The processor reports the same installed and available version.");
        if (available < installed) return ("InstalledNewer", "Installed version is newer; no downgrade will be offered.");
        var ids = eligibility.EligibleDeviceIds;
        if (ids is not { Length: > 0 } || ids.Any (id => id <= 0) || ids.Distinct ().Count () != ids.Length
            || devices.Length != ids.Length || !devices.Select (d => d.Id).Order ().SequenceEqual (ids.Order ()) || devices.Any (d => d.Model != driver.Model || !DriverVersions.Equal (d.Version, eligibility.InstalledDriverVersion))
            || !DriverVersions.Equal (driver.Version, eligibility.AvailableDriverVersion))
            return ("Unknown", "Catalogue version or affected installed instances could not be confirmed.");
        if (eligibility.IsSupportsSwapDriver == false) return ("Unsupported", "Newer version reported, but in-place update is unsupported.");
        if (eligibility.IsSupportsSwapDriver != true || eligibility.IsSwapDriverRequiresReboot == null)
            return ("Unknown", "Update or restart support is unconfirmed.");
        return eligibility.IsSwapDriverRequiresReboot.Value
            ? ("RebootRequired", "Newer version available; explicit processor restart authorization is required.")
            : ("UpdateAvailable", "Newer version available without a processor reboot.");
        }
    private static bool Actionable (AvailableDriverUpdate row) => row.Status is "UpdateAvailable" or "RebootRequired";
    private static DriverUpdateDevice Snapshot (DeviceInfo d) => new (d.Id, d.Name, d.Model, d.LocationId, d.ParentDeviceId,
        d.PropertyValues.TryGetValue ("cp.driverInformation:version", out var value) && value.ValueKind == JsonValueKind.String ? value.GetString () : null);
    private static void ValidateTarget (DriverUpdateTarget target)
        {
        if (target == null || string.IsNullOrWhiteSpace (target.Host) || target.HttpsPort is < 1 or > 65535 || target.WebSocketPort is < 1 or > 65535
            || target.CertificateSha256?.Length != 64 || !target.CertificateSha256.All (char.IsAsciiHexDigit))
            throw new ArgumentException ("An update report requires a processor host, ports and verified certificate SHA-256.");
        }
    private static void ValidateReport (DriverUpdateReport report)
        {
        if (report == null || report.SchemaVersion != 1 || report.Drivers == null || report.UnresolvedDevices == null
            || report.Drivers.Any (r => r == null || string.IsNullOrWhiteSpace (r.DriverId) || r.Devices == null
                || r.Devices.Any (d => d == null || d.Id <= 0) || r.Devices.Select (d => d.Id).Distinct ().Count () != r.Devices.Length)
            || report.Drivers.Select (r => r.DriverId).Distinct (StringComparer.Ordinal).Count () != report.Drivers.Length)
            throw new ArgumentException ("Invalid driver update report.");
        ValidateTarget (report.Target);
        }
    private static async Task SaveNewAsync<T> (string path, T value, CancellationToken token)
        {
        await using var file = new FileStream (path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        await JsonSerializer.SerializeAsync (file, value, Json, token).ConfigureAwait (false);
        await file.FlushAsync (token).ConfigureAwait (false);
        file.Flush (true);
        }
    private static async Task<DriverUpdateBatchResult> FinishAsync (string directory, DriverUpdateBatchResult result)
        {
        await SaveNewAsync (Path.Combine (directory, "result.json"), result, CancellationToken.None).ConfigureAwait (false);
        return result;
        }
    }
