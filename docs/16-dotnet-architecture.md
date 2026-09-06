# 16 — .NET Architecture

> Supersedes the Python-specific parts of `02-architecture.md`, `06-ingest-service.md`,
> `07-state-and-websocket.md` and `09-rest-api.md`.
>
> **What does not change:** the F1 wire protocol (`03`, `04`), field names and the data model
> (`05`), the merge algorithm's *rules* (`06`), delay and replay *semantics* (`08`), and the track
> map mathematics (`10`). Those documents remain authoritative. Only the implementation language
> and the concurrency primitives change.

---

## 1. Why .NET, and where the boundary sits

Throughput is not the bottleneck. Ingest is 50–200 KB/s decompressed and roughly 10 deltas per
second. All three candidate runtimes (.NET, Go, Python) are an order of magnitude faster than that
requirement. The decision rests on three things that are *not* raw speed:

| | .NET 9 | Go | Python |
|---|---|---|---|
| Fanout to 500 WS clients | `System.Threading.Channels` maps 1:1 onto the bounded per-client queue design in `07` | goroutine-per-connection, lowest memory per connection | asyncio is adequate; GIL plus one replay engine per client is the weak point |
| ~30 typed models from `05` | `record` types + source-generated `System.Text.Json` | hand-written struct boilerplate | Pydantic, least code |
| FastF1 (lap tables, per-lap telemetry, results) | **no equivalent** | **no equivalent** | native |

**Decision: .NET for the hot path, a Python worker for FastF1 only.**

The Python worker is an offline batch job. It writes bundle files to disk or S3 and never sits in
an HTTP request path, so its performance is irrelevant to the user-facing system. This keeps the
one thing Python is uniquely good at without paying for it anywhere that matters.

### 1.1 Two traps to know before writing a line of code

**Trap 1 — .NET's SignalR client cannot talk to the F1 feed.**
F1 uses *legacy* ASP.NET SignalR 1.5. `Microsoft.AspNetCore.SignalR.Client` implements the SignalR
**Core** protocol only and will not connect. The old `Microsoft.AspNet.SignalR.Client` package
targets .NET Standard but is in maintenance mode and does not give the control over exact
case-sensitive request headers and the negotiate cookie that `04-signalr-protocol.md` requires.

So the ingest client is a raw `ClientWebSocket` plus a hand-written negotiate and framing layer —
roughly 200 lines, the same work as in Go or Python. **Prove this first, in Phase 4, before the
rest of the backend commits to .NET.**

.NET's SignalR *server* remains an option for browser fanout, but we use a plain WebSocket because
`05-data-model.md` already fixes the browser-facing wire format.

**Trap 2 — `StackExchange.Redis` does not do blocking reads.**
It multiplexes commands over a shared connection, so `XREAD BLOCK` as written in `07` will stall
the multiplexer. Use the pub/sub channel `f1:notify` (already in the design) as a wake-up signal
and a non-blocking `XRANGE` from the last seen ID. A 250 ms fallback poll covers a missed
notification.

---

## 2. Solution layout

```
F1Dash.sln
│
├── src/
│   ├── F1Dash.Shared/                 net9.0, no ASP.NET dependency
│   │   ├── Models/                    records mirroring docs/05, one file per topic
│   │   ├── Merge/
│   │   │   ├── DeltaMerge.cs          the merge algorithm — docs/06 rules, verbatim
│   │   │   └── StateAccumulator.cs    per-topic special cases
│   │   ├── Signalr/
│   │   │   ├── FrameParser.cs         {} | {"R":…} | {"M":[…]} → topic updates
│   │   │   └── Inflate.cs             base64 + raw DEFLATE for .z topics
│   │   ├── Track/TrackTransform.cs    rotate + flip-Y — docs/10, shared with the client
│   │   └── Constants.cs               topic names, tyre colours, status codes
│   │
│   ├── F1Dash.Ingest/                 Worker Service (no HTTP server)
│   │   ├── Sources/
│   │   │   ├── LiveSignalrSource.cs   negotiate → ClientWebSocket → frames
│   │   │   └── SimulatorSource.cs     F1_DEV_URL — identical frame shape
│   │   ├── RawRecorder.cs             append raw frames to the archive, off the hot path
│   │   ├── RedisPublisher.cs          XADD deltas, checkpoint snapshots
│   │   ├── SingletonLease.cs          Redis lock — see §5.1
│   │   └── SessionRotator.cs          SessionStatus / SessionInfo.Key → archive rotation
│   │
│   ├── F1Dash.Realtime/               ASP.NET Core minimal API, :4000
│   │   ├── Live/
│   │   │   ├── DeltaBroadcaster.cs    ONE Redis reader per process
│   │   │   ├── SnapshotCache.cs       pre-serialised + pre-gzipped snapshot — see §4.2
│   │   │   └── ClientConnection.cs    bounded Channel + writer loop
│   │   ├── Replay/
│   │   │   ├── SessionStreamCache.cs  immutable parsed stream, shared across engines
│   │   │   ├── ReplayEngine.cs        per-client cursor + state
│   │   │   └── KeyframeIndex.cs
│   │   └── Health/
│   │
│   ├── F1Dash.Api/                    ASP.NET Core minimal API, :4001
│   │   ├── Endpoints/                 schedule, sessions, track, replay, standings
│   │   ├── Upstream/                  Jolpica, MultiViewer — typed HttpClients
│   │   └── Caching/                   in-memory TTL + disk tier
│   │
│   └── F1Dash.Simulator/              dev only, :8000
│
├── tools/precompute/                  Python 3.12 — FastF1 → bundles/**  (the only Python)
├── web/                               Vite SPA — see docs/17
├── tests/
│   ├── F1Dash.Shared.Tests/           merge fixtures, shared JSON with the web vitest suite
│   ├── F1Dash.Integration.Tests/      full-session replay → final classification
│   └── F1Dash.Load.Tests/
├── shared-fixtures/                   consumed by BOTH .NET and TypeScript test suites
└── compose.yaml
```

