using System.Net.WebSockets;
using System.Text;
using System.Text.Json.Nodes;
using F1Dash.Core.Archive;
using F1Dash.Core.Signalr;

namespace F1Dash.Server.Ingest;

/// <summary>
/// Runs where F1 accepts connections — typically a home machine — and forwards
/// the feed to a backend that F1 will not talk to.
///
/// It is the same binary in a different mode rather than a second application:
/// the sources it reads from are the ones the backend already has, and keeping
/// them in one place is what stops the collector's ingest from quietly
/// diverging from the server's.
///
/// Enabled with RELAY_UPSTREAM (say wss://apex.example/relay) and RELAY_TOKEN.
/// </summary>
public sealed class RelayCollector(ILoggerFactory loggers) : IHostedService
{
    private readonly ILogger _logger = loggers.CreateLogger<RelayCollector>();
    private CancellationTokenSource? _running;
    private Task? _task;

    public static string? Upstream => Environment.GetEnvironmentVariable("RELAY_UPSTREAM");

    public static bool Enabled => !string.IsNullOrWhiteSpace(Upstream);

    public Task StartAsync(CancellationToken ct)
    {
        if (!Enabled) return Task.CompletedTask;

        _running = new CancellationTokenSource();
        _task = Task.Run(() => RunAsync(_running.Token), CancellationToken.None);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken ct)
    {
        if (_running is null) return;

        await _running.CancelAsync().ConfigureAwait(false);
        if (_task is not null)
        {
            try { await _task.ConfigureAwait(false); } catch (OperationCanceledException) { }
        }
    }

    private async Task RunAsync(CancellationToken ct)
    {
        var backoff = TimeSpan.FromSeconds(1);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await ForwardAsync(ct).ConfigureAwait(false);
                backoff = TimeSpan.FromSeconds(1);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception e)
            {
                _logger.LogWarning(e, "Relay link failed; retrying in {Backoff}", backoff);
            }

            await Task.Delay(backoff, ct).ConfigureAwait(false);
            backoff = TimeSpan.FromSeconds(Math.Min(30, backoff.TotalSeconds * 2));
        }
    }

    private async Task ForwardAsync(CancellationToken ct)
    {
        var proxy = Environment.GetEnvironmentVariable("F1_HTTP_PROXY");
        var token = Environment.GetEnvironmentVariable("RELAY_TOKEN")
            ?? throw new InvalidOperationException("RELAY_TOKEN is required to run as a collector.");

        using var socket = new ClientWebSocket();
        socket.Options.SetRequestHeader("X-Relay-Token", token);

        await socket.ConnectAsync(new Uri(Upstream!), ct).ConfigureAwait(false);

        // The server opens by saying where it got to, so a reconnect does not
        // resend a session's worth of frames the server already has.
        var resumeFrom = await ReadResumePointAsync(socket, ct).ConfigureAwait(false);
        _logger.LogInformation("Relay connected; server has up to sequence {Seq}", resumeFrom);

        // The same failover the backend uses. A collector that can only speak
        // SignalR would be useless on a network where SignalR is the thing
        // that fails.
        var source = new FailoverSessionSource(
            [
                new LiveSignalrSource(proxy, loggers.CreateLogger<LiveSignalrSource>()),
                new StaticPollingSource(
                    ArchiveClient.CreateHttpClient(proxy),
                    loggers.CreateLogger<StaticPollingSource>()),
            ],
            loggers.CreateLogger<FailoverSessionSource>());

        long sequence = resumeFrom;

        await foreach (var update in source.ReadAsync(ct).ConfigureAwait(false))
        {
            var frame = new JsonObject
            {
                ["seq"] = ++sequence,
                ["topic"] = update.Topic,
                ["ts"] = update.Timestamp,
                ["payload"] = update.Payload.DeepClone(),
            };

            await socket.SendAsync(
                Encoding.UTF8.GetBytes(frame.ToJsonString()),
                WebSocketMessageType.Text, true, ct).ConfigureAwait(false);
        }
    }

    private static async Task<long> ReadResumePointAsync(WebSocket socket, CancellationToken ct)
    {
        var buffer = new byte[1024];
        var result = await socket.ReceiveAsync(buffer, ct).ConfigureAwait(false);
        var text = Encoding.UTF8.GetString(buffer, 0, result.Count);

        return (long?)(JsonNode.Parse(text) as JsonObject)?["resumeFrom"] ?? 0;
    }
}
