using System.Runtime.CompilerServices;
using System.Threading.Channels;
using System.Text.Json.Nodes;
using F1Dash.Core.Signalr;

namespace F1Dash.Server.Ingest;

/// <summary>
/// Receives the feed from a collector running somewhere F1 accepts connections.
///
/// The collector <b>dials in</b>; this is the listening half. That direction is
/// the entire design: a server-initiated link would need a static address, port
/// forwarding and a firewall rule wherever the collector runs, and would break
/// the first time a home connection was renumbered.
///
/// What arrives is raw topic updates, never merged state. Merged state would be
/// one to two megabytes per update instead of a few kilobytes, and would put a
/// second copy of the merge algorithm in production where it could drift from
/// the first.
/// </summary>
public sealed class RelaySessionSource(ILogger logger) : ISessionSource
{
    /// <summary>
    /// Bounded, and it blocks rather than dropping when full.
    ///
    /// The same reasoning as the client fan-out (D-010): silently discarding an
    /// update leaves the accumulated state permanently wrong, and there is no
    /// way to detect it afterwards. Blocking pushes the backpressure onto the
    /// collector, which can slow down.
    /// </summary>
    private readonly Channel<TopicUpdate> _channel =
        Channel.CreateBounded<TopicUpdate>(new BoundedChannelOptions(2048)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
        });

    /// <summary>The highest sequence accepted, so a reconnecting collector can resume.</summary>
    public long LastSequence { get; private set; }

    public bool Connected { get; private set; }

    public string Description =>
        Connected ? "live (relayed from a collector)" : "live (waiting for a collector)";

    /// <summary>Accepts one update from the collector. Called by the relay endpoint.</summary>
    public async ValueTask<bool> OfferAsync(long sequence, TopicUpdate update, CancellationToken ct)
    {
        // Replayed frames after a reconnect are expected, not an error: the
        // collector resends from the last sequence we acknowledged, and the
        // boundary is inclusive on its side.
        if (sequence <= LastSequence) return true;

        await _channel.Writer.WriteAsync(update, ct).ConfigureAwait(false);
        LastSequence = sequence;
        return true;
    }

    public void CollectorConnected()
    {
        Connected = true;
        logger.LogInformation("Collector connected; resuming from sequence {Seq}", LastSequence);
    }

    public void CollectorDisconnected()
    {
        Connected = false;
        // The channel is deliberately NOT completed. The collector reconnects
        // and continues; completing here would end the session for good over a
        // dropped Wi-Fi connection.
        logger.LogWarning("Collector disconnected at sequence {Seq}; waiting for it to return", LastSequence);
    }

    public IAsyncEnumerable<TopicUpdate> ReadAsync(CancellationToken ct) =>
        _channel.Reader.ReadAllAsync(ct);

    /// <summary>Parses one relay frame. Returns false for anything malformed.</summary>
    public static bool TryParseFrame(string text, out long sequence, out TopicUpdate update)
    {
        sequence = 0;
        update = default;

        JsonObject? frame;
        try
        {
            frame = JsonNode.Parse(text) as JsonObject;
        }
        catch (System.Text.Json.JsonException)
        {
            return false;
        }

        if (frame?["topic"] is not JsonValue topicValue) return false;
        if (frame["payload"] is not JsonObject payload) return false;
        if (!topicValue.TryGetValue<string>(out var topic)) return false;

        sequence = (long?)frame["seq"] ?? 0;
        update = new TopicUpdate(topic, payload.DeepClone().AsObject(), (string?)frame["ts"], false);
        return true;
    }
}
