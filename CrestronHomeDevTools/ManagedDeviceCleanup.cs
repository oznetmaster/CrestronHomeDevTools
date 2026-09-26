// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Text.Json;

namespace CrestronHomeDevTools;

public sealed record ManagedDeviceCleanupResult (int DeviceId, bool Removed, bool OtherDevicesPreserved);

public static partial class ManagedDeviceCommissioning
	{
	/// <summary>Removes the receipt-owned child, including a wrapper with one native light. The caller holds the processor lease and verifies physical-state restoration first.</summary>
	/// <remarks>A partial cleanup journal is never replayed. Resolve uncertain outcomes by inspecting the recorded identity and processor state.</remarks>
	public static async Task<ManagedDeviceCleanupResult> RemoveCreatedAsync (ConfigurationClient client, string commissioningJournal,
		TimeSpan timeout, CancellationToken cancellationToken = default)
		{
		ArgumentNullException.ThrowIfNull (client);
		if (timeout <= TimeSpan.Zero || timeout > TimeSpan.FromHours (1)) throw new ArgumentOutOfRangeException (nameof (timeout));
		string journal = Path.GetFullPath (commissioningJournal);
		var request = ReadRequest (Path.Combine (journal, "request.json"));
		using var created = JsonDocument.Parse (File.ReadAllText (Path.Combine (journal, "created-child.json")));
		int id = created.RootElement.GetProperty ("DeviceId").GetInt32 ();
		var expected = JsonSerializer.SerializeToElement (new { DeviceId = id, request.ParentId, request.Name, request.ChildModel, request.ParentVersion, request.LocationId });
		using var response = JsonDocument.Parse (File.ReadAllText (Path.Combine (journal, "commission-response.json")));
		var commissioned = response.RootElement.GetProperty ("Response");
		if (id <= 0 || id == request.ParentId || !JsonElement.DeepEquals (created.RootElement, expected) ||
			commissioned.GetProperty ("Id").GetInt32 () != id || commissioned.GetProperty ("CommissioningResult").GetString () != "Success")
			throw new InvalidDataException ("The commissioning identity and returned child receipt do not agree.");
		var completed = JsonSerializer.Deserialize<ManagedDeviceResult> (File.ReadAllText (Path.Combine (journal, "result.json")));
		if (completed == null || completed.DeviceId != id || completed.State is not ("Ready" or "ConfigurationRequired"))
			throw new InvalidDataException ("Commissioning has no confirmed terminal result; reconcile it before cleanup.");
		string cleanup = Path.Combine (journal, "cleanup");
		if (Directory.Exists (cleanup) || File.Exists (cleanup)) throw new InvalidOperationException ("Cleanup has already been attempted; reconcile its retained journal instead of repeating removal.");
		Directory.CreateDirectory (cleanup);
		void Record (string name, object value)
			{
			using var stream = new FileStream (Path.Combine (cleanup, name + ".json"), FileMode.CreateNew, FileAccess.Write, FileShare.Read);
			JsonSerializer.Serialize (stream, value);
			stream.Flush (true);
			}
		// CreateNew also prevents two callers that raced directory creation from both submitting removal.
		Record ("request", new { DeviceId = id, Request = request });
		using var deadline = CancellationTokenSource.CreateLinkedTokenSource (cancellationToken);
		deadline.CancelAfter (timeout);
		var token = deadline.Token;
		try
			{
			var before = await client.GetDevicesAsync (token).ConfigureAwait (false);
			var parent = before.SingleOrDefault (d => d.Id == request.ParentId);
			var child = before.SingleOrDefault (d => d.Id == id);
			if (parent == null || parent.Model != request.ParentModel || !VersionMatches (parent, request.ParentVersion) ||
				!IsTrue (parent, "cp.driverConfiguration:supportsUnloadReloadDriver") ||
				!parent.PropertyValues.TryGetValue ("cp.driverConfiguration:swapDriverRequiresReboot", out var reboot) || reboot.ValueKind != JsonValueKind.False ||
				child == null || child.ParentDeviceId != request.ParentId || child.Name != request.Name || child.Model != request.ChildModel)
				throw new InvalidOperationException ("Cleanup requires the unchanged owned child and its reloadable Entity V2 platform.");
			var removalIds = new HashSet<int> { id };
			int commandDeviceId = id;
			if (child.LocationId == null)
				{
				// Home can replace an activated managed light with an unlocated wrapper,
				// retaining its receipt ID, and create a native load below it. Match the
				// physical managed identity as well as the complete parent chain.
				if (child.PropertyValues.ContainsKey ("cp.driverInformation:version") && !VersionMatches (child, request.ParentVersion) ||
					!child.PropertyValues.TryGetValue ("platform:managedDevices", out var managed) || managed.ValueKind != JsonValueKind.Array ||
					managed.GetArrayLength () != 1 || !managed[0].TryGetProperty ("Id", out var managedId) || managedId.ValueKind != JsonValueKind.String ||
					managedId.GetString () != request.ManagedDeviceId)
					throw new InvalidOperationException ("The native wrapper does not match the commissioned managed device.");
				var loads = before.Where (d => d.ParentDeviceId == id).ToArray ();
				if (loads.Length != 1 || loads[0].Id <= 0 || loads[0].Name != request.Name || loads[0].Model != request.ChildModel ||
					loads[0].LocationId != request.LocationId || !loads[0].Commands.Contains ("cp.deviceConfiguration:setLocation") ||
					!loads[0].PropertyValues.TryGetValue ("lightType:variant", out var variant) || variant.ValueKind != JsonValueKind.String || variant.GetString () != "load" ||
					before.Any (d => d.ParentDeviceId == loads[0].Id))
					throw new InvalidOperationException ("Cleanup requires exactly one unchanged native light below the receipt-owned wrapper.");
				commandDeviceId = loads[0].Id;
				removalIds.Add (commandDeviceId);
				}
			else if (child.LocationId != request.LocationId || !VersionMatches (child, request.ParentVersion) ||
				!child.Commands.Contains ("cp.deviceConfiguration:setLocation") || before.Any (d => d.ParentDeviceId == id))
				throw new InvalidOperationException ("Cleanup requires the unchanged owned leaf child.");
			Record ("before", before);
			Record ("remove-intent", new { DeviceId = id, CommandDeviceId = commandDeviceId, ExpectedRemovedIds = removalIds.Order ().ToArray (), Utc = DateTimeOffset.UtcNow });
			// A managed child's location removal disposes that child. It is not a
			// driver package replacement or root-platform unload/reload operation.
			var result = await client.ExecuteDeviceCommandAsync (commandDeviceId, "cp.deviceConfiguration:setLocation", new { locationId = (int?)null }, token).ConfigureAwait (false);
			Record ("remove-response", new { Response = result });
			while (true)
				{
				var after = await client.GetDevicesAsync (token).ConfigureAwait (false);
				if (after.All (d => !removalIds.Contains (d.Id)))
					{
					Record ("after", after);
					if (after.Count != before.Count - removalIds.Count || before.Where (d => !removalIds.Contains (d.Id)).Any (old => !after.Any (now => Preserved (old, now))))
						throw new InvalidDataException ("The owned child was removed, but preservation of other devices was not confirmed.");
					var outcome = new ManagedDeviceCleanupResult (id, true, true);
					Record ("result", outcome);
					return outcome;
					}
				await Task.Delay (200, token).ConfigureAwait (false);
				}
			}
		catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
			{
			throw new TimeoutException ("Managed-child removal was not verified before the deadline. The cleanup journal is retained; no command was retried.");
			}
		}

	private static bool Preserved (DeviceInfo before, DeviceInfo after)
		{
		if (before.Id != after.Id || before.Name != after.Name || before.Model != after.Model || before.ParentDeviceId != after.ParentDeviceId || before.LocationId != after.LocationId)
			return false;
		foreach (string key in new[] { "cp.driverInformation:version", "cp.driverConfiguration:driverLoadingStatus", "onlineIndicator:isOnline", "readyIndicator:isReady" })
			{
			bool first = before.PropertyValues.TryGetValue (key, out var old), second = after.PropertyValues.TryGetValue (key, out var current);
			if (first != second || first && !JsonElement.DeepEquals (old, current)) return false;
			}
		return true;
		}
	}
