// Copyright (c) 2026 Neil Colvin. MIT license.
using System.Text.Json;

namespace CrestronHomeDevTools;

/// <summary>Dedicated refresh completion. Null IDs mean unavailable, not an empty successful import.</summary>
public sealed record DriverRefreshCompletion (string OperationId, IReadOnlyList<string>? NewDriverIds);

/// <summary>Correlated observations; neither Ended nor a completion list proves that a requested package imported.</summary>
public sealed record DriverRefreshReceipt (string OperationId, OperationResult Operation,
    bool CompletionMonitoringAvailable, DriverRefreshCompletion? Completion);

/// <summary>Optional connection capability; existing configuration connections remain compatible.</summary>
public interface IDriverRefreshEventSource
    {
    Task<DriverRefreshCompletion> WaitForDriverRefreshCompletionAsync (string operationId, TimeSpan timeout, CancellationToken cancellationToken = default);
    }

internal sealed class DriverRefreshTracker
    {
    private sealed class State
        {
        internal DriverRefreshCompletion? Completion;
        internal Exception? Failure;
        internal int Waiters;
        internal TaskCompletionSource Changed = new (TaskCreationOptions.RunContinuationsAsynchronously);
        }
    private readonly object _gate = new ();
    private readonly Dictionary<string, State> _states = new (StringComparer.Ordinal);
    private Exception? _failure;

    private State Get (string id)
        {
        if (_states.TryGetValue (id, out var state)) return state;
        if (_states.Count >= 256)
            {
            var removable = _states.FirstOrDefault (p => p.Value.Waiters == 0);
            if (removable.Key == null) throw new IOException ("Too many outstanding refresh observations; continuity cannot be confirmed.");
            _states.Remove (removable.Key);
            }
        return _states[id] = new ();
        }

    internal void Accept (JsonElement message)
        {
        if (message.ValueKind != JsonValueKind.Object || !message.TryGetProperty ("EventType", out var type)
            || type.ValueKind != JsonValueKind.String || type.GetString () != "cp.platformDriverController:driversRefreshCompleted"
            || !message.TryGetProperty ("OperationId", out var operation) || operation.ValueKind != JsonValueKind.String) return;
        string? id = operation.GetString ();
        if (string.IsNullOrWhiteSpace (id) || id.Length > 256) return;
        DriverRefreshCompletion? completion = null; Exception? error = null;
        try
            {
            IReadOnlyList<string>? ids = null;
            if (message.TryGetProperty ("NewDriverIds", out var field) && field.ValueKind != JsonValueKind.Null)
                {
                if (field.ValueKind != JsonValueKind.Array || field.GetArrayLength () > 4096) throw new InvalidDataException ();
                var parsed = new List<string> ();
                foreach (var item in field.EnumerateArray ())
                    {
                    if (item.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace (item.GetString ()) || item.GetString ()!.Length > 2048) throw new InvalidDataException ();
                    parsed.Add (item.GetString ()!);
                    }
                ids = parsed.AsReadOnly ();
                }
            completion = new (id, ids);
            }
        catch (InvalidDataException) { error = new InvalidDataException ("The refresh completion layout is unsupported; import outcome is not established."); }
        lock (_gate)
            {
            State state;
            try { state = Get (id); }
            catch (IOException failure) { FailLocked (failure); return; }
            if (error != null) state.Failure ??= error;
            else if (state.Completion == null) state.Completion = completion;
            else if (!Equal (state.Completion.NewDriverIds, completion!.NewDriverIds))
                state.Failure ??= new InvalidDataException ("Conflicting refresh completions were received for one operation.");
            state.Changed.TrySetResult ();
            }
        }

    internal async Task<DriverRefreshCompletion> WaitAsync (string id, TimeSpan timeout, CancellationToken token)
        {
        ArgumentException.ThrowIfNullOrWhiteSpace (id);
        if (id.Length > 256 || timeout <= TimeSpan.Zero || timeout > TimeSpan.FromHours (1)) throw new ArgumentOutOfRangeException (nameof (timeout));
        token.ThrowIfCancellationRequested ();
        State state;
        lock (_gate)
            {
            if (_states.TryGetValue (id, out state!))
                {
                if (state.Failure != null) throw state.Failure;
                if (state.Completion != null) return state.Completion;
                }
            if (_failure != null) throw new IOException ("The refresh event connection ended; outcome is unknown.", _failure);
            state = Get (id); state.Waiters++;
            }
        try
            {
            await state.Changed.Task.WaitAsync (timeout, token).ConfigureAwait (false);
            lock (_gate)
                {
                if (state.Failure != null) throw state.Failure;
                if (state.Completion == null && _failure != null) throw new IOException ("The refresh event connection ended; outcome is unknown.", _failure);
                return state.Completion ?? throw new IOException ("Refresh completion is unavailable.");
                }
            }
        finally { lock (_gate) state.Waiters--; }
        }
    private static bool Equal (IReadOnlyList<string>? first, IReadOnlyList<string>? second)
        => first == null ? second == null : second != null && first.SequenceEqual (second);
    internal void Fail (Exception error) { lock (_gate) FailLocked (error); }
    private void FailLocked (Exception error)
        {
        _failure ??= error;
        foreach (var state in _states.Values) state.Changed.TrySetResult ();
        }
    }
