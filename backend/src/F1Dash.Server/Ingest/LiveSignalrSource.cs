using System.Buffers;
using System.Net;
using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json.Nodes;
using System.Web;
using F1Dash.Core;
using F1Dash.Core.Signalr;

namespace F1Dash.Server.Ingest;

/// <summary>
/// The live F1 feed.
///
/// docs/03 names legacy ASP.NET SignalR 1.5 as the primary transport and
/// SignalR Core as a fallback. As of 2026-09 that is inverted: the legacy
/// endpoint answers every negotiate with
///
///     HTTP 401  www-authenticate: Basic realm="Authentication"
///                                 Bearer
///
/// regardless of User-Agent, while the static archive on the same host still
/// answers 200 — so this is authentication, not IP blocking. SignalR Core
/// negotiates cleanly, so it is the only path that connects and therefore the
/// primary one. See DECISIONS D-011.
///
/// Microsoft.AspNetCore.SignalR.Client is still not usable here: it would send
/// its own handshake and hub-invocation shapes, and the AWS load balancer
/// cookie from negotiate has to be replayed on the upgrade. The protocol is
/// small enough to write directly.
/// </summary>
public sealed class LiveSignalrSource(string? proxy, ILogger<LiveSignalrSource> logger) : ISessionSource
{
    private const string NegotiateUrl = "https://livetiming.formula1.com/signalrcore/negotiate?negotiateVersion=1";
    private const string ConnectUrl = "wss://livetiming.formula1.com/signalrcore";

    /// <summary>
    /// Identifies as BestHTTP, the Unity HTTP library the official F1 app uses.
    /// Kept even though Core does not appear to check it, because the legacy
    /// endpoint did and the behaviour may return.
    /// </summary>
    private const string UserAgent = "BestHTTP";

    /// <summary>No frame of any kind for this long means the socket is dead.</summary>
    private static readonly TimeSpan KeepAliveTimeout = TimeSpan.FromSeconds(30);

    public string Description => "live F1 feed (SignalR Core)";

    public async IAsyncEnumerable<TopicUpdate> ReadAsync([EnumeratorCancellation] CancellationToken ct)
    {
        var (token, cookie) = await NegotiateAsync(ct, proxy).ConfigureAwait(false);
        logger.LogInformation("Negotiated connection token ({Length} chars)", token.Length);

        using var socket = await ConnectAsync(token, cookie, ct).ConfigureAwait(false);

        await SendAsync(socket, CoreFrameParser.Handshake, ct).ConfigureAwait(false);

        // The handshake must COMPLETE before any invocation is sent. Sending
        // Subscribe immediately after the handshake request gets the connection
        // closed with no frames at all — which is exactly how this failed on the
        // first attempt, and it looks identical to an unreachable feed.
        var handshakeComplete = false;

        await foreach (var buffer in ReadBuffersAsync(socket, ct).ConfigureAwait(false))
        {
            foreach (var message in CoreFrameParser.SplitMessages(buffer))
            {
                if (!handshakeComplete)
                {
                    if (message.Contains("\"error\"", StringComparison.Ordinal))
                    {
                        throw new WebSocketException($"Handshake rejected: {message}");
                    }

                    handshakeComplete = true;
                    await SubscribeAsync(socket, ct).ConfigureAwait(false);
                    logger.LogInformation("Subscribed to {Count} topics", Topics.Subscription.Length);
                    continue;
                }

                // Type 6 is a ping and must be answered or the server drops us.
                if (message.Contains("\"type\":6", StringComparison.Ordinal))
                {
                    await SendAsync(socket, "{\"type\":6}", ct).ConfigureAwait(false);
                    continue;
                }

                foreach (var update in CoreFrameParser.Parse(message))
                {
                    yield return update;
                }
            }
        }
    }

