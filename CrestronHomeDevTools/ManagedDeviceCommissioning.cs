// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Text.Json;

namespace CrestronHomeDevTools;

public sealed record ManagedDeviceRequest (int ParentId, string ParentModel, string ParentVersion,
	string ManagedDeviceId, string Name, string ChildModel, int LocationId);
public sealed record ManagedDeviceResult (int DeviceId, string State);

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
			VerifyChild (child);
			bool configurationEntered = false;
			while (true)
				{
				child = await client.GetDeviceAsync (idValue, token).ConfigureAwait (false) ?? throw new InvalidOperationException ("The newly commissioned child disappeared.");
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

	private static bool VersionMatches (DeviceInfo device, string expected) => device.PropertyValues.TryGetValue ("cp.driverInformation:version", out var version)
		&& version.ValueKind == JsonValueKind.String && DriverVersions.Equal (version.GetString (), expected);
	private static bool IsTrue (DeviceInfo device, string property) => device.PropertyValues.TryGetValue (property, out var value) && value.ValueKind == JsonValueKind.True;
	}