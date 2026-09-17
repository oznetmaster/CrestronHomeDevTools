// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Text.Json;

using NUnit.Framework;

namespace CrestronHomeDevTools.Tests;

[TestFixture]
public sealed class ManagedDeviceConfigurationTests
	{
	private static readonly DriverInstanceReady CHILD = new (18, "Example child", "1.0.000.0001", "Installed");

	[TestCase (false)]
	[TestCase (true)]
	public async Task NoPromptChildEntersConfigurationEvenWhenConfiguredFlagHasAlreadyChanged (bool configured)
		{
		var connection = new Connection (configured);
		Assert.That (await DriverConfiguration.BeginManagedDeviceAsync (new (connection), CHILD, 17), Is.Null);
		Assert.That (connection.Commands, Is.EqualTo (1));
		Assert.That (connection.Parameters.GetProperty ("isReconfiguring").GetBoolean (), Is.False);
		}

	[Test]
	public async Task ConfigurationPromptIsReturnedWithoutApplyingValues ()
		{
		var step = JsonSerializer.SerializeToElement (new { Id = "Settings", Items = new[] { new { Id = "UserValue" } } });
		var connection = new Connection (false) { Response = step };
		Assert.That ((await DriverConfiguration.BeginManagedDeviceAsync (new (connection), CHILD, 17))?.GetRawText (), Is.EqualTo (step.GetRawText ()));
		Assert.That (connection.Commands, Is.EqualTo (1));
		}

	[TestCase ("parent")]
	[TestCase ("model")]
	[TestCase ("version")]
	[TestCase ("capability")]
	[TestCase ("id")]
	public void ChangedChildIsRejectedBeforeConfiguration (string change)
		{
		var connection = new Connection (false);
		connection.Device = change switch
			{
			"parent" => connection.Device with { ParentDeviceId = 19 },
			"model" => connection.Device with { Model = "Other" },
			"capability" => connection.Device with { Commands = [] },
			"id" => connection.Device with { Id = 19 },
			_ => connection.Device
			};
		if (change == "version") connection.Device.PropertyValues["cp.driverInformation:version"] = JsonSerializer.SerializeToElement ("2.0.0.1");
		Assert.ThrowsAsync<InvalidOperationException> (async () => await DriverConfiguration.BeginManagedDeviceAsync (new (connection), CHILD, 17));
		Assert.That (connection.Commands, Is.Zero);
		}

	[Test]
	public void ExistingInstanceIsRejected ()
		{
		var connection = new Connection (true);
		Assert.ThrowsAsync<ArgumentException> (async () => await DriverConfiguration.BeginManagedDeviceAsync (new (connection), CHILD with { Action = "Existing" }, 17));
		Assert.That (connection.Commands, Is.Zero);
		}

	[Test]
	public void FailedEntryIsNeverAutomaticallyRepeated ()
		{
		var connection = new Connection (false) { Fail = true };
		Assert.ThrowsAsync<IOException> (async () => await DriverConfiguration.BeginManagedDeviceAsync (new (connection), CHILD, 17));
		Assert.That (connection.Commands, Is.EqualTo (1));
		}

	private sealed class Connection (bool configured) : IConfigurationConnection
		{
		public DeviceInfo Device { get; set; } = new ()
			{
			Id = 18, ParentDeviceId = 17, Model = "Example child",
			Commands = ["cp.driverConfiguration:getFirstConfigurationStep"],
			PropertyValues = new ()
				{
				["cp.driverInformation:version"] = JsonSerializer.SerializeToElement ("1.0.0.1"),
				["cp.driverConfiguration:isConfigured"] = JsonSerializer.SerializeToElement (configured)
				}
			};
		public JsonElement? Response { get; init; }
		public bool Fail { get; init; }
		public int Commands { get; private set; }
		public JsonElement Parameters { get; private set; }
		public Task<T?> GetAsync<T> (string path, CancellationToken cancellationToken = default)
			=> Task.FromResult (JsonSerializer.Deserialize<T> (JsonSerializer.Serialize (Device)));
		public Task<T?> ExecuteAsync<T> (int id, string command, object? parameters = null, CancellationToken cancellationToken = default)
			{
			Commands++;
			Assert.That (id, Is.EqualTo (18));
			Assert.That (command, Is.EqualTo ("cp.driverConfiguration:getFirstConfigurationStep"));
			Parameters = JsonSerializer.SerializeToElement (parameters);
			if (Fail) throw new IOException ("Uncertain response.");
			return Task.FromResult (JsonSerializer.Deserialize<T> (JsonSerializer.Serialize (Response)));
			}
		public Task<OperationResult> WaitForOperationAsync (string operationId, TimeSpan timeout, CancellationToken cancellationToken = default) => throw new NotSupportedException ();
		public ValueTask DisposeAsync () => ValueTask.CompletedTask;
		}
	}