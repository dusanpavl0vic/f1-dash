using System.Net.WebSockets;

namespace F1Dash.Server.Realtime;

/// <summary>
/// GET /ws — sends a snapshot, then every subsequent delta.
///
/// The message shapes are the contract with the browser (docs/05 §wire
/// protocol) and the client merges deltas with the identical algorithm the
/// server used to produce them.
/// </summary>
public static class WebSocketEndpoint
{
    public static void MapLiveSocket(this WebApplication app)
    {
        app.Map("/ws", async (HttpContext context, LiveSessionState state, ILoggerFactory loggers) =>
        {
            if (!context.WebSockets.IsWebSocketRequest)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await context.Response.WriteAsync("This endpoint requires a WebSocket upgrade.");
                return;
            }

            var logger = loggers.CreateLogger("F1Dash.Realtime");

            using var socket = await context.WebSockets.AcceptWebSocketAsync();
            var client = new ClientConnection(socket, ClientConnection.DefaultCapacity);

            // A reconnecting client sends the last sequence it saw. When the
            // backlog still reaches that far it gets only the missing deltas —
            // a few kilobytes instead of a megabyte-plus snapshot. Otherwise it
            // gets the snapshot, because a gap in a delta stream is
            // unrecoverable and pretending otherwise corrupts the client state.
            var since = long.TryParse(context.Request.Query["since"], out var s0) ? s0 : 0;
            var resumed = state.AddAndCatchUp(client, since);

            logger.LogInformation(
                "Client {Id} connected via {Path} ({Count} total)",
                client.Id, resumed ? $"resume from {since}" : "snapshot", state.ClientCount);

            var writer = client.WriteLoopAsync(context.RequestAborted);

            try
            {
                await ReadUntilClosedAsync(socket, context.RequestAborted);
            }
            catch (OperationCanceledException)
            {
                // Client went away or the host is shutting down.
            }
            finally
            {
                state.Remove(client.Id);
                client.Complete();
                await writer;
                logger.LogInformation(
                    "Client {Id} disconnected{Reason} ({Count} remain)",
                    client.Id, client.Overflowed ? " (too slow)" : "", state.ClientCount);
            }
        });
    }

    /// <summary>
    /// Drains inbound frames. The client sends only heartbeats for now, but the
    /// read loop must exist regardless: without it a close frame is never
    /// observed and the connection leaks.
    /// </summary>
    private static async Task ReadUntilClosedAsync(WebSocket socket, CancellationToken ct)
    {
        var buffer = new byte[1024];

        while (socket.State == WebSocketState.Open && !ct.IsCancellationRequested)
        {
            var result = await socket.ReceiveAsync(buffer, ct).ConfigureAwait(false);

            if (result.MessageType == WebSocketMessageType.Close)
            {
                await socket.CloseAsync(
                    WebSocketCloseStatus.NormalClosure, null, CancellationToken.None).ConfigureAwait(false);
                return;
            }
        }
    }
}
