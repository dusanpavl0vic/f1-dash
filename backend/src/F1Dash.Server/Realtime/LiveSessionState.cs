using System.Collections.Concurrent;
using System.Text;
using System.Text.Json.Nodes;
using F1Dash.Core.Merge;
using F1Dash.Core.Signalr;

namespace F1Dash.Server.Realtime;

/// <summary>
/// Owns the authoritative state and fans deltas out to every connected browser.
///
/// v1 keeps this in-process rather than behind Redis. The backend is one
/// application (see DECISIONS D-009), so the split that Redis existed to
/// bridge — a singleton ingest against a replicable fanout — does not exist
/// yet. The seam is preserved: swapping this for a Redis-backed implementation
/// touches this file only.
/// </summary>
public sealed class LiveSessionState
{
    private StateAccumulator _accumulator = new();
    private readonly ConcurrentDictionary<Guid, ClientConnection> _clients = new();
    private readonly Lock _stateLock = new();

    /// <summary>
    /// The serialised snapshot, rebuilt at most once per checkpoint rather than
    /// once per delta.
    ///
    /// docs/06 writes the whole state on every delta; at a 1–2 MB state and ~10
    /// deltas/s that is 10–20 MB/s of pure waste and the single largest cost in
    /// the naive design. Rebuilding on demand and caching gives byte-identical
    /// results because a client also receives every delta after its snapshot.
    /// </summary>
    private byte[]? _snapshotCache;

    private long _sequence;

    public int ClientCount => _clients.Count;
    public long Sequence => Interlocked.Read(ref _sequence);
    public bool HasData { get; private set; }

    /// <summary>Applies one update and broadcasts the resulting delta.</summary>
    public void Apply(in TopicUpdate update)
    {
        byte[]? frame = null;

        lock (_stateLock)
        {
            var delta = _accumulator.Apply(update);
            if (delta is null) return;

            _snapshotCache = null;
            HasData = true;
            var seq = Interlocked.Increment(ref _sequence);

            // Serialise ONCE, then hand the same bytes to every client. This is
            // what keeps fanout nearly free: client count then affects only the
            // number of socket writes.
            frame = Encode(new JsonObject
            {
                ["type"] = "delta",
                ["seq"] = seq,
                ["ts"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                ["data"] = delta,
            });
        }

        Broadcast(frame);
    }

    /// <summary>
    /// The snapshot a connecting client receives, with the sequence number it
    /// corresponds to. Both are read under one lock: reading them separately
    /// lets a delta slip between the two and be lost forever.
    /// </summary>
    public byte[] SnapshotFrame()
    {
        lock (_stateLock)
        {
            return _snapshotCache ??= Encode(new JsonObject
            {
                ["type"] = "snapshot",
                ["seq"] = Interlocked.Read(ref _sequence),
                ["ts"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                // serverTime lets the client estimate clock skew, which the
                // delay buffer needs (docs/08 §clock skew).
                ["serverTime"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                ["data"] = _accumulator.Snapshot(),
            });
        }
    }

    /// <summary>
    /// Direct access for the analysis observers. Deliberately not a clone:
    /// copying the whole state on every delta would dominate the ingest path.
    /// Callers must only READ.
    /// </summary>
    public StateAccumulator Accumulator => _accumulator;

    public JsonObject Snapshot()
    {
        lock (_stateLock) return _accumulator.Snapshot();
    }

    /// <summary>
    /// Discards everything and starts a new session.
    ///
    /// State is delta-accumulated, so switching sources without a reset would
    /// merge one session's drivers, laps and messages into another's. The
    /// sequence keeps counting up so a connected client sees the change as new
    /// frames rather than as a rewind.
    /// </summary>
    public void Reset()
    {
        byte[] frame;

        lock (_stateLock)
        {
            _accumulator = new StateAccumulator();
            _snapshotCache = null;
            HasData = false;

            // Connected clients hold the OUTGOING session's accumulated state.
            // Resetting only the server leaves them merging the new session's
            // deltas on top of the old one's drivers — which showed up as a
            // 26-car field with two cars sharing P1.
            //
            // A snapshot carrying an empty state tells them to replace, not
            // merge. `reason` exists so the client can tell a session change
            // apart from an ordinary reconnect.
            frame = Encode(new JsonObject
            {
                ["type"] = "snapshot",
                ["reason"] = "session-changed",
                ["seq"] = Interlocked.Read(ref _sequence),
                ["ts"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                ["serverTime"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                ["data"] = new JsonObject(),
            });
        }

        Broadcast(frame);
    }

    public void Add(ClientConnection client) => _clients[client.Id] = client;

    public void Remove(Guid id)
    {
        if (_clients.TryRemove(id, out var client)) client.Complete();
    }

    private void Broadcast(byte[]? frame)
    {
        if (frame is null) return;

        foreach (var (id, client) in _clients)
        {
            if (!client.Offer(frame))
            {
                // Too slow. Drop it; it reconnects and gets a fresh snapshot.
                _clients.TryRemove(id, out _);
            }
        }
    }

    private static byte[] Encode(JsonNode node) =>
        Encoding.UTF8.GetBytes(node.ToJsonString());
}
