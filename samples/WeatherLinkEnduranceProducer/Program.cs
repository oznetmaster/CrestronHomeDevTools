// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License.

using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

using CrestronHomeDevTools;
using Renci.SshNet;
using WeatherLinkLive;

try
    {
    await RunAsync (args);
    }
catch (Exception error) when (error is not OutOfMemoryException)
    {
    Console.Error.WriteLine ($"Producer stopped ({error.GetType ().Name}); no observation accepted.");
    Environment.ExitCode = 2;
    }

static async Task RunAsync (string[] args)
    {
const int FixedClockToleranceSeconds = 2;
const int FixedMaximumFreshnessSeconds = 600;

var json = ProducerJson.CreateOptions ();

if (args.SequenceEqual (["--self-test"]))
    {
    await OfflineChecks.RunAsync ();
    Console.WriteLine ("self-test-passed");
    return;
    }
var request = JsonSerializer.Deserialize<SubmissionEnduranceProbeRequest> (await Console.In.ReadToEndAsync (), json)
    ?? throw new InvalidDataException ("Probe request is missing.");
if (request.SchemaVersion != 1 || string.IsNullOrWhiteSpace (request.SettingsFile) || !Path.IsPathFullyQualified (request.SettingsFile))
    throw new InvalidDataException ("Probe settings must be an absolute private file.");

var evidence = new Dictionary<string, object?> { ["schemaVersion"] = 1, ["startedUtc"] = DateTimeOffset.UtcNow };
var outcome = SubmissionEvidenceOutcome.Failed;
var runtime = new ProbeRuntimeState ();
try
    {
    SubmissionEndurance.ValidatePlan (request.Plan);
    var settings = await ReadSettingsAsync (request.SettingsFile, json, CancellationToken.None);
    ValidatePlanBinding (request.Plan, settings);
    using var deadline = new CancellationTokenSource (request.Plan.ProbeTimeout);
    var baseline = await LifetimeBaselineStore.ReadOrCreateAsync (settings, request.Plan, deadline.Token);
    baseline.Validate ();
    await ObserveAsync (settings, baseline, runtime, evidence, deadline.Token);
    outcome = SubmissionEvidenceOutcome.Passed;
    }
catch (Exception error) when (error is not OutOfMemoryException)
    {
    evidence["reason"] = SecretSafeReason (error);
    }
var result = new SubmissionEnduranceProbeResult (request.Plan.Identity, request.Plan.ProcessorIdentity,
    request.Plan.InstallationIdentity, request.Plan.ReservationId, request.Plan.ProducerId, runtime.BootIdentity, outcome,
    JsonSerializer.SerializeToUtf8Bytes (evidence, json));
Console.Write (JsonSerializer.Serialize (result, json));

static async Task<ProducerSettings> ReadSettingsAsync (string path, JsonSerializerOptions json, CancellationToken token)
    {
    if (!OperatingSystem.IsWindows ()) throw new PlatformNotSupportedException ("Saved processor credentials require Windows.");
    string full = Path.GetFullPath (path);
    string root = Path.TrimEndingDirectorySeparator (Path.GetFullPath (AppContext.BaseDirectory)) + Path.DirectorySeparatorChar;
    if (!Path.IsPathFullyQualified (path) || !full.StartsWith (root, StringComparison.OrdinalIgnoreCase) ||
        new FileInfo (full).Length > 65536) throw new InvalidDataException ("Settings must be a pinned file inside the published producer.");
    var input = JsonSerializer.Deserialize<ProducerSettingsInput> (await File.ReadAllTextAsync (full, token), json)
        ?? throw new InvalidDataException ("Producer settings are missing.");
    input.Validate ();
    var connection = DevToolsCredentialBindings.Read (input.CredentialBindings)
        .Resolve (DevToolsCredentialPurpose.Processor, input.ProcessorHost);
    if (string.IsNullOrWhiteSpace (connection.SshFingerprint) || string.IsNullOrWhiteSpace (connection.CertificateSha256))
        throw new InvalidDataException ("Verified processor trust pins are required.");
    return new (input, connection);
    }

static void ValidatePlanBinding (SubmissionEndurancePlan plan, ProducerSettings settings)
    {
    if (plan.Identity != settings.Input.Identity || plan.InstallationIdentity != settings.Input.InstallationIdentity ||
        plan.Requirement.Id != settings.Input.RequirementId || plan.ProcessorIdentity != $"processor:{settings.ProcessorHost}")
        throw new InvalidDataException ("Candidate, processor, or endurance policy differs from the pinned settings.");
    }
static async Task ObserveAsync (ProducerSettings settings, LifetimeBaseline baseline, ProbeRuntimeState runtime,
    IDictionary<string, object?> evidence, CancellationToken token)
    {
    string ProcessorHost = settings.ProcessorHost, PackageSha256 = settings.Input.Identity.PackageSha256, DriverVersion = settings.Input.DriverVersion;
    int DeviceId = settings.Input.DeviceId;
    var credential = new NetworkCredential (settings.UserName, settings.Password);
    var connection = new ProcessorConnectionOptions { Host = ProcessorHost, CertificateSha256 = settings.CertificateSha256 };
    await using var configuration = await ConfigurationClient.ConnectAsync (connection, credential, token);
    var uptime = await ProcessorUptime.ReadAsync (ProcessorHost, credential, settings.SshFingerprint, TimeSpan.FromSeconds (30), token);
    var device = await configuration.GetDeviceAsync (DeviceId, token) ?? throw new InvalidDataException ("WeatherLink device is missing.");
    evidence["driverReadUtc"] = DateTimeOffset.UtcNow;
    evidence["driverRawValues"] = RawDisplayValues (device.PropertyValues);
    VerifyDevice (device, settings.Input, ProcessorClock.Now (uptime), out var displays);
    evidence["driverValues"] = displays;
    evidence["forecastSummary"] = displays.ForecastSummary;
    evidence["forecastUpdatedSummary"] = displays.ForecastUpdatedSummary;
    if (!baseline.Contains (uptime, TimeSpan.FromSeconds (FixedClockToleranceSeconds)))
        throw new InvalidDataException ("Processor boot window differs from the original baseline.");
    var taskstat = await LifetimeReader.ReadTaskstatAsync (settings, token);
    if (taskstat.ParentPid != baseline.ParentPid || !taskstat.Children.SetEquals (baseline.ChildPids))
        throw new InvalidDataException ("Managed driver process identity changed.");
    runtime.MarkLifetimeVerified (baseline.Identity);
    evidence["bootIdentity"] = runtime.BootIdentity;
    evidence["bootWindowVerified"] = true;
    evidence["bootEarliestUtc"] = baseline.BootEarliestUtc;
    evidence["bootLatestUtc"] = baseline.BootLatestUtc;
    evidence["processorLocalObserved"] = ProcessorClock.Now (uptime).ToString ("O", CultureInfo.InvariantCulture);
    evidence["parentPid"] = taskstat.ParentPid;
    evidence["managedChildPids"] = taskstat.Children.Order ().ToArray ();
    using var station = new WeatherLinkLiveAPI.WeatherLinkLive (settings.StationAddress, 30, 120, true, true, true, true);
    await station.InitializeAsync (token);
    var stationValues = new StationValues (station.Temperature, station.Humidity, station.WindSpeedLast, station.RainRate,
        station.BarometerAtSeaLevel * 1.33322387415);
    evidence["stationReadUtc"] = DateTimeOffset.UtcNow;
    evidence["stationValues"] = stationValues;
    evidence["comparison"] = WeatherComparison.Describe ();
    WeatherComparison.Validate (displays, stationValues);
    var payload = await DriverPayloadInspection.CompareAsync (ProcessorHost, credential, settings.SshFingerprint,
        settings.PackagePath, PackageSha256, settings.CatalogueId, TimeSpan.FromMinutes (2), token);
    evidence["deviceId"] = DeviceId;
    evidence["driverVersion"] = DriverVersion;
    evidence["payloadFileCount"] = payload.Files.Count;
    evidence["metricValidity"] = new { Temperature = "finite", Humidity = "finite, 0-100 percent", Wind = "finite, nonnegative",
        Rain = "finite, nonnegative", Barometer = "finite, nonnegative" };
    evidence["updatedAgeSeconds"] = Freshness.AgeSeconds (Text (device.PropertyValues, "tileStatus"), ProcessorClock.Now (uptime));
    evidence["lifetimeLimit"] = "TASKSTAT samples PID identities; PID reuse between samples is not cryptographically detectable.";
    }

static void VerifyDevice (DeviceInfo device, ProducerSettingsInput settings, DateTime processorLocalNow, out DisplayValues values)
    {
    int DeviceId = settings.DeviceId, LocationId = settings.LocationId;
    string DriverName = settings.DriverName, DriverModel = settings.DriverModel, DriverVersion = settings.DriverVersion;
    if (device.Id != DeviceId || device.Name != DriverName || device.Model != DriverModel || device.LocationId != LocationId)
        throw new InvalidDataException ("WeatherLink instance identity changed.");
    var p = device.PropertyValues;
    if (Text (p, "cp.driverInformation:version") != DriverVersion || Text (p, "cp.driverConfiguration:driverLoadingStatus") != "Loaded" ||
        !Bool (p, "cp.driverConfiguration:isConfigured") || !Bool (p, "onlineIndicator:isOnline") || !Bool (p, "readyIndicator:isReady"))
        throw new InvalidDataException ("WeatherLink loaded, configured, online, or ready state failed.");
    if (!Text (p, "sourceSummary").StartsWith ("Source: WeatherLink Live +", StringComparison.Ordinal))
        throw new InvalidDataException ("WeatherLink no longer reports its local station source.");
    var updated = Text (p, "tileStatus");
    if (!Regex.IsMatch (updated, @"^Updated\s+\d{2}:\d{2}$", RegexOptions.CultureInvariant))
        throw new InvalidDataException ("WeatherLink current observation has no parseable update time.");
    if (!Freshness.IsCurrent (updated, processorLocalNow, TimeSpan.FromSeconds (FixedMaximumFreshnessSeconds)))
        throw new InvalidDataException ("WeatherLink current observation is stale.");
    var temperature = Text (p, "currentTemperatureDisplay"); var humidity = Text (p, "humiditySummary");
    var wind = Text (p, "windSummary"); var rain = Text (p, "rainRateSummary"); var pressure = Text (p, "pressureSummary");
    var forecast = Text (p, "forecastSummary");
    if (!temperature.EndsWith ("°C", StringComparison.Ordinal) || !humidity.EndsWith ("%", StringComparison.Ordinal) ||
        !wind.EndsWith ("kph", StringComparison.Ordinal) || !rain.EndsWith ("mm/hr", StringComparison.Ordinal) ||
        !pressure.Contains ("hPa", StringComparison.Ordinal) || string.IsNullOrWhiteSpace (forecast) ||
        forecast.StartsWith ("No forecast", StringComparison.OrdinalIgnoreCase) || forecast.StartsWith ("Forecast unavailable", StringComparison.OrdinalIgnoreCase) ||
        !Regex.IsMatch (Text (p, "forecastUpdatedSummary"), @"^Forecast updated\s+\d{2}:\d{2}$", RegexOptions.CultureInvariant))
        throw new InvalidDataException ("WeatherLink metric display units or forecast availability changed.");
    values = new (DisplayParser.Number (temperature), DisplayParser.Number (humidity), DisplayParser.Number (wind),
        DisplayParser.Number (rain), DisplayParser.Number (pressure), forecast, Text (p, "forecastUpdatedSummary"));
    }

static string Text (IReadOnlyDictionary<string, JsonElement> values, string key) =>
    values.TryGetValue (key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString () ?? "" : "";
static bool Bool (IReadOnlyDictionary<string, JsonElement> values, string key) =>
    values.TryGetValue (key, out var value) && value.ValueKind is JsonValueKind.True;
static IReadOnlyDictionary<string, string> RawDisplayValues (IReadOnlyDictionary<string, JsonElement> values) =>
    new Dictionary<string, string>
        {
        ["temperature"] = Text (values, "currentTemperatureDisplay"),
        ["humidity"] = Text (values, "humiditySummary"),
        ["wind"] = Text (values, "windSummary"),
        ["rain"] = Text (values, "rainRateSummary"),
        ["barometer"] = Text (values, "pressureSummary"),
        ["updated"] = Text (values, "tileStatus"),
        ["source"] = Text (values, "sourceSummary")
        };
static string SecretSafeReason (Exception error) => error switch
    {
    InvalidDataException => error.Message,
    OperationCanceledException => "probe-cancelled-or-timed-out",
    TimeoutException => "probe-timed-out",
    _ => "probe-observation-failed"
    };

    }

sealed record ProducerSettingsInput (string StationAddress, string CredentialBindings, string PackagePath,
    string CatalogueId, string ProcessorHost, int DeviceId, int LocationId, string DriverName,
    string DriverModel, string DriverVersion, SubmissionEvidenceIdentity Identity, string InstallationIdentity,
    string RequirementId, string BaselineFile)
    {
    public void Validate ()
        {
        if (DeviceId <= 0 || LocationId <= 0 || string.IsNullOrWhiteSpace (ProcessorHost) ||
            string.IsNullOrWhiteSpace (StationAddress) || string.IsNullOrWhiteSpace (CatalogueId) ||
            string.IsNullOrWhiteSpace (DriverName) || string.IsNullOrWhiteSpace (DriverModel) ||
            string.IsNullOrWhiteSpace (DriverVersion) || string.IsNullOrWhiteSpace (InstallationIdentity) ||
            string.IsNullOrWhiteSpace (RequirementId) || !Path.IsPathFullyQualified (PackagePath) ||
            !Path.IsPathFullyQualified (CredentialBindings) || !Path.IsPathFullyQualified (BaselineFile))
            throw new InvalidDataException ("Complete the reviewed producer settings before publication.");
        string published = Path.TrimEndingDirectorySeparator (Path.GetFullPath (AppContext.BaseDirectory)) + Path.DirectorySeparatorChar;
        if (Path.GetFullPath (BaselineFile).StartsWith (published, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException ("Mutable lifetime state must be outside the pinned producer.");
        }
    }
sealed record ProducerSettings (ProducerSettingsInput Input, DevToolsStoredCredential Credential)
    {
    public string StationAddress => Input.StationAddress;
    public string ProcessorHost => Input.ProcessorHost;
    public string PackagePath => Input.PackagePath;
    public string CatalogueId => Input.CatalogueId;
    public string UserName => Credential.UserName;
    public string Password => Credential.Password;
    public string SshFingerprint => Credential.SshFingerprint!;
    public string CertificateSha256 => Credential.CertificateSha256!;
    public override string ToString () => "Private producer settings (values hidden)";
    }
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
sealed record DisplayValues (double Temperature, double Humidity, double Wind, double Rain, double Barometer,
    string ForecastSummary = "", string ForecastUpdatedSummary = "");
sealed record StationValues (double Temperature, double Humidity, double Wind, double Rain, double Barometer);
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

static class Freshness
    {
    internal static bool IsCurrent (string updated, DateTime processorLocalNow, TimeSpan maximumAge)
        {
        if (!Regex.IsMatch (updated, @"^Updated\s+\d{2}:\d{2}$", RegexOptions.CultureInvariant) || maximumAge <= TimeSpan.Zero) return false;
        var time = DateTime.ParseExact (updated[8..], "HH:mm", CultureInfo.InvariantCulture);
        var today = new DateTime (processorLocalNow.Year, processorLocalNow.Month, processorLocalNow.Day, time.Hour, time.Minute, 0, DateTimeKind.Unspecified);
        var shown = new[] { today, today.AddDays (-1) }.MinBy (value => Math.Abs ((processorLocalNow - value).TotalSeconds));
        return Math.Abs ((processorLocalNow - shown).TotalSeconds) <= maximumAge.TotalSeconds;
        }
    internal static double AgeSeconds (string updated, DateTime processorLocalNow)
        {
        if (!Regex.IsMatch (updated, @"^Updated\s+\d{2}:\d{2}$", RegexOptions.CultureInvariant)) return double.NaN;
        var time = DateTime.ParseExact (updated[8..], "HH:mm", CultureInfo.InvariantCulture);
        var today = new DateTime (processorLocalNow.Year, processorLocalNow.Month, processorLocalNow.Day, time.Hour, time.Minute, 0, DateTimeKind.Unspecified);
        var shown = new[] { today, today.AddDays (-1) }.MinBy (value => Math.Abs ((processorLocalNow - value).TotalSeconds));
        return Math.Abs ((processorLocalNow - shown).TotalSeconds);
        }
    }

static class ProcessorClock
    {
    // ProcessorUptime supplies its console's local start timestamp plus the precise elapsed duration.
    internal static DateTime Now (ProcessorUptimeSnapshot uptime) => uptime.LocalStartedAt.Add (uptime.Uptime);
    }

static class DisplayParser
    {
    internal static double Number (string value)
        {
        var match = Regex.Match (value, @"[-+]?\d+(?:[.,]\d+)?", RegexOptions.CultureInvariant);
        if (!match.Success || !double.TryParse (match.Value.Replace (',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var result) ||
            !double.IsFinite (result)) throw new InvalidDataException ("WeatherLink display has no finite numeric value.");
        return result;
        }
    }

static class WeatherComparison
    {
    internal static object Describe () => new
        {
        equalityCompared = Array.Empty<string> (),
        validityOnly = new[] { "temperature", "humidity", "wind", "rain", "barometer" },
        rationale = "The driver can retain local station values until its configured refresh, while the independent observation is sampled later. The two samples do not share a timestamp, so all weather values are retained as diagnostics and checked only for finite, valid metric ranges; this is not a numerical-accuracy comparison."
        };
    internal static void Validate (DisplayValues displays, StationValues station)
        {
        Finite ("temperature", displays.Temperature, station.Temperature);
        Finite ("humidity", displays.Humidity, station.Humidity);
        Finite ("wind", displays.Wind, station.Wind);
        Finite ("rain", displays.Rain, station.Rain);
        Finite ("barometer", displays.Barometer, station.Barometer);
        NonNegative ("humidity", displays.Humidity, station.Humidity);
        NonNegative ("wind", displays.Wind, station.Wind);
        NonNegative ("rain", displays.Rain, station.Rain);
        NonNegative ("barometer", displays.Barometer, station.Barometer);
        HumidityRange (displays.Humidity, station.Humidity);
        }
    private static void Finite (string name, double displayed, double observed)
        {
        if (!double.IsFinite (displayed) || !double.IsFinite (observed))
            throw new InvalidDataException ($"WeatherLink {name} has a nonfinite value.");
        }
    private static void NonNegative (string name, double displayed, double observed)
        {
        if (displayed < 0 || observed < 0)
            throw new InvalidDataException ($"WeatherLink {name} has a negative value.");
        }
    private static void HumidityRange (double displayed, double observed)
        {
        if (displayed > 100 || observed > 100)
            throw new InvalidDataException ("WeatherLink humidity is outside its percentage range.");
        }
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

static class OfflineChecks
    {
    public static async Task RunAsync ()
        {
        await Task.CompletedTask;
        string Row(int pid,int parent,string name) => $"{pid} {parent} 0 0 0 0 0 0 0 0 0 0 {name}";
        var before = TaskstatBaseline.Parse (string.Join("\n",Row(4719,1,"SSP[1]"),Row(4810,4719,"mono-sgen"),Row(6007,4719,"mono-sgen"),Row(25467,4719,"mono-sgen")));
        var after = TaskstatBaseline.Parse (string.Join("\n",Row(4719,1,"SSP[1]"),Row(4810,4719,"mono-sgen"),Row(8153,4719,"mono-sgen"),Row(25467,4719,"mono-sgen")));
        if (before.ParentPid != 4719 || !before.Children.SetEquals ([4810, 6007, 25467])) throw new InvalidDataException ("Baseline TASKSTAT fixture parsed unexpectedly.");
        if (after.ParentPid != 4719 || !after.Children.SetEquals ([4810, 8153, 25467])) throw new InvalidDataException ("Reload TASKSTAT fixture parsed unexpectedly.");
        if (before.Children.SetEquals (after.Children)) throw new InvalidDataException ("Reload fixture did not reject a changed managed child set.");
        ExpectInvalid (() => TaskstatBaseline.Parse ("malformed"), "Malformed TASKSTAT unexpectedly parsed.");
        var baseline = new LifetimeBaseline (DateTimeOffset.Parse ("2026-09-20T10:00:00Z"), DateTimeOffset.Parse ("2026-09-20T10:00:02Z"), 1, [2], DateTimeOffset.UtcNow, 2, "test");
        var stable = new ProcessorUptimeSnapshot (TimeSpan.FromHours (1), DateTime.UtcNow, DateTimeOffset.Parse ("2026-09-20T11:00:00Z"), DateTimeOffset.Parse ("2026-09-20T11:00:01Z"));
        var changed = new ProcessorUptimeSnapshot (TimeSpan.FromHours (1), DateTime.UtcNow, DateTimeOffset.Parse ("2026-09-20T12:00:10Z"), DateTimeOffset.Parse ("2026-09-20T12:00:11Z"));
        if (!baseline.Contains (stable, TimeSpan.FromSeconds (2)) || baseline.Contains (changed, TimeSpan.FromSeconds (2))) throw new InvalidDataException ("Boot-window comparison test failed.");
        if (Math.Abs (DisplayParser.Number ("Temperature 10.2°C") - 10.2) > .001) throw new InvalidDataException ("Display numeric parser failed.");
        ExpectInvalid (() => DisplayParser.Number ("Temperature NaN°C"), "Nonfinite display unexpectedly parsed.");
        WeatherComparison.Validate (new (10, 90, 90, 0, 1010), new (20, 80, 0, 12, 990));
        ExpectInvalid (() => WeatherComparison.Validate (new (10, 90, 5, 0, 1010), new (double.NaN, 90, 5, 0, 1010)), "Nonfinite station value unexpectedly passed.");
        ExpectInvalid (() => WeatherComparison.Validate (new (10, 101, 5, 0, 1010), new (10, 90, 5, 0, 1010)), "Out-of-range humidity unexpectedly passed.");
        ExpectInvalid (() => WeatherComparison.Validate (new (10, 90, -1, 0, 1010), new (10, 90, 5, 0, 1010)), "Negative wind unexpectedly passed.");
        var runtime = new ProbeRuntimeState ();
        runtime.MarkLifetimeVerified ("lifetime:test");
        ExpectInvalid (() => WeatherComparison.Validate (new (10, 90, -1, 0, 1010), new (10, 90, 5, 0, 1010)), "Post-lifetime comparison failure unexpectedly passed.");
        if (runtime.BootIdentity != "lifetime:test") throw new InvalidDataException ("Verified lifetime identity was lost after a comparison failure.");
        var diagnostic = new Dictionary<string, object?>
            {
            ["driverReadUtc"] = DateTimeOffset.Parse ("2026-09-22T08:12:28Z"),
            ["driverValues"] = new DisplayValues (10, 90, 5, 0, 1010),
            ["stationReadUtc"] = DateTimeOffset.Parse ("2026-09-22T08:12:29Z"),
            ["stationValues"] = new StationValues (double.NaN, 90, 5, 0, 1010)
            };
        try { WeatherComparison.Validate ((DisplayValues)diagnostic["driverValues"]!, (StationValues)diagnostic["stationValues"]!); }
        catch (InvalidDataException error) { diagnostic["reason"] = error.Message; }
        var diagnosticJson = Encoding.UTF8.GetString (JsonSerializer.SerializeToUtf8Bytes (diagnostic, ProducerJson.CreateOptions ()));
        if (!diagnosticJson.Contains ("\"NaN\"", StringComparison.Ordinal) || !diagnosticJson.Contains ("driverReadUtc", StringComparison.Ordinal) ||
            !diagnosticJson.Contains ("stationReadUtc", StringComparison.Ordinal) || !diagnosticJson.Contains ("reason", StringComparison.Ordinal))
            throw new InvalidDataException ("Failure diagnostics did not preserve values, times, and reason.");
        var freshNow = new DateTime (2026, 9, 21, 12, 34, 30, DateTimeKind.Unspecified);
        if (!Freshness.IsCurrent ("Updated 12:34", freshNow, TimeSpan.FromMinutes (10)) ||
            Freshness.IsCurrent ("Updated 12:20", freshNow, TimeSpan.FromMinutes (10))) throw new InvalidDataException ("Freshness test failed.");
        var processorClock = ProcessorClock.Now (new ProcessorUptimeSnapshot (TimeSpan.FromHours (1).Add (TimeSpan.FromMinutes (17)),
            new DateTime (2026, 9, 21, 0, 0, 0, DateTimeKind.Unspecified), DateTimeOffset.Parse ("2026-09-21T00:17:00Z"), DateTimeOffset.Parse ("2026-09-21T00:17:01Z")));
        if (processorClock != new DateTime (2026, 9, 21, 1, 17, 0, DateTimeKind.Unspecified) ||
            !Freshness.IsCurrent ("Updated 01:17", processorClock, TimeSpan.FromMinutes (10))) throw new InvalidDataException ("Processor-local freshness test failed.");
        }
    private static void ExpectInvalid (Action action, string failure)
        {
        try { action (); }
        catch (InvalidDataException) { return; }
        throw new InvalidDataException (failure);
        }
    }
