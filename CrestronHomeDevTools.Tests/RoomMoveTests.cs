// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Text.Json;

using NUnit.Framework;

namespace CrestronHomeDevTools.Tests;

[TestFixture]
public sealed class RoomMoveTests
	{
	[Test]
	public async Task MovesOnceAndConfirmsSameLoadedInstance ()
		{
		var connection = new Connection ();
		var result = await new ConfigurationClient (connection).MoveDriverInstanceAsync (17, "Tests", "1.0.0.1", 12, 13, TimeSpan.FromSeconds (1));
		Assert.That (result, Is.EqualTo (new DriverRoomMoveResult (17, "Tests", "1.0.0.1", 12, 13, true)));
		Assert.That (connection.Writes, Is.EqualTo (1));
		Assert.That (connection.Parameters.GetProperty ("locationId").GetInt32 (), Is.EqualTo (13));
		}

	[Test]
	public async Task CurrentRoomDoesNotResubmitMove ()
		{
		var connection = new Connection ();
		var result = await new ConfigurationClient (connection).MoveDriverInstanceAsync (17, "Tests", "1.0.0.1", 12, 12, TimeSpan.FromSeconds (1));
		Assert.That (result.Changed, Is.False);
		Assert.That (connection.Writes, Is.Zero);
		}

	[TestCase ("unknown-room")]
	[TestCase ("model")]
	[TestCase ("version")]
	[TestCase ("room")]
	[TestCase ("child")]
	[TestCase ("command")]
	[TestCase ("dependants")]
	[TestCase ("collision")]
	[TestCase ("changed-before-submit")]
	[TestCase ("reboot")]
	[TestCase ("unsupported")]
	[TestCase ("unloaded")]
	public void RejectsChangedIdentityOrUnreviewedScopeBeforeMutation (string failure)
		{
		var connection = new Connection { Failure = failure };
		Assert.CatchAsync<InvalidOperationException> (async () => await new ConfigurationClient (connection).MoveDriverInstanceAsync (17, "Tests", "1.0.0.1", 12, 13, TimeSpan.FromSeconds (1)));
		Assert.That (connection.Writes, Is.Zero);
		}

	[TestCase ("lost-reply")]
	[TestCase ("disappeared")]
	[TestCase ("wrong-destination")]
	public void UncertainSubmissionIsNeverRepeated (string failure)
		{
		var connection = new Connection { Failure = failure };
		Assert.CatchAsync (async () => await new ConfigurationClient (connection).MoveDriverInstanceAsync (17, "Tests", "1.0.0.1", 12, 13, TimeSpan.FromSeconds (1)));
		Assert.That (connection.Writes, Is.EqualTo (1));
		}

	private sealed class Connection : IConfigurationConnection
		{
		public string? Failure;
		public int Writes;
		private int _deviceReads;
		public JsonElement Parameters;
		private DeviceInfo Device => new ()
			{
			Id = 17,
			Model = Failure == "model" ? "other" : "Tests",
			Name = "Test host",
			ParentDeviceId = Failure == "child" ? 1 : -6,
			LocationId = Writes > 0 ? Failure == "wrong-destination" ? 99 : 13 : Failure == "room" || Failure == "changed-before-submit" && _deviceReads > 1 ? 14 : 12,
			Commands = Failure == "command" ? [] : ["cp.deviceConfiguration:setLocation"],
			PropertyValues = new ()
				{
				["cp.driverInformation:version"] = JsonSerializer.SerializeToElement (Failure == "version" ? "2.0.0.1" : "1.0.000.0001"),
				["cp.driverConfiguration:driverLoadingStatus"] = JsonSerializer.SerializeToElement (Failure == "unloaded" ? "Unloaded" : "Loaded"),
				["cp.driverConfiguration:supportsUnloadReloadDriver"] = JsonSerializer.SerializeToElement (Failure != "unsupported"),
				["cp.driverConfiguration:swapDriverRequiresReboot"] = JsonSerializer.SerializeToElement (Failure == "reboot")
				}
			};
		public Task<T?> GetAsync<T> (string path, CancellationToken cancellationToken = default)
			{
			object? value;
			if (path == "v2/Locations")
				value = new[] { new ProcessorLocation (12, "One", "Room"), new ProcessorLocation (Failure == "unknown-room" ? 99 : 13, "Two", "Room") };
			else if (path == "v2/Devices")
				{
				var devices = new Dictionary<string, DeviceInfo> { ["17"] = Device };
				if (Failure is "dependants" or "collision")
					devices["18"] = new ()
						{
						Id = 18,
						ParentDeviceId = Failure == "dependants" ? 17 : -6,
						Name = "Test host",
						LocationId = 13
						};
				value = devices;
				}
			else
				{
				_deviceReads++;
				value = Failure == "disappeared" && Writes > 0 ? null : Device;
				}
			return Task.FromResult (JsonSerializer.Deserialize<T> (JsonSerializer.Serialize (value)));
			}
		public Task<T?> ExecuteAsync<T> (int deviceId, string commandName, object? parameters = null, CancellationToken cancellationToken = default)
			{
			Assert.That (deviceId, Is.EqualTo (17));
			Assert.That (commandName, Is.EqualTo ("cp.deviceConfiguration:setLocation"));
			Writes++;
			Parameters = JsonSerializer.SerializeToElement (parameters);
			if (Failure == "lost-reply")
				throw new IOException ("Unknown outcome");
			return Task.FromResult (default (T));
			}
		public Task<OperationResult> WaitForOperationAsync (string operationId, TimeSpan timeout, CancellationToken cancellationToken = default) => throw new NotSupportedException ();
		public ValueTask DisposeAsync () => ValueTask.CompletedTask;
		}
	}