---

## 3. Primitive mapping

The algorithms are unchanged; only the tools differ.

| `docs/` says (Python) | .NET |
|---|---|
| `asyncio.Queue(maxsize=500)` | `Channel.CreateBounded<ReadOnlyMemory<byte>>(500)`, `FullMode = DropWrite` → close the client |
| `orjson` | `System.Text.Json` with a `JsonSerializerContext` (source-generated, reflection-free, AOT-safe) |
| `zlib.decompress(data, -MAX_WBITS)` | `DeflateStream` — .NET's `DeflateStream` is *raw* deflate with no zlib header, which is exactly what the feed sends. Do **not** reach for `ZLibStream`. |
| `redis.xread(block=1000)` | pub/sub `f1:notify` wake-up + `XRANGE` from the last ID, 250 ms fallback poll |
| `websockets.connect(additional_headers=…)` | `ClientWebSocket.Options.SetRequestHeader` — headers are case-sensitive, see `04` |
| Pydantic models | `record` types. **The merge operates on `JsonNode`, not on typed models** — see §3.1 |
| `pydantic2ts` | `TypeGen` or a small reflection emitter → `web/src/types/generated.ts`, staleness checked in CI |
| `prometheus-client` | `prometheus-net.AspNetCore` |
| `uvicorn` | Kestrel |

### 3.1 Why the merge works on `JsonNode`, not on typed models

A delta patches an arbitrarily nested subtree — one sector time inside one driver inside
`TimingData.Lines`. A strongly-typed merge would need hand-written per-field code across ~30
models, and every feed change would silently break a mapping layer. `docs/00` rule 3 forbids
exactly that.

So: **authoritative state is a `JsonObject` tree; the merge is generic; typed records exist for API
responses and for generating TypeScript types.** The typed layer is a read-side projection, not the
storage format.

The four merge rules from `06` translate directly:

1. Recursive merge, never replace — a nested `JsonObject` is merged key by key.
2. Numeric-string keys are sparse array indices — merge each, drop none.
3. `_kf` is stripped; `_deleted` removes the listed keys.
4. Genuine JSON arrays are normalised into index-keyed objects at ingest, so downstream code sees
   exactly one shape.

`DeltaMerge.cs` and `web/src/features/live/lib/merge.ts` are tested against the **same** JSON
fixture files in `shared-fixtures/merge/`. If they diverge, CI fails. This is the guard against
the entire class of "the browser shows something different from the server" bugs.

---

## 4. Performance design

The stated target is **under 300 ms from feed to pixel**. The design below budgets roughly 40 ms,
leaving the rest as headroom.

### 4.1 Latency budget

