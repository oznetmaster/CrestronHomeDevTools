// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Text.Json;

namespace CrestronHomeDevTools;

internal sealed class OperationTracker
	{
	private readonly object _gate = new ();
	private readonly Dictionary<string, TaskCompletionSource<OperationResult>> _waiting = [];
	private readonly Dictionary<string, OperationResult> _finished = [];
	private Exception? _failure;

	public void Accept (JsonElement message)
		{
		if (!message.TryGetProperty ("EventType", out var type) || type.GetString () != "cp.types:operationStatusChanged"
			 || !message.TryGetProperty ("OperationId", out var idField) || idField.ValueKind != JsonValueKind.String
			 || !message.TryGetProperty ("LatestStatus", out var statusField))
			return;
		var id = idField.GetString ();
		if (string.IsNullOrEmpty (id))
			return;
		var status = statusField.ValueKind == JsonValueKind.String ? statusField.GetString () : statusField.ToString () switch
			{
				"0" => "Started",
				"1" => "Progress",
				"2" => "Failed",
				"3" => "Succeeded",
				"4" => "Ended",
				_ => null
				};
		if (status is not ("Succeeded" or "Failed" or "Ended"))
			return;
		var result = new OperationResult (id, status, message.TryGetProperty ("Message", out var text) && text.ValueKind == JsonValueKind.String ? text.GetString () : null);
		lock (_gate)
			{
			// Ended is not evidence of success and must not overwrite a preceding failure.
			if (_finished.ContainsKey (id))
				return;
			_finished[id] = result;
			if (_waiting.Remove (id, out var waiter))
				waiter.TrySetResult (result);
			if (_finished.Count > 256)
				_finished.Remove (_finished.Keys.First ());
			}
		}

	public async Task<OperationResult> WaitAsync (string id, TimeSpan timeout, CancellationToken cancellationToken)
		{
		ArgumentException.ThrowIfNullOrWhiteSpace (id);
		if (timeout <= TimeSpan.Zero)
			throw new ArgumentOutOfRangeException (nameof (timeout));
		TaskCompletionSource<OperationResult> waiter;
		lock (_gate)
			{
			if (_finished.TryGetValue (id, out var completed))
				return completed;
			if (_failure != null)
				throw new IOException ("The processor event connection is unavailable; operation outcome is unknown.", _failure);
			if (_waiting.ContainsKey (id))
				throw new InvalidOperationException ("An operation waiter already exists for this ID.");
			_waiting[id] = waiter = new (TaskCreationOptions.RunContinuationsAsynchronously);
			}
		try
			{
			return await waiter.Task.WaitAsync (timeout, cancellationToken).ConfigureAwait (false);
			}
		finally { lock (_gate) { _waiting.Remove (id); } }
		}

	public void Fail (Exception failure)
		{
		lock (_gate)
			{
			_failure = failure;
			foreach (var waiter in _waiting.Values)
				waiter.TrySetException (new IOException ("Processor connection closed; operation outcome is unknown.", failure));
			_waiting.Clear ();
			}
		}
	}