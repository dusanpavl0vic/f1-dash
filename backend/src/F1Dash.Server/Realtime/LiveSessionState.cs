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

    /// <summary>
    /// The most recent deltas, so a client that drops for a few seconds can
    /// resume instead of re-downloading a 1-2 MB snapshot.
    ///
    /// Bounded on purpose. An unbounded backlog would turn a client that never
    /// comes back into a memory leak, and a client far enough behind is better
    /// served by a snapshot anyway: at roughly ten deltas a second this holds
    /// about a minute, which covers a tunnel, a Wi-Fi handover or a laptop lid.
    /// </summary>
    private readonly Queue<(long Seq, byte[] Frame)> _recent = new();
    private const int RecentCapacity = 600;

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

            _recent.Enqueue((seq, frame));
            while (_recent.Count > RecentCapacity) _recent.Dequeue();
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
        lock (_stateLock) return _snapshotCache ??= Encode(SnapshotNode());
    }

    /// <summary>Caller must hold <see cref="_stateLock"/>.</summary>
    private JsonObject SnapshotNode() => new()
    {
        ["type"] = "snapshot",
        ["seq"] = Interlocked.Read(ref _sequence),
        ["ts"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
        // serverTime lets the client estimate clock skew, which the delay
        // buffer needs (docs/08 §clock skew).
        ["serverTime"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
        ["data"] = _accumulator.Snapshot(),
    };

    /// <summary>
    /// Direct access for the analysis observers. Deliberately not a clone:
    /// copying the whole state on every delta would dominate the ingest path.
    /// Callers must only READ.
    /// </summary>
    public StateAccumulator Accumulator => _accumulator;

    /// <summary>
    /// Brings a new client up to date and registers it for deltas, atomically.
    ///
    /// The two halves cannot be separated. A delta broadcast between "work out
    /// what the client has missed" and "start sending it deltas" is lost, and a
    /// lost delta is unrecoverable: the client's state is delta-accumulated, so
    /// it stays silently wrong for the rest of the session. Holding the lock
    /// across both means the client is registered before any delta it has not
    /// already been handed can be produced.
    ///
    /// Returns true when the client was resumed from the backlog, false when it
    /// was given a full snapshot — which happens when it is further behind than
    /// the backlog reaches, or the session was reset under it. Both failures
    /// have the same correct remedy.
    /// </summary>
    public bool AddAndCatchUp(ClientConnection client, long since)
    {
        lock (_stateLock)
        {
            var resumable =
                since > 0
                && since <= Interlocked.Read(ref _sequence)
                && _recent.Count > 0
                && _recent.Peek().Seq <= since + 1;

            if (resumable)
            {
                foreach (var (seq, frame) in _recent)
                {
                    if (seq > since) client.Offer(frame);
                }
            }
            else
            {
                // Queued before registration on purpose: registering first would
                // let a delta reach the client ahead of the state it patches.
                // This way the worst case is a duplicated delta, and the merge
                // is idempotent for a repeated value.
                client.Offer(_snapshotCache ??= Encode(SnapshotNode()));
            }

            _clients[client.Id] = client;
            return resumable;
        }
    }

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

            // The backlog describes the outgoing session. Replaying any of it
            // into the new one would reintroduce exactly the cross-session
            // merge this reset exists to prevent.
            _recent.Clear();

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
