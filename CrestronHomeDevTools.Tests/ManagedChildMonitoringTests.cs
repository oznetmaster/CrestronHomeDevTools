// Copyright (c) 2026 Neil Colvin. MIT license.
using System.Text.Json;
using NUnit.Framework;

namespace CrestronHomeDevTools.Tests;

[TestFixture]
public sealed class ManagedChildMonitoringTests
    {
    private static JsonElement Json (string text) => JsonSerializer.Deserialize<JsonElement> (text);
    private static JsonElement Change (int parent = 7, string values = """{"opaque":{"on":false}}""")
        => Json ($$"""{"EventType":"cp.platformController:managedDeviceStateChanged","ParentDeviceId":{{parent}},"PropertyValues":{{values}}}""");
    private static DeviceInfo Parent (string? extended = null, string? legacy = null, int id = 7)
        {
        var properties = new Dictionary<string, JsonElement> ();
        if (extended != null) properties["cp.platformController:managedDevicesExtended"] = Json (extended);
        if (legacy != null) properties["platform:managedDevices"] = Json (legacy);
        return new () { Id = id, PropertyValues = properties };
        }

    [TestCase (false)] [TestCase (true)]
    public async Task DescriptorIdsStayOpaqueAndUnavailableStateDoesNotBecomeEmpty (bool values)
        {
        var connection = new ProcessorCapabilitiesTests.ReadOnlyConnection (Parent ("""[{"Id":"E00001","Model":"new","PropertyValues":null},{"Id":"-6","PropertyValues":{}},{"Id":"child/value","PropertyValues":{"on":false,"password":"private-sentinel"}}]""", id: -6));
        var result = await new ConfigurationClient (connection).GetManagedChildrenAsync (-6, values);
        Assert.That (result.Children.Select (c => c.Id), Is.EqualTo (new[] { "E00001", "-6", "child/value" }));
        Assert.That (result.Children[0].State.Available, Is.False);
        Assert.That (result.Children[1].State.Available, Is.True);
        Assert.That (result.Children[1].State.PropertyIds, Is.Empty);
        Assert.That (result.Children[2].State.PropertyIds, Is.EqualTo (new[] { "on", "password" }));
        Assert.That (result.Children[2].State.Values != null, Is.EqualTo (values));
        Assert.That (connection.Reads, Is.EqualTo (new[] { "v2/Devices/-6" })); // No invented device-ID reads.
        if (!values) Assert.That (JsonSerializer.Serialize (result), Does.Not.Contain ("private-sentinel"));
        }

    [Test]
    public async Task LegacyFallbackDoesNotOverrideAnAdvertisedNullExtendedList ()
        {
        var client = new ConfigurationClient (new ProcessorCapabilitiesTests.ReadOnlyConnection (Parent (legacy: """[{"Id":"old"}]""")));
        Assert.That ((await client.GetManagedChildrenAsync (7)).Children.Single ().Id, Is.EqualTo ("old"));
        client = new ConfigurationClient (new ProcessorCapabilitiesTests.ReadOnlyConnection (Parent ("null", """[{"Id":"old"}]""")));
        var result = await client.GetManagedChildrenAsync (7);
        Assert.That (result.MetadataProperty, Is.EqualTo ("cp.platformController:managedDevicesExtended"));
        Assert.That (result.DescriptorsAvailable, Is.False); Assert.That (result.Children, Is.Empty);
        }

    [Test]
    public async Task EncodedMetadataAndOlderConnectionWithoutEventInterfaceRemainUsable ()
        {
        var parent = Parent ();
        parent.PropertyValues["platform:managedDevices"] = JsonSerializer.SerializeToElement ("""[{"Id":"old"}]""");
        await using var monitor = await new ConfigurationClient (new ProcessorCapabilitiesTests.ReadOnlyConnection (parent)).MonitorManagedChildrenAsync (new[] { 7 });
        Assert.That (monitor.InitialSnapshots.Single ().Children.Single ().Id, Is.EqualTo ("old"));
        Assert.That (monitor.CanReceiveEvents, Is.False); Assert.That (monitor.HasObservedStateEvent, Is.False);
        await using var reader = monitor.ReadChangesAsync ().GetAsyncEnumerator ();
        await Assert.ThrowsAsync<NotSupportedException> (async () => await reader.MoveNextAsync ());
        }

    [TestCase (null, false)] [TestCase ("null", false)] [TestCase ("[]", true)]
    public async Task MissingNullAndEmptyDescriptorsRemainDistinct (string? metadata, bool available)
        {
        var snapshot = await new ConfigurationClient (new ProcessorCapabilitiesTests.ReadOnlyConnection (Parent (metadata))).GetManagedChildrenAsync (7);
        Assert.That (snapshot.DescriptorsAvailable, Is.EqualTo (available)); Assert.That (snapshot.Children, Is.Empty);
        Assert.That (snapshot.MetadataProperty == null, Is.EqualTo (metadata == null));
        }

    [TestCase ("{}")] [TestCase ("[null]")] [TestCase ("[{}]")]
    [TestCase ("[{\"Id\":\"x\"},{\"Id\":\"x\"}]")] [TestCase ("[{\"Id\":\"x\",\"PropertyValues\":[]}]")]
    [TestCase ("[{\"Id\":\"x\",\"PropertyValues\":{\"p\":1,\"p\":2}}]")]
    public async Task MalformedDescriptorsAreNotPartialSuccess (string metadata)
        {
        var client = new ConfigurationClient (new ProcessorCapabilitiesTests.ReadOnlyConnection (Parent (metadata)));
        await Assert.ThrowsAsync<InvalidDataException> (async () => await client.GetManagedChildrenAsync (7));
        }

    [Test]
    public async Task SubscriptionPrecedesBaselineAndOverlappingEventsAreMarkedNotMerged ()
        {
        var source = new Source (Parent ("""[{"Id":"opaque","PropertyValues":{"on":true}}]"""));
        source.DuringRead = () => source.Hub.Accept (Change ());
        await using var monitor = await new ConfigurationClient (source).MonitorManagedChildrenAsync (new[] { 7 });
        Assert.That (monitor.InitialSnapshots.Single ().Children.Single ().State.PropertyIds, Is.EqualTo (new[] { "on" }));
        Assert.That (monitor.HasObservedStateEvent, Is.False);
        source.Hub.Accept (Change (values: """{"opaque":{"on":true,"private":"private-sentinel"}}"""));
        await using var reader = monitor.ReadChangesAsync ().GetAsyncEnumerator ();
        Assert.That (await reader.MoveNextAsync (), Is.True);
        Assert.That (reader.Current.OverlapsInitialSnapshot, Is.True);
        Assert.That (reader.Current.Children["opaque"].Values, Is.Null);
        Assert.That (await reader.MoveNextAsync (), Is.True);
        Assert.That (reader.Current.OverlapsInitialSnapshot, Is.False);
        Assert.That (JsonSerializer.Serialize (reader.Current), Does.Not.Contain ("private-sentinel"));
        }

    [Test]
    public async Task RawDeltaClonesJsonAndRetainsUnavailableEmptyAndExplicitNull ()
        {
        var hub = new ManagedChildEventHub ();
        await using var subscription = hub.Subscribe (new[] { 7 }, 2);
        using (var document = JsonDocument.Parse (Change (values: """{"unknown":null,"empty":{},"opaque":{"null":null,"object":{"x":1}}}""").GetRawText ()))
            hub.Accept (document.RootElement);
        await using var reader = subscription.ReadChangesAsync ().GetAsyncEnumerator ();
        Assert.That (await reader.MoveNextAsync (), Is.True);
        Assert.That (reader.Current.Children["unknown"].Available, Is.False);
        Assert.That (reader.Current.Children["empty"].Available, Is.True);
        Assert.That (reader.Current.Children["empty"].Values, Is.Empty);
        Assert.That (reader.Current.Children["opaque"].Values!["null"].ValueKind, Is.EqualTo (JsonValueKind.Null));
        Assert.That (reader.Current.Children["opaque"].Values!["object"].GetProperty ("x").GetInt32 (), Is.EqualTo (1));
        }

    [Test]
    public async Task FiltersOtherParentsAndSupportsCancellationThenLaterRead ()
        {
        var hub = new ManagedChildEventHub ();
        await using var subscription = hub.Subscribe (new[] { 7 }, 2);
        hub.Accept (Change (8));
        using var timeout = new CancellationTokenSource (20);
        await using (var reader = subscription.ReadChangesAsync (timeout.Token).GetAsyncEnumerator ())
            await Assert.ThrowsAsync<OperationCanceledException> (async () => await reader.MoveNextAsync ());
        hub.Accept (Change ());
        await using var next = subscription.ReadChangesAsync ().GetAsyncEnumerator ();
        Assert.That (await next.MoveNextAsync (), Is.True);
        Assert.That (next.Current.ParentDeviceId, Is.EqualTo (7));
        }

    [Test]
    public async Task OverflowFailsOnlyAffectedSubscriberAndNeverDropsSilently ()
        {
        var hub = new ManagedChildEventHub ();
        await using var slow = hub.Subscribe (new[] { 7 }, 1);
        await using var other = hub.Subscribe (new[] { 8 }, 1);
        hub.Accept (Change ()); hub.Accept (Change ()); hub.Accept (Change (8));
        await using var slowReader = slow.ReadChangesAsync ().GetAsyncEnumerator ();
        await Assert.ThrowsAsync<IOException> (async () => await slowReader.MoveNextAsync ());
        await using var reader = other.ReadChangesAsync ().GetAsyncEnumerator ();
        Assert.That (await reader.MoveNextAsync (), Is.True);
        }

    [TestCase ("null")] [TestCase ("[]")] [TestCase ("{\"x\":42}")]
    [TestCase ("{\"x\":{\"p\":1,\"p\":2}}")]
    public async Task BadDeltaFailsRelevantMonitorWithoutAffectingOtherParents (string values)
        {
        var hub = new ManagedChildEventHub ();
        await using var bad = hub.Subscribe (new[] { 7 }, 1);
        await using var good = hub.Subscribe (new[] { 8 }, 1);
        hub.Accept (Change (values: values)); hub.Accept (Change (8));
        await using var reader = bad.ReadChangesAsync ().GetAsyncEnumerator ();
        await Assert.ThrowsAsync<InvalidDataException> (async () => await reader.MoveNextAsync ());
        await using var other = good.ReadChangesAsync ().GetAsyncEnumerator ();
        Assert.That (await other.MoveNextAsync (), Is.True);
        }

    [Test]
    public async Task DisposeDiscardsQueuedEventsAndUnblocksWaitingReader ()
        {
        var hub = new ManagedChildEventHub ();
        var subscription = hub.Subscribe (new[] { 7 }, 1);
        hub.Accept (Change ());
        await subscription.DisposeAsync ();
        await using var reader = subscription.ReadChangesAsync ().GetAsyncEnumerator ();
        Assert.That (await reader.MoveNextAsync (), Is.False);
        var waiting = hub.Subscribe (new[] { 7 }, 1);
        await using var pending = waiting.ReadChangesAsync ().GetAsyncEnumerator ();
        var move = pending.MoveNextAsync ().AsTask ();
        await waiting.DisposeAsync ();
        Assert.That (await move, Is.False);
        }

    [Test]
    public async Task DisconnectTerminatesActiveAndFutureSubscriptions ()
        {
        var hub = new ManagedChildEventHub ();
        await using var subscription = hub.Subscribe (new[] { 7 }, 1);
        await using var reader = subscription.ReadChangesAsync ().GetAsyncEnumerator ();
        var pending = reader.MoveNextAsync ().AsTask ();
        hub.Fail (new IOException ());
        await Assert.ThrowsAsync<IOException> (async () => await pending);
        Assert.Throws<IOException> (() => hub.Subscribe (new[] { 7 }, 1));
        }

    [Test]
    public async Task MultipleReadersAreRejectedAndListenerRegistrationIsBounded ()
        {
        var hub = new ManagedChildEventHub ();
        var subscriptions = Enumerable.Range (0, 16).Select (_ => hub.Subscribe (new[] { 7 }, 1)).ToArray ();
        try
            {
            Assert.Throws<InvalidOperationException> (() => hub.Subscribe (new[] { 7 }, 1));
            using var cancel = new CancellationTokenSource ();
            await using var first = subscriptions[0].ReadChangesAsync (cancel.Token).GetAsyncEnumerator ();
            var move = first.MoveNextAsync ().AsTask ();
            await using var second = subscriptions[0].ReadChangesAsync ().GetAsyncEnumerator ();
            await Assert.ThrowsAsync<InvalidOperationException> (async () => await second.MoveNextAsync ());
            cancel.Cancel ();
            await Assert.ThrowsAsync<OperationCanceledException> (async () => await move);
            }
        finally { foreach (var s in subscriptions) await s.DisposeAsync (); }
        }

    [TestCase (7, 0)] [TestCase (7, 257)]
    public void InvalidSubscriptionBoundsAreRejected (int id, int capacity)
        => Assert.Throws<ArgumentException> (() => new ManagedChildEventHub ().Subscribe (new[] { id }, capacity));

    [Test]
    public async Task ZeroSystemParentSupportsSnapshotsAndEvents ()
        {
        var client = new ConfigurationClient (new ProcessorCapabilitiesTests.ReadOnlyConnection (Parent ("[]", id: 0)));
        Assert.That ((await client.GetManagedChildrenAsync (0)).ParentDeviceId, Is.Zero);
        var hub = new ManagedChildEventHub ();
        await using var subscription = hub.Subscribe (new[] { 0 }, 1);
        hub.Accept (Change (0));
        await using var reader = subscription.ReadChangesAsync ().GetAsyncEnumerator ();
        Assert.That (await reader.MoveNextAsync (), Is.True);
        Assert.That (reader.Current.ParentDeviceId, Is.Zero);
        }

    [Test]
    public async Task BaselineFailureReleasesTheEventSubscription ()
        {
        var source = new Source (Parent ());
        source.DuringRead = () => throw new InvalidDataException ("baseline failed");
        await Assert.ThrowsAsync<InvalidDataException> (async () => await new ConfigurationClient (source).MonitorManagedChildrenAsync (new[] { 7 }));
        var remaining = Enumerable.Range (0, 16).Select (_ => source.Hub.Subscribe (new[] { 7 }, 1)).ToArray ();
        foreach (var subscription in remaining) await subscription.DisposeAsync ();
        }

    [Test]
    public async Task ByteBudgetFailsBeforeAHighCapacityQueueCanRetainUnboundedValues ()
        {
        var hub = new ManagedChildEventHub ();
        await using var subscription = hub.Subscribe (new[] { 7 }, 256);
        var payload = Change (values: JsonSerializer.Serialize (new { child = new { large = new string ('a', 600_000) } }));
        for (int i = 0; i < 16; i++) hub.Accept (payload);
        await using var reader = subscription.ReadChangesAsync ().GetAsyncEnumerator ();
        var error = await Assert.ThrowsAsync<IOException> (async () => await reader.MoveNextAsync ());
        Assert.That (error!.Message, Does.Contain ("overflowed"));
        }

    [Test]
    public async Task ActualConnectionRoutesManagedDeltasWithoutInterferingWithOperationReceipts ()
        {
        await using var connection = new ProcessorConnection (new HttpClient ());
        await using var subscription = connection.SubscribeManagedChildChanges (new[] { -6 });
        var operation = connection.WaitForOperationAsync ("independent", TimeSpan.FromSeconds (1));
        connection.AcceptEvent (Change (-6, """{"opaque":{"on":true}}"""));
        connection.AcceptEvent (Change (-6, """{"opaque":42}""")); // Fail this monitor only.
        connection.AcceptEvent (Json ("""{"EventType":"cp.types:operationStatusChanged","OperationId":"independent","LatestStatus":"Succeeded"}"""));
        Assert.That ((await operation).Succeeded, Is.True);
        await using var reader = subscription.ReadChangesAsync ().GetAsyncEnumerator ();
        await Assert.ThrowsAsync<InvalidDataException> (async () => await reader.MoveNextAsync ());
        }

    private sealed class Source (DeviceInfo parent) : IConfigurationConnection, IManagedChildEventSource
        {
        internal ManagedChildEventHub Hub { get; } = new ();
        internal Action? DuringRead;
        public IManagedChildEventSubscription SubscribeManagedChildChanges (IReadOnlyCollection<int> parents, int bufferCapacity = 32) => Hub.Subscribe (parents, bufferCapacity);
        public Task<T?> GetAsync<T> (string path, CancellationToken token = default)
            { DuringRead?.Invoke (); return Task.FromResult (JsonSerializer.Deserialize<T> (JsonSerializer.Serialize (parent))); }
        public Task<T?> ExecuteAsync<T> (int deviceId, string command, object? parameters = null, CancellationToken token = default) => throw new AssertionException ("Inspection must not issue commands.");
        public Task<OperationResult> WaitForOperationAsync (string id, TimeSpan timeout, CancellationToken token = default) => throw new AssertionException ("Inspection must not start operations.");
        public ValueTask DisposeAsync () => ValueTask.CompletedTask;
        }
    }
