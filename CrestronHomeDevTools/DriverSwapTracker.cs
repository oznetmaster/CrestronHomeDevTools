// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Text.Json;

namespace CrestronHomeDevTools;

public sealed record DriverSwapResult (string OperationId, string DriverId, bool IsRebootRequired, int[] DeviceIdsRequiringReconfiguration);

internal sealed class DriverSwapTracker
	{
	private readonly object _gate = new ();
	private readonly OperationTracker _completion = new ();
	private readonly Dictionary<string, DriverSwapResult> _results = [];

	public void Accept (JsonElement message)
		{
		if (!message.TryGetProperty ("EventType", out var type) || type.ValueKind != JsonValueKind.String
			 || !message.TryGetProperty ("OperationId", out var id) || id.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace (id.GetString ()))
			return;
		var eventType = type.GetString ();
		var operation = id.GetString ()!;
		if (eventType == "cp.types:operationStatusChanged")
			{
			// Generic success/Ended does not establish that the driver was staged for reboot.
			if (message.TryGetProperty ("LatestStatus", out var status) && status.ToString () is "Failed" or "2")
				_completion.Accept (message);
			return;
			}
		if (eventType is not ("cp.platformDriverController:swapDriverCompleted" or "cp.platformDriverController:swapDriverFailed"))
			return;
		bool valid = eventType.EndsWith (":swapDriverCompleted", StringComparison.Ordinal)
			 && message.TryGetProperty ("DriverId", out var driver) && driver.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace (driver.GetString ())
			 && message.TryGetProperty ("IsRebootRequired", out var reboot) && reboot.ValueKind is JsonValueKind.True or JsonValueKind.False
			 && message.TryGetProperty ("DeviceIdsRequiringReconfiguration", out var reconfiguration) && reconfiguration.ValueKind == JsonValueKind.Array;
		DriverSwapResult? result = null;
		if (valid)
			{
			try
				{
				result = new (operation, message.GetProperty ("DriverId").GetString ()!, message.GetProperty ("IsRebootRequired").GetBoolean (), message.GetProperty ("DeviceIdsRequiringReconfiguration").Deserialize<int[]> ()!);
				}
			catch (JsonException) { valid = false; }
			}
		lock (_gate)
			{
			if (result != null && valid && !_results.ContainsKey (operation))
				_results.Add (operation, result);
			if (_results.Count > 256)
				_results.Remove (_results.Keys.First ());
			_completion.Accept (JsonSerializer.SerializeToElement (new
				{
				EventType = "cp.types:operationStatusChanged",
				OperationId = operation,
				LatestStatus = valid ? "Succeeded" : "Failed"
				}));
			}
		}

	public async Task<DriverSwapResult> WaitAsync (string operation, string driver, TimeSpan timeout, CancellationToken token)
		{
		var completed = await _completion.WaitAsync (operation, timeout, token).ConfigureAwait (false);
		lock (_gate)
			{
			if (completed.Status != "Succeeded" || !_results.TryGetValue (operation, out var result) || result.DriverId != driver)
				throw new ProcessorApiException ("Driver swap completion did not confirm the requested operation and driver. Reboot was not authorized by this result.");
			return result;
			}
		}

	public void Fail (Exception exception) => _completion.Fail (exception);
	}