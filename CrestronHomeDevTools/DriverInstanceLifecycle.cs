// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Globalization;
using System.Text.Json;

namespace CrestronHomeDevTools;

public sealed record DriverInstanceReady (int DeviceId, string Model, string Version, string Action);

public static class DriverInstanceLifecycle
	{
	public static async Task<DriverInstanceReady> EnsureAsync (ConfigurationClient client, string driverId, string instanceName,
		 int locationId, int? expectedDeviceId, TimeSpan timeout, CancellationToken cancellationToken = default, DriverRebootHandler? reboot = null)
		{
		ArgumentNullException.ThrowIfNull (client);
		ArgumentException.ThrowIfNullOrWhiteSpace (driverId);
		ArgumentException.ThrowIfNullOrWhiteSpace (instanceName);
		if (instanceName.Length > 32)
			throw new ArgumentException ("Instance names must not exceed 32 characters.", nameof (instanceName));
		if (locationId <= 0 || expectedDeviceId is <= 0 || timeout <= TimeSpan.Zero)
			throw new ArgumentException ("Location, optional device ID and timeout must be positive.");
		using var deadline = CancellationTokenSource.CreateLinkedTokenSource (cancellationToken);
		deadline.CancelAfter (timeout);
		DriverInfo? driver;
		while ((driver = await client.GetDriverAsync (driverId, deadline.Token).ConfigureAwait (false)) == null)
			await Task.Delay (500, deadline.Token).ConfigureAwait (false);
		if (string.IsNullOrWhiteSpace (driver.Model) || string.IsNullOrWhiteSpace (driver.Version))
			throw new ProcessorApiException ("The requested catalogue driver did not identify its model and version.");
		var devices = await client.GetDevicesAsync (deadline.Token).ConfigureAwait (false);
		var candidates = devices.Where (device => device.Model == driver.Model && device.Name == instanceName && device.LocationId == locationId).ToArray ();
		if (candidates.Length > 1)
			throw new InvalidOperationException ("More than one instance matches this target.");
		DeviceInfo? current = expectedDeviceId.HasValue ? devices.SingleOrDefault (device => device.Id == expectedDeviceId) : candidates.SingleOrDefault ();
		if (current == null && expectedDeviceId.HasValue && candidates.Length != 0)
			throw new InvalidOperationException ("A matching instance has a different device ID; inspect it before proceeding.");
		if (current != null && (current.Model != driver.Model || current.Name != instanceName || current.LocationId != locationId))
			throw new InvalidOperationException ("The selected device no longer matches the configured model, name and room.");
		var action = "Existing";
		int deviceId;
		if (current == null)
			{
			if (devices.Any (device => device.Name == instanceName && device.LocationId == locationId))
				throw new InvalidOperationException ("This room already contains another device with the configured instance name.");
			var installReboot = reboot?.RebootAfterInstall == true ? new DriverRebootRequest ("Install", null, driver.Model, driver.Version, DriverRebootMode.ExplicitAfterOperation) : null;
			if (installReboot != null)
				await reboot!.BeforeSubmitAsync (installReboot, deadline.Token).ConfigureAwait (false);
			var prepared = await client.ExecuteDeviceCommandAsync (-6, "cp.platformDriverController:prepareDriverForUse", new
				{
				driverId
				}, deadline.Token).ConfigureAwait (false);
			if (prepared?.ValueKind != JsonValueKind.String || prepared.Value.GetString () != "Success")
				throw new ProcessorApiException ("The processor did not confirm that this driver is ready to install.");
			var result = await client.ExecuteDeviceCommandAsync (-6, "cp.platformDriverController:commissionDevice",
				 new
					 {
					 driverId,
					 name = instanceName,
					 locationId = locationId.ToString (CultureInfo.InvariantCulture)
					 }, deadline.Token).ConfigureAwait (false);
			if (result?.ValueKind != JsonValueKind.Object || !result.Value.TryGetProperty ("CommissioningResult", out var outcome)
				 || outcome.ValueKind != JsonValueKind.String || outcome.GetString () != "Success"
				 || !result.Value.TryGetProperty ("Id", out var id) || !id.TryGetInt32 (out deviceId) || deviceId <= 0)
				throw new ProcessorApiException ("Installation was not confirmed. Inspect device inventory before retrying.");
			action = "Installed";
			if (installReboot != null)
				client = await reboot!.RecoverAsync (installReboot with
					{
					DeviceId = deviceId
					}, client, deadline.Token).ConfigureAwait (false);
			}
		else
			{
			deviceId = current.Id;
			if (!current.PropertyValues.TryGetValue ("cp.driverInformation:version", out var version) || version.ValueKind != JsonValueKind.String)
				throw new ProcessorApiException ("The installed driver version is unknown.");
			var installed = version.GetString ();
			if (!DriverVersions.Equal (installed, driver.Version))
				{
				if (!Version.TryParse (installed, out var oldVersion) || !Version.TryParse (driver.Version, out var newVersion) || oldVersion >= newVersion)
					throw new InvalidOperationException ("The requested change is not a confirmed upgrade; an automatic downgrade is not allowed.");
				DriverUpdateEligibility? eligibility;
				while (true)
					{
					eligibility = await client.GetDriverUpdateEligibilityAsync (driverId, deadline.Token).ConfigureAwait (false);
					if ((eligibility?.IsSwapDriverRequiresReboot == true && reboot == null) || eligibility?.IsSupportsSwapDriver == false)
						throw new InvalidOperationException ("The processor does not support this update without reboot.");
					if (eligibility != null && DriverVersions.Equal (eligibility.AvailableDriverVersion, driver.Version) && DriverVersions.Equal (eligibility.InstalledDriverVersion, installed)
						 && eligibility.IsSupportsSwapDriver == true && eligibility.IsSwapDriverRequiresReboot != null && (eligibility.IsSwapDriverRequiresReboot == false || reboot != null)
						 && eligibility.EligibleDeviceIds is { Length: > 0 })
						break;
					await Task.Delay (500, deadline.Token).ConfigureAwait (false);
					}
				if (eligibility.EligibleDeviceIds.Length != 1 || eligibility.EligibleDeviceIds[0] != deviceId)
					throw new InvalidOperationException ("Updating this driver would affect other instances; use an explicitly reviewed update plan instead.");
				var updateReboot = eligibility.IsSwapDriverRequiresReboot == true
					 ? new DriverRebootRequest ("Update", deviceId, driver.Model, driver.Version, DriverRebootMode.ExplicitAfterOperation) : null;
				if (updateReboot != null)
					await reboot!.BeforeSubmitAsync (updateReboot, deadline.Token).ConfigureAwait (false);
				var operationId = await client.BeginDriverUpdateAsync (new (driverId, eligibility), deadline.Token, updateReboot != null).ConfigureAwait (false);
				if (updateReboot != null)
					{
					// V1 swap stages the update; the caller must request reboot after this exact completion event.
					var completed = await client.WaitForDriverSwapAsync (operationId, driverId, timeout, deadline.Token).ConfigureAwait (false);
					if (!completed.IsRebootRequired || completed.DeviceIdsRequiringReconfiguration.Length != 0)
						throw new InvalidOperationException ("Driver swap did not confirm a reboot-only completion. Inspect reconfiguration requirements before continuing.");
					updateReboot = updateReboot with
						{
						SwapCompletion = completed
						};
					}
				else
					{
					var operation = await client.WaitForOperationAsync (operationId, timeout, deadline.Token).ConfigureAwait (false);
					if (operation.Status == "Failed")
						throw new ProcessorApiException ("The processor reported an update failure.");
					}
				if (updateReboot != null)
					client = await reboot!.RecoverAsync (updateReboot, client, deadline.Token).ConfigureAwait (false);
				action = "Updated";
				}
			}
		await client.WaitForDriverVersionAsync ([deviceId], driver.Version, timeout, deadline.Token).ConfigureAwait (false);
		if (reboot != null)
			{
			var verified = await client.GetDeviceAsync (deviceId, deadline.Token).ConfigureAwait (false);
			if (verified?.Model != driver.Model || verified.Name != instanceName || verified.LocationId != locationId)
				throw new InvalidOperationException ("Driver identity changed during restart; activation was not verified.");
			}
		return new (deviceId, driver.Model, driver.Version, action);
		}
	}