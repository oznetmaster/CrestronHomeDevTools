// Copyright (c) 2026 Neil Colvin. MIT license.
using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;

namespace CrestronHomeDevTools;

/// <summary>Unavailable state is not an empty or false state. Values are present only when explicitly requested.</summary>
public sealed record ManagedPropertySet (bool Available, IReadOnlyList<string> PropertyIds, IReadOnlyDictionary<string, JsonElement>? Values);
public sealed record ManagedChildState (string Id, string? Model, ManagedPropertySet State);
public sealed record ManagedChildrenSnapshot (int ParentDeviceId, string? MetadataProperty, bool DescriptorsAvailable,
    bool ValuesIncluded, IReadOnlyList<ManagedChildState> Children);

/// <summary>A raw server delta, never a synthesized initial-state event. Sequence is local to one connection.</summary>
public sealed record ManagedChildStateChange (long Sequence, DateTimeOffset ObservedUtc, int ParentDeviceId,
    IReadOnlyDictionary<string, ManagedPropertySet> Children, bool OverlapsInitialSnapshot = false);

public interface IManagedChildEventSubscription : IAsyncDisposable
    {
    long CurrentSequence { get; }
    IAsyncEnumerable<ManagedChildStateChange> ReadChangesAsync (CancellationToken cancellationToken = default);
    }

/// <summary>Optional local event-channel capability; its presence does not prove that older firmware emits these events.</summary>
public interface IManagedChildEventSource
    {
    IManagedChildEventSubscription SubscribeManagedChildChanges (IReadOnlyCollection<int> parentDeviceIds, int bufferCapacity = 32);
    }