| Stage | Budget | How it is achieved |
|---|---|---|
| WS receive + raw record | 1 ms | Recording is handed to an unbounded channel and written by a separate task; it never blocks parsing |
| Parse + inflate `.z` | 3 ms | `Utf8JsonReader` over pooled buffers; `DeflateStream` into a pooled array |
| Merge into state | 2 ms | In-place `JsonNode` mutation; no clone of the state tree per delta |
| `XADD` to Redis | 1 ms | Pipelined, fire-and-forget on the delta path |
| Notify → realtime picks up | 2 ms | Pub/sub wake-up, not a poll |
| Serialise the outbound frame | 1 ms | **Once per delta, not once per client** |
| Write to N sockets | 1 ms | The same `ReadOnlyMemory<byte>` to every client; no per-client allocation |
| Browser parse + merge | 2 ms | Same generic merge, small payloads |
| rAF flush + paint | ≤ 16 ms | Deltas are coalesced into one React commit per frame |
| **Total** | **~30 ms** | vs. a 300 ms budget |

### 4.2 Five decisions that carry the performance

**(a) Snapshot checkpointing instead of per-delta snapshot writes.**
`docs/06` writes the entire accumulated state to `f1:state` on *every* delta. With a 1–2 MB state
at 10 deltas/s that is 10–20 MB/s of pure waste to Redis, and it is the single largest cost in the
naive design.

Instead: write a checkpoint **once per second**, storing the snapshot and the stream ID it
corresponds to atomically in one hash. A connecting client receives that snapshot plus every delta
from its cursor to now via `XRANGE`. The result is byte-identical state, because the merge is
idempotent for a repeated value, at ~1/10th the Redis traffic.

**(b) The snapshot is serialised and gzipped once, then shared.**
The initial snapshot is the largest payload in the system — megabytes during a race, sent to every
connecting client. `SnapshotCache` holds the serialised bytes *and* their gzipped form, rebuilt
only when the checkpoint changes. Every connecting client receives the same pre-compressed buffer.

Deltas are the opposite case: small and numerous. They are sent **uncompressed**, because
per-message deflate would give each client its own compression context and destroy the
"serialise once, send the same bytes to everyone" property that makes fanout cheap.

**(c) One Redis reader per process, never one per client.**
`DeltaBroadcaster` is a single hosted service. It reads, serialises once, and offers the resulting
buffer to every `ClientConnection`. Client count affects only the number of socket writes.

**(d) The parsed replay stream is shared; only the cursor is per-client.**
A two-hour race is ~500k stream entries. Parsing that per viewer would be fatal at 50 concurrent
replays. `SessionStreamCache` parses a session once into an immutable array, reference-counted with
a sliding expiration. Each `ReplayEngine` holds an index into it plus its own accumulated state
(1–2 MB). Fifty concurrent replays of the same race cost one copy of the stream, not fifty.

**(e) Keyframes make backward seek cheap.**
State is delta-accumulated, so seeking backwards means rebuilding. A full state snapshot every 60
seconds of session time turns a worst-case ~2 s rebuild into replaying at most 60 s of deltas —
under 100 ms. Build this in Phase 12, after correctness.

### 4.3 Allocation discipline on the hot path

- `ArrayPool<byte>` for WebSocket receive buffers and inflate output.
- `Utf8JsonReader` over `ReadOnlySpan<byte>`; no intermediate `string` for the envelope.
- Source-generated serialisation contexts — no reflection, no per-call metadata lookup.
- `ReadOnlyMemory<byte>` for outbound frames, never `byte[]` copies per client.
- `ServerGarbageCollection` on for `Realtime`, off for `Ingest` (single-threaded workload).

None of this is premature: it is the difference between 500 concurrent clients on one core and
120.

---

## 5. Reliability design

Reliability matters more than speed here. A dashboard that is 50 ms faster but shows a stale
timing tower after a reconnect is worse than useless — the user cannot tell it is wrong.

### 5.1 Ingest must be exactly one process

Two SignalR connections double every delta and risk F1 rate-limiting. Deployment enforces this
(`replicas: 1`, `strategy: Recreate`), but deployment mistakes happen. Belt and braces:

`SingletonLease` acquires `SET f1:ingest:lock <instance-id> NX PX 15000` and renews every 5 s.
Without the lease the process runs in standby and publishes nothing. An accidental second instance
is harmless, and a crashed primary is replaced within 15 s.

### 5.2 Record raw frames before parsing anything

Every frame is appended to the archive **byte-identical, before any parsing**. If the parser has a
bug, the data is not lost — the session can be reprocessed. This is also what feeds the simulator
and the regression fixtures. It is the reason a parsing bug during a live race is recoverable
rather than terminal.

### 5.3 Reconnection and state continuity

