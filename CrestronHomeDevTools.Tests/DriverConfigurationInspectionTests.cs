// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Text.Json;
using NUnit.Framework;

namespace CrestronHomeDevTools.Tests;

[TestFixture]
public sealed class DriverConfigurationInspectionTests
	{
	[TestCase ("Password", true)]
	[TestCase ("HubSecret", false)]
	[TestCase ("OpenWeatherApiKey", false)]
	[TestCase ("RefreshToken", false)]
	[TestCase ("Anything", true)]
	[TestCase ("ClientId", false)]
	public void DisplayUsesOnlyTheDriversMaskMetadata (string id, bool masked)
		{
		var device = Device (new { Id = id, Title = id, ValueType = "String", Value = new { Masked = masked, CurrentValue = "private-value", DefaultValue = "private-value", CurrentValueDisplayOverride = "private-value", ReadOnly = false } });
		var snapshot = DriverConfigurationInspection.FromDevice (device);
		Assert.That (snapshot.Items.Single ().Masked, Is.EqualTo (masked));
		if (masked) Assert.That (JsonSerializer.Serialize (snapshot), Does.Not.Contain ("private-value"));
		else Assert.That (snapshot.Items.Single ().CurrentValue!.Value.GetString (), Is.EqualTo ("private-value"));
		}

	[Test]
	public void UnmarkedValuesAreDisplayed ()
		{
		var snapshot = DriverConfigurationInspection.FromDevice (Device (new { Id = "Custom", Value = new { CurrentValue = "private-value" } }));
		Assert.That (snapshot.Items.Single ().Masked, Is.False);
		Assert.That (snapshot.Items.Single ().CurrentValue!.Value.GetString (), Is.EqualTo ("private-value"));
		}

	[Test]
	public void OrdinaryValuesKeepTheirTypesAndEditability ()
		{
		var snapshot = DriverConfigurationInspection.FromDevice (Device (new { Id = "PollSeconds", Required = true, ValueType = "Number", Value = new { Masked = false, CurrentValue = 30, ReadOnly = false } }));
		var item = snapshot.Items.Single ();
		Assert.That (item.CurrentValue!.Value.GetInt32 (), Is.EqualTo (30));
		Assert.That (item.ReadOnly, Is.False);
		Assert.That (item.Required, Is.True);
		}

	[Test]
	public void MissingValuesAndMissingItemsAreNotPresentedAsSavedDefaults ()
		{
		var snapshot = DriverConfigurationInspection.FromDevice (Device (new { Id = "Setting", Value = new { Masked = false, DefaultValue = "default" } }));
		Assert.That (snapshot.Items.Single ().HasCurrentValue, Is.False);
		Assert.That (snapshot.Items.Single ().CurrentValue, Is.Null);
		var absent = DriverConfigurationInspection.FromDevice (new () { Id = 17 });
		Assert.That (absent.ItemsAvailable, Is.False);
		Assert.That (absent.IsConfigured, Is.Null);
		}

	[Test]
	public async Task InspectionOnlyReadsTheRequestedDevice ()
		{
		var connection = new Connection ();
		var snapshot = await DriverConfigurationInspection.GetAsync (new (connection), 17);
		Assert.That (connection.Path, Is.EqualTo ("v2/Devices/17"));
		Assert.That (snapshot.DeviceId, Is.EqualTo (17));
		}
	private static DeviceInfo Device (object item) => new ()
		{
		Id = 17, Model = "Example", PropertyValues = new ()
			{
			["cp.driverConfiguration:configurationItems"] = JsonSerializer.SerializeToElement (new[] { item })
			}
		};
	private sealed class Connection : IConfigurationConnection
		{
		public string? Path { get; private set; }
		public Task<T?> GetAsync<T> (string path, CancellationToken cancellationToken = default)
			{
			Path = path;
			return Task.FromResult (JsonSerializer.Deserialize<T> (JsonSerializer.Serialize (Device (new { Id = "Setting" }))));
			}
		public Task<T?> ExecuteAsync<T> (int id, string command, object? parameters = null, CancellationToken cancellationToken = default) => throw new AssertionException ("Inspection must not submit commands.");
		public Task<OperationResult> WaitForOperationAsync (string operationId, TimeSpan timeout, CancellationToken cancellationToken = default) => throw new NotSupportedException ();
		public ValueTask DisposeAsync () => ValueTask.CompletedTask;
		}
	}