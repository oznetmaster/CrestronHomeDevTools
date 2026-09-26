// Copyright (c) 2026 Neil Colvin. MIT licensed.
using System.Net.WebSockets;
using NUnit.Framework;

namespace CrestronHomeDevTools.Tests;

[TestFixture]
public sealed class EventSocketShutdownTests
{
    [Test]
    public async Task CompletedHandshakeDoesNotAbortOrStartAnotherReceive()
    {
        using var socket = new ProbeSocket { Acknowledge = true };
        await EventSocketShutdown.CloseAsync(socket, socket.Receiver.Task, TimeSpan.FromSeconds(1));
        Assert.Multiple(() => {
            Assert.That(socket.CloseFrames, Is.EqualTo(1));
            Assert.That(socket.SentStatus, Is.EqualTo(WebSocketCloseStatus.NormalClosure));
            Assert.That(socket.State, Is.EqualTo(WebSocketState.Closed));
            Assert.That(socket.Aborts, Is.Zero);
        });
    }

    [Test]
    public async Task MissingAcknowledgmentAbortsWithinBound()
    {
        using var socket = new ProbeSocket();
        await EventSocketShutdown.CloseAsync(socket, socket.Receiver.Task, TimeSpan.FromMilliseconds(30)).WaitAsync(TimeSpan.FromSeconds(3));
        Assert.That(socket.CloseFrames, Is.EqualTo(1));
        Assert.That(socket.Aborts, Is.EqualTo(1));
    }

    [Test]
    public async Task FailedCloseStillAborts()
    {
        using var socket = new ProbeSocket { FailClose = true };
        await EventSocketShutdown.CloseAsync(socket, socket.Receiver.Task, TimeSpan.FromSeconds(1));
        Assert.That(socket.Aborts, Is.EqualTo(1));
    }

    [TestCase(WebSocketState.Closed)]
    [TestCase(WebSocketState.Aborted)]
    public async Task TerminalSocketIsNotClosedAgain(WebSocketState state)
    {
        using var socket = new ProbeSocket { CurrentState = state };
        await EventSocketShutdown.CloseAsync(socket, socket.Receiver.Task, TimeSpan.FromSeconds(1));
        Assert.That(socket.CloseFrames, Is.Zero);
        Assert.That(socket.Aborts, Is.Zero);
    }

    private sealed class ProbeSocket : WebSocket
    {
        internal readonly TaskCompletionSource Receiver = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal bool Acknowledge, FailClose;
        internal int Aborts, CloseFrames;
        internal WebSocketState CurrentState = WebSocketState.Open;
        internal WebSocketCloseStatus? SentStatus;
        public override WebSocketCloseStatus? CloseStatus => SentStatus;
        public override string? CloseStatusDescription => "";
        public override string? SubProtocol => null;
        public override WebSocketState State => CurrentState;
        public override void Abort() { Aborts++; CurrentState = WebSocketState.Aborted; Receiver.TrySetResult(); }
        public override void Dispose() { }
        public override Task CloseOutputAsync(WebSocketCloseStatus status, string? description, CancellationToken token)
        {
            CloseFrames++; SentStatus = status;
            if (FailClose) throw new WebSocketException();
            CurrentState = Acknowledge ? WebSocketState.Closed : WebSocketState.CloseSent;
            if (Acknowledge) Receiver.TrySetResult();
            return Task.CompletedTask;
        }
        public override Task CloseAsync(WebSocketCloseStatus status, string? description, CancellationToken token) => throw new AssertionException("Must not introduce a second receive");
        public override Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken token) => throw new AssertionException("Existing receiver owns the stream");
        public override Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType type, bool end, CancellationToken token) => throw new AssertionException("Only close output is expected");
    }
}
