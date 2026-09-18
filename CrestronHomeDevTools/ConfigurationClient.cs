// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Net;
using System.Text.Json;

namespace CrestronHomeDevTools;

public sealed class ConfigurationClient : IAsyncDisposable
	{
	private const int DriverControllerId = -6;
	private const string DriverCommandPrefix = "cp.platformDriverController:";
	private readonly IConfigurationConnection _connection;
	private readonly bool _ownsConnection;

	public ConfigurationClient (IConfigurationConnection connection, bool ownsConnection = false)
		{
		_connection = connection ?? throw new ArgumentNullException (nameof (connection));
		_ownsConnection = ownsConnection;
		}

	public static async Task<ConfigurationClient> ConnectAsync (ProcessorConnectionOptions options, NetworkCredential credential, CancellationToken cancellationToken = default)
		 => new (await ProcessorConnection.ConnectAsync (options, credential, cancellationToken).ConfigureAwait (false), true);

	public async Task<IReadOnlyList<DriverInfo>> GetDriversAsync (string? search = null, CancellationToken cancellationToken = default)
		{
		var tokens = string.IsNullOrWhiteSpace (search) ? [] : search.Split (' ', StringSplitOptions.RemoveEmptyEntries);
		string[] filterIds = [];
		if (tokens.Length == 0)
			{
			// The processor rejects an empty category list combined with an empty search.
			var categories = await DriverCommandAsync<DriverCategory[]> ("getDriverMetadataFilterOptions", new
				{
				filterType = "PrimaryFunction"
				}, cancellationToken).ConfigureAwait (false)
				?? throw new ProcessorApiException ("Driver categories response was missing.");
			filterIds = categories.Select (category => category.Id).Where (id => !string.IsNullOrWhiteSpace (id)).Distinct ().ToArray ();
			if (filterIds.Length == 0)
				return [];
			}
		// Home rejects more than three search tokens. Intersect catalogue IDs so all
		// requested terms retain the processor's matching rules without broadening the result.
		DriverInfo[]? matches = null;
		foreach (string[] batch in tokens.Chunk (3).DefaultIfEmpty ([]))
			{
			var drivers = await DriverCommandAsync<DriverInfo[]> ("getDrivers", new
				{
				filterType = "PrimaryFunction",
				filterIds,
				substringFilterTextTokens = batch,
				excludeFilterIds = Array.Empty<string> ()
				}, cancellationToken).ConfigureAwait (false) ?? throw new ProcessorApiException ("Driver catalogue response was missing.");
			if (matches is null)
				matches = drivers;
			else
				{
				var ids = drivers.Select (driver => driver.Id).ToHashSet (StringComparer.Ordinal);
				matches = matches.Where (driver => ids.Contains (driver.Id)).ToArray ();
				}
			if (matches.Length == 0)
				break;
			}
		return matches ?? [];
		}

	private sealed record DriverCategory (string Id);

	public async Task<IReadOnlyList<DeviceInfo>> GetDevicesAsync (CancellationToken cancellationToken = default)
		 => (await _connection.GetAsync<Dictionary<string, DeviceInfo>> ("v2/Devices", cancellationToken).ConfigureAwait (false)
			  ?? throw new ProcessorApiException ("Device inventory response was missing.")).Values.ToArray ();

	public async Task<DeviceInfo?> GetDeviceAsync (int deviceId, CancellationToken cancellationToken = default)
		{
		try
			{
			return await _connection.GetAsync<DeviceInfo> ($"v2/Devices/{deviceId}", cancellationToken).ConfigureAwait (false);
			}
		catch (ProcessorApiException exception) when (exception.StatusCode == HttpStatusCode.NotFound) { return null; }
		}

	public Task<DriverInfo?> GetDriverAsync (string driverId, CancellationToken cancellationToken = default)
		{
		ArgumentException.ThrowIfNullOrWhiteSpace (driverId);
		return DriverCommandAsync<DriverInfo> ("getDriver", new
			{
			driverId
			}, cancellationToken);
		}

	public Task<DriverUpdateEligibility?> GetDriverUpdateEligibilityAsync (string driverId, CancellationToken cancellationToken = default)
		{
		ArgumentException.ThrowIfNullOrWhiteSpace (driverId);
		return DriverCommandAsync<DriverUpdateEligibility> ("getDevicesEligibleForDriverUpdate", new
			{
			driverId
			}, cancellationToken);
		}

	public async Task<DriverUpdatePlan> PlanDriverUpdateAsync (string driverId, CancellationToken cancellationToken = default)
		 => new (driverId, await GetDriverUpdateEligibilityAsync (driverId, cancellationToken).ConfigureAwait (false)
			  ?? throw new ProcessorApiException ("The processor did not provide update eligibility for this driver."));

	// Recheck immediately before submission: an old plan must not silently gain extra targets.
	public async Task<string> BeginDriverUpdateAsync (DriverUpdatePlan plan, CancellationToken cancellationToken = default, bool allowProcessorReboot = false)
		{
		ArgumentNullException.ThrowIfNull (plan);
		ValidateUpdate (plan.Eligibility, allowProcessorReboot);
		var current = await PlanDriverUpdateAsync (plan.DriverId, cancellationToken).ConfigureAwait (false);
		ValidateUpdate (current.Eligibility, allowProcessorReboot);
		if (current.Eligibility.IsSwapDriverRequiresReboot != plan.Eligibility.IsSwapDriverRequiresReboot
				|| !DriverVersions.Equal (current.Eligibility.InstalledDriverVersion, plan.Eligibility.InstalledDriverVersion)
			 || !DriverVersions.Equal (current.Eligibility.AvailableDriverVersion, plan.Eligibility.AvailableDriverVersion)
			 || !current.Eligibility.EligibleDeviceIds!.Order ().SequenceEqual (plan.Eligibility.EligibleDeviceIds!.Order ()))
			throw new InvalidOperationException ("Driver eligibility changed. Review a new update plan before applying it.");
		return RequireOperationId (await DriverCommandAsync<string> ("beginSwapDriverForAllEligibleDevices", new
			{
			driverId = plan.DriverId
			}, cancellationToken).ConfigureAwait (false));
		}

	public async Task<string> BeginLocalDriverRefreshAsync (CancellationToken cancellationToken = default)
		 => RequireOperationId (await DriverCommandAsync<string> ("beginLocalDriverRefresh", null, cancellationToken).ConfigureAwait (false));

	public Task<bool?> IsDriverRefreshInProgressAsync (CancellationToken cancellationToken = default)
		 => DriverCommandAsync<bool?> ("getIsDriversRefreshInProgress", null, cancellationToken);

	public async Task<IReadOnlyList<int>> GetReloadAffectedDevicesAsync (int deviceId, CancellationToken cancellationToken = default)
		 => await DriverCommandAsync<int[]> ("findUnloadReloadDriversAffectedDevices", new
			 {
			 deviceId
			 }, cancellationToken).ConfigureAwait (false)
			  ?? throw new ProcessorApiException ("The processor did not provide the reload scope.");

	public async Task<string> BeginReloadDriverAsync (int deviceId, CancellationToken cancellationToken = default)
		{
		var device = await GetDeviceAsync (deviceId, cancellationToken).ConfigureAwait (false)
			 ?? throw new ProcessorApiException ("The device was not found.");
		if (!device.PropertyValues.TryGetValue ("cp.driverConfiguration:supportsUnloadReloadDriver", out var supported)
			 || supported.ValueKind != JsonValueKind.True)
			throw new InvalidOperationException ("The processor has not confirmed that this driver supports reload.");
		// This first version deliberately restricts automation to confirmed reboot-free drivers.
		if (!device.PropertyValues.TryGetValue ("cp.driverConfiguration:swapDriverRequiresReboot", out var reboot)
			 || reboot.ValueKind != JsonValueKind.False)
			throw new InvalidOperationException ("This driver is not confirmed to support reboot-free replacement.");
		return RequireOperationId (await DriverCommandAsync<string> ("beginReloadDrivers", new
			{
			deviceId,
			reloadReferenceDeviceOnly = true
			}, cancellationToken).ConfigureAwait (false));
		}

	/// <summary>Read the processor's configured location identities without device settings.</summary>
	public async Task<IReadOnlyList<ProcessorLocation>> GetLocationsAsync (CancellationToken cancellationToken = default)
		=> await _connection.GetAsync<ProcessorLocation[]> ("v2/Locations", cancellationToken).ConfigureAwait (false)
			?? throw new ProcessorApiException ("Location inventory response was missing.");

	/// <summary>Move one childless installed driver to an existing room, preserving its identity and version.</summary>
	public async Task<DriverRoomMoveResult> MoveDriverInstanceAsync (int deviceId, string expectedModel, string expectedVersion,
		 int expectedLocationId, int destinationLocationId, TimeSpan timeout, CancellationToken cancellationToken = default)
		{
		ArgumentException.ThrowIfNullOrWhiteSpace (expectedModel);
		ArgumentException.ThrowIfNullOrWhiteSpace (expectedVersion);
		if (deviceId <= 0 || expectedLocationId <= 0 || destinationLocationId <= 0 || timeout <= TimeSpan.Zero)
			throw new ArgumentException ("Device, original room, destination room and timeout must be positive.");
		using var deadline = CancellationTokenSource.CreateLinkedTokenSource (cancellationToken);
		deadline.CancelAfter (timeout);
		var locations = await GetLocationsAsync (deadline.Token).ConfigureAwait (false);
		if (locations.Count (location => location.Id == destinationLocationId && location.Category == "Room") != 1)
			throw new InvalidOperationException ("The destination must identify exactly one existing room.");
		var original = await GetDeviceAsync (deviceId, deadline.Token).ConfigureAwait (false)
			?? throw new InvalidOperationException ("The selected driver instance was not found.");
		bool IdentityMatches (DeviceInfo device) => device.Id == deviceId && device.Model == expectedModel && device.ParentDeviceId == DriverControllerId
			&& device.Name == original.Name && device.PropertyValues.TryGetValue ("cp.driverInformation:version", out var version)
			&& version.ValueKind == JsonValueKind.String && DriverVersions.Equal (version.GetString (), expectedVersion);
		if (!IdentityMatches (original) || original.LocationId != expectedLocationId || !original.Commands.Contains ("cp.deviceConfiguration:setLocation"))
			throw new InvalidOperationException ("Driver identity, original room or advertised move command changed.");
		if (!original.PropertyValues.TryGetValue ("cp.driverConfiguration:supportsUnloadReloadDriver", out var supports) || supports.ValueKind != JsonValueKind.True
			|| !original.PropertyValues.TryGetValue ("cp.driverConfiguration:swapDriverRequiresReboot", out var reboot) || reboot.ValueKind != JsonValueKind.False
			|| !original.PropertyValues.TryGetValue ("cp.driverConfiguration:driverLoadingStatus", out var loading) || loading.GetString () != "Loaded")
			throw new InvalidOperationException ("Room moves require a loaded driver with confirmed reboot-free lifecycle support.");
		var devices = await GetDevicesAsync (deadline.Token).ConfigureAwait (false);
		if (devices.Any (device => device.ParentDeviceId == deviceId))
			throw new InvalidOperationException ("Managed-child room changes need a separate reviewed plan; this operation moves childless driver instances only.");
		if (devices.Any (device => device.Id != deviceId && device.LocationId == destinationLocationId && device.Name == original.Name))
			throw new InvalidOperationException ("The destination already contains another device with this name.");
		bool changed = expectedLocationId != destinationLocationId;
		if (changed)
			{
			var current = await GetDeviceAsync (deviceId, deadline.Token).ConfigureAwait (false);
			if (current == null || !IdentityMatches (current) || current.LocationId != expectedLocationId)
				throw new InvalidOperationException ("Driver identity or room changed before submission.");
			await ExecuteDeviceCommandAsync (deviceId, "cp.deviceConfiguration:setLocation", new
				{
				// This command requires a JSON number. A string can be treated as null/removal.
				locationId = destinationLocationId
				}, deadline.Token).ConfigureAwait (false);
			}
		while (true)
			{
			var current = await GetDeviceAsync (deviceId, deadline.Token).ConfigureAwait (false);
			if (current == null || !IdentityMatches (current) || current.LocationId != expectedLocationId && current.LocationId != destinationLocationId)
				throw new InvalidOperationException ("Room move outcome is unconfirmed; inspect the instance before retrying.");
			if (current.LocationId == destinationLocationId && current.PropertyValues.TryGetValue ("cp.driverConfiguration:driverLoadingStatus", out var status)
				&& status.ValueKind == JsonValueKind.String && status.GetString () == "Loaded")
				return new (deviceId, expectedModel, expectedVersion, expectedLocationId, destinationLocationId, changed);
			await Task.Delay (500, deadline.Token).ConfigureAwait (false);
			}
		}

	public async Task RemoveDriverInstanceAsync (int deviceId, string expectedModel, string expectedVersion, TimeSpan timeout, CancellationToken cancellationToken = default, DriverRebootHandler? rebootHandler = null)
		{
		ArgumentException.ThrowIfNullOrWhiteSpace (expectedModel);
		ArgumentException.ThrowIfNullOrWhiteSpace (expectedVersion);
		if (deviceId <= 0 || timeout <= TimeSpan.Zero)
			throw new ArgumentOutOfRangeException (nameof (deviceId));
		using var deadline = CancellationTokenSource.CreateLinkedTokenSource (cancellationToken);
		deadline.CancelAfter (timeout);
		var device = await GetDeviceAsync (deviceId, deadline.Token).ConfigureAwait (false)
			?? throw new ProcessorApiException ("The selected driver instance was not found.");
		if (device.Model != expectedModel || !device.PropertyValues.TryGetValue ("cp.driverInformation:version", out var version)
			|| version.ValueKind != JsonValueKind.String || !DriverVersions.Equal (version.GetString (), expectedVersion))
			throw new InvalidOperationException ("Driver instance identity or version changed; removal was not submitted.");
		if (!device.Commands.Contains ("cp.deviceConfiguration:setLocation")
			|| (rebootHandler?.RebootAfterRemoval != true &&
					 (!device.PropertyValues.TryGetValue ("cp.driverConfiguration:supportsUnloadReloadDriver", out var supported) || supported.ValueKind != JsonValueKind.True
					 || !device.PropertyValues.TryGetValue ("cp.driverConfiguration:swapDriverRequiresReboot", out var reboot) || reboot.ValueKind != JsonValueKind.False)))
			throw new InvalidOperationException ("This instance is not confirmed to support removal without reboot.");
		var affected = await GetReloadAffectedDevicesAsync (deviceId, deadline.Token).ConfigureAwait (false);
		var additional = rebootHandler?.AdditionalRemovalRebootDeviceIds ?? [];
		if (additional.Length > 0 && (rebootHandler?.RebootAfterRemoval != true || additional.Any (id => id <= 0 || id == deviceId)
			|| additional.Distinct ().Count () != additional.Length))
			throw new InvalidOperationException ("Additional removal scope requires distinct reviewed instances and an explicit reboot policy.");
		if (affected.Count != additional.Length + 1 || affected.Distinct ().Count () != affected.Count
			|| !affected.Order ().SequenceEqual (additional.Append (deviceId).Order ()))
			throw new InvalidOperationException ("The dependency scope includes other devices or is unknown; automatic removal was not submitted.");
		var preserved = new List<DeviceInfo> ();
		foreach (var id in additional)
			{
			var other = await GetDeviceAsync (id, deadline.Token).ConfigureAwait (false)
				?? throw new InvalidOperationException ("A reviewed shared instance is missing.");
			if (other.Model != expectedModel || Property (other, "cp.driverConfiguration:driverLoadingStatus") != "Loaded"
				|| !DriverVersions.Equal (Property (other, "cp.driverInformation:version"), expectedVersion))
				throw new InvalidOperationException ("A reviewed shared instance changed model, version or loading state.");
			preserved.Add (other);
			}
		var removalReboot = rebootHandler?.RebootAfterRemoval == true
			 ? new DriverRebootRequest ("Remove", deviceId, expectedModel, expectedVersion, DriverRebootMode.ExplicitAfterOperation) : null;
		if (removalReboot != null)
			await rebootHandler!.BeforeSubmitAsync (removalReboot, deadline.Token).ConfigureAwait (false);
		await ExecuteDeviceCommandAsync (deviceId, "cp.deviceConfiguration:setLocation", new
			{
			locationId = (string?)null
			}, deadline.Token).ConfigureAwait (false);
		var verification = removalReboot == null ? this : await rebootHandler!.RecoverAsync (removalReboot, this, deadline.Token).ConfigureAwait (false);
		// Verify disappearance, rather than mistaking a removed room assignment for disposal.
		while (true)
			{
			var current = await verification.GetDevicesAsync (deadline.Token).ConfigureAwait (false);
			if (current.All (item => item.Id != deviceId) && preserved.All (old => current.Any (item => Preserved (old, item))))
				break;
			await Task.Delay (500, deadline.Token).ConfigureAwait (false);
			}
		static string? Property (DeviceInfo value, string name) => value.PropertyValues.TryGetValue (name, out var item) ? item.ToString () : null;
		static bool SameProperty (DeviceInfo old, DeviceInfo current, string name)
			{
			bool before = old.PropertyValues.TryGetValue (name, out var first), after = current.PropertyValues.TryGetValue (name, out var second);
			return before == after && (!before || JsonElement.DeepEquals (first, second));
			}
		static bool Preserved (DeviceInfo old, DeviceInfo current) => old.Id == current.Id && old.Name == current.Name && old.Model == current.Model
			&& old.ParentDeviceId == current.ParentDeviceId && old.LocationId == current.LocationId
			&& DriverVersions.Equal (Property (old, "cp.driverInformation:version"), Property (current, "cp.driverInformation:version"))
			&& Property (current, "cp.driverConfiguration:driverLoadingStatus") == "Loaded"
			&& SameProperty (old, current, "cp.driverConfiguration:isConfigured") && SameProperty (old, current, "cp.driverConfiguration:configurationItems");
		}

	public async Task<IReadOnlyList<DriverInstanceState>> WaitForDriverVersionAsync (IReadOnlyList<int> deviceIds, string expectedVersion, TimeSpan timeout, CancellationToken cancellationToken = default)
		{
		ArgumentNullException.ThrowIfNull (deviceIds);
		ArgumentException.ThrowIfNullOrWhiteSpace (expectedVersion);
		if (deviceIds.Count == 0 || deviceIds.Any (id => id <= 0) || deviceIds.Distinct ().Count () != deviceIds.Count)
			throw new ArgumentException ("Provide the unique installed device IDs whose version must be verified.", nameof (deviceIds));
		if (timeout <= TimeSpan.Zero)
			throw new ArgumentOutOfRangeException (nameof (timeout));
		using var deadline = CancellationTokenSource.CreateLinkedTokenSource (cancellationToken);
		deadline.CancelAfter (timeout);
		while (true)
			{
			var states = new List<DriverInstanceState> ();
			foreach (var deviceId in deviceIds)
				{
				var device = await GetDeviceAsync (deviceId, deadline.Token).ConfigureAwait (false);
				string? Property (string name) => device?.PropertyValues.TryGetValue (name, out var value) == true && value.ValueKind == JsonValueKind.String ? value.GetString () : null;
				states.Add (new (deviceId, Property ("cp.driverInformation:version"), Property ("cp.driverConfiguration:driverLoadingStatus")));
				}
			var failed = states.Where (state => DriverVersions.Equal (state.Version, expectedVersion) && state.LoadingStatus == "FailedToLoad").ToArray ();
			if (failed.Length != 0)
				throw new InvalidOperationException ($"Driver instance(s) {string.Join (", ", failed.Select (state => state.DeviceId))} reported FailedToLoad for version {expectedVersion}.");
			if (states.All (state => DriverVersions.Equal (state.Version, expectedVersion) && state.LoadingStatus == "Loaded"))
				return states;
			await Task.Delay (500, deadline.Token).ConfigureAwait (false);
			}
		}

	/// <summary>Request Home's configuration-aware restart once, after explicit caller confirmation.</summary>
	public async Task<string?> RequestProcessorRebootAsync (Func<DeviceInfo, CancellationToken, Task<bool>> confirm,
		 string reason, CancellationToken cancellationToken = default)
		{
		ArgumentNullException.ThrowIfNull (confirm);
		ArgumentException.ThrowIfNullOrWhiteSpace (reason);
		const string command = "cp.processorOperations:beginReboot";
		var targets = (await GetDevicesAsync (cancellationToken).ConfigureAwait (false)).Where (d => d.Commands.Contains (command)).ToArray ();
		if (targets.Length != 1)
			throw new InvalidOperationException ("The connection does not identify a unique processor reboot capability.");
		var target = targets[0];
		if (!await confirm (target, cancellationToken).ConfigureAwait (false))
			return null;
		var current = await GetDeviceAsync (target.Id, cancellationToken).ConfigureAwait (false);
		if (current == null || current.Model != target.Model || !current.Commands.Contains (command))
			throw new InvalidOperationException ("Processor identity or reboot capability changed before submission.");
		return RequireOperationId (await _connection.ExecuteAsync<string> (target.Id, command, new
			{
			rebootReasonInAFewWords = reason
			}, cancellationToken).ConfigureAwait (false));
		}

	public Task<DriverSwapResult> WaitForDriverSwapAsync (string operationId, string driverId, TimeSpan timeout, CancellationToken cancellationToken = default)
		 => _connection.WaitForDriverSwapAsync (operationId, driverId, timeout, cancellationToken);

	public Task<OperationResult> WaitForOperationAsync (string operationId, TimeSpan timeout, CancellationToken cancellationToken = default)
		 => _connection.WaitForOperationAsync (operationId, timeout, cancellationToken);

	public Task<JsonElement?> ExecuteDeviceCommandAsync (int deviceId, string commandName, object? parameters = null, CancellationToken cancellationToken = default)
		{
		ArgumentException.ThrowIfNullOrWhiteSpace (commandName);
		return _connection.ExecuteAsync<JsonElement?> (deviceId, commandName, parameters, cancellationToken);
		}

	public Task WaitForDisconnectAsync (CancellationToken cancellationToken = default) => _connection.WaitForDisconnectAsync (cancellationToken);

	public ValueTask DisposeAsync () => _ownsConnection ? _connection.DisposeAsync () : ValueTask.CompletedTask;

	private Task<T?> DriverCommandAsync<T> (string command, object? parameters, CancellationToken cancellationToken)
		 => _connection.ExecuteAsync<T> (DriverControllerId, DriverCommandPrefix + command, parameters, cancellationToken);

	private static string RequireOperationId (string? id)
		 => !string.IsNullOrWhiteSpace (id) ? id : throw new ProcessorApiException ("The processor did not return an operation ID; submission cannot be confirmed.");

	private static void ValidateUpdate (DriverUpdateEligibility eligibility, bool allowProcessorReboot)
		{
		ArgumentNullException.ThrowIfNull (eligibility);
		if (eligibility.IsSupportsSwapDriver != true || eligibility.IsSwapDriverRequiresReboot == null || (eligibility.IsSwapDriverRequiresReboot == true && !allowProcessorReboot))
			throw new InvalidOperationException ("This update is not confirmed to be supported without a processor reboot.");
		if (string.IsNullOrWhiteSpace (eligibility.InstalledDriverVersion) || string.IsNullOrWhiteSpace (eligibility.AvailableDriverVersion))
			throw new InvalidOperationException ("The processor did not identify both driver versions.");
		if (eligibility.EligibleDeviceIds is not { Length: > 0 })
			throw new InvalidOperationException ("The update has no confirmed eligible devices.");
		}
	}