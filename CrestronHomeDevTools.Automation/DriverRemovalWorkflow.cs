// Copyright (c) 2026 Neil Colvin. MIT licensed.
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using CrestronHomeNUnit.Android;
using CrestronHomeNUnit.Client;
using CrestronHomeNUnit.Workflow;
using ProcessorLease = CrestronHomeNUnit.Client.ProcessorLease;

namespace CrestronHomeDevTools.Automation;

public sealed record DriverRemovalWorkflowPlan(string Host, string CertificateSha256, string SshFingerprint,
    string PackagePath, string PackageSha256, InstalledDriverTestTarget Target, DriverRemovalAppPlan App, int TimeoutSeconds = 600);
public sealed record DriverRemovalWorkflowResult(bool RemovalRequested, bool CandidateVerified, bool ReservationsReleased,
    DriverRemovalValidationResult? Removal, DriverRemovalUiOutcome? Baseline)
{
    public bool Passed => CandidateVerified && ReservationsReleased && (RemovalRequested ? Removal?.Passed == true : Baseline is { Passed: true, HomeRestored: true });
}

/// <summary>Candidate-bound processor/Android coordination for final removal, or an explicitly non-removing app baseline.</summary>
public static class DriverRemovalWorkflow
{
    public static Task<DriverRemovalWorkflowResult> ObserveBaselineAsync(DriverRemovalWorkflowPlan plan, NetworkCredential credential,
        string evidenceDirectory, CancellationToken token = default) => Run(plan, credential, evidenceDirectory, false, token);

    /// <remarks>Caller must have completed physical-state restoration and all tests that need the installed driver.</remarks>
    public static Task<DriverRemovalWorkflowResult> RemoveAsync(DriverRemovalWorkflowPlan plan, NetworkCredential credential,
        string evidenceDirectory, CancellationToken token = default) => Run(plan, credential, evidenceDirectory, true, token);

