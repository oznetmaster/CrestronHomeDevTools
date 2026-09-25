// Copyright (c) 2026 Neil Colvin. MIT licensed. Reuses the WeatherLink sample lifetime checks.
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using CrestronHomeDevTools;
using Renci.SshNet;
static class LifetimeBaselineStore
    {
    private sealed record Saved(string PlanSha256, string SettingsSha256, LifetimeBaseline Baseline);
    public static async Task<LifetimeBaseline> ReadOrCreateAsync(ProducerSettings settings, SubmissionEndurancePlan plan, CancellationToken token)
        => await ReadOrCreateAsync(settings.Input.BaselineFile, plan, settings.Input,
            ct => LifetimeReader.AcquireAsync(settings, ct), token);
    internal static async Task<LifetimeBaseline> ReadOrCreateAsync(string path, SubmissionEndurancePlan plan,
        ProducerSettingsInput input, Func<CancellationToken,Task<LifetimeBaseline>> acquire, CancellationToken token)
        {
        var json=ProducerJson.CreateOptions();
        string Hash<T>(T value) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value,json)));
        string planHash=Hash(plan), settingsHash=Hash(input);
        bool existing=File.Exists(path);
        // CreateNew also records an interrupted first acquisition: an empty/partial file is never replaced.
        // The collector serializes probes; concurrent direct invocations fail the exclusive open.
        await using var file=new FileStream(path,existing?FileMode.Open:FileMode.CreateNew,FileAccess.ReadWrite,FileShare.None);
        if(existing) {
            if(file.Length>65536)throw new InvalidDataException("Lifetime baseline exceeds its limit.");
            var saved=await JsonSerializer.DeserializeAsync<Saved>(file,json,token)
                ??throw new InvalidDataException("Lifetime acquisition was interrupted.");
            if(saved.PlanSha256!=planHash || saved.SettingsSha256!=settingsHash)
                throw new InvalidDataException("Lifetime baseline belongs to different frozen inputs.");
            saved.Baseline.Validate();return saved.Baseline;
        }
        var baseline=await acquire(token);baseline.Validate();
        await JsonSerializer.SerializeAsync(file,new Saved(planHash,settingsHash,baseline),json,token);
        file.Flush(true);return baseline;
        }
    }
sealed record LifetimeBaseline (DateTimeOffset BootEarliestUtc, DateTimeOffset BootLatestUtc, int ParentPid, int[] ChildPids,
    DateTimeOffset ObservedUtc, int ClockToleranceSeconds, string Identity)
    {
    public void Validate ()
        {
        if (BootEarliestUtc == default || BootLatestUtc < BootEarliestUtc || ParentPid <= 0 || ChildPids is not { Length: > 0 } ||
            ChildPids.Any (pid => pid <= 0) || ChildPids.Distinct ().Count () != ChildPids.Length || ClockToleranceSeconds != 2 ||
            string.IsNullOrWhiteSpace (Identity)) throw new InvalidDataException ("Lifetime baseline is incomplete.");
        }
    public bool Contains (ProcessorUptimeSnapshot current, TimeSpan tolerance) =>
        current.EarliestStartUtc <= BootLatestUtc + tolerance && current.LatestStartUtc >= BootEarliestUtc - tolerance;
    }
sealed record TaskstatBaseline (int ParentPid, HashSet<int> Children)
    {
    public static TaskstatBaseline Parse (string text)
        {
        int parent = 0;
        var rows = new List<(int Pid, int Parent, string Name)> ();
        foreach (var line in text.Split ('\n'))
            {
            var parts = Regex.Split (line.Trim (), @"\s+");
            if (parts.Length < 13 || !int.TryParse (parts[0], CultureInfo.InvariantCulture, out int pid) ||
                !int.TryParse (parts[1], CultureInfo.InvariantCulture, out int ppid)) continue;
            // TASKSTAT columns begin with PID and PPID; its process name is the thirteenth token (index 12).
            var name = parts[12];
            rows.Add ((pid, ppid, name));
            if (Regex.IsMatch (name, @"^SSP\[\d+\]$", RegexOptions.CultureInvariant)) parent = pid;
            }
        if (parent <= 0) throw new InvalidDataException ("TASKSTAT output has no SSP parent identity.");
        var children = rows.Where (row => row.Parent == parent && row.Name == "mono-sgen").Select (row => row.Pid).ToHashSet ();
        if (children.Count == 0) throw new InvalidDataException ("TASKSTAT output has no managed mono child identity.");
        return new (parent, children);
        }
    }

sealed class ProbeRuntimeState
    {
    public string BootIdentity { get; private set; } = "unverified";
    public void MarkLifetimeVerified (string identity)
        {
        if (string.IsNullOrWhiteSpace (identity)) throw new InvalidDataException ("Verified lifetime identity is missing.");
        BootIdentity = identity;
        }
    }
static class ProducerJson
    {
    internal static JsonSerializerOptions CreateOptions () => new ()
        {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectRequiredConstructorParameters = true,
        RespectNullableAnnotations = true,
        AllowDuplicateProperties = false,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        Converters = { new JsonStringEnumConverter () }
        };
    }


static class LifetimeReader
    {
    private static readonly Regex Prompt = new (@"(?:^|[\r\n])[A-Za-z0-9][A-Za-z0-9_.:-]{0,80}>", RegexOptions.CultureInvariant);
    public static async Task<LifetimeBaseline> AcquireAsync (ProducerSettings settings, CancellationToken token)
        {
        var credential = new NetworkCredential (settings.UserName, settings.Password);
        var uptime = await ProcessorUptime.ReadAsync (settings.ProcessorHost, credential, settings.SshFingerprint, TimeSpan.FromSeconds (30), token);
        var taskstat = await ReadTaskstatAsync (settings, token);
        return new (uptime.EarliestStartUtc, uptime.LatestStartUtc, taskstat.ParentPid, taskstat.Children.Order ().ToArray (), DateTimeOffset.UtcNow,
            2, $"uptime-window:{uptime.EarliestStartUtc:O}/{uptime.LatestStartUtc:O};ssp:{taskstat.ParentPid};children:{string.Join (',', taskstat.Children.Order ())}");
        }
    public static async Task<TaskstatBaseline> ReadTaskstatAsync (ProducerSettings settings, CancellationToken token)
        {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource (token);
        deadline.CancelAfter (TimeSpan.FromSeconds (30));
        using var client = new SshClient (settings.ProcessorHost, settings.UserName, settings.Password);
        client.ConnectionInfo.Timeout = TimeSpan.FromSeconds (30);
        client.HostKeyReceived += (_, key) => key.CanTrust = string.Equals (key.FingerPrintSHA256, settings.SshFingerprint, StringComparison.Ordinal);
        await client.ConnectAsync (deadline.Token);
        using var shell = client.CreateShellStream ("xterm", 100, 24, 800, 600, 65536);
        var text = new StringBuilder (); bool sent = false;
        while (true)
            {
            deadline.Token.ThrowIfCancellationRequested ();
            if (!client.IsConnected) throw new IOException ("Processor disconnected before TASKSTAT was read.");
            if (shell.DataAvailable) text.Append (shell.Read ());
            if (text.Length > 65536) throw new InvalidDataException ("TASKSTAT response exceeded its limit.");
            if (!sent && Prompt.IsMatch (text.ToString ())) { text.Clear (); sent = true; shell.WriteLine ("TASKSTAT"); }
            else if (sent && Prompt.IsMatch (text.ToString ())) return TaskstatBaseline.Parse (text.ToString ());
            await Task.Delay (50, deadline.Token);
            }
        }
    }