    /// <summary>
    /// Returns the connection token and the load-balancer cookie the upgrade
    /// needs. Exposed so a connectivity check can run without a full connection.
    /// </summary>
    public static async Task<(string Token, string Cookie)> NegotiateAsync(
        CancellationToken ct, string? proxy = null)
    {
        var handler = new HttpClientHandler { UseCookies = false };
        if (!string.IsNullOrWhiteSpace(proxy))
        {
            handler.Proxy = new WebProxy(proxy);
            handler.UseProxy = true;
        }

        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(20) };

        // Negotiate is a POST on Core. A GET returns 405.
        using var request = new HttpRequestMessage(HttpMethod.Post, NegotiateUrl);
        request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);

        using var response = await http.SendAsync(request, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        var parsed = JsonNode.Parse(body);

        var token = (string?)parsed?["connectionToken"]
            ?? throw new InvalidOperationException("Negotiate response carried no connectionToken.");

        // AWSALB pins the connection to one load-balancer node. Without it the
        // upgrade can land on a node that has never seen this token.
        var cookie = response.Headers.TryGetValues("Set-Cookie", out var values)
            ? string.Join("; ", values.Select(v => v.Split(';')[0]))
            : "";

        return (token, cookie);
    }

    private async Task<ClientWebSocket> ConnectAsync(string token, string cookie, CancellationToken ct)
    {
        var socket = new ClientWebSocket();

        if (!string.IsNullOrWhiteSpace(proxy))
        {
            socket.Options.Proxy = new WebProxy(proxy);
        }

        socket.Options.SetRequestHeader("User-Agent", UserAgent);
        if (!string.IsNullOrEmpty(cookie))
        {
            socket.Options.SetRequestHeader("Cookie", cookie);
        }

        var url = $"{ConnectUrl}?id={HttpUtility.UrlEncode(token)}";
        await socket.ConnectAsync(new Uri(url), ct).ConfigureAwait(false);
        return socket;
    }

    private static Task SubscribeAsync(WebSocket socket, CancellationToken ct)
    {
        // Core invocation: type 1, a target, and arguments. Note the nesting —
        // arguments is an array containing ONE array of topic names.
        var subscribe = new JsonObject
        {
            ["type"] = 1,
            ["invocationId"] = "0",
            ["target"] = "Subscribe",
            ["arguments"] = new JsonArray(
                new JsonArray([.. Topics.Subscription.Select(t => (JsonNode)t!)])),
        };

        return SendAsync(socket, subscribe.ToJsonString(), ct);
    }

    /// <summary>Every Core message is terminated by the record separator.</summary>
    private static Task SendAsync(WebSocket socket, string payload, CancellationToken ct)
    {
        var bytes = Encoding.UTF8.GetBytes(payload + CoreFrameParser.RecordSeparator);
        return socket.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, ct);
    }

    /// <summary>
    /// Yields complete WebSocket messages. The initial state is several
    /// megabytes and arrives in many fragments, so partials are accumulated
    /// until EndOfMessage.
    /// </summary>
    private static async IAsyncEnumerable<string> ReadBuffersAsync(
        WebSocket socket, [EnumeratorCancellation] CancellationToken ct)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(64 * 1024);
        var message = new ArrayBufferWriter<byte>(64 * 1024);

        try
        {
            while (socket.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                using var idle = CancellationTokenSource.CreateLinkedTokenSource(ct);
                idle.CancelAfter(KeepAliveTimeout);

                WebSocketReceiveResult result;
                try
                {
                    result = await socket.ReceiveAsync(buffer, idle.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    // Silence past the keep-alive window. A dead-but-open socket
                    // is invisible without this check, and the dashboard would
                    // freeze on stale data instead of reconnecting.
                    throw new WebSocketException("No frame within the keep-alive timeout.");
                }

                if (result.MessageType == WebSocketMessageType.Close) yield break;

                message.Write(buffer.AsSpan(0, result.Count));
                if (!result.EndOfMessage) continue;

                var text = Encoding.UTF8.GetString(message.WrittenSpan);
                message.Clear();

                if (text.Length > 0) yield return text;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}
