// Copyright (c) 2026 Neil Colvin. Licensed under the MIT License.
using System.Net;
using System.Text.Json;
using CrestronHomeDevTools;
using KasaTapoClient;

try { await Producer.RunAsync(args); }
catch (Exception e) when (e is not OutOfMemoryException)
{
    Console.Error.WriteLine($"Producer stopped ({e.GetType().Name}); no observation accepted.");
    Environment.ExitCode = 2;
}

static class Producer
{
    public static async Task RunAsync(string[] args)
    {
        if (args.SequenceEqual(["--self-test"])) { OfflineChecks.Run(); Console.WriteLine("self-test-passed"); return; }
        if (args.Length != 0) throw new ArgumentException("Use a probe request on standard input.");
        var json = ProducerJson.CreateOptions();
        var request = JsonSerializer.Deserialize<SubmissionEnduranceProbeRequest>(await Console.In.ReadToEndAsync(), json)
            ?? throw new InvalidDataException("Missing request.");
        if (request.SchemaVersion != 1) throw new InvalidDataException("Unsupported request.");
        var evidence = new Dictionary<string, object?> { ["schemaVersion"] = 1, ["startedUtc"] = DateTimeOffset.UtcNow };
        var runtime = new ProbeRuntimeState();
        var outcome = SubmissionEvidenceOutcome.Failed;
        try
        {
            evidence["phase"] = "validate-plan";
            SubmissionEndurance.ValidatePlan(request.Plan);
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
            string file = request.SettingsFile ?? throw new InvalidDataException("Missing settings.");
            string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(AppContext.BaseDirectory)) + Path.DirectorySeparatorChar;
            if (!Path.IsPathFullyQualified(file) || !Path.GetFullPath(file).StartsWith(root, StringComparison.OrdinalIgnoreCase) || new FileInfo(file).Length > 65536)
                throw new InvalidDataException("Settings must be inside the pinned producer.");
            var input = JsonSerializer.Deserialize<ProducerSettingsInput>(await File.ReadAllTextAsync(file), json)
                ?? throw new InvalidDataException("Missing settings.");
            input.Validate();
            if (request.Plan.Identity != input.Identity || request.Plan.InstallationIdentity != input.InstallationIdentity ||
                request.Plan.Requirement.Id != input.RequirementId || request.Plan.ProcessorIdentity != $"processor:{input.ProcessorHost}")
                throw new InvalidDataException("Settings differ from the frozen plan.");
            var saved = DevToolsCredentialBindings.Read(input.CredentialBindings).Resolve(DevToolsCredentialPurpose.Processor, input.ProcessorHost);
            if (string.IsNullOrWhiteSpace(saved.SshFingerprint) || string.IsNullOrWhiteSpace(saved.CertificateSha256))
                throw new InvalidDataException("Processor trust pins are required.");
            var settings = new ProducerSettings(input, saved);
            using var timeout = new CancellationTokenSource(request.Plan.ProbeTimeout);
            evidence["phase"] = "lifetime-baseline";
            var baseline = await LifetimeBaselineStore.ReadOrCreateAsync(settings, request.Plan, timeout.Token);
            var credential = new NetworkCredential(saved.UserName, saved.Password);
            await using var api = await ConfigurationClient.ConnectAsync(new() { Host = input.ProcessorHost, CertificateSha256 = saved.CertificateSha256 }, credential, timeout.Token);
            evidence["phase"] = "lifetime-observation";
            var uptime = await ProcessorUptime.ReadAsync(input.ProcessorHost, credential, saved.SshFingerprint, TimeSpan.FromSeconds(30), timeout.Token);
            if (!baseline.Contains(uptime, TimeSpan.FromSeconds(2))) throw new InvalidDataException("Processor boot window changed.");
            var lifetime = await LifetimeReader.ReadTaskstatAsync(settings, timeout.Token);
            if (lifetime.ParentPid != baseline.ParentPid || !lifetime.Children.SetEquals(baseline.ChildPids))
                throw new InvalidDataException("Managed driver process identity changed.");
            runtime.MarkLifetimeVerified(baseline.Identity);
            evidence["lifetime"] = baseline;
            evidence["phase"] = "platform-identity";
            var platform = await api.GetDeviceAsync(input.DeviceId, timeout.Token) ?? throw new InvalidDataException("Platform missing.");
            VerifyIdentity(platform, input.DriverName, input.DriverModel, input.LocationId, input.DriverVersion);
            var observed = new List<object>();
            foreach (var child in input.Children)
            {
                evidence["phase"] = "child-state";
                evidence["childIndex"] = observed.Count;
                var device = await api.GetDeviceAsync(child.DeviceId, timeout.Token) ?? throw new InvalidDataException("Selected child missing.");
                if (device.ParentDeviceId != input.DeviceId) throw new InvalidDataException("Selected child belongs to another platform.");
                VerifyIdentity(device, child.Name, child.Model, child.LocationId, input.DriverVersion);
                var values = new Dictionary<string, JsonElement>();
                foreach (var property in child.Properties)
                {
                    if (!device.PropertyValues.TryGetValue(property.Name, out var value)) throw new InvalidDataException("Selected property missing.");
                    property.Verify(value);
                    values.Add(property.Name, value.Clone());
                }
                observed.Add(new { child.Alias, child.DeviceId, Values = values, ObservedUtc = DateTimeOffset.UtcNow });
            }
            evidence["children"] = observed;
            evidence["phase"] = "independent-outlets";
            evidence["independentOutlets"] = await ReadOutletsAsync(input, timeout.Token);
            evidence["phase"] = "candidate-payload";
            var payload = await DriverPayloadInspection.CompareAsync(input.ProcessorHost, credential, saved.SshFingerprint,
                input.PackagePath, input.Identity.PackageSha256, input.CatalogueId, TimeSpan.FromMinutes(2), timeout.Token);
            evidence["payloadFileCount"] = payload.Files.Count;
            evidence["comparison"] = "Independent outlet observations and driver values have separate timestamps. This validates identity, availability and value types/ranges, not synchronized equality or measurement freshness. No commands are sent.";
            evidence["lifetimeLimit"] = "TASKSTAT checks the host's managed PID set; PID reuse between samples is not cryptographically detectable.";
            evidence["phase"] = "complete";
            outcome = SubmissionEvidenceOutcome.Passed;
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            // External libraries may put credentials in their exception text.
            evidence["reason"] = e is OperationCanceledException ? "probe-cancelled-or-timed-out" : "probe-observation-failed";
            evidence["errorType"] = e.GetType().Name;
        }
        var result = new SubmissionEnduranceProbeResult(request.Plan.Identity, request.Plan.ProcessorIdentity, request.Plan.InstallationIdentity,
            request.Plan.ReservationId, request.Plan.ProducerId, runtime.BootIdentity, outcome, JsonSerializer.SerializeToUtf8Bytes(evidence, json));
        Console.Write(JsonSerializer.Serialize(result, json));
    }

    internal static void VerifyIdentity(DeviceInfo device, string name, string model, int room, string version)
    {
        if (device.Name != name || device.Model != model || device.LocationId != room) throw new InvalidDataException("Installed identity changed.");
        var values = device.PropertyValues;
        if (!values.TryGetValue("cp.driverInformation:version", out var actualVersion) || actualVersion.GetString() != version ||
            !values.TryGetValue("cp.driverConfiguration:driverLoadingStatus", out var loaded) || loaded.GetString() != "Loaded" ||
            !values.TryGetValue("cp.driverConfiguration:isConfigured", out var configured) || configured.ValueKind != JsonValueKind.True ||
            !values.TryGetValue("onlineIndicator:isOnline", out var online) || online.ValueKind != JsonValueKind.True ||
            !values.TryGetValue("readyIndicator:isReady", out var ready) || ready.ValueKind != JsonValueKind.True)
            throw new InvalidDataException("Loaded, configured, online or ready check failed.");
    }

    private static async Task<IReadOnlyList<object>> ReadOutletsAsync(ProducerSettingsInput input, CancellationToken token)
    {
        // The existing restricted fixture file supplies credentials only. Physical
        // identities are pinned separately in the immutable producer settings.
        if (new FileInfo(input.DeviceCredentialsFile).Length > 65536) throw new InvalidDataException("Credential file too large.");
        using var privateJson = JsonDocument.Parse(await File.ReadAllTextAsync(input.DeviceCredentialsFile, token));
        var credentials = privateJson.RootElement.GetProperty("credentials");
        var login = new DeviceCredentials(credentials.GetProperty("userName").GetString(), credentials.GetProperty("password").GetString());
        var discovered = await Discover.DiscoverAsync(TimeSpan.FromSeconds(3), cancellationToken: token);
        var result = new List<object>();
        foreach (var outlet in input.Outlets)
        {
            var matches = discovered.Where(d => string.Equals(d.DeviceId, outlet.DiscoveryId, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (matches.Length == 0 || matches.Select(d => d.Host).Distinct(StringComparer.OrdinalIgnoreCase).Count() != 1)
                throw new InvalidDataException("Physical identity unavailable or ambiguous.");
            var selected = matches.OrderByDescending(d => d.TpapPreferred == true || d.TpapMetadata != null).First();
            using var physical = await Discover.ConnectAsync(Discover.CreateConfiguration(selected, login, TimeSpan.FromSeconds(15)), token);
            if (!string.Equals(physical.SystemInfo?.DeviceId, outlet.AuthenticatedId, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Authenticated physical identity changed.");
            bool? power = outlet.ChildId == null ? physical.IsOn : physical.GetChild(outlet.ChildId)?.IsOn;
            if (power == null) throw new InvalidDataException("Physical outlet state unavailable.");
            result.Add(new { outlet.Alias, Power = power.Value, ObservedUtc = DateTimeOffset.UtcNow });
        }
        return result;
    }
}

sealed record PropertyRule(string Name, string Kind, double? Minimum = null, double? Maximum = null)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Name) || Kind is not ("boolean" or "number" or "text") ||
            (Kind == "number" ? Minimum == null || Maximum == null || !double.IsFinite(Minimum.Value) || !double.IsFinite(Maximum.Value) || Minimum > Maximum : Minimum != null || Maximum != null))
            throw new InvalidDataException("Invalid property rule.");
    }
    public void Verify(JsonElement value)
    {
        Validate();
        bool valid = Kind switch
        {
            "boolean" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
            "text" => value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString()),
            "number" => value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out double n) && double.IsFinite(n) && n >= Minimum && n <= Maximum,
            _ => false
        };
        if (!valid) throw new InvalidDataException("Selected property failed its type/range rule.");
    }
}
sealed record ChildTarget(string Alias, int DeviceId, string Model, string Name, int LocationId, PropertyRule[] Properties);
sealed record OutletTarget(string Alias, string DiscoveryId, string AuthenticatedId, string? ChildId);
sealed record ProducerSettingsInput(string CredentialBindings, string DeviceCredentialsFile, string PackagePath,
    string CatalogueId, string ProcessorHost, int DeviceId, int LocationId, string DriverName, string DriverModel, string DriverVersion,
    SubmissionEvidenceIdentity Identity, string InstallationIdentity, string RequirementId, string BaselineFile, ChildTarget[] Children, OutletTarget[] Outlets)
{
    public void Validate()
    {
        if (DeviceId <= 0 || LocationId <= 0 || new[] { CatalogueId, ProcessorHost, DriverName, DriverModel, DriverVersion, InstallationIdentity, RequirementId }.Any(string.IsNullOrWhiteSpace) ||
            new[] { CredentialBindings, DeviceCredentialsFile, PackagePath, BaselineFile }.Any(p => !Path.IsPathFullyQualified(p)))
            throw new InvalidDataException("Incomplete producer settings.");
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(AppContext.BaseDirectory)) + Path.DirectorySeparatorChar;
        if (Path.GetFullPath(BaselineFile).StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Mutable baseline must be outside the producer.");
        if (Children.Length == 0 || Children.Any(c => c.DeviceId <= 0 || c.DeviceId == DeviceId || c.LocationId <= 0 || string.IsNullOrWhiteSpace(c.Alias) ||
                string.IsNullOrWhiteSpace(c.Model) || string.IsNullOrWhiteSpace(c.Name) || c.Properties.Length == 0 || c.Properties.Select(p => p.Name).Distinct().Count() != c.Properties.Length) ||
            Children.Select(c => c.DeviceId).Distinct().Count() != Children.Length || Children.Select(c => c.Alias).Distinct().Count() != Children.Length ||
            Outlets.Length == 0 || Outlets.Any(o => !Children.Any(c => c.Alias == o.Alias) || string.IsNullOrWhiteSpace(o.DiscoveryId) || string.IsNullOrWhiteSpace(o.AuthenticatedId) || o.ChildId is not null && string.IsNullOrWhiteSpace(o.ChildId)) ||
            Outlets.Select(o => o.Alias).Distinct().Count() != Outlets.Length)
            throw new InvalidDataException("Explicit distinct child/physical bindings are required.");
        foreach (var rule in Children.SelectMany(c => c.Properties)) rule.Validate();
    }
}
sealed record ProducerSettings(ProducerSettingsInput Input, DevToolsStoredCredential Credential)
{
    public string ProcessorHost => Input.ProcessorHost;
    public string UserName => Credential.UserName;
    public string Password => Credential.Password;
    public string SshFingerprint => Credential.SshFingerprint!;
    public override string ToString() => "Private producer settings (values hidden)";
}

