// Copyright (c) 2026 Neil Colvin. Licensed under the MIT License.
using System.Net;
using System.Text.Json;
namespace CrestronHomeDevTools.DriverUpdates;
internal sealed class UpdateSession
    {
    private readonly ProcessorConnectionOptions _options;
    private readonly NetworkCredential _credential;
    private readonly string? _sshFingerprint;
    public DriverUpdateTarget Target { get; }
    public bool CanApply => !string.IsNullOrWhiteSpace (_sshFingerprint);
    private UpdateSession (ProcessorConnectionOptions options, NetworkCredential credential, string? sshFingerprint)
        {
        _options = options; _credential = credential; _sshFingerprint = sshFingerprint;
        Target = new (options.Host, options.HttpsPort, options.WebSocketPort, options.CertificateSha256!.ToUpperInvariant ());
        }
    public static string[] Profiles ()
        {
        var directory = Path.Combine (Environment.GetFolderPath (Environment.SpecialFolder.LocalApplicationData), "CrestronHomeDevTools", "Profiles");
        return Directory.Exists (directory) ? Directory.GetFiles (directory, "*.profile").Select (p => Path.GetFileNameWithoutExtension (p)).Order ().ToArray () : [];
        }
    public static async Task<UpdateSession> LoadAsync (string profile, string processor)
        {
        var settings = new global::ProfileStore ().Load (profile);
        var selector = string.IsNullOrWhiteSpace (processor) ? settings.Host ?? settings.SystemName : processor.Trim ();
        if (string.IsNullOrWhiteSpace (selector)) throw new ArgumentException ("The profile has no processor address.");
        var host = IPAddress.TryParse (selector, out _) ? selector : (await ProcessorDiscovery.ResolveAsync (selector, CancellationToken.None)).Address;
        if (string.IsNullOrWhiteSpace (settings.UserName) || string.IsNullOrEmpty (settings.Password) || string.IsNullOrWhiteSpace (settings.CertificateSha256))
            throw new ArgumentException ("The saved profile needs credentials and verified certificate trust. Configure it in the DevTools console first.");
        return new (new () { Host = host, HttpsPort = settings.HttpsPort, WebSocketPort = settings.WebSocketPort, CertificateSha256 = settings.CertificateSha256 },
            new (settings.UserName, settings.Password), settings.SshFingerprint);
        }
    public async Task<(DriverUpdateReport Report, IReadOnlyDictionary<int, string> Rooms)> ScanAsync ()
        {
        await using var client = await ConfigurationClient.ConnectAsync (_options, _credential);
        var report = await DriverUpdateManager.InspectAsync (client, Target);
        var rooms = (await client.GetLocationsAsync ()).ToDictionary (r => r.Id, r => r.Name ?? r.Id.ToString ());
        return (report, rooms);
        }
    public async Task<DriverUpdateBatchResult> ApplyAsync (DriverUpdateReport report, string[] selected, bool allowRestart,
        string journal, IProgress<DriverUpdateProgress> progress)
        {
        if (!CanApply) throw new ArgumentException ("This profile needs verified SSH trust for the shared processor reservation. Configure it in the DevTools console first.");
        // Selection and target must still match the scan shown in the confirmation.
        DriverUpdateManager.SelectUpdates (report, false, selected, allowRestart);
        if (report.Target != Target) throw new ArgumentException ("The scan belongs to another processor. Scan again.");
        Directory.CreateDirectory (journal);
        ProcessorOperationLease? lease = null;
        bool submitted = false, safe = false;
        try
            {
            lease = await ProcessorOperationLease.AcquireAsync (Target.Host, _credential, _sshFingerprint!, Guid.NewGuid ().ToString ("N"), CancellationToken.None);
            await SaveLeaseAsync ("Held");
            await using var client = await ConfigurationClient.ConnectAsync (_options, _credential);
            var timeout = TimeSpan.FromMinutes (10);
            DriverRebootHandler? reboot = allowRestart ? new ((_, _) => Task.CompletedTask, async (_, previous, token) =>
                {
                await previous.RequestProcessorRebootAsync ((_, _) => Task.FromResult (true), "Confirmed desktop driver update", token);
                var recovered = await ProcessorRestartRecovery.WaitAsync (previous, ct => ConfigurationClient.ConnectAsync (_options, _credential, ct), timeout, token);
                try { await lease.VerifyAfterReconnectAsync (Target.Host, token); return recovered; }
                catch { await recovered.DisposeAsync (); throw; }
                }) : null;
            submitted = true;
            var result = await DriverUpdateManager.ApplyAsync (client, Target, report, selected, journal, timeout, reboot, progress: progress);
            safe = result.SafeToReleaseReservation;
            return result;
            }
        finally
            {
            try
                {
                if (lease != null && (!submitted || safe))
                    {
                    using var cleanup = new CancellationTokenSource (TimeSpan.FromSeconds (20));
                    await lease.ReleaseAsync (cleanup.Token);
                    await SaveLeaseAsync ("Released");
                    }
                else if (lease != null) await SaveLeaseAsync ("HeldForInspection");
                }
            finally { lease?.Dispose (); }
            }
        async Task SaveLeaseAsync (string state) => await File.WriteAllTextAsync (Path.Combine (journal, "reservation.json"),
            JsonSerializer.Serialize (new { Target.Host, Owner = lease!.Owner, State = state, UpdatedUtc = DateTimeOffset.UtcNow }));
        }
    }
