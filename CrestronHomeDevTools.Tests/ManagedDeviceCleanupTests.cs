// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Text.Json;

using NUnit.Framework;

namespace CrestronHomeDevTools.Tests;

[TestFixture]
public sealed class ManagedDeviceCleanupTests
	{
	private string _journal = null!;
	private static readonly ManagedDeviceRequest REQUEST = new (17, "Platform", "1.0.0.1", "child", "New child", "Child", 3);
	[SetUp]
	public void SetUp ()
		{
		_journal = Path.Combine (TestContext.CurrentContext.WorkDirectory, "child-cleanup-" + Guid.NewGuid ().ToString ("N"));
		Directory.CreateDirectory (_journal);
		File.WriteAllText (Path.Combine (_journal, "request.json"), JsonSerializer.Serialize (REQUEST));
		File.WriteAllText (Path.Combine (_journal, "created-child.json"), JsonSerializer.Serialize (new { DeviceId = 18, REQUEST.ParentId, REQUEST.Name, REQUEST.ChildModel, REQUEST.ParentVersion, REQUEST.LocationId }));
		File.WriteAllText (Path.Combine (_journal, "commission-response.json"), JsonSerializer.Serialize (new { Response = new { Id = 18, CommissioningResult = "Success" } }));
		File.WriteAllText (Path.Combine (_journal, "result.json"), JsonSerializer.Serialize (new ManagedDeviceResult (18, "Ready")));
		}
	[TearDown]
	public void TearDown () => Directory.Delete (_journal, true);

	[Test]
	public async Task RemovesOnlyReceiptChildAndVerifiesOtherDevices ()
		{
		var connection = new Connection (_journal);
		var result = await Remove (connection);
		Assert.That (result, Is.EqualTo (new ManagedDeviceCleanupResult (18, true, true)));
		Assert.That (connection.RemovalCalls, Is.EqualTo (1));
		Assert.That (File.Exists (Path.Combine (_journal, "cleanup", "result.json")), Is.True);
		Assert.ThrowsAsync<InvalidOperationException> (async () => await Remove (connection));
		Assert.That (connection.RemovalCalls, Is.EqualTo (1));
		}

	[TestCase ("name")]
	[TestCase ("location")]
	[TestCase ("model")]
	[TestCase ("version")]
	[TestCase ("parent")]
	[TestCase ("descendant")]
	[TestCase ("reboot")]
	[TestCase ("absent")]
	public void UnconfirmedChildOrPlatformIsNeverRemoved (string fault)
		{
		var connection = new Connection (_journal) { Fault = fault };
		Assert.ThrowsAsync<InvalidOperationException> (async () => await Remove (connection));
		Assert.That (connection.RemovalCalls, Is.Zero);
		Assert.That (File.Exists (Path.Combine (_journal, "cleanup", "remove-intent.json")), Is.False);
		}

	[Test]
	public void ReceiptMismatchIsRejectedBeforeProcessorAccess ()
		{
		File.WriteAllText (Path.Combine (_journal, "commission-response.json"), JsonSerializer.Serialize (new { Response = new { Id = 99, CommissioningResult = "Success" } }));
		var connection = new Connection (_journal);
		Assert.ThrowsAsync<InvalidDataException> (async () => await Remove (connection));
		Assert.That (connection.Reads, Is.Zero);
		Assert.That (connection.RemovalCalls, Is.Zero);
		}

	[Test]
	public void IncompleteCommissioningIsNotAutomaticallyCleanedUp ()
		{
		File.Delete (Path.Combine (_journal, "result.json"));
		var connection = new Connection (_journal);
		Assert.ThrowsAsync<FileNotFoundException> (async () => await Remove (connection));
		Assert.That (connection.Reads, Is.Zero);
		Assert.That (connection.RemovalCalls, Is.Zero);
		}

	[Test]
	public void LostRemovalReplyIsNotReplayedEvenIfDeviceDisappeared ()
		{
		var connection = new Connection (_journal) { Fault = "lost-reply" };
		Assert.ThrowsAsync<IOException> (async () => await Remove (connection));
		Assert.ThrowsAsync<InvalidOperationException> (async () => await Remove (connection));
		Assert.That (connection.RemovalCalls, Is.EqualTo (1));
		Assert.That (File.Exists (Path.Combine (_journal, "cleanup", "remove-intent.json")), Is.True);
		Assert.That (File.Exists (Path.Combine (_journal, "cleanup", "result.json")), Is.False);
		}

	[Test]
	public void ChangedOtherDeviceDoesNotProduceSuccessfulCleanup ()
		{
		var connection = new Connection (_journal) { Fault = "other-changed" };
		Assert.ThrowsAsync<InvalidDataException> (async () => await Remove (connection));
		Assert.That (connection.RemovalCalls, Is.EqualTo (1));
		Assert.That (File.Exists (Path.Combine (_journal, "cleanup", "after.json")), Is.True);
		Assert.That (File.Exists (Path.Combine (_journal, "cleanup", "result.json")), Is.False);
		}

	[Test]
	public void RemovalTimeoutKeepsIntentAndDoesNotRetry ()
		{
		var connection = new Connection (_journal) { Fault = "remains" };
		Assert.ThrowsAsync<TimeoutException> (async () => await ManagedDeviceCommissioning.RemoveCreatedAsync (new (connection), _journal, TimeSpan.FromSeconds (2)));
		Assert.That (connection.RemovalCalls, Is.EqualTo (1));
		Assert.ThrowsAsync<InvalidOperationException> (async () => await Remove (connection));
		Assert.That (connection.RemovalCalls, Is.EqualTo (1));
		}

	private Task<ManagedDeviceCleanupResult> Remove (Connection connection) => ManagedDeviceCommissioning.RemoveCreatedAsync (new (connection), _journal, TimeSpan.FromSeconds (2));
	private sealed class Connection (string journal) : IConfigurationConnection
		{
		public string? Fault { get; init; }
		public int RemovalCalls { get; private set; }
		public int Reads { get; private set; }
		public Task<T?> GetAsync<T> (string path, CancellationToken cancellationToken = default)
			{
			cancellationToken.ThrowIfCancellationRequested ();
			Reads++;
			Assert.That (path, Is.EqualTo ("v2/Devices"));
			var parent = new DeviceInfo { Id = 17, Model = "Platform", PropertyValues = new ()
				{
				["cp.driverInformation:version"] = JsonSerializer.SerializeToElement ("1.0.0.1"),
				["cp.driverConfiguration:supportsUnloadReloadDriver"] = JsonSerializer.SerializeToElement (true),
				["cp.driverConfiguration:swapDriverRequiresReboot"] = JsonSerializer.SerializeToElement (Fault == "reboot")
				} };
			var child = new DeviceInfo { Id = 18, ParentDeviceId = Fault == "parent" ? 99 : 17, Name = Fault == "name" ? "Manual child" : "New child",
				Model = Fault == "model" ? "Changed" : "Child", LocationId = Fault == "location" ? 4 : 3,
				Commands = ["cp.deviceConfiguration:setLocation"], PropertyValues = new () { ["cp.driverInformation:version"] = JsonSerializer.SerializeToElement (Fault == "version" ? "1.0.0.2" : "1.0.0.1") } };
			var other = new DeviceInfo { Id = 99, Name = RemovalCalls > 0 && Fault == "other-changed" ? "Changed" : "Preserved", Model = "Other", ParentDeviceId = Fault == "descendant" ? 18 : null };
			var devices = new Dictionary<string, DeviceInfo> { ["17"] = parent, ["99"] = other };
			if (Fault != "absent" && (RemovalCalls == 0 || Fault == "remains")) devices["18"] = child;
			return Task.FromResult (JsonSerializer.Deserialize<T> (JsonSerializer.Serialize (devices)));
			}
		public Task<T?> ExecuteAsync<T> (int id, string command, object? parameters = null, CancellationToken cancellationToken = default)
			{
			cancellationToken.ThrowIfCancellationRequested ();
			Assert.That (id, Is.EqualTo (18));
			Assert.That (command, Is.EqualTo ("cp.deviceConfiguration:setLocation"));
			Assert.That (JsonSerializer.SerializeToElement (parameters).GetProperty ("locationId").ValueKind, Is.EqualTo (JsonValueKind.Null));
			Assert.That (File.Exists (Path.Combine (journal, "cleanup", "remove-intent.json")), Is.True);
			RemovalCalls++;
			if (Fault == "lost-reply") throw new IOException ("Reply lost after removal.");
			return Task.FromResult<T?> (default);
			}
		public Task<OperationResult> WaitForOperationAsync (string operationId, TimeSpan timeout, CancellationToken cancellationToken = default) => throw new NotSupportedException ();
		public ValueTask DisposeAsync () => ValueTask.CompletedTask;
		}
	}