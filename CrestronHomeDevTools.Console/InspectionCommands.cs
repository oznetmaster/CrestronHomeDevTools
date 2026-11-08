// Copyright (c) 2026 Neil Colvin. MIT license.
using System.Text.Json;
using CrestronHomeDevTools;

internal sealed record InspectionCommandOptions (bool IncludeValues, bool Receipt, int[] Parents, int DurationSeconds, int BufferCapacity)
    {
    internal static InspectionCommandOptions Read (string command, IReadOnlyDictionary<string, string> options)
        {
        bool Flag (string key)
            => !options.TryGetValue (key, out string? text) ? false : bool.TryParse (text, out bool value) ? value : throw new ArgumentException ($"--{key} requires true or false.");
        int Bounded (string key, int fallback, int maximum)
            => !options.TryGetValue (key, out string? text) ? fallback : int.TryParse (text, out int value) && value >= 1 && value <= maximum ? value : throw new ArgumentException ($"--{key} must be 1 to {maximum}.");
        int[] parents = [];
        if (command == "monitor-managed-children")
            {
            if (!options.TryGetValue ("parents", out string? text)) throw new ArgumentException ("Missing --parents.");
            parents = text.Split (',').Select (part => int.TryParse (part.Trim (), out int id) ? id : throw new ArgumentException ("--parents requires comma-separated integer IDs.")).ToArray ();
            if (parents.Length is < 1 or > 64 || parents.Distinct ().Count () != parents.Length) throw new ArgumentException ("--parents requires 1-64 distinct integer IDs.");
            }
        return new (Flag ("include-values"), Flag ("receipt"), parents, Bounded ("duration", 30, 3600), Bounded ("buffer", 32, 256));
        }
    }

internal static class InspectionCommands
    {
    // JSON lines deliberately distinguish HTTP baselines from raw WebSocket deltas.
    internal static async Task<object> MonitorAsync (ConfigurationClient client, InspectionCommandOptions options, TextWriter output, CancellationToken token)
        {
        await using var monitor = await client.MonitorManagedChildrenAsync (options.Parents, options.IncludeValues, options.BufferCapacity, token);
        await output.WriteLineAsync (JsonSerializer.Serialize (new { Kind = "InitialSnapshots", monitor.CanReceiveEvents, monitor.BaselineCompletedSequence, monitor.InitialSnapshots }));
        using var duration = CancellationTokenSource.CreateLinkedTokenSource (token);
        duration.CancelAfter (TimeSpan.FromSeconds (options.DurationSeconds));
        int count = 0;
        if (monitor.CanReceiveEvents)
            {
            try
                {
                await foreach (var change in monitor.ReadChangesAsync (duration.Token))
                    { await output.WriteLineAsync (JsonSerializer.Serialize (new { Kind = "ManagedChildStateChange", Change = change })); count++; }
                }
            catch (OperationCanceledException) when (!token.IsCancellationRequested && duration.IsCancellationRequested) { }
            }
        token.ThrowIfCancellationRequested ();
        return new { Kind = "MonitoringCompleted", monitor.CanReceiveEvents, monitor.HasObservedStateEvent, EventCount = count,
            ValuesIncluded = options.IncludeValues, RequestedDurationSeconds = options.DurationSeconds };
        }
    }
