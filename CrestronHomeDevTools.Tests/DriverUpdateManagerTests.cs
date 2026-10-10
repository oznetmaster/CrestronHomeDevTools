// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.
using System.Text.Json;
using NUnit.Framework;

namespace CrestronHomeDevTools.Tests;

[TestFixture]
public sealed class DriverUpdateManagerTests
    {
    private static readonly DriverUpdateTarget Target = new ("192.0.2.10", 443, 49000, new string ('A', 64));
    private string _root = null!;
    [SetUp] public void Setup () => _root = Path.Combine (TestContext.CurrentContext.WorkDirectory, "updates-" + Guid.NewGuid ().ToString ("N"));
    [TearDown] public void Cleanup () { if (Directory.Exists (_root)) Directory.Delete (_root, true); }

    [Test]
    public async Task InspectionUsesEligibilityAndDoesNotMutateOrExposePrivateFields ()
        {
        var wire = new Wire ();
        var report = await Inspect (wire);
        Assert.That (report.Drivers.Single ().Status, Is.EqualTo ("UpdateAvailable"));
        Assert.That (report.Drivers.Single ().Devices.Select (d => d.Id), Is.EqualTo (new[] { 10 }));
        Assert.That (report.UnresolvedDevices, Is.Empty);
        Assert.That (wire.Submitted, Is.Empty);
        Assert.That (JsonSerializer.Serialize (report), Does.Not.Contain ("secret-value"));
        Assert.That (wire.EligibilityQueries, Is.EqualTo (new[] { "driver-a" }));
        }

    [TestCase ("same", "Current")]
    [TestCase ("downgrade", "InstalledNewer")]
    [TestCase ("missing", "Unknown")]
    [TestCase ("unavailable", "Unknown")]
    [TestCase ("unsupported", "Unsupported")]
    [TestCase ("restart", "RebootRequired")]
    [TestCase ("unknown-restart", "Unknown")]
    [TestCase ("unknown-device", "Unknown")]
    [TestCase ("duplicate-device", "Unknown")]
    [TestCase ("catalogue-version", "Unknown")]
    public async Task ReportNeverGuessesUpgradeEligibility (string scenario, string status)
        {
        var wire = new Wire ();
        var e = wire.Eligibility["driver-a"]!;
        wire.Eligibility["driver-a"] = scenario switch
            {
            "same" => e with { AvailableDriverVersion = "1.0.000.0000" },
            "downgrade" => e with { AvailableDriverVersion = "0.9.0.0" },
            "missing" => null,
            "unsupported" => e with { IsSupportsSwapDriver = false },
            "restart" => e with { IsSwapDriverRequiresReboot = true },
            "unknown-restart" => e with { IsSwapDriverRequiresReboot = null },
            "unknown-device" => e with { EligibleDeviceIds = [10, 99] },
            "duplicate-device" => e with { EligibleDeviceIds = [10, 10] },
            "catalogue-version" => e with { AvailableDriverVersion = "3.0.0.0" },
            _ => e
            };
        wire.QueryFailure = scenario == "unavailable";
        Assert.That ((await Inspect (wire)).Drivers.Single ().Status, Is.EqualTo (status));
        Assert.That (wire.Submitted, Is.Empty);
        }

    [Test]
    public async Task TargetedSearchFindsInstalledModelBeyondBroadCatalogueLimit ()
        {
        var wire = new Wire { TruncateBroadCatalogue = true };
        var report = await Inspect (wire);
        Assert.That (report.Drivers.Single ().Status, Is.EqualTo ("UpdateAvailable"));
        Assert.That (report.UnresolvedDevices, Is.Empty);
        Assert.That (wire.Searches, Is.EqualTo (new[] { "Light" }));
        }

    [TestCase (true, "Current", 0)]
    [TestCase (false, "Unknown", 1)]
    public async Task CurrentDriverWithNullEligibilityRequiresMatchingIdentity (bool matchingDeveloper, string status, int unresolved)
        {
        var wire = new Wire ();
        wire.Catalogue["driver-a"] = wire.Catalogue["driver-a"] with { Version = "1.0.0.0", AdditionalFields = new () { ["ControlType"] = JsonSerializer.SerializeToElement ("tcpClient") } };
        wire.Devices[10].PropertyValues["cp.driverInformation:developer"] = JsonSerializer.SerializeToElement (matchingDeveloper ? "Example" : "Other");
        wire.Devices[10].PropertyValues["cp.driverInformation:controlType"] = JsonSerializer.SerializeToElement ("tcpclient");
        wire.Eligibility["driver-a"] = null;
        var report = await Inspect (wire);
        Assert.That (report.Drivers.Single ().Status, Is.EqualTo (status));
        Assert.That (report.UnresolvedDevices.Length, Is.EqualTo (unresolved));
        Assert.That (DriverUpdateManager.SelectUpdates (report, true, [], false), Is.Empty);
        Assert.That (wire.Submitted, Is.Empty);
        }

    [Test]
    public async Task MissingCatalogueCoverageIsExplicit ()
        {
        var wire = new Wire ();
        wire.Catalogue.Clear ();
        var report = await Inspect (wire);
        Assert.That (report.Drivers, Is.Empty);
        Assert.That (report.UnresolvedDevices.Single ().Id, Is.EqualTo (10));
        }

    [TestCase (false)]
    [TestCase (true)]
    public async Task AlternateVersionsAreNotAppliedTwice (bool ambiguous)
        {
        var wire = new Wire ();
        wire.Catalogue["driver-b"] = wire.Catalogue["driver-a"] with { Id = "driver-b", Version = ambiguous ? "2.0.0.0" : "3.0.0.0" };
        wire.Eligibility["driver-b"] = wire.Eligibility["driver-a"]! with { AvailableDriverVersion = wire.Catalogue["driver-b"].Version };
        var report = await Inspect (wire);
        var selected = DriverUpdateManager.SelectUpdates (report, true, [], false);
        Assert.That (selected.Length, Is.EqualTo (ambiguous ? 0 : 1));
        Assert.That (report.Drivers.Count (r => r.Status == "Conflict"), Is.EqualTo (ambiguous ? 2 : 0));
        if (!ambiguous) Assert.That (selected.Single ().DriverId, Is.EqualTo ("driver-b"));
        }

    [Test]
    public async Task AllExcludesRestartsUnlessExplicitlyAuthorized ()
        {
        var wire = new Wire ();
        wire.Eligibility["driver-a"] = wire.Eligibility["driver-a"]! with { IsSwapDriverRequiresReboot = true };
        var report = await Inspect (wire);
        Assert.That (DriverUpdateManager.SelectUpdates (report, true, [], false), Is.Empty);
        Assert.Throws<ArgumentException> (() => DriverUpdateManager.SelectUpdates (report, false, ["driver-a"], false));
        Assert.That (DriverUpdateManager.SelectUpdates (report, true, [], true).Length, Is.EqualTo (1));
        }

    [Test]
    public async Task SuccessfulBatchVerifiesVersionsAndRetainsEachStep ()
        {
        var wire = new Wire ();
        wire.AddSecondDriver ();
        var report = await Inspect (wire);
        var result = await Apply (wire, report);
        Assert.That (result.State, Is.EqualTo ("Completed"));
        Assert.That (result.SafeToReleaseReservation, Is.True);
        Assert.That (wire.Submitted, Is.EqualTo (new[] { "driver-a", "driver-b" }));
        Assert.That (result.Steps.All (s => s.State == "Updated"), Is.True);
        Assert.That (File.Exists (Path.Combine (_root, "001-completed.json")), Is.True);
        Assert.That (wire.Completed, Is.EqualTo (wire.Submitted));
        }

    [TestCase ("version")]
    [TestCase ("scope")]
    [TestCase ("room")]
    [TestCase ("catalogue")]
    public async Task ChangedReviewStopsWholeBatchBeforeFirstMutation (string change)
        {
        var wire = new Wire ();
        wire.AddSecondDriver ();
        var report = await Inspect (wire);
        switch (change)
            {
            case "version": wire.Eligibility["driver-b"] = wire.Eligibility["driver-b"]! with { AvailableDriverVersion = "9.0.0.0" }; break;
            case "scope": wire.Eligibility["driver-b"] = wire.Eligibility["driver-b"]! with { EligibleDeviceIds = [11, 12] }; break;
            case "room": wire.Devices[11] = wire.Devices[11] with { LocationId = 9 }; break;
            default: wire.Catalogue["driver-b"] = wire.Catalogue["driver-b"] with { Developer = "another developer" }; break;
            }
        var result = await Apply (wire, report);
        Assert.That (result.State, Is.EqualTo ("Stopped"));
        Assert.That (result.SafeToReleaseReservation, Is.True);
        Assert.That (wire.Submitted, Is.Empty);
        }

    [TestCase ("failed", true)]
    [TestCase ("timeout", false)]
    [TestCase ("submit-lost", false)]
    [TestCase ("unknown", false)]
    [TestCase ("identity", false)]
    public async Task StopsAfterFirstFailedOrUncertainOperationWithoutReplay (string fault, bool safe)
        {
        var wire = new Wire ();
        wire.AddSecondDriver ();
        var report = await Inspect (wire);
        wire.Fault = fault;
        var result = await Apply (wire, report);
        Assert.That (result.State, Is.EqualTo ("Stopped"));
        Assert.That (result.SafeToReleaseReservation, Is.EqualTo (safe));
        Assert.That (wire.Submitted, Is.EqualTo (new[] { "driver-a" }));
        Assert.That (File.Exists (Path.Combine (_root, "result.json")), Is.True);
        }

    [Test]
    public async Task WrongProcessorAndReusedJournalNeverSubmit ()
        {
        var wire = new Wire ();
        var report = await Inspect (wire);
        await Assert.ThrowsAsync<ArgumentException> (async () => await DriverUpdateManager.ApplyAsync (new (wire), Target with { Host = "192.0.2.11" }, report,
            ["driver-a"], _root, TimeSpan.FromSeconds (1)));
        Directory.CreateDirectory (_root);
        File.WriteAllText (Path.Combine (_root, "intent.json"), "original");
        await Assert.ThrowsAsync<IOException> (async () => await Apply (wire, report));
        Assert.That (wire.Submitted, Is.Empty);
        Assert.That (File.ReadAllText (Path.Combine (_root, "intent.json")), Is.EqualTo ("original"));
        }

    [Test]
    public async Task RebootUsesExplicitRecoveryCallbackAndVerifiesAfterIt ()
        {
        var wire = new Wire ();
        wire.Eligibility["driver-a"] = wire.Eligibility["driver-a"]! with { IsSwapDriverRequiresReboot = true };
        var report = await Inspect (wire);
        int before = 0, recovered = 0;
        var handler = new DriverRebootHandler ((_, _) => { before++; return Task.CompletedTask; },
            (request, client, _) => { Assert.That (request.SwapCompletion!.IsRebootRequired, Is.True); recovered++; wire.Complete ("driver-a"); return Task.FromResult (client); });
        var result = await DriverUpdateManager.ApplyAsync (new (wire), Target, report, ["driver-a"], _root, TimeSpan.FromSeconds (1), handler);
        Assert.That (result.State, Is.EqualTo ("Completed"));
        Assert.That ((before, recovered), Is.EqualTo ((1, 1)));
        }

    [Test]
    public async Task MultipleRestartsDisposeReplacedConnectionsButLeaveCallerConnectionOpen ()
        {
        var wire = new Wire ();
        wire.AddSecondDriver ();
        foreach (var id in new[] { "driver-a", "driver-b" })
            wire.Eligibility[id] = wire.Eligibility[id]! with { IsSwapDriverRequiresReboot = true };
        var report = await Inspect (wire);
        var initial = new ConfigurationClient (wire, ownsConnection: true);
        var firstRecovery = new Wire ();
        firstRecovery.AddSecondDriver ();
        firstRecovery.Eligibility["driver-b"] = firstRecovery.Eligibility["driver-b"]! with { IsSwapDriverRequiresReboot = true };
        firstRecovery.Complete ("driver-a");
        var secondRecovery = new Wire ();
        secondRecovery.AddSecondDriver ();
        secondRecovery.Complete ("driver-a");
        secondRecovery.Complete ("driver-b");
        int recoveryCount = 0;
        var handler = new DriverRebootHandler ((_, _) => Task.CompletedTask,
            (_, _, _) => Task.FromResult (new ConfigurationClient (++recoveryCount == 1 ? firstRecovery : secondRecovery, ownsConnection: true)));
        var result = await DriverUpdateManager.ApplyAsync (initial, Target, report, ["driver-a", "driver-b"], _root, TimeSpan.FromSeconds (1), handler);
        Assert.That (result.State, Is.EqualTo ("Completed"));
        Assert.That (wire.DisposeCount, Is.Zero);
        Assert.That (firstRecovery.DisposeCount, Is.EqualTo (1));
        Assert.That (secondRecovery.DisposeCount, Is.EqualTo (1));
        await initial.DisposeAsync ();
        }

    [Test]
    public async Task ForgedScopeAndDowngradeInReportAreRejected ()
        {
        var report = await Inspect (new Wire ());
        var row = report.Drivers[0];
        Assert.Throws<ArgumentException> (() => DriverUpdateManager.SelectUpdates (report with { Drivers = [row with { Devices = [row.Devices[0] with { Id = 999 }] }] }, true, [], false));
        Assert.Throws<ArgumentException> (() => DriverUpdateManager.SelectUpdates (report with { Drivers = [row with { Eligibility = row.Eligibility! with { AvailableDriverVersion = "0.5.0.0" } }] }, true, [], false));
        }

    [TestCase ("success")]
    [TestCase ("restart")]
    [TestCase ("failed")]
    public async Task ProgressOnlyReportsUpdatedAfterVerification (string scenario)
        {
        var wire = new Wire ();
        if (scenario == "restart") wire.Eligibility["driver-a"] = wire.Eligibility["driver-a"]! with { IsSwapDriverRequiresReboot = true };
        var report = await Inspect (wire);
        if (scenario == "failed") wire.Fault = "failed";
        var progress = new RecordedProgress ();
        DriverRebootHandler? handler = scenario == "restart" ? new ((_, _) => Task.CompletedTask,
            (_, client, _) => { wire.Complete ("driver-a"); return Task.FromResult (client); }) : null;
        var result = await DriverUpdateManager.ApplyAsync (new (wire), Target, report, ["driver-a"], _root, TimeSpan.FromSeconds (1), handler, progress: progress);
        var expected = scenario switch
            {
            "restart" => new[] { "Checking", "Updating", "Updating", "Restarting", "Verifying", "Updated" },
            "failed" => new[] { "Checking", "Updating" },
            _ => new[] { "Checking", "Updating", "Verifying", "Updated" }
            };
        Assert.That (progress.Items.Select (p => p.State), Is.EqualTo (expected));
        Assert.That (progress.Items.All (p => p.DriverId == "driver-a"), Is.True);
        Assert.That (result.State, Is.EqualTo (scenario == "failed" ? "Stopped" : "Completed"));
        }
    private sealed class RecordedProgress : IProgress<DriverUpdateProgress>
        {
        public List<DriverUpdateProgress> Items { get; } = [];
        public void Report (DriverUpdateProgress value) => Items.Add (value);
        }

    private static Task<DriverUpdateReport> Inspect (Wire wire) => DriverUpdateManager.InspectAsync (new (wire), Target);
    private Task<DriverUpdateBatchResult> Apply (Wire wire, DriverUpdateReport report) => DriverUpdateManager.ApplyAsync (new (wire), Target, report,
        report.Drivers.Where (r => r.Status == "UpdateAvailable").Select (r => r.DriverId).ToArray (), _root, TimeSpan.FromSeconds (1));

    private sealed class Wire : IConfigurationConnection
        {
        public readonly Dictionary<string, DriverInfo> Catalogue = [];
        public readonly Dictionary<string, DriverUpdateEligibility?> Eligibility = [];
        public readonly Dictionary<int, DeviceInfo> Devices = [];
        public readonly List<string> Submitted = [], Completed = [], EligibilityQueries = [];
        public string? Fault;
        public bool QueryFailure, TruncateBroadCatalogue;
        public readonly List<string> Searches = [];
        public int DisposeCount;
        public Wire ()
            {
            Add ("driver-a", "Light", 10);
            Catalogue["irrelevant"] = new () { Id = "irrelevant", Model = "Not installed", Version = "1.0.0.0" };
            }
        public void AddSecondDriver () => Add ("driver-b", "Other", 11);
        private void Add (string id, string model, int device)
            {
            Catalogue[id] = new () { Id = id, Model = model, Manufacturer = "Example", Developer = "Example", Version = "2.0.0.0", AvailabilityState = "Available" };
            Eligibility[id] = new () { InstalledDriverVersion = "1.0.0.0", AvailableDriverVersion = "2.0.0.0", IsSupportsSwapDriver = true,
                IsSwapDriverRequiresReboot = false, EligibleDeviceIds = [device], AdditionalFields = new () { ["password"] = JsonSerializer.SerializeToElement ("secret-value") } };
            Devices[device] = new () { Id = device, Model = model, Name = "Device " + device, LocationId = 1,
                PropertyValues = new () { ["cp.driverInformation:version"] = JsonSerializer.SerializeToElement ("1.0.0.0"), ["password"] = JsonSerializer.SerializeToElement ("secret-value") } };
            }
        public void Complete (string id)
            {
            Completed.Add (id);
            foreach (var device in Eligibility[id]!.EligibleDeviceIds!)
                {
                Devices[device].PropertyValues["cp.driverInformation:version"] = JsonSerializer.SerializeToElement (Eligibility[id]!.AvailableDriverVersion);
                Devices[device].PropertyValues["cp.driverConfiguration:driverLoadingStatus"] = JsonSerializer.SerializeToElement ("Loaded");
                if (Fault == "identity") Devices[device] = Devices[device] with { LocationId = 99 };
                }
            }
        public Task<T?> GetAsync<T> (string path, CancellationToken token) => Task.FromResult (Convert<T> (path == "v2/Devices"
            ? Devices.ToDictionary (d => d.Key.ToString (), d => d.Value) : (object)Devices[int.Parse (path.Split ('/').Last ())]));
        public Task<T?> ExecuteAsync<T> (int id, string command, object? parameters, CancellationToken token)
            {
            object? result;
            if (command.EndsWith (":getDriverMetadataFilterOptions", StringComparison.Ordinal)) result = new[] { new { Id = "Lighting" } };
            else if (command.EndsWith (":getDrivers", StringComparison.Ordinal))
                {
                var tokens = JsonSerializer.SerializeToElement (parameters).GetProperty ("substringFilterTextTokens").EnumerateArray ().Select (x => x.GetString ()!).ToArray ();
                Searches.Add (string.Join (" ", tokens));
                result = TruncateBroadCatalogue && tokens.Length == 0 ? Array.Empty<DriverInfo> () : Catalogue.Values.Where (d => tokens.All (t => d.Model!.Contains (t, StringComparison.OrdinalIgnoreCase))).ToArray ();
                }
            else
                {
                var key = JsonSerializer.SerializeToElement (parameters).GetProperty ("driverId").GetString ()!;
                if (command.EndsWith (":getDriver", StringComparison.Ordinal)) result = Catalogue[key];
                else if (command.EndsWith (":getDevicesEligibleForDriverUpdate", StringComparison.Ordinal))
                    { EligibilityQueries.Add (key); if (QueryFailure) throw new ProcessorApiException ("secret-value"); result = Eligibility[key]; }
                else if (command.EndsWith (":beginSwapDriverForAllEligibleDevices", StringComparison.Ordinal))
                    { Submitted.Add (key); if (Fault == "submit-lost") throw new IOException ("lost"); result = key; }
                else throw new InvalidOperationException (command);
                }
            return Task.FromResult (Convert<T> (result));
            }
        public Task<OperationResult> WaitForOperationAsync (string operation, TimeSpan timeout, CancellationToken token)
            {
            if (Fault == "timeout") throw new TimeoutException ();
            if (Fault == "failed") return Task.FromResult (new OperationResult (operation, "Failed", "private"));
            if (Fault == "unknown") return Task.FromResult (new OperationResult (operation, "Unknown", null));
            Complete (operation);
            return Task.FromResult (new OperationResult (operation, "Succeeded", null));
            }
        public Task<DriverSwapResult> WaitForDriverSwapAsync (string operation, string driver, TimeSpan timeout, CancellationToken token)
            => Task.FromResult (new DriverSwapResult (operation, driver, true, []));
        private static T? Convert<T> (object? value) => value == null ? default : JsonSerializer.Deserialize<T> (JsonSerializer.Serialize (value));
        public ValueTask DisposeAsync () { DisposeCount++; return ValueTask.CompletedTask; }
        }
    }
