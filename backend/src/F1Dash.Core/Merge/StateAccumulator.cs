using System.Text.Json.Nodes;
using F1Dash.Core.Signalr;

namespace F1Dash.Core.Merge;

/// <summary>
/// Holds the authoritative accumulated state and applies topic updates to it.
///
/// State is keyed by the feed's own topic names — SessionInfo, TimingData,
/// DriverList — and the field names inside are untouched. docs/00 rule 3: never
/// invent field names. That means the browser receives the feed's own shape and
/// no mapping layer exists on the server to drift out of date; the view models
/// live in the client's selector layer instead.
/// </summary>
public sealed class StateAccumulator
{
    private readonly JsonObject _state = new();

    /// <summary>
    /// Topics whose payload replaces the previous value wholesale rather than
    /// merging into it (docs/06 §special-cased topics).
    /// </summary>
    private static readonly HashSet<string> ReplaceTopics =
    [
        // Only the newest frame matters. Merging would accumulate stale cars
        // forever and grow the heap across a race.
        Topics.Position,
        Topics.CarData,
        // Changes only between sessions.
        Topics.SessionInfo,
    ];

    /// <summary>
    /// Topics that are logs, not state. New entries are appended; an existing
    /// entry is never merged over, because a race control message is a
    /// historical record and rewriting one loses what was actually said.
    /// </summary>
    private static readonly Dictionary<string, string> AppendOnlyTopics = new()
    {
        [Topics.RaceControlMessages] = "Messages",
        [Topics.TeamRadio] = "Captures",
    };

    /// <summary>A snapshot of the full accumulated state, safe to serialise.</summary>
    public JsonObject Snapshot() => _state.DeepClone().AsObject();

    /// <summary>Direct read access, for tests and for projections.</summary>
    public JsonNode? this[string topic] => _state[topic];

    public int TopicCount => _state.Count;

    /// <summary>
    /// Applies one topic update and returns the delta that should be published
    /// to clients, or null when the update changed nothing worth sending.
    ///
    /// The returned delta is shaped as { "&lt;Topic&gt;": &lt;payload&gt; } so a
    /// client can merge it into its own state with the identical algorithm.
    /// </summary>
    public JsonObject? Apply(in TopicUpdate update)
    {
        var topic = update.Topic;
        var payload = update.Payload;

        if (ReplaceTopics.Contains(topic))
        {
            _state[topic] = payload.DeepClone();
            return Delta(topic, payload);
        }

        if (AppendOnlyTopics.TryGetValue(topic, out var collectionKey))
        {
            return ApplyAppendOnly(topic, collectionKey, payload, update.IsSnapshot);
        }

        if (_state[topic] is JsonObject existing)
        {
            DeltaMerge.Merge(existing, payload);
        }
        else
        {
            _state[topic] = payload.DeepClone();
        }

        return Delta(topic, payload);
    }

    /// <summary>
    /// Append-only topics arrive two ways: the initial snapshot carries the
    /// whole collection, while a delta carries just the new entries — sometimes
    /// index-keyed, sometimes as a bare array that always starts at index 0 and
    /// would therefore overwrite the first message of the session.
    /// </summary>
    private JsonObject? ApplyAppendOnly(string topic, string collectionKey, JsonObject payload, bool isSnapshot)
    {
        if (_state[topic] is not JsonObject stored)
        {
            stored = new JsonObject { [collectionKey] = new JsonObject() };
            _state[topic] = stored;
        }

        if (stored[collectionKey] is not JsonObject collection)
        {
            collection = new JsonObject();
            stored[collectionKey] = collection;
        }

        if (payload[collectionKey] is not JsonObject incoming)
        {
            // Some other field on the topic changed; merge it normally.
            DeltaMerge.Merge(stored, payload);
            return Delta(topic, payload);
        }

        var appended = new JsonObject();
        var nextIndex = NextIndex(collection);

        foreach (var (key, value) in incoming)
        {
            // The snapshot's own indices are authoritative and are kept as sent.
            // A delta's indices are only trustworthy when they do not collide
            // with something already recorded.
            var targetKey = isSnapshot || !collection.ContainsKey(key)
                ? key
                : (nextIndex++).ToString();

            var clone = value?.DeepClone();
            collection[targetKey] = clone;
            appended[targetKey] = clone?.DeepClone();
        }

        if (appended.Count == 0) return null;

        return new JsonObject
        {
            [topic] = new JsonObject { [collectionKey] = appended },
        };
    }

    private static int NextIndex(JsonObject collection)
    {
        var max = -1;
        foreach (var (key, _) in collection)
        {
            if (int.TryParse(key, out var index) && index > max) max = index;
        }
        return max + 1;
    }

    private static JsonObject Delta(string topic, JsonObject payload) =>
        new() { [topic] = payload.DeepClone() };
}