public sealed class ManagedChildMonitor : IAsyncDisposable
    {
    private readonly IManagedChildEventSubscription? _subscription;
    private readonly bool _includeValues;
    private int _reader;
    internal ManagedChildMonitor (IReadOnlyList<ManagedChildrenSnapshot> initial, IManagedChildEventSubscription? subscription, bool includeValues, long fence)
        { InitialSnapshots = initial; _subscription = subscription; _includeValues = includeValues; BaselineCompletedSequence = fence; }
    public IReadOnlyList<ManagedChildrenSnapshot> InitialSnapshots { get; }
    public bool CanReceiveEvents => _subscription != null;
    public long BaselineCompletedSequence { get; }
    public bool HasObservedStateEvent { get; private set; }

    /// <remarks>Overlapping events cannot be ordered against the HTTP baseline without a server revision. This API does not merge them into a latest-state cache.</remarks>
    public async IAsyncEnumerable<ManagedChildStateChange> ReadChangesAsync ([EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
        if (_subscription == null) throw new NotSupportedException ("This connection cannot observe managed-child events; the initial snapshots remain available.");
        if (Interlocked.CompareExchange (ref _reader, 1, 0) != 0) throw new InvalidOperationException ("One reader may consume this monitor at a time.");
        try
            {
            await foreach (var change in _subscription.ReadChangesAsync (cancellationToken).ConfigureAwait (false))
                {
                HasObservedStateEvent = true;
                var children = change.Children.ToDictionary (p => p.Key,
                    p => _includeValues ? p.Value : p.Value with { Values = null }, StringComparer.Ordinal);
                yield return change with { Children = new ReadOnlyDictionary<string, ManagedPropertySet> (children), OverlapsInitialSnapshot = change.Sequence <= BaselineCompletedSequence };
                }
            }
        finally { Interlocked.Exchange (ref _reader, 0); }
        }
    public ValueTask DisposeAsync () => _subscription?.DisposeAsync () ?? ValueTask.CompletedTask;
    }

internal static class ManagedChildInspection
    {
    internal static async Task<ManagedChildrenSnapshot> GetAsync (ConfigurationClient client, int parentId, bool includeValues, CancellationToken token)
        {
        var parent = await client.GetDeviceAsync (parentId, token).ConfigureAwait (false) ?? throw new InvalidOperationException ("The managed-child parent was not found.");
        if (parent.Id != parentId) throw new InvalidOperationException ("The managed-child parent identity changed.");
        string? property = parent.PropertyValues.ContainsKey ("cp.platformController:managedDevicesExtended") ? "cp.platformController:managedDevicesExtended"
            : parent.PropertyValues.ContainsKey ("platform:managedDevices") ? "platform:managedDevices" : null;
        var raw = property == null ? null : InspectionMetadata.Array (parent, property, out _);
        var result = new List<ManagedChildState> (); var ids = new HashSet<string> (StringComparer.Ordinal);
        if (raw != null)
            foreach (var item in raw.Value.EnumerateArray ())
                {
                if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty ("Id", out var id) || id.ValueKind != JsonValueKind.String
                    || string.IsNullOrWhiteSpace (id.GetString ()) || id.GetString ()!.Length > 2048 || !ids.Add (id.GetString ()!))
                    throw new InvalidDataException ("Managed-child descriptors have missing or ambiguous identities.");
                string? model = item.TryGetProperty ("Model", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString () : null;
                var values = item.TryGetProperty ("PropertyValues", out var state) ? state : default;
                result.Add (new (id.GetString ()!, model, InspectionMetadata.PropertySet (values, includeValues)));
                }
        return new (parentId, property, raw != null, includeValues, result.AsReadOnly ());
        }
    }

internal sealed class ManagedChildEventHub
    {
    private readonly object _gate = new ();
    private readonly HashSet<Subscription> _subscriptions = [];
    private long _sequence;
    private Exception? _failure;

    internal IManagedChildEventSubscription Subscribe (IReadOnlyCollection<int> parents, int capacity)
        {
        ArgumentNullException.ThrowIfNull (parents);
        if (parents.Count is < 1 or > 64 || parents.Distinct ().Count () != parents.Count || capacity is < 1 or > 256)
            throw new ArgumentException ("Select 1-64 distinct parent IDs and a buffer capacity of 1-256.");
        lock (_gate)
            {
            if (_failure != null) throw new IOException ("The managed-child event connection is closed.", _failure);
            if (_subscriptions.Count >= 16) throw new InvalidOperationException ("The connection's managed-child monitor limit has been reached.");
            var subscription = new Subscription (this, parents, capacity); _subscriptions.Add (subscription); return subscription;
            }
        }

    internal void Accept (JsonElement message)
        {
        if (message.ValueKind != JsonValueKind.Object || !message.TryGetProperty ("EventType", out var type) || type.ValueKind != JsonValueKind.String
            || type.GetString () != "cp.platformController:managedDeviceStateChanged") return;
        int? parent = message.TryGetProperty ("ParentDeviceId", out var p) && p.ValueKind == JsonValueKind.Number && p.TryGetInt32 (out int id) ? id : null;
        lock (_gate)
            {
            var targets = _subscriptions.Where (s => parent == null || s.Parents.Contains (parent.Value)).ToArray ();
            if (targets.Length == 0) return;
            try
                {
                int bytes = Encoding.UTF8.GetByteCount (message.GetRawText ());
                if (parent == null || bytes > 1024 * 1024 || !message.TryGetProperty ("PropertyValues", out var values) || values.ValueKind != JsonValueKind.Object)
                    throw new InvalidDataException ();
                var children = new Dictionary<string, ManagedPropertySet> (StringComparer.Ordinal);
                foreach (var child in values.EnumerateObject ())
                    if (string.IsNullOrWhiteSpace (child.Name) || child.Name.Length > 2048 || children.Count >= 4096
                        || !children.TryAdd (child.Name, InspectionMetadata.PropertySet (child.Value, true))) throw new InvalidDataException ();
                var change = new ManagedChildStateChange (++_sequence, DateTimeOffset.UtcNow, parent.Value, new ReadOnlyDictionary<string, ManagedPropertySet> (children));
                foreach (var target in targets)
                    {
                    if (target.Failure != null || target.Disposed) continue;
                    if (target.QueuedBytes + bytes > 8 * 1024 * 1024 || !target.Channel.Writer.TryWrite (new (change, bytes)))
                        target.Stop (new IOException ("Managed-child monitoring buffer overflowed; state continuity is unconfirmed."));
                    else target.QueuedBytes += bytes;
                    }
                }
            catch (InvalidDataException)
                { foreach (var target in targets) target.Stop (new InvalidDataException ("A managed-child event had an unsupported layout; state continuity is unconfirmed.")); }
            }
        }

    internal void Fail (Exception error)
        {
        lock (_gate)
            { _failure ??= error; foreach (var subscription in _subscriptions) subscription.Stop (new IOException ("The managed-child event connection ended; state continuity is unconfirmed.", error)); }
        }

    private sealed record Queued (ManagedChildStateChange Change, int Bytes);
    private sealed class Subscription : IManagedChildEventSubscription
        {
        private readonly ManagedChildEventHub _owner;
        internal readonly HashSet<int> Parents;
        internal readonly Channel<Queued> Channel;
        internal int QueuedBytes;
        internal Exception? Failure;
        internal bool Disposed;
        private int _reader;
        internal Subscription (ManagedChildEventHub owner, IReadOnlyCollection<int> parents, int capacity)
            { _owner = owner; Parents = parents.ToHashSet (); Channel = System.Threading.Channels.Channel.CreateBounded<Queued> (new BoundedChannelOptions (capacity) { SingleReader = true, SingleWriter = false, FullMode = BoundedChannelFullMode.Wait }); }
        public long CurrentSequence { get { lock (_owner._gate) return _owner._sequence; } }
        internal void Stop (Exception error) { Failure ??= error; Channel.Writer.TryComplete (error); }
        public async IAsyncEnumerable<ManagedChildStateChange> ReadChangesAsync ([EnumeratorCancellation] CancellationToken cancellationToken = default)
            {
            if (Interlocked.CompareExchange (ref _reader, 1, 0) != 0) throw new InvalidOperationException ("One reader may consume this subscription at a time.");
            try
                {
                while (await Channel.Reader.WaitToReadAsync (cancellationToken).ConfigureAwait (false))
                    {
                    ManagedChildStateChange? change = null;
                    lock (_owner._gate)
                        {
                        if (Disposed) yield break;
                        if (Failure != null) throw Failure;
                        if (Channel.Reader.TryRead (out var item)) { QueuedBytes -= item.Bytes; change = item.Change; }
                        }
                    if (change != null) yield return change;
                    }
                }
            finally { Interlocked.Exchange (ref _reader, 0); }
            }
        public ValueTask DisposeAsync ()
            {
            lock (_owner._gate)
                {
                if (!Disposed)
                    {
                    Disposed = true; _owner._subscriptions.Remove (this); Channel.Writer.TryComplete ();
                    while (Channel.Reader.TryRead (out _)) { }
                    QueuedBytes = 0;
                    }
                }
            return ValueTask.CompletedTask;
            }
        }
    }