static class OfflineChecks
{
    public static void Run()
    {
        void Invalid(Action action) { try { action(); } catch (InvalidDataException) { return; } throw new Exception("Invalid observation accepted."); }
        JsonElement Json(string text) { using var doc = JsonDocument.Parse(text); return doc.RootElement.Clone(); }
        new PropertyRule("power", "boolean").Verify(Json("false"));
        Invalid(() => new PropertyRule("power", "boolean").Verify(Json("\"false\"")));
        new PropertyRule("humidity", "number", 0, 100).Verify(Json("52.3"));
        Invalid(() => new PropertyRule("humidity", "number", 0, 100).Verify(Json("101")));
        Invalid(() => new PropertyRule("humidity", "number", 0, 100).Verify(Json("null")));
        Invalid(() => new PropertyRule("status", "text").Verify(Json("\" \"")));
        Invalid(() => new PropertyRule("value", "number", double.NaN, 100).Validate());
        string Row(int pid, int parent, string name) => $"{pid} {parent} 0 0 0 0 0 0 0 0 0 0 {name}";
        var before = TaskstatBaseline.Parse(Row(100, 1, "SSP[1]") + "\n" + Row(200, 100, "mono-sgen"));
        var after = TaskstatBaseline.Parse(Row(100, 1, "SSP[1]") + "\n" + Row(201, 100, "mono-sgen"));
        if (before.Children.SetEquals(after.Children)) throw new Exception("Process replacement not detected.");
        Invalid(() => TaskstatBaseline.Parse("malformed"));
    }
}

