# 02 — Architecture

## Reference: how f1-dash was structured

f1-dash split into these top-level packages (Rust backend, Next.js frontend):

```
api/         Rust + Axum   — non-realtime data (past/future sessions, schedule)
realtime/    Rust + Axum   — SignalR client + WebSocket fanout to browsers
signalr/     Rust crate    — the SignalR protocol implementation
simulator/   Rust          — replays recorded sessions over a local WS for dev
dashboard/   Next.js + TS  — the website
shared/      Rust crate    — shared types
```

Its runtime env vars confirm the split:

```
dashboard: NEXT_PUBLIC_LIVE_URL=http://localhost:4000   (realtime service)
           API_URL=http://localhost:4001                 (api service)
realtime:  ADDRESS=0.0.0.0:4000, ORIGIN=<dashboard url>, F1_DEV_URL=ws://localhost:8000/ws
api:       ADDRESS=0.0.0.0:4001, ORIGIN=<dashboard url>
```

**We mirror this separation**, but in Python/FastAPI.

## Our services

```
┌──────────────────────────────────────────────────────────────────┐
│                     EXTERNAL DATA SOURCES                        │
│  livetiming.formula1.com/signalrcore   (live WebSocket/SignalR)  │
│  livetiming.formula1.com/static/...    (archived jsonStream)     │
│  api.multiviewer.app/api/v1/circuits   (track geometry)          │
│  api.jolpi.ca/ergast/f1/...            (schedule, results)       │
└────────────┬─────────────────────────────────────────────────────┘
             │
     ┌───────▼────────┐
     │  ingest        │  Python, no HTTP server. One process.
     │  (worker)      │  - SignalR client
     │                │  - decompress .z topics
     │                │  - normalise into deltas
     │                │  - raw recorder → archive store
     └───────┬────────┘
             │  publishes to Redis
             │    - key   f1:state          (full JSON snapshot, overwritten)
             │    - stream f1:deltas        (XADD, capped ~50k entries)
             │    - pubsub f1:notify        (wake-up signal)
     ┌───────▼────────┐
     │     Redis      │
     └───┬────────┬───┘
         │        │
 ┌───────▼──┐  ┌──▼────────────┐
 │ realtime │  │  api          │   Both FastAPI. Horizontally scalable.
 │ :4000    │  │  :4001        │
 │          │  │               │
 │ WS /ws   │  │ REST /api/... │
 │ snapshot │  │ schedule      │
 │ + deltas │  │ session list  │
 │ replay   │  │ track geom    │
 │ streams  │  │ results       │
 └────┬─────┘  └──────┬────────┘
      │               │
      └───────┬───────┘
              │
      ┌───────▼────────┐
      │   dashboard    │  Next.js 15, :3000
      │                │  - WS client → Zustand store
      │                │  - delay buffer (client-side)
      │                │  - SVG track map, uPlot telemetry
      └────────────────┘

      ┌────────────────┐
      │   simulator    │  Dev only, :8000. Replays a recorded
      │                │  session over WS in the exact wire
      │                │  format the ingest service produces.
      └────────────────┘

      ┌────────────────┐
      │ archive store  │  Filesystem or S3/R2.
      │                │  raw/{session_key}/*.jsonl
      │                │  bundles/{year}/{round}/{type}/*.json
      └────────────────┘
```

## Why Redis sits in the middle

- The ingest process must be a **singleton** — two SignalR connections would double-count deltas and F1 may rate-limit.
- The `realtime` service must be **replicable** — hundreds of browser WS connections.
- Redis Streams give us an ordered, ID'd, replayable delta log for free. A client that reconnects can resume from the last stream ID it saw instead of re-downloading a full snapshot.

## Process responsibilities — precise boundaries

### `ingest`
- Owns the single SignalR connection.
- Owns the authoritative accumulated state.
- Emits **normalised deltas** (already merged into canonical field names, `.z` topics already decompressed).
- Writes the raw, untouched wire messages to the archive store for later replay and for regression fixtures.
- Detects session start/end from `SessionStatus` and rotates the archive file accordingly.
- **Does not** speak HTTP to browsers.

### `realtime`
- Accepts browser WS connections at `GET /ws`.
- On connect: reads `f1:state`, sends `{"type":"snapshot","data":{...},"streamId":"<last-id>"}`.
- Then: `XREAD BLOCK` on `f1:deltas` from that ID, forwarding each as `{"type":"delta","data":{...},"ts":<unix-ms>}`.
- Also serves replay streams at `GET /ws/replay/{year}/{round}/{session}` — same message shapes, driven by the replay engine instead of Redis.
- Handles backpressure: if a client's send queue exceeds N frames, drop the client and let it reconnect with a fresh snapshot.

### `api`
- Stateless. Everything cacheable.
- Season schedule, session list, session results, driver list, track geometry, precomputed replay bundle metadata.
- Triggers on-demand archive processing when a user requests a session that hasn't been precomputed.

### `dashboard`
- Never talks to any F1 endpoint directly.
- Holds the delay buffer. See `08-delay-and-replay.md`.

## Repository layout

```
f1-web/
├── docs/                      ← these spec files, copied in
├── DECISIONS.md               ← agent logs any deviation from spec here
├── compose.yaml
├── .env.example
│
├── packages/
│   ├── ingest/
│   │   ├── pyproject.toml
│   │   └── src/ingest/
│   │       ├── __main__.py
│   │       ├── signalr/
│   │       │   ├── client.py        SignalR negotiate + WS + handshake
│   │       │   ├── protocol.py      message framing, ping/pong
│   │       │   └── decode.py        base64+zlib for .z topics
│   │       ├── topics.py            topic enum + per-topic parsers
│   │       ├── state.py             accumulator + merge algorithm
│   │       ├── normalise.py         wire shape → canonical model
│   │       ├── publisher.py         Redis writes
│   │       ├── recorder.py          raw archive writer
│   │       └── config.py
│   │
│   ├── api/
│   │   ├── pyproject.toml
│   │   └── src/api/
│   │       ├── main.py
│   │       ├── routers/  schedule.py sessions.py track.py results.py replay.py
│   │       ├── services/ archive.py fastf1_loader.py multiviewer.py jolpica.py
│   │       └── cache.py
│   │
│   ├── realtime/
│   │   ├── pyproject.toml
│   │   └── src/realtime/
│   │       ├── main.py
│   │       ├── ws_live.py
│   │       ├── ws_replay.py
│   │       ├── replay_engine.py
│   │       └── connection.py        per-client queue + backpressure
│   │
│   ├── simulator/
│   │   └── src/simulator/main.py
│   │
│   ├── shared/                      installed as editable dep by the others
│   │   └── src/f1shared/
│   │       ├── models.py            Pydantic models — SINGLE SOURCE OF TRUTH
│   │       ├── merge.py             the delta merge algorithm (shared, tested)
│   │       └── constants.py         topic names, team colours, tyre colours
│   │
│   └── dashboard/                   Next.js
│       ├── package.json
│       └── src/
│           ├── app/
│           ├── components/
│           ├── stores/
│           ├── lib/
│           └── types/               generated from shared/models.py
│
└── scripts/
    ├── record_session.py            capture a live session to fixtures
    ├── download_archive.py          pull an archived session from F1 static
    └── gen_types.py                 Pydantic → TypeScript
```

## Type sharing

`packages/shared/src/f1shared/models.py` is the single source of truth. `scripts/gen_types.py` uses `pydantic2ts` (or `datamodel-code-generator` in reverse) to emit `packages/dashboard/src/types/generated.ts`. This runs in CI and the build fails if the committed output is stale.
