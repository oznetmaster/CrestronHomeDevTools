// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Text.Json;

namespace CrestronHomeDevTools;

public sealed record ManagedDeviceRequest (int ParentId, string ParentModel, string ParentVersion,
	string ManagedDeviceId, string Name, string ChildModel, int LocationId);
public sealed record ManagedDeviceResult (int DeviceId, string State)
	{
	/// <summary>The native load below a commissioned light wrapper, when present. DeviceId remains the receipt-owned wrapper.</summary>
	public int? NativeLoadId { get; init; }
	}

/// <summary>Commissions one new child and records its initial configuration and readiness. The caller holds the shared processor lease.</summary>
public static partial class ManagedDeviceCommissioning
	{
	public static ManagedDeviceRequest ReadRequest (string path)
		{
		var request = JsonSerializer.Deserialize<ManagedDeviceRequest> (File.ReadAllText (path),
			new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? throw new ArgumentException ("A managed-child request is required.");
		Validate (request);
		return request;
		}

	internal static void Validate (ManagedDeviceRequest request)
		{
		ArgumentNullException.ThrowIfNull (request);
		if (request.ParentId <= 0 || request.LocationId <= 0 || string.IsNullOrWhiteSpace (request.ParentModel)
			 || !Version.TryParse (request.ParentVersion, out _) || string.IsNullOrWhiteSpace (request.ManagedDeviceId)
			 || string.IsNullOrWhiteSpace (request.Name) || request.Name.Length > 32 || string.IsNullOrWhiteSpace (request.ChildModel))
			throw new ArgumentException ("Managed-child identity, name, version and positive parent/location IDs are required.");
		}

	/// <remarks>
	/// The journal must be a new private directory. Existing journals are never replayed. On interruption,
	/// inspect the recorded child ID and processor state before cleanup or explicit continuation.
	/// A ConfigurationRequired result is not ready; the private journal contains the returned wizard step.
	/// </remarks>
	public static async Task<ManagedDeviceResult> CommissionAsync (ConfigurationClient client, ManagedDeviceRequest request,
		string journalDirectory, TimeSpan timeout, CancellationToken cancellationToken = default)
		{
		ArgumentNullException.ThrowIfNull (client);
		Validate (request);
		ArgumentException.ThrowIfNullOrWhiteSpace (journalDirectory);
		if (timeout <= TimeSpan.Zero || timeout > TimeSpan.FromHours (1)) throw new ArgumentOutOfRangeException (nameof (timeout));
		string journal = Path.GetFullPath (journalDirectory);
		if (Directory.Exists (journal) || File.Exists (journal)) throw new InvalidOperationException ("The commissioning journal already exists; inspect it instead of repeating the operation.");
		Directory.CreateDirectory (journal);
		void Record (string name, object value)
			{
			using var stream = new FileStream (Path.Combine (journal, name + ".json"), FileMode.CreateNew, FileAccess.Write, FileShare.None);
			JsonSerializer.Serialize (stream, value);
			stream.Flush (true);
			}
		Record ("request", request);
		using var deadline = CancellationTokenSource.CreateLinkedTokenSource (cancellationToken);
		deadline.CancelAfter (timeout);
		var token = deadline.Token;
		try
			{
			var before = await client.GetDevicesAsync (token).ConfigureAwait (false);
			var parent = before.SingleOrDefault (d => d.Id == request.ParentId);
			const string command = "cp.platformController:commissionManagedDevice";
			if (parent == null || parent.Model != request.ParentModel || !VersionMatches (parent, request.ParentVersion)
				 || !parent.Commands.Contains (command) || !parent.PropertyValues.TryGetValue ("platform:managedDevices", out var managed)
				 || managed.ValueKind != JsonValueKind.Array || managed.EnumerateArray ().Count (d => d.ValueKind == JsonValueKind.Object
					 && d.TryGetProperty ("Id", out var id) && id.ValueKind == JsonValueKind.String && id.GetString () == request.ManagedDeviceId) != 1)
				throw new InvalidOperationException ("The platform identity or advertised managed child is unconfirmed.");
			if (before.Any (d => d.LocationId == request.LocationId && d.Name == request.Name))
				throw new InvalidOperationException ("The requested room already contains this device name.");
			Record ("commission-intent", new { Utc = DateTimeOffset.UtcNow, Request = request });
			var response = await client.ExecuteDeviceCommandAsync (request.ParentId, command, new
				{
				managedDeviceId = request.ManagedDeviceId, name = request.Name,
				parameters = new { Username = (string?)null, Password = (string?)null, Port = (string?)null, Option = "None", request.LocationId, LightGroupId = -1 }
				}, token).ConfigureAwait (false);
			Record ("commission-response", new { Response = response });
			if (response is not { ValueKind: JsonValueKind.Object } || !response.Value.TryGetProperty ("CommissioningResult", out var status)
				 || status.ValueKind != JsonValueKind.String || status.GetString () != "Success" || !response.Value.TryGetProperty ("Id", out var childId)
				 || !childId.TryGetInt32 (out int idValue) || idValue <= 0 || before.Any (d => d.Id == idValue))
				throw new InvalidOperationException ("A new child was not confirmed; reconcile the commissioning receipt before continuing.");
			Record ("created-child", new { DeviceId = idValue, request.ParentId, request.Name, request.ChildModel, request.ParentVersion, request.LocationId });
			DeviceInfo? child;
			while ((child = await client.GetDeviceAsync (idValue, token).ConfigureAwait (false)) == null)
				await Task.Delay (100, token).ConfigureAwait (false);
			void VerifyChild (DeviceInfo observed)
				{
				if (observed.Id != idValue || observed.ParentDeviceId != request.ParentId || observed.Name != request.Name
					 || observed.Model != request.ChildModel || observed.LocationId != request.LocationId || !VersionMatches (observed, request.ParentVersion))
					throw new InvalidOperationException ("The created child no longer matches the commissioning receipt.");
				}
			bool configurationEntered = false;
			while (true)
				{
				child = await client.GetDeviceAsync (idValue, token).ConfigureAwait (false) ?? throw new InvalidOperationException ("The newly commissioned child disappeared.");
				if (child.LocationId == null)
					{
					var inventory = await client.GetDevicesAsync (token).ConfigureAwait (false);
					var load = ObserveNativeLoad (request, child, inventory);
					if (load != null)
						{
						if (before.Any (d => d.Id == load.Id)) throw new InvalidDataException ("The native load existed before commissioning.");
						var native = new ManagedDeviceResult (idValue, "Ready") { NativeLoadId = load.Id };
						Record ("ready-observation", new { Utc = DateTimeOffset.UtcNow, WrapperId = idValue, NativeLoadId = load.Id,
							request.ParentId, request.ManagedDeviceId, request.Name, request.ChildModel, request.LocationId });
						Record ("result", native);
						return native;
						}
					await Task.Delay (200, token).ConfigureAwait (false);
					continue;
					}
				VerifyChild (child);
				if (!configurationEntered && child.Commands.Contains ("cp.driverConfiguration:getFirstConfigurationStep"))
					{
					configurationEntered = true;
					Record ("configuration-entry-intent", new { DeviceId = idValue, Utc = DateTimeOffset.UtcNow });
					var first = await DriverConfiguration.BeginManagedDeviceAsync (client,
						new (idValue, request.ChildModel, request.ParentVersion, "Installed"), request.ParentId, token).ConfigureAwait (false);
					Record ("configuration-entry-response", new { Response = first });
					if (first is { ValueKind: not JsonValueKind.Null })
						{
						var required = new ManagedDeviceResult (idValue, "ConfigurationRequired");
						Record ("result", required);
						return required;
						}
					}
				child = await client.GetDeviceAsync (idValue, token).ConfigureAwait (false) ?? throw new InvalidOperationException ("The newly commissioned child disappeared.");
				if (child.LocationId == null) continue;
				VerifyChild (child);
				if (IsTrue (child, "onlineIndicator:isOnline") && IsTrue (child, "readyIndicator:isReady")) break;
				await Task.Delay (200, token).ConfigureAwait (false);
				}
			Record ("ready-observation", new { Utc = DateTimeOffset.UtcNow, Device = child });
			var result = new ManagedDeviceResult (idValue, "Ready");
			Record ("result", result);
			return result;
			}
		catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
			{
			throw new TimeoutException ("Managed-child commissioning was not verified before the deadline. The journal is retained; no command was retried.");
			}
		}

	internal static DeviceInfo? ObserveNativeLoad (ManagedDeviceRequest request, DeviceInfo wrapper, IReadOnlyList<DeviceInfo> inventory)
		{
		var parent = inventory.SingleOrDefault (d => d.Id == request.ParentId);
		if (parent == null || parent.Model != request.ParentModel || !VersionMatches (parent, request.ParentVersion) ||
			wrapper.Id <= 0 || wrapper.ParentDeviceId != request.ParentId || wrapper.Model != request.ChildModel || wrapper.Name != request.Name || wrapper.LocationId != null ||
			wrapper.PropertyValues.ContainsKey ("cp.driverInformation:version") && !VersionMatches (wrapper, request.ParentVersion) ||
			!wrapper.PropertyValues.TryGetValue ("platform:managedDevices", out var managed) || managed.ValueKind != JsonValueKind.Array || managed.GetArrayLength () != 1 ||
			!managed[0].TryGetProperty ("Id", out var id) || id.ValueKind != JsonValueKind.String || id.GetString () != request.ManagedDeviceId)
			throw new InvalidDataException ("The native wrapper does not match the commissioned device and candidate platform.");
		var loads = inventory.Where (d => d.ParentDeviceId == wrapper.Id).ToArray ();
		if (loads.Length == 0) return null;
		if (loads.Length != 1 || loads[0].Id <= 0 || loads[0].Name != request.Name || loads[0].Model != request.ChildModel ||
			loads[0].LocationId != request.LocationId || inventory.Any (d => d.ParentDeviceId == loads[0].Id) ||
			!loads[0].PropertyValues.TryGetValue ("lightType:variant", out var variant) || variant.ValueKind != JsonValueKind.String || variant.GetString () != "load")
			throw new InvalidDataException ("The native light identity or room is unconfirmed.");
		// Offline native loads can omit their dimmer commands and current level altogether.
		// Identity is still required, but absent controls while offline are not a different device.
		if (!IsTrue (wrapper, "onlineIndicator:isOnline") ||
			!wrapper.PropertyValues.TryGetValue ("cp.driverConfiguration:driverLoadingStatus", out var loading) ||
			loading.ValueKind != JsonValueKind.String || loading.GetString () != "Loaded") return null;
		if (
			!loads[0].Commands.Contains ("lightDimmer:setLevel") ||
			!loads[0].PropertyValues.TryGetValue ("lightDimmer:level", out var level) || level.ValueKind != JsonValueKind.Number ||
			!level.TryGetDouble (out double value) || !double.IsFinite (value) || value < 0 || value > 1)
			throw new InvalidDataException ("The native light identity, room or state is unconfirmed.");
		return loads[0];
		}

	/// <summary>Read current readiness of a receipt-owned child after an interrupted setup. Sends no commands and does not rewrite the original outcome.</summary>
	public static async Task<ManagedDeviceResult> ObserveCreatedAsync (ConfigurationClient client, string commissioningJournal, CancellationToken cancellationToken = default)
		{
		ArgumentNullException.ThrowIfNull (client);
		string journal = Path.GetFullPath (commissioningJournal);
		var request = ReadRequest (Path.Combine (journal, "request.json"));
		using var created = JsonDocument.Parse (File.ReadAllText (Path.Combine (journal, "created-child.json")));
		int id = created.RootElement.GetProperty ("DeviceId").GetInt32 ();
		var expected = JsonSerializer.SerializeToElement (new { DeviceId = id, request.ParentId, request.Name, request.ChildModel, request.ParentVersion, request.LocationId });
		using var response = JsonDocument.Parse (File.ReadAllText (Path.Combine (journal, "commission-response.json")));
		var commissioned = response.RootElement.GetProperty ("Response");
		if (id <= 0 || id == request.ParentId || !JsonElement.DeepEquals (created.RootElement, expected) ||
			commissioned.GetProperty ("Id").GetInt32 () != id || commissioned.GetProperty ("CommissioningResult").GetString () != "Success")
			throw new InvalidDataException ("The commissioning receipt does not identify the created child.");
		var inventory = await client.GetDevicesAsync (cancellationToken).ConfigureAwait (false);
		var parent = inventory.SingleOrDefault (d => d.Id == request.ParentId);
		var child = inventory.SingleOrDefault (d => d.Id == id);
		if (parent == null || parent.Model != request.ParentModel || !VersionMatches (parent, request.ParentVersion) || child == null ||
			child.ParentDeviceId != request.ParentId || child.Name != request.Name || child.Model != request.ChildModel)
			throw new InvalidDataException ("Current identities differ from the commissioning receipt.");
		if (child.LocationId == null)
			{
			var native = ObserveNativeLoad (request, child, inventory);
			return new (id, native == null ? "NotReady" : "Ready") { NativeLoadId = native?.Id };
			}
		if (child.LocationId != request.LocationId || !VersionMatches (child, request.ParentVersion))
			throw new InvalidDataException ("Current child location or version differs from the commissioning receipt.");
		return new (id, IsTrue (child, "onlineIndicator:isOnline") && IsTrue (child, "readyIndicator:isReady") ? "Ready" : "NotReady");
		}

	private static bool VersionMatches (DeviceInfo device, string expected) => device.PropertyValues.TryGetValue ("cp.driverInformation:version", out var version)
		&& version.ValueKind == JsonValueKind.String && DriverVersions.Equal (version.GetString (), expected);
	private static bool IsTrue (DeviceInfo device, string property) => device.PropertyValues.TryGetValue (property, out var value) && value.ValueKind == JsonValueKind.True;
	}