| Failure | Behaviour |
|---|---|
| F1 socket drops | Exponential backoff 1 s → 30 s with ±20% jitter, re-negotiating each attempt (the token expires). **Accumulated state is not cleared** — the fresh `R` snapshot overwrites it wholesale, and keeping the old state means the dashboard does not blank out during a blip. |
| No frame for `KEEPALIVE_TIMEOUT` (30 s) | Treat as dead, reconnect. A silently-dead-but-open socket is common behind proxies and is invisible without this check. |
| Redis unavailable | Ingest buffers to disk and keeps recording; realtime tells clients and retries. Recording never stops — data loss is the only unrecoverable failure. |
| Browser socket drops | Client reconnects with `?since=<streamId>`. If the cursor is still in the retained window, deltas resume with no snapshot. If not, a full snapshot is sent **with `reason: "cursor-expired"`** so the client knows to reset its store rather than merging onto stale state. |
| Dead-but-open browser socket | Client sends `ping` every 20 s; no `pong` within 10 s forces a reconnect. |
| A slow client | Its bounded channel fills, it is dropped with close code 1013, and it reconnects with a fresh snapshot. **One stalled browser tab must never back up the shared reader.** |
| F1 blocks the ingest IP | The app degrades to replay-only. `api` and `realtime` touch no F1 endpoint at request time, so schedule, standings and replay stay fully functional and the dashboard says "no live session" rather than showing an error. |

### 5.4 Correctness guards that run in CI

1. **Merge parity** — `DeltaMerge.cs` and `merge.ts` produce identical output for every fixture in
   `shared-fixtures/merge/`. Divergence fails the build.
2. **Full-session classification** — replaying a recorded 2024 race through `StateAccumulator`
   must produce a final top-10 identical to the official result. This single test validates
   parsing, decompression, merging and the state model at once. Written in Phase 2, never allowed
   to break.
3. **Round-trip recording** — raw frames replayed through the simulator are byte-identical to the
   original capture.
4. **Memory flatness** — a full recorded race must not grow the ingest heap. `Position` and
   `CarData` hold the latest batch only; history is the client's or the bundle's job.

### 5.5 Observability

Prometheus at `/metrics` on each service. Log JSON to stdout.

| Metric | Service | Why it matters |
|---|---|---|
| `f1_ingest_connected` (0/1) | ingest | The single most important signal in the system |
| `f1_ingest_frames_total{topic}` | ingest | A topic going silent means a feed change |
| `f1_ingest_reconnects_total` | ingest | |
| `f1_ingest_lease_held` (0/1) | ingest | Confirms singleton behaviour |
| `f1_realtime_clients` | realtime | HPA input |
| `f1_realtime_dropped_clients_total{reason}` | realtime | Backpressure drops vs. errors |
| `f1_realtime_delta_lag_seconds` | realtime | End-to-end freshness |
| `f1_replay_engines_active` | realtime | Against `MAX_CONCURRENT_REPLAYS` |
| `f1_api_cache_hits_total{endpoint}` | api | |

**Log every unrecognised topic, field and status code exactly once, deduplicated by key.** This is
how a feed change is discovered before users report a broken UI — and the feed does change.

---

## 6. Service contracts

Unchanged from `07` and `09`. Restated for completeness:

| Service | Port | Replicas | Endpoints |
|---|---|---|---|
| `redis` | 6379 | 1 | — |
| `ingest` | — | **exactly 1** | none (worker) |
| `realtime` | 4000 | 1–N | `GET /ws`, `GET /ws/replay/{year}/{round}/{session}`, `/health`, `/metrics` |
| `api` | 4001 | 1–N | `/api/schedule/*`, `/api/sessions/*`, `/api/track/*`, `/api/replay/*`, `/api/standings/*` |
| `web` | 3000 | 1–N | static SPA |
| `simulator` | 8000 | dev only | `GET /ws` |

Browser-facing message shapes are exactly as specified in `05-data-model.md` §"The wire protocol
to the browser". They are the contract between backend and frontend and are not renegotiated here.

---

## 7. Deployment notes specific to .NET

- Multi-stage Dockerfiles on `mcr.microsoft.com/dotnet/aspnet:9.0-alpine`, non-root user.
- `InvariantGlobalization=true` — smaller image; the app has no locale-dependent formatting.
- Consider `PublishAot` for `Ingest` (fast start, small footprint, no reflection needed once
  serialisation is source-generated). Not for `Api`, where the payoff is smaller.
- `ServerGarbageCollection=true` for `Realtime`; leave it off for `Ingest`.
- Graceful shutdown: `terminationGracePeriodSeconds: 60` so in-flight WebSocket connections drain.
- The reverse proxy **must** set `proxy_buffering off` on the WS route. With buffering on, nginx
  batches frames and the entire latency design is wasted.
