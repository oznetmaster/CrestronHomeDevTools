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
		return await DriverCommandAsync<DriverInfo[]> ("getDrivers", new
			{
			filterType = "PrimaryFunction",
			filterIds,
			substringFilterTextTokens = tokens,
			excludeFilterIds = Array.Empty<string> ()
			}, cancellationToken).ConfigureAwait (false) ?? throw new ProcessorApiException ("Driver catalogue response was missing.");
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
		if (affected.Count != 1 || affected[0] != deviceId)
			throw new InvalidOperationException ("The dependency scope includes other devices or is unknown; automatic removal was not submitted.");
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
		while ((await verification.GetDevicesAsync (deadline.Token).ConfigureAwait (false)).Any (current => current.Id == deviceId))
			await Task.Delay (500, deadline.Token).ConfigureAwait (false);
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