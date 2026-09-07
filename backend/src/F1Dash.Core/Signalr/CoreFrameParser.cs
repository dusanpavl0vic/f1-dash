using System.Text.Json.Nodes;
using F1Dash.Core.Merge;

namespace F1Dash.Core.Signalr;

/// <summary>
/// Decodes SignalR <b>Core</b> frames.
///
/// This is a different protocol from the legacy one in <see cref="FrameParser"/>,
/// not a variant of it. docs/03 lists Core as "FALLBACK ONLY", but as of
/// 2026-09 the legacy endpoint answers every negotiate with HTTP 401 behind
/// Basic/Bearer authentication, so Core is the only path that connects.
///
/// Wire format: JSON objects separated by the ASCII record separator 0x1E.
/// The shapes that matter:
///
///   {"type":1,"target":"feed","arguments":[topic, payload, timestamp]}
///   {"type":3,"invocationId":"0","result":{ ...every topic... }}   initial state
///   {"type":6}                                                     ping
/// </summary>
public static class CoreFrameParser
{
    /// <summary>SignalR Core terminates every message with this byte.</summary>
    public const char RecordSeparator = '';

    /// <summary>The handshake a client must send before anything else.</summary>
    public const string Handshake = "{\"protocol\":\"json\",\"version\":1}";

    /// <summary>Splits a received buffer into complete messages.</summary>
    public static IEnumerable<string> SplitMessages(string buffer)
    {
        foreach (var part in buffer.Split(RecordSeparator))
        {
            if (!string.IsNullOrWhiteSpace(part)) yield return part;
        }
    }

    public static IEnumerable<TopicUpdate> Parse(string message)
    {
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(message);
        }
        catch (System.Text.Json.JsonException)
        {
            yield break;
        }

        if (root is not JsonObject frame) yield break;

        switch ((int?)frame["type"])
        {
            // Completion of the Subscribe invocation: the full initial state.
            case 3 when frame["result"] is JsonObject snapshot:
                foreach (var (topic, payload) in snapshot)
                {
                    if (TryDecode(topic, payload, null, isSnapshot: true, out var seeded))
                    {
                        yield return seeded;
                    }
                }
                break;

            // A streamed topic update.
            case 1 when (string?)frame["target"] == "feed"
                        && frame["arguments"] is JsonArray args && args.Count >= 2:
            {
                var topic = (string?)args[0];
                if (topic is null) break;

                var timestamp = args.Count > 2 ? (string?)args[2] : null;
                if (TryDecode(topic, args[1], timestamp, isSnapshot: false, out var update))
                {
                    yield return update;
                }
                break;
            }

            // 6 is a ping and 7 a close; both are the caller's concern.
            default:
                break;
        }
    }

    private static bool TryDecode(
        string topic, JsonNode? payload, string? timestamp, bool isSnapshot, out TopicUpdate update)
    {
        update = default;

        JsonNode? decoded;
        if (Topics.IsCompressed(topic))
        {
            if (payload is not JsonValue value || !value.TryGetValue<string>(out var base64)) return false;

            try
            {
                decoded = Inflate.Decode(base64);
            }
            catch (Exception e) when (e is FormatException or InvalidDataException or System.Text.Json.JsonException)
            {
                return false;
            }
        }
        else
        {
            decoded = payload;
        }

        update = new TopicUpdate(
            Topics.Normalise(topic), DeltaMerge.Normalise(decoded), timestamp, isSnapshot);
        return true;
    }
}
