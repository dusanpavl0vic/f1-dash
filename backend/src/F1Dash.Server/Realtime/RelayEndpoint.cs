using System.Net.WebSockets;
using System.Text;
using F1Dash.Server.Ingest;

namespace F1Dash.Server.Realtime;

/// <summary>
/// GET /relay — where a collector delivers the feed.
///
/// Authenticated with a shared secret from RELAY_TOKEN. Without it the endpoint
/// is refused outright rather than left open: anything accepted here becomes
/// the session everybody sees, so an unauthenticated relay is a way for a
/// stranger to write whatever they like onto the dashboard.
/// </summary>
public static class RelayEndpoint
{
    public static void MapRelay(this WebApplication app, RelaySessionSource relay)
    {
        app.Map("/relay", async (HttpContext context, ILoggerFactory loggers) =>
        {
            var expected = Environment.GetEnvironmentVariable("RELAY_TOKEN");
            var logger = loggers.CreateLogger("F1Dash.Relay");

            if (string.IsNullOrWhiteSpace(expected))
            {
                logger.LogWarning("A collector tried to connect but RELAY_TOKEN is not set.");
                context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                await context.Response.WriteAsync("Relay is not configured.");
                return;
            }

            var offered = context.Request.Headers["X-Relay-Token"].ToString();

            // Fixed-time comparison: a length-or-prefix comparison leaks the
            // token one character at a time to anyone willing to measure.
            if (!System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
                    Encoding.UTF8.GetBytes(offered.PadRight(expected.Length)[..expected.Length]),
                    Encoding.UTF8.GetBytes(expected))
                || offered.Length != expected.Length)
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }

            if (!context.WebSockets.IsWebSocketRequest)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await context.Response.WriteAsync("This endpoint requires a WebSocket upgrade.");
                return;
            }

            using var socket = await context.WebSockets.AcceptWebSocketAsync();
            relay.CollectorConnected();

            // The collector needs to know where to resume from, so the first
            // thing sent is the last sequence accepted.
            var hello = Encoding.UTF8.GetBytes($"{{\"resumeFrom\":{relay.LastSequence}}}");
            await socket.SendAsync(hello, WebSocketMessageType.Text, true, context.RequestAborted);

            var buffer = new byte[256 * 1024];
            var pending = new StringBuilder();

            try
            {
                while (socket.State == WebSocketState.Open && !context.RequestAborted.IsCancellationRequested)
                {
                    var result = await socket.ReceiveAsync(buffer, context.RequestAborted);
                    if (result.MessageType == WebSocketMessageType.Close) break;

                    pending.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));

                    // A message can be split across frames; only act on the
                    // complete one.
                    if (!result.EndOfMessage) continue;

                    var text = pending.ToString();
                    pending.Clear();

                    if (RelaySessionSource.TryParseFrame(text, out var seq, out var update))
                    {
                        await relay.OfferAsync(seq, update, context.RequestAborted);
                    }
                    else
                    {
                        logger.LogDebug("Discarded a malformed relay frame ({Length} chars)", text.Length);
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (WebSocketException e)
            {
                logger.LogWarning(e, "Relay connection failed");
            }
            finally
            {
                relay.CollectorDisconnected();
            }
        });
    }
}
