// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Text.Json;

using NUnit.Framework;

namespace CrestronHomeDevTools.Tests;

[TestFixture]
public sealed class ConfigurationRebootTests
	{
	private static DeviceInfo Target (int id = 10) => new () { Id = id, Model = "MC4-R", Commands = ["cp.processorOperations:beginReboot"] };
	private static Dictionary<string, DeviceInfo> Inventory (params DeviceInfo[] devices) => devices.ToDictionary (d => d.Id.ToString ());

	[TestCase (true)]
	[TestCase (false)]
	public async Task ExplicitConfirmationControlsSubmission (bool confirmed)
		{
		var fake = new Fake (Inventory (Target ()), Target (), "reboot-op");
		var result = await new ConfigurationClient (fake).RequestProcessorRebootAsync ((target, _) => { Assert.That (target.Id, Is.EqualTo (10)); return Task.FromResult (confirmed); }, "test");
		Assert.That (result, Is.EqualTo (confirmed ? "reboot-op" : null));
		Assert.That (fake.Writes, Is.EqualTo (confirmed ? 1 : 0));
		if (confirmed)
			Assert.That (fake.Parameters!.Value.GetProperty ("rebootReasonInAFewWords").GetString (), Is.EqualTo ("test"));
		}

	[TestCase (false)]
	[TestCase (true)]
	public void MissingOrAmbiguousCapabilityPreventsSubmission (bool ambiguous)
		{
		var fake = new Fake (Inventory (ambiguous ? [Target (), Target (11)] : []));
		Assert.ThrowsAsync<InvalidOperationException> (async () => await new ConfigurationClient (fake).RequestProcessorRebootAsync ((_, _) => throw new AssertionException ("Cannot confirm an ambiguous target"), "test"));
		Assert.That (fake.Writes, Is.Zero);
		}

	[Test]
	public void ChangedIdentityAfterConfirmationStopsReboot ()
		{
		var fake = new Fake (Inventory (Target ()), Target () with
			{
			Model = "Other"
			});
		Assert.ThrowsAsync<InvalidOperationException> (async () => await new ConfigurationClient (fake).RequestProcessorRebootAsync ((_, _) => Task.FromResult (true), "test"));
		Assert.That (fake.Writes, Is.Zero);
		}

	[Test]
	public void LostResponseDoesNotRepeatReboot ()
		{
		var fake = new Fake (Inventory (Target ()), Target (), new IOException ());
		Assert.ThrowsAsync<IOException> (async () => await new ConfigurationClient (fake).RequestProcessorRebootAsync ((_, _) => Task.FromResult (true), "test"));
		Assert.That (fake.Writes, Is.EqualTo (1));
		}

	private sealed class Fake (params object?[] replies) : IConfigurationConnection
		{
		private readonly Queue<object?> _replies = new (replies);
		public int Writes
			{
			get; private set;
			}
		public JsonElement? Parameters
			{
			get; private set;
			}
		private Task<T?> Next<T> ()
			{
			var value = _replies.Dequeue ();
			if (value is Exception error)
				throw error;
			return Task.FromResult (JsonSerializer.Deserialize<T> (JsonSerializer.Serialize (value)));
			}
		public Task<T?> GetAsync<T> (string path, CancellationToken token = default) => Next<T> ();
		public Task<T?> ExecuteAsync<T> (int id, string command, object? parameters = null, CancellationToken token = default)
			{
			Writes++;
			Assert.That (id, Is.EqualTo (10));
			Assert.That (command, Is.EqualTo ("cp.processorOperations:beginReboot"));
			Parameters = JsonSerializer.SerializeToElement (parameters);
			return Next<T> ();
			}
		public Task<OperationResult> WaitForOperationAsync (string id, TimeSpan timeout, CancellationToken token = default) => throw new AssertionException ("Restart completion is verified through a new connection");
		public ValueTask DisposeAsync () => ValueTask.CompletedTask;
		}
	}