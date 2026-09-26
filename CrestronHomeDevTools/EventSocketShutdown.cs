// Copyright (c) 2026 Neil Colvin. MIT licensed.
using System.Net.WebSockets;

namespace CrestronHomeDevTools;

internal static class EventSocketShutdown
{
    internal static async Task CloseAsync(WebSocket socket, Task? receiveLoop, TimeSpan timeout)
    {
        using var deadline = new CancellationTokenSource(timeout);
        try
        {
            if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                // The existing event loop owns receives. CloseAsync would introduce
                // a competing receive; send only the close frame and await that loop.
                await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "", deadline.Token).ConfigureAwait(false);
                if (receiveLoop != null)
                    await receiveLoop.WaitAsync(deadline.Token).ConfigureAwait(false);
            }
        }
        catch (Exception error) when (error is OperationCanceledException or WebSocketException or ObjectDisposedException)
        {
            // A failed or silent peer must not prevent bounded local cleanup.
        }
        finally
        {
            if (socket.State is not (WebSocketState.Closed or WebSocketState.Aborted))
                socket.Abort();
        }
    }
}
