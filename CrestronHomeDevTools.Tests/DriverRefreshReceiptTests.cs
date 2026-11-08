// Copyright (c) 2026 Neil Colvin. MIT license.
using System.Text.Json;
using NUnit.Framework;

namespace CrestronHomeDevTools.Tests;

[TestFixture]
public sealed class DriverRefreshReceiptTests
    {
    private static JsonElement Json (string text) => JsonSerializer.Deserialize<JsonElement> (text);
    private static JsonElement Completion (string id = "one", string? ids = null)
        => Json ($$"""{"EventType":"cp.platformDriverController:driversRefreshCompleted","OperationId":"{{id}}"{{(ids == null ? "" : ",\"NewDriverIds\":" + ids)}}} """);

    [TestCase (null, -1)] // 4.12: the field is absent.
    [TestCase ("null", -1)]
    [TestCase ("[]", 0)]
    [TestCase ("[\"new-a\",\"new-b\"]", 2)]
    public async Task OptionalListsPreserveUnavailableVersusEmpty (string? ids, int count)
        {
        var tracker = new DriverRefreshTracker ();
        tracker.Accept (Completion (ids: ids));
        var result = await tracker.WaitAsync ("one", TimeSpan.FromSeconds (1), default);
        Assert.That (result.NewDriverIds?.Count ?? -1, Is.EqualTo (count));
        }

    [Test]
    public async Task EarlyCompletionIsCorrelatedAndCanBeObservedAgainAfterDisconnect ()
        {
        var tracker = new DriverRefreshTracker ();
        tracker.Accept (Completion ("other", "[\"other\"]"));
        var waiting = tracker.WaitAsync ("one", TimeSpan.FromSeconds (1), default);
        Assert.That (waiting.IsCompleted, Is.False);
        tracker.Accept (Completion (ids: "[\"new\"]"));
        tracker.Accept (Completion (ids: "[\"new\"]"));
        tracker.Fail (new IOException ("disconnect"));
        Assert.That ((await waiting).NewDriverIds, Is.EqualTo (new[] { "new" }));
        Assert.That ((await tracker.WaitAsync ("one", TimeSpan.FromSeconds (1), default)).NewDriverIds, Is.EqualTo (new[] { "new" }));
        await Assert.ThrowsAsync<IOException> (async () => await tracker.WaitAsync ("missing", TimeSpan.FromSeconds (1), default));
        }

    [TestCase ("{}")]
    [TestCase ("[null]")]
    [TestCase ("[3]")]
    [TestCase ("[\"\"]")]
    [TestCase ("[\" \"]")]
    public async Task MalformedMatchingCompletionFailsSafelyWithoutLeakingValues (string ids)
        {
        var tracker = new DriverRefreshTracker ();
        tracker.Accept (Completion (ids: ids));
        var error = await Assert.ThrowsAsync<InvalidDataException> (async () => await tracker.WaitAsync ("one", TimeSpan.FromSeconds (1), default));
        Assert.That (error!.Message, Does.Contain ("unsupported").And.Not.Contain (ids));
        }

    [TestCase ("null", "[]")]
    [TestCase ("[\"a\"]", "[\"b\"]")]
    [TestCase ("[\"a\",\"b\"]", "[\"b\",\"a\"]")]
    public async Task ConflictingReceiptsAreNotMergedOrSilentlyReplaced (string first, string second)
        {
        var tracker = new DriverRefreshTracker ();
        tracker.Accept (Completion (ids: first)); tracker.Accept (Completion (ids: second));
        await Assert.ThrowsAsync<InvalidDataException> (async () => await tracker.WaitAsync ("one", TimeSpan.FromSeconds (1), default));
        }

    [Test]
    public async Task CancellationAndTimeoutDoNotConsumeLaterCompletion ()
        {
        var tracker = new DriverRefreshTracker ();
        using var cancellation = new CancellationTokenSource ();
        var waiting = tracker.WaitAsync ("one", TimeSpan.FromSeconds (1), cancellation.Token);
        cancellation.Cancel ();
        await Assert.ThrowsAsync<TaskCanceledException> (async () => await waiting);
        await Assert.ThrowsAsync<TimeoutException> (async () => await tracker.WaitAsync ("one", TimeSpan.FromMilliseconds (10), default));
        tracker.Accept (Completion ());
        Assert.That ((await tracker.WaitAsync ("one", TimeSpan.FromSeconds (1), default)).OperationId, Is.EqualTo ("one"));
        }

    [Test]
    public async Task DisconnectFailsPendingObservation ()
        {
        var tracker = new DriverRefreshTracker ();
        var waiting = tracker.WaitAsync ("one", TimeSpan.FromSeconds (1), default);
        tracker.Fail (new IOException ());
        await Assert.ThrowsAsync<IOException> (async () => await waiting);
        }

    [Test]
    public async Task BoundedHistoryDoesNotEvictAnActiveWaiter ()
        {
        var tracker = new DriverRefreshTracker ();
        var waiting = tracker.WaitAsync ("one", TimeSpan.FromSeconds (2), default);
        for (int i = 0; i < 600; i++) tracker.Accept (Completion ("op-" + i));
        tracker.Accept (Completion ());
        Assert.That ((await waiting).OperationId, Is.EqualTo ("one"));
        }

    [TestCase ("{}")] [TestCase ("null")] [TestCase ("{\"EventType\":42}")]
    [TestCase ("{\"EventType\":\"other\",\"OperationId\":\"one\"}")]
    [TestCase ("{\"EventType\":\"cp.platformDriverController:driversRefreshCompleted\",\"OperationId\":null}")]
    public async Task UncorrelatableFramesAreIgnored (string message)
        {
        var tracker = new DriverRefreshTracker (); tracker.Accept (Json (message));
        await Assert.ThrowsAsync<TimeoutException> (async () => await tracker.WaitAsync ("one", TimeSpan.FromMilliseconds (5), default));
        }

    [TestCase ("Ended", false)] [TestCase ("Succeeded", true)] [TestCase ("Failed", false)]
    public async Task LegacyConnectionRetainsTerminalStatusWithoutNewInterface (string status, bool succeeded)
        {
        var connection = new Legacy (status);
        var receipt = await new ConfigurationClient (connection).WaitForDriverRefreshReceiptAsync ("one", TimeSpan.FromSeconds (1));
        Assert.That (receipt.CompletionMonitoringAvailable, Is.False);
        Assert.That (receipt.Completion, Is.Null);
        Assert.That (receipt.Operation.Succeeded, Is.EqualTo (succeeded));
        Assert.That (connection.Calls, Is.EqualTo (1));
        }

    [Test]
    public async Task ProcessorRoutingCombinesTerminalAndDedicatedFramesInEitherOrder ()
        {
        foreach (bool dedicatedFirst in new[] { false, true })
            {
            await using var connection = new ProcessorConnection (new HttpClient ());
            var waiting = new ConfigurationClient (connection).WaitForDriverRefreshReceiptAsync ("one", TimeSpan.FromSeconds (1));
            void Terminal () => connection.AcceptEvent (Json ("""{"EventType":"cp.types:operationStatusChanged","OperationId":"one","LatestStatus":"Ended"}"""));
            if (dedicatedFirst) connection.AcceptEvent (Completion (ids: "[\"imported\"]"));
            Terminal ();
            if (!dedicatedFirst) connection.AcceptEvent (Completion (ids: "[\"imported\"]"));
            var receipt = await waiting;
            Assert.That (receipt.Operation.Status, Is.EqualTo ("Ended"));
            Assert.That (receipt.Operation.Succeeded, Is.False);
            Assert.That (receipt.Completion!.NewDriverIds, Is.EqualTo (new[] { "imported" }));
            }
        }

    [Test]
    public async Task DedicatedSilenceIsUnknownAndFailedTerminalDoesNotWaitForIt ()
        {
        var source = new Source ("Ended");
        var receipt = await new ConfigurationClient (source).WaitForDriverRefreshReceiptAsync ("one", TimeSpan.FromSeconds (1), TimeSpan.FromMilliseconds (5));
        Assert.That (receipt.CompletionMonitoringAvailable, Is.True); Assert.That (receipt.Completion, Is.Null);
        source = new Source ("Failed");
        receipt = await new ConfigurationClient (source).WaitForDriverRefreshReceiptAsync ("one", TimeSpan.FromSeconds (1));
        Assert.That (receipt.Operation.Status, Is.EqualTo ("Failed")); Assert.That (source.EventCalls, Is.Zero);
        }

    [Test]
    public async Task OperationCorrelationMismatchIsRejected ()
        {
        await Assert.ThrowsAsync<ProcessorApiException> (async () => await new ConfigurationClient (new Legacy ("Ended", "other")).WaitForDriverRefreshReceiptAsync ("one", TimeSpan.FromSeconds (1)));
        }

    private class Legacy (string status, string? returnedId = null) : ProcessorCapabilitiesTests.ReadOnlyConnection, IConfigurationConnection
        {
        internal int Calls;
        public new Task<OperationResult> WaitForOperationAsync (string id, TimeSpan timeout, CancellationToken token = default)
            { Calls++; return Task.FromResult (new OperationResult (returnedId ?? id, status, null)); }
        }
    private sealed class Source (string status) : Legacy (status), IDriverRefreshEventSource
        {
        internal int EventCalls;
        public Task<DriverRefreshCompletion> WaitForDriverRefreshCompletionAsync (string operationId, TimeSpan timeout, CancellationToken cancellationToken = default)
            { EventCalls++; throw new TimeoutException (); }
        }
    }
