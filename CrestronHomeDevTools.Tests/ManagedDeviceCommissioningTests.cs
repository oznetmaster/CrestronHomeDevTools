// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Text.Json;

using NUnit.Framework;

namespace CrestronHomeDevTools.Tests;

[TestFixture]
public sealed class ManagedDeviceCommissioningTests
	{
	private string _root = null!;
	private string Journal => Path.Combine (_root, "journal");
	private static readonly ManagedDeviceRequest REQUEST = new (17, "Platform", "1.0.0.1", "child", "New child", "Child", 3);
	[SetUp]
	public void SetUp () => Directory.CreateDirectory (_root = Path.Combine (TestContext.CurrentContext.WorkDirectory, "managed-" + Guid.NewGuid ().ToString ("N")));
	[TearDown]
	public void TearDown () => Directory.Delete (_root, true);

	[Test]
	public async Task ReadyResultRequiresConfigurationEntryAndObservedReadiness ()
		{
		var connection = new Connection (Journal);
		var result = await Run (connection);
		Assert.That (result, Is.EqualTo (new ManagedDeviceResult (18, "Ready")));
		Assert.That (connection.Commands, Is.EqualTo (new[] { "cp.platformController:commissionManagedDevice", "cp.driverConfiguration:getFirstConfigurationStep" }));
		Assert.That (File.Exists (Path.Combine (Journal, "ready-observation.json")), Is.True);
		Assert.That (File.ReadAllText (Path.Combine (Journal, "created-child.json")), Does.Contain ("18"));
		}

	[Test]
	public async Task RequiredConfigurationRemainsPrivateAndDoesNotClaimReady ()
		{
		var connection = new Connection (Journal) { Prompt = true };
		var result = await Run (connection);
		Assert.That (result.State, Is.EqualTo ("ConfigurationRequired"));
		Assert.That (JsonSerializer.Serialize (result), Does.Not.Contain ("private-value"));
		Assert.That (File.ReadAllText (Path.Combine (Journal, "configuration-entry-response.json")), Does.Contain ("private-value"));
		Assert.That (File.Exists (Path.Combine (Journal, "ready-observation.json")), Is.False);
		}

	[Test]
	public void UncertainCommissionCannotBeRepeatedWithTheSameJournal ()
		{
		var connection = new Connection (Journal) { FailCommission = true };
		Assert.ThrowsAsync<IOException> (async () => await Run (connection));
		Assert.ThrowsAsync<InvalidOperationException> (async () => await Run (connection));
		Assert.That (connection.Commands.Count, Is.EqualTo (1));
		Assert.That (File.Exists (Path.Combine (Journal, "commission-intent.json")), Is.True);
		}

	[Test]
	public void UncertainConfigurationRetainsTheCreatedChildForReconciliation ()
		{
		var connection = new Connection (Journal) { FailEntry = true };
		Assert.ThrowsAsync<IOException> (async () => await Run (connection));
		Assert.That (connection.Commands.Count, Is.EqualTo (2));
		Assert.That (File.Exists (Path.Combine (Journal, "created-child.json")), Is.True);
		Assert.That (File.Exists (Path.Combine (Journal, "configuration-entry-intent.json")), Is.True);
		Assert.That (File.Exists (Path.Combine (Journal, "result.json")), Is.False);
		}

	[Test]
	public void ReadinessTimeoutDoesNotRepeatEitherCommand ()
		{
		var connection = new Connection (Journal) { RemainOffline = true };
		Assert.ThrowsAsync<TimeoutException> (async () => await ManagedDeviceCommissioning.CommissionAsync (new (connection), REQUEST, Journal, TimeSpan.FromMilliseconds (80)));
		Assert.That (connection.Commands.Count, Is.EqualTo (2));
		Assert.That (File.Exists (Path.Combine (Journal, "result.json")), Is.False);
		}

	[Test]
	public void ExistingChildIdReturnedByProcessorIsNotInitialized ()
		{
		var connection = new Connection (Journal) { ReuseParentId = true };
		Assert.ThrowsAsync<InvalidOperationException> (async () => await Run (connection));
		Assert.That (connection.Commands.Count, Is.EqualTo (1));
		}

	[Test]
	public void ChangedParentIsRejectedBeforeCommission ()
		{
		var connection = new Connection (Journal) { WrongParent = true };
		Assert.ThrowsAsync<InvalidOperationException> (async () => await Run (connection));
		Assert.That (connection.Commands, Is.Empty);
		}

	private Task<ManagedDeviceResult> Run (Connection connection) => ManagedDeviceCommissioning.CommissionAsync (new (connection), REQUEST, Journal, TimeSpan.FromSeconds (2));
	private sealed class Connection (string journal) : IConfigurationConnection
		{
		public List<string> Commands { get; } = [];
		public bool Prompt { get; init; }
		public bool FailCommission { get; init; }
		public bool FailEntry { get; init; }
		public bool RemainOffline { get; init; }
		public bool ReuseParentId { get; init; }
		public bool WrongParent { get; init; }
		private bool _entered;
		public Task<T?> GetAsync<T> (string path, CancellationToken cancellationToken = default)
			{
			var parent = new DeviceInfo { Id = 17, Model = WrongParent ? "Changed" : "Platform", Commands = ["cp.platformController:commissionManagedDevice"], PropertyValues = new () { ["cp.driverInformation:version"] = JsonSerializer.SerializeToElement ("1.0.0.1"), ["platform:managedDevices"] = JsonSerializer.SerializeToElement (new[] { new { Id = "child" } }) } };
			var child = new DeviceInfo { Id = 18, ParentDeviceId = 17, Name = "New child", Model = "Child", LocationId = 3, Commands = ["cp.driverConfiguration:getFirstConfigurationStep"], PropertyValues = new () { ["cp.driverInformation:version"] = JsonSerializer.SerializeToElement ("1.0.0.1"), ["cp.driverConfiguration:isConfigured"] = JsonSerializer.SerializeToElement (true), ["onlineIndicator:isOnline"] = JsonSerializer.SerializeToElement (_entered && !RemainOffline), ["readyIndicator:isReady"] = JsonSerializer.SerializeToElement (_entered && !RemainOffline) } };
			object result = path == "v2/Devices" ? new Dictionary<string, DeviceInfo> { ["17"] = parent } : child;
			return Task.FromResult (JsonSerializer.Deserialize<T> (JsonSerializer.Serialize (result)));
			}
		public Task<T?> ExecuteAsync<T> (int id, string command, object? parameters = null, CancellationToken cancellationToken = default)
			{
			Commands.Add (command);
			object? result;
			if (command == "cp.platformController:commissionManagedDevice")
				{
				Assert.That (File.Exists (Path.Combine (journal, "commission-intent.json")), Is.True);
				if (FailCommission) throw new IOException ("Unknown outcome.");
				result = new { Id = ReuseParentId ? 17 : 18, CommissioningResult = "Success" };
				}
			else
				{
				Assert.That (command, Is.EqualTo ("cp.driverConfiguration:getFirstConfigurationStep"));
				Assert.That (File.Exists (Path.Combine (journal, "created-child.json")), Is.True);
				Assert.That (File.Exists (Path.Combine (journal, "configuration-entry-intent.json")), Is.True);
				if (FailEntry) throw new IOException ("Unknown outcome.");
				_entered = true;
				result = Prompt ? new { Id = "Settings", PrivateValue = "private-value" } : null;
				}
			return Task.FromResult (JsonSerializer.Deserialize<T> (JsonSerializer.Serialize (result)));
			}
		public Task<OperationResult> WaitForOperationAsync (string operationId, TimeSpan timeout, CancellationToken cancellationToken = default) => throw new NotSupportedException ();
		public ValueTask DisposeAsync () => ValueTask.CompletedTask;
		}
	}