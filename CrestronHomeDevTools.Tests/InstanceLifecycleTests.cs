// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Text.Json;

using NUnit.Framework;

namespace CrestronHomeDevTools.Tests;

[TestFixture]
public sealed class InstanceLifecycleTests
	{
	private static DriverInfo Catalogue => new () { Id = "catalogue", Model = "Example Tests", Version = "1.1" };
	private static DeviceInfo Device (string version) => new ()
		{
		Id = 17,
		Model = "Example Tests",
		Name = "Example Tests",
		LocationId = 12,
		PropertyValues = new ()
			{
			["cp.driverInformation:version"] = JsonSerializer.SerializeToElement (version),
			["cp.driverConfiguration:driverLoadingStatus"] = JsonSerializer.SerializeToElement ("Loaded")
			}
		};
	private static Dictionary<string, DeviceInfo> Inventory (string version) => new () { ["17"] = Device (version) };
	private static DriverUpdateEligibility Eligible (params int[] ids) => new () { InstalledDriverVersion = "1.0", AvailableDriverVersion = "1.1", IsSupportsSwapDriver = true, IsSwapDriverRequiresReboot = false, EligibleDeviceIds = ids };

	[Test]
	public async Task InstallsWhenInstanceIsAbsentAndConfirmsLoadedVersion ()
		{
		var transport = new Fake (Catalogue, new Dictionary<string, DeviceInfo> (), "Success", new
			{
			Id = 17,
			CommissioningResult = "Success"
			}, Device ("1.1"));
		var ready = await Run (transport);
		Assert.That (ready.Action, Is.EqualTo ("Installed"));
		Assert.That (ready.DeviceId, Is.EqualTo (17));
		Assert.That (transport.Commands, Does.Contain ("cp.platformDriverController:commissionDevice"));
		}

	[Test]
	public async Task CorrectLoadedVersionDoesNotReinstallOrUpdate ()
		{
		var transport = new Fake (Catalogue, Inventory ("1.1"), Device ("1.1"));
		Assert.That ((await Run (transport)).Action, Is.EqualTo ("Existing"));
		Assert.That (transport.Commands, Is.EqualTo (new[] { "cp.platformDriverController:getDriver" }));
		}

	[TestCase ("{\"CommissioningResult\":\"Failed\",\"Id\":0}")]
	[TestCase ("{\"CommissioningResult\":\"Success\",\"Id\":\"17\"}")]
	[TestCase ("{\"CommissioningResult\":\"Success\"}")]
	[TestCase ("null")]
	public void UnconfirmedInstallationRetainsReplyWithoutRepeatingTheCommand (string responseJson)
		{
		var response = JsonSerializer.Deserialize<JsonElement> (responseJson);
		var transport = new Fake (Catalogue, new Dictionary<string, DeviceInfo> (), "Success", response);
		var exception = Assert.ThrowsAsync<ProcessorApiException> (async () => await Run (transport))!;
		Assert.That (exception.DiagnosticCommand, Is.EqualTo ("cp.platformDriverController:commissionDevice"));
		Assert.That (exception.DiagnosticResponse.HasValue, Is.True);
		Assert.That (JsonElement.DeepEquals (exception.DiagnosticResponse!.Value, response), Is.True);
		Assert.That (transport.Commands.Count (command => command.EndsWith (":commissionDevice")), Is.EqualTo (1));
		Assert.That (transport.Commands.Count, Is.EqualTo (3));
		}

	[Test]
	public void FailedPreparationRetainsReplyAndNeverCommissions ()
		{
		var transport = new Fake (Catalogue, new Dictionary<string, DeviceInfo> (), "Unavailable");
		var exception = Assert.ThrowsAsync<ProcessorApiException> (async () => await Run (transport))!;
		Assert.That (exception.DiagnosticCommand, Is.EqualTo ("cp.platformDriverController:prepareDriverForUse"));
		Assert.That (exception.DiagnosticResponse!.Value.GetString (), Is.EqualTo ("Unavailable"));
		Assert.That (transport.Commands.Any (command => command.EndsWith (":commissionDevice")), Is.False);
		}

	[Test]
	public async Task DifferentZeroPaddingReusesTheSameDebugBuild ()
		{
		var catalogue = Catalogue with
			{
			Version = "2.0.000.0005"
			};
		var transport = new Fake (catalogue, Inventory ("2.0.0.5"), Device ("2.000.0000.0005"));
		Assert.That ((await Run (transport)).Action, Is.EqualTo ("Existing"));
		Assert.That (transport.Commands, Is.EqualTo (new[] { "cp.platformDriverController:getDriver" }));
		}

	[Test]
	public async Task LaterDebugBuildUpdatesEvenWhenReleaseComponentsMatch ()
		{
		var catalogue = Catalogue with
			{
			Version = "2.0.000.0006"
			};
		var eligible = Eligible (17) with
			{
			InstalledDriverVersion = "2.0.000.0005",
			AvailableDriverVersion = "2.0.0.6"
			};
		var rechecked = eligible with
			{
			InstalledDriverVersion = "2.000.0.5",
			AvailableDriverVersion = "2.0.000.0006"
			};
		var transport = new Fake (catalogue, Inventory ("2.0.0.5"), eligible, rechecked, "operation", Device ("2.0.0.6"));
		Assert.That ((await Run (transport)).Action, Is.EqualTo ("Updated"));
		Assert.That (transport.Commands.Count (command => command.EndsWith (":beginSwapDriverForAllEligibleDevices")), Is.EqualTo (1));
		}

	[Test]
	public async Task WaitsForEligibilityThenUpdatesOnlyOnce ()
		{
		var transport = new Fake (Catalogue, Inventory ("1.0"), null, Eligible (17), Eligible (17), "operation", Device ("1.1"));
		Assert.That ((await Run (transport)).Action, Is.EqualTo ("Updated"));
		Assert.That (transport.Commands.Count (command => command.EndsWith (":getDevicesEligibleForDriverUpdate")), Is.EqualTo (3));
		Assert.That (transport.Commands.Count (command => command.EndsWith (":beginSwapDriverForAllEligibleDevices")), Is.EqualTo (1));
		}

	[Test]
	public void UpgradeCannotSilentlyAffectAnotherInstance ()
		{
		var transport = new Fake (Catalogue, Inventory ("1.0"), Eligible (17, 18));
		Assert.ThrowsAsync<InvalidOperationException> (async () => await Run (transport));
		Assert.That (transport.Commands.Any (command => command.Contains ("beginSwap")), Is.False);
		}

	[Test]
	public void ExistingNewerVersionIsNotAutomaticallyDowngraded ()
		{
		var transport = new Fake (Catalogue, Inventory ("2.0"));
		Assert.ThrowsAsync<InvalidOperationException> (async () => await Run (transport));
		Assert.That (transport.Commands.Count, Is.EqualTo (1));
		}

	[Test]
	public void MissingExpectedIdCannotAdoptDifferentInstance ()
		{
		var transport = new Fake (Catalogue, Inventory ("1.1"));
		Assert.ThrowsAsync<InvalidOperationException> (async () => await DriverInstanceLifecycle.EnsureAsync (new ConfigurationClient (transport), "catalogue", "Example Tests", 12, 99, TimeSpan.FromSeconds (2)));
		Assert.That (transport.Commands.Count, Is.EqualTo (1));
		}

	[Test]
	public void LongInstanceNameFailsBeforeProcessorRequest ()
		{
		var transport = new Fake ();
		Assert.ThrowsAsync<ArgumentException> (async () => await DriverInstanceLifecycle.EnsureAsync (new ConfigurationClient (transport), "catalogue", new string ('x', 33), 12, null, TimeSpan.FromSeconds (2)));
		Assert.That (transport.Commands, Is.Empty);
		}

	private static Task<DriverInstanceReady> Run (Fake transport) => DriverInstanceLifecycle.EnsureAsync (new ConfigurationClient (transport), "catalogue", "Example Tests", 12, null, TimeSpan.FromSeconds (5));
	private sealed class Fake (params object?[] responses) : IConfigurationConnection
		{
		private readonly Queue<object?> _responses = new (responses);
		public List<string> Commands { get; } = [];
		private Task<T?> Next<T> () => Task.FromResult (JsonSerializer.Deserialize<T> (JsonSerializer.Serialize (_responses.Dequeue ())));
		public Task<T?> GetAsync<T> (string path, CancellationToken token = default) => Next<T> ();
		public Task<T?> ExecuteAsync<T> (int deviceId, string command, object? parameters = null, CancellationToken token = default)
			{
			Commands.Add (command);
			return Next<T> ();
			}
		public Task<OperationResult> WaitForOperationAsync (string operationId, TimeSpan timeout, CancellationToken token = default) => Task.FromResult (new OperationResult (operationId, "Ended", null));
		public ValueTask DisposeAsync () => ValueTask.CompletedTask;
		}
	}