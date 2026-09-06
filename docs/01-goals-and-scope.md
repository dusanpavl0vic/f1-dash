# 01 — Goals and Scope

## Primary goals

1. **Sub-second live timing.** From the moment the F1 SignalR feed emits a delta, the browser should render it in under ~300 ms (excluding any user-configured delay).
2. **Works offline / out of season.** The entire app must be developable and demoable from recorded session data, without a live race.
3. **Full replay.** Any archived session from 2018 onward can be replayed with scrub, pause, and variable speed.
4. **Self-hostable.** One `docker compose up` gets a working stack.

## Non-goals (explicitly out of scope for v1)

- Video streaming or F1 TV integration. We show data only.
- User accounts, auth, persistence of user preferences server-side. Preferences live in `localStorage`.
- Mobile native apps.
- Betting, predictions, or ML models.
- Team radio audio playback (v2 candidate — the feed exposes it, see `03-data-sources.md`).

## Constraints that shape the design

### The feed is delta-based

The F1 live timing feed does **not** send full snapshots. After an initial state dump, every message is a partial patch — often deeply nested, often containing only one changed field. Example: a driver's sector-2 time arrives as

```json
{"Lines": {"44": {"Sectors": {"1": {"Value": "28.412"}}}}}
```

This means:
- The server must maintain authoritative accumulated state.
- New clients need a **full snapshot on connect**, then deltas.
- The delay feature is only possible because deltas are timestamped and replayable in order.

### Arrays arrive as objects

The feed frequently sends what is logically an array as an object keyed by stringified index:

```json
{"Sectors": {"0": {...}, "2": {...}}}
```

means "sector 0 and sector 2 changed; sector 1 unchanged". A naive merge that treats this as an object replacement will corrupt state. See the merge algorithm in `07-state-and-websocket.md`.

### F1 blocks datacentre IPs

Formula 1 has applied IP blocking against known cloud provider ranges. Plan for:
- A configurable outbound proxy (`F1_HTTP_PROXY` env var) on the ingest service.
- Realistic browser-like headers on the negotiate call (documented in `04-signalr-protocol.md`).
- Graceful degradation to replay-only mode if live ingest is unavailable.

### Session cadence

`Position.z` (GPS) arrives at roughly **4 Hz**; `CarData.z` (telemetry) at roughly **4 Hz** as well but batched. `TimingData` is bursty. Total sustained throughput during a race is on the order of 50–200 KB/s decompressed. This is fine for one ingest process; it is not fine to re-broadcast raw to every client without diffing.

## Legal notice (must appear in the app footer and README)

> This project is unofficial and is not associated in any way with the Formula 1 companies. F1, FORMULA ONE, FORMULA 1, FIA FORMULA ONE WORLD CHAMPIONSHIP, GRAND PRIX and related marks are trade marks of Formula One Licensing B.V.

Licence recommendation: **AGPL-3.0** (matches f1-dash, keeps derivative hosted forks open).