    private static async Task<DriverRemovalWorkflowResult> Run(DriverRemovalWorkflowPlan plan, NetworkCredential credential,
        string root, bool remove, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(plan); ArgumentNullException.ThrowIfNull(credential);
        if (string.IsNullOrWhiteSpace(plan.Host) || string.IsNullOrWhiteSpace(plan.CertificateSha256) || string.IsNullOrWhiteSpace(plan.SshFingerprint) ||
            plan.TimeoutSeconds is < 30 or > 900 || plan.PackageSha256.Length != 64 || !plan.PackageSha256.All(char.IsAsciiHexDigit) ||
            !Path.IsPathFullyQualified(plan.PackagePath) || !Path.IsPathFullyQualified(root) || Directory.Exists(root) || File.Exists(root))
            throw new ArgumentException("Provide a pinned candidate, bounded plan and fresh private evidence path.");
        plan.App.Profile.Validate();
        // Freeze caller-owned mutable arrays before crossing asynchronous boundaries.
        plan = plan with { App = plan.App with { Tiles = plan.App.Tiles.ToArray(), NonvisualDeviceIds = plan.App.NonvisualDeviceIds.ToArray() } };
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromSeconds(plan.TimeoutSeconds)); token = deadline.Token;
        Directory.CreateDirectory(root);
        AutomationFiles.Write(Path.Combine(root, "plan.json"), plan);
        string candidate = Path.Combine(root, "candidate.pkg");
        await using (var input = new FileStream(plan.PackagePath, FileMode.Open, FileAccess.Read, FileShare.Read))
        await using (var output = new FileStream(candidate, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            if (input.Length is <= 0 or > 64 * 1024 * 1024) throw new InvalidDataException("Candidate exceeds its bound.");
            await input.CopyToAsync(output, token); output.Flush(true);
        }
        await using var held = new FileStream(candidate, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (!Convert.ToHexString(await SHA256.HashDataAsync(held, token)).Equals(plan.PackageSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Candidate digest differs; no reservations or app inputs started.");
        var package = DriverDeployment.Inspect(candidate);
        var target = plan.Target;
        if (package.Model != target.Model || Version.Parse(package.Version) != Version.Parse(target.Version))
            throw new InvalidDataException("Candidate and target identity differ.");
        string owner = Guid.NewGuid().ToString("N");
        AutomationFiles.Write(Path.Combine(root, "ownership.json"), new { Owner = owner, plan.Host, RemovalRequested = remove });
        ProcessorLease? processor = null; AndroidSessionLease? android = null;
        bool safe = true, control = false, verified = false, released = false;
        DriverRemovalValidationResult? removal = null; DriverRemovalUiOutcome? baseline = null;
        var connection = new ProcessorConnectionOptions { Host = plan.Host, CertificateSha256 = plan.CertificateSha256, RequestTimeout = TimeSpan.FromSeconds(30) };
        void Phase(string phase, string state) => File.AppendAllText(Path.Combine(root, "phases.jsonl"), JsonSerializer.Serialize(new { Owner = owner, Phase = phase, State = state, Utc = DateTimeOffset.UtcNow }) + "\n");
        async Task Verify(CancellationToken ct)
        {
            await processor!.VerifyAfterReconnectAsync(plan.Host, ct);
            AndroidSessionLease.VerifyOwner(plan.App.Profile.LockPath, owner);
        }
        Task<ConfigurationClient> Open(CancellationToken ct) => ConfigurationClient.ConnectAsync(connection, credential, ct);
        async Task<DriverRemovalDevice[]> Inventory(CancellationToken ct)
        {
            await Verify(ct); await using var client = await Open(ct);
            var all = (await client.GetDevicesAsync(ct)).Select(DriverRemovalValidation.Summarize).ToArray();
            if (all.Length > 4096 || all.Select(d => d.Id).Distinct().Count() != all.Length) throw new InvalidDataException("Invalid device inventory.");
            var scope = new HashSet<int> { target.DeviceId }; bool changed;
            do { changed = false; foreach (var d in all) if (d.ParentDeviceId is int p && scope.Contains(p) && scope.Add(d.Id)) changed = true; } while (changed);
            return all.Where(d => scope.Contains(d.Id)).OrderBy(d => d.Id).ToArray();
        }
        async Task VerifyCandidate(string phase, CancellationToken ct)
        {
            await Verify(ct);
            async Task Identity()
            {
                await using var client = await Open(ct);
                var item = await client.GetDeviceAsync(target.DeviceId, ct); var catalog = await client.GetDriverAsync(target.CatalogueId, ct);
                string? Property(string key) => item?.PropertyValues.TryGetValue(key, out var v) == true && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
                if (item == null || item.Id != target.DeviceId || item.ParentDeviceId != target.ParentDeviceId || item.Name != target.Name || item.Model != target.Model || item.LocationId != target.LocationId ||
                    !Version.TryParse(Property("cp.driverInformation:version"), out var version) || version != Version.Parse(target.Version) ||
                    Property("cp.driverConfiguration:driverLoadingStatus") != "Loaded" || Property("cp.driverInformation:developer") != target.Developer || Property("cp.driverInformation:controlType") != target.ControlType ||
                    catalog == null || catalog.Id != target.CatalogueId || catalog.Model != package.Model || catalog.Manufacturer != package.Manufacturer || catalog.Developer != target.Developer)
                    throw new InvalidDataException("Candidate/installed target association changed.");
            }
            await Identity();
            var payload = await DriverPayloadInspection.CompareAsync(plan.Host, credential, plan.SshFingerprint, candidate,
                plan.PackageSha256, target.CatalogueId, TimeSpan.FromMinutes(2), ct);
            await Identity(); await Verify(ct);
            AutomationFiles.Write(Path.Combine(root, phase + "-candidate.json"), new { Target = target, Payload = payload });
        }
        try
        {
            Phase("Processor", "Acquiring"); processor = await ProcessorLease.AcquireAsync(plan.Host, credential, plan.SshFingerprint, owner, token); Phase("Processor", "Held");
            Phase("Android", "Acquiring"); android = AndroidSessionLease.Acquire(plan.App.Profile.LockPath, owner); Phase("Android", "Held");
            await VerifyCandidate("before", token); verified = true;
            var selected = await Inventory(token); DriverRemovalAppObserver.ValidateScope(plan.App, selected);
            AutomationFiles.Write(Path.Combine(root, "selected-devices.json"), selected);
            safe = false; Phase("Control", "Starting"); await processor.BeginControlAsync(token); control = true; Phase("Control", "Held");
            var observer = new DriverRemovalAppObserver(plan.App, Verify);
            if (remove)
            {
                removal = await DriverRemovalValidation.RunAsync(new(plan.Host, target.DeviceId, target.ParentDeviceId, target.Name, target.Model, target.Version, target.LocationId),
                    Path.Combine(root, "removal"), TimeSpan.FromSeconds(plan.TimeoutSeconds), Open, Verify, observer.ObserveAsync,
                    ct => ProcessorErrorLog.ReadAsync(plan.Host, credential, plan.SshFingerprint, TimeSpan.FromSeconds(45), ct), token);
                safe = removal.SafeToRelease;
            }
            else
            {
                string directory = Path.Combine(root, "baseline"); Directory.CreateDirectory(directory);
                baseline = await observer.ObserveAsync(selected, false, directory, token);
                await VerifyCandidate("after", token);
                var after = await Inventory(token);
                if (!selected.SequenceEqual(after)) throw new InvalidDataException("Device identities changed during app baseline.");
                safe = baseline.HomeRestored;
            }
        }
        finally
        {
            try
            {
                if (safe && processor != null)
                {
                    using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(45));
                    if (control) { await processor.EndControlAsync(cleanup.Token); Phase("Control", "Released"); }
                    if (android != null) { android.Release(); Phase("Android", "Released"); }
                    await processor.ReleaseAsync(cleanup.Token); Phase("Processor", "Released"); released = true;
                }
            }
            finally { android?.Dispose(); processor?.Dispose(); }
        }
        var result = new DriverRemovalWorkflowResult(remove, verified, released, removal, baseline);
        AutomationFiles.Write(Path.Combine(root, "result.json"), result); return result;
    }
}
