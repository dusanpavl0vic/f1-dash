using System.Net.WebSockets;
using System.Text;
using System.Text.Json.Nodes;
using System.Web;
using F1Dash.Core;
using F1Dash.Core.Signalr;
using Xunit;
using Xunit.Abstractions;

namespace F1Dash.Core.Tests;

/// <summary>
/// A protocol probe, not a regression test. It answers "why does the server hang
/// up?" against the real endpoint, one variable at a time. Opt-in via
/// F1_LIVE_TEST=1.
/// </summary>
public class LiveSignalrDiagnosticTests(ITestOutputHelper output)
{
    private static bool Enabled => Environment.GetEnvironmentVariable("F1_LIVE_TEST") == "1";

    private const string NegotiateUrl =
        "https://livetiming.formula1.com/signalrcore/negotiate?negotiateVersion=1";

    [SkippableTheory]
    [InlineData(true, true)]    // negotiate with BestHTTP, replay the cookie
    [InlineData(true, false)]   // negotiate with BestHTTP, no cookie
    [InlineData(false, true)]   // negotiate with no UA, replay the cookie
    [InlineData(false, false)]  // neither — exactly what the working node probe did
    public async Task Connect(bool negotiateWithUserAgent, bool sendCookie)
    {
        Skip.IfNot(Enabled, "Set F1_LIVE_TEST=1 to run against the live F1 endpoint.");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(25));

        // --- negotiate -------------------------------------------------------
        using var handler = new HttpClientHandler { UseCookies = false };
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(20) };

        using var request = new HttpRequestMessage(HttpMethod.Post, NegotiateUrl);
        if (negotiateWithUserAgent) request.Headers.TryAddWithoutValidation("User-Agent", "BestHTTP");

        using var response = await http.SendAsync(request, cts.Token);
        var body = await response.Content.ReadAsStringAsync(cts.Token);
        var token = (string?)JsonNode.Parse(body)?["connectionToken"] ?? "";

        var cookie = response.Headers.TryGetValues("Set-Cookie", out var values)
            ? string.Join("; ", values.Select(v => v.Split(';')[0]))
            : "";

        // --- connect ---------------------------------------------------------
        using var socket = new ClientWebSocket();
        if (sendCookie && cookie.Length > 0) socket.Options.SetRequestHeader("Cookie", cookie);

        var verdict = "no verdict";
        try
        {
            await socket.ConnectAsync(
                new Uri($"wss://livetiming.formula1.com/signalrcore?id={HttpUtility.UrlEncode(token)}"),
                cts.Token);

            await Send(socket, CoreFrameParser.Handshake, cts.Token);

            var buffer = new byte[256 * 1024];
            var subscribed = false;

            while (socket.State == WebSocketState.Open)
            {
                var result = await socket.ReceiveAsync(buffer, cts.Token);
                if (result.MessageType == WebSocketMessageType.Close) { verdict = "server closed"; break; }

                var text = Encoding.UTF8.GetString(buffer, 0, result.Count);

                if (text.Contains("\"type\":7", StringComparison.Ordinal))
                {
                    verdict = "REJECTED " + text[..Math.Min(90, text.Length)];
                    break;
                }

                if (text.Contains("\"type\":3", StringComparison.Ordinal))
                {
                    verdict = $"OK — initial state, {result.Count} bytes";
                    break;
                }

                if (!subscribed)
                {
                    subscribed = true;
                    await Send(socket, Subscribe(), cts.Token);
                }
            }
        }
        catch (OperationCanceledException) { verdict = "timed out"; }
        catch (WebSocketException e) { verdict = "WS: " + e.Message; }

        output.WriteLine($"negotiateUA={negotiateWithUserAgent} cookie={sendCookie}  ->  {verdict}");
        Assert.StartsWith("OK", verdict, StringComparison.Ordinal);
    }

    private static string Subscribe() => new JsonObject
    {
        ["type"] = 1,
        ["invocationId"] = "0",
        ["target"] = "Subscribe",
        ["arguments"] = new JsonArray(new JsonArray([.. Topics.Subscription.Select(t => (JsonNode)t!)])),
    }.ToJsonString();

    private static Task Send(WebSocket socket, string payload, CancellationToken ct) =>
        socket.SendAsync(
            Encoding.UTF8.GetBytes(payload + CoreFrameParser.RecordSeparator),
            WebSocketMessageType.Text, true, ct);
}
