// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Text.Json;

namespace CrestronHomeDevTools;

public sealed record DriverConfigurationItem (string Id, string? Title, string? ValueType, bool? Required,
	bool? ReadOnly, bool Masked, bool HasCurrentValue, JsonElement? CurrentValue);

public sealed record DriverConfigurationSnapshot (int DeviceId, string? Name, string? Model, string? Version,
	bool? IsConfigured, bool? IsReconfigurable, bool ItemsAvailable, IReadOnlyList<DriverConfigurationItem> Items);

public static class DriverConfigurationInspection
	{
	// Fetches advertised current settings only. It neither starts nor advances a configuration wizard.
	public static async Task<DriverConfigurationSnapshot> GetAsync (ConfigurationClient client, int deviceId,
		CancellationToken cancellationToken = default)
		{
		ArgumentNullException.ThrowIfNull (client);
		if (deviceId <= 0) throw new ArgumentOutOfRangeException (nameof (deviceId));
		var device = await client.GetDeviceAsync (deviceId, cancellationToken).ConfigureAwait (false)
			?? throw new InvalidOperationException ("The installed driver was not found.");
		if (device.Id != deviceId) throw new InvalidOperationException ("The processor returned a different device.");
		return FromDevice (device);
		}

	internal static DriverConfigurationSnapshot FromDevice (DeviceInfo device)
		{
		JsonElement Property (string name) => device.PropertyValues.TryGetValue ("cp.driverConfiguration:" + name, out var value) ? value : default;
		var rawItems = Property ("configurationItems");
		var items = new List<DriverConfigurationItem> ();
		if (rawItems.ValueKind == JsonValueKind.Array)
			foreach (var item in rawItems.EnumerateArray ())
				{
				string? id = Text (item, "Id");
				if (string.IsNullOrWhiteSpace (id)) throw new InvalidDataException ("Configuration item metadata was incomplete.");
				string? title = Text (item, "Title");
				var value = Member (item, "Value");
				bool masked = Boolean (Member (value, "Masked")) == true;
				var current = Member (value, "CurrentValue");
				bool available = current.ValueKind is JsonValueKind.String or JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False;
				items.Add (new (id, title, Text (item, "ValueType"), Boolean (Member (item, "Required")),
					Boolean (Member (value, "ReadOnly")), masked, available, !masked && available ? current.Clone () : null));
				}
		return new (device.Id, device.Name, device.Model,
			device.PropertyValues.TryGetValue ("cp.driverInformation:version", out var version) && version.ValueKind == JsonValueKind.String ? version.GetString () : null,
			Boolean (Property ("isConfigured")), Boolean (Property ("isReconfigurable")), rawItems.ValueKind == JsonValueKind.Array, items);
		}

	private static JsonElement Member (JsonElement element, string name)
		=> element.ValueKind == JsonValueKind.Object && element.TryGetProperty (name, out var value) ? value : default;
	private static string? Text (JsonElement element, string name)
		=> Member (element, name) is { ValueKind: JsonValueKind.String } text ? text.GetString () : null;
	private static bool? Boolean (JsonElement value) => value.ValueKind switch { JsonValueKind.True => true, JsonValueKind.False => false, _ => null };
	}