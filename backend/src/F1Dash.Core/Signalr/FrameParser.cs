using System.Text.Json.Nodes;
using F1Dash.Core.Merge;

namespace F1Dash.Core.Signalr;

/// <summary>One topic update lifted out of a wire frame.</summary>
/// <param name="Topic">Normalised topic name — the ".z" suffix already stripped.</param>
/// <param name="Payload">The decompressed, array-normalised payload.</param>
/// <param name="Timestamp">Feed-supplied UTC timestamp, absent on the initial snapshot.</param>
/// <param name="IsSnapshot">True when this came from the initial "R" full-state message.</param>
public readonly record struct TopicUpdate(
    string Topic,
    JsonObject Payload,
    string? Timestamp,
    bool IsSnapshot);

/// <summary>
/// Decodes the three shapes that arrive on the SignalR socket (docs/04 §4):
///
///   {}                      keep-alive — reset the idle timer, nothing else
///   {"R": { ... }}          the initial full-state dump for every topic
///   {"M": [ ... ]}          deltas; ALWAYS a list, never assume length 1
/// </summary>
public static class FrameParser
{
    /// <summary>
    /// Lifts every topic update out of one raw frame. Unparseable payloads are
    /// skipped rather than throwing: one malformed topic must not take down the
    /// connection mid-session.
    /// </summary>
    public static IEnumerable<TopicUpdate> Parse(string frame)
    {
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(frame);
        }
        catch (System.Text.Json.JsonException)
        {
            yield break;
        }

        if (root is not JsonObject message || message.Count == 0)
        {
            // Empty object is the keep-alive.
            yield break;
        }

        if (message["R"] is JsonObject snapshot)
        {
            foreach (var (topic, payload) in snapshot)
            {
                if (TryDecode(topic, payload, out var update, timestamp: null, isSnapshot: true))
                {
                    yield return update;
                }
            }
            yield break;
        }

        if (message["M"] is not JsonArray invocations)
        {
            yield break;
        }

        // A single frame routinely carries several topic updates.
        foreach (var entry in invocations)
        {
            if (entry is not JsonObject invocation) continue;
            if ((string?)invocation["M"] != "feed") continue;
            if (invocation["A"] is not JsonArray args || args.Count < 2) continue;

            var topic = (string?)args[0];
            if (topic is null) continue;

            var timestamp = args.Count > 2 ? (string?)args[2] : null;

            if (TryDecode(topic, args[1], out var update, timestamp, isSnapshot: false))
            {
                yield return update;
            }
        }
    }

    private static bool TryDecode(
        string topic,
        JsonNode? payload,
        out TopicUpdate update,
        string? timestamp,
        bool isSnapshot)
    {
        update = default;

        JsonNode? decoded;
        if (Topics.IsCompressed(topic))
        {
            // A compressed topic arrives as a bare base64 string.
            if (payload is not JsonValue value || !value.TryGetValue<string>(out var base64))
            {
                return false;
            }

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
            Topics.Normalise(topic),
            DeltaMerge.Normalise(decoded),
            timestamp,
            isSnapshot);
        return true;
    }
}
