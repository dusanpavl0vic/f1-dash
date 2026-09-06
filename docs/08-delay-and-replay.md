# 08 — Broadcast Delay & Replay

Two distinct features that share machinery. Read both before implementing either.

---

# Part A — Broadcast delay

## The problem

The F1 data feed is **ahead of every TV broadcast** — typically by 3 to 40 seconds depending on the provider and region. A viewer watching TV alongside the dashboard sees an overtake on the dashboard before it happens on screen. f1-dash solved this with a user-configurable delay slider; it is one of the most-used features and must be in v1.

## The decision: buffer on the client

| | Server-side | Client-side ✅ |
|---|---|---|
| Per-user delay values | needs one buffer per user | free |
| Server memory | O(users × delay) | O(1) |
| Changing delay | requires server round-trip | instant |
| Cost of a 120 s buffer | — | ~2–6 MB in the tab |

Buffer in the browser. The server always sends live.

## Implementation

```typescript
interface BufferedFrame {
  ts: number;                        // server observation time, unix ms
  kind: "snapshot" | "delta";
  data: unknown;
}

class DelayBuffer {
  private queue: BufferedFrame[] = [];
  private delayMs = 0;

  push(frame: BufferedFrame) {
    this.queue.push(frame);
  }

  /** Called every animation frame. Returns frames now due for display. */
  drain(now: number): BufferedFrame[] {
    const cutoff = now - this.delayMs;
    const due: BufferedFrame[] = [];
    while (this.queue.length && this.queue[0].ts <= cutoff) {
      due.push(this.queue.shift()!);
    }
    return due;
  }

  setDelay(ms: number) { /* see below */ }
}
```

### Clock skew

`frame.ts` is the **server's** clock; `now` is the **browser's**. These can differ by seconds. Correct for it:

1. On connect, the server includes `serverTime` in the snapshot message.
2. Client computes `skew = serverTime - Date.now()` at that moment.
3. All comparisons use `now + skew`.
4. Re-estimate skew every 30 s via a `ping`/`pong` round-trip, taking the minimum-RTT sample (like NTP) to reduce jitter.

### Changing the delay while running

- **Increasing** the delay: trivial — frames simply sit in the queue longer. The display appears to freeze for `Δdelay` and then resumes.
- **Decreasing** the delay: frames become due immediately. Do **not** dump 30 s of deltas into React in one tick. Drain them at a capped rate (e.g. 200 frames per animation frame) until caught up, with the UI showing a "catching up" indicator.
- **Setting delay to 0 from a high value**: fastest path is to discard the buffer, request a fresh snapshot (`{"type":"resync"}` to the server), and reset the store. This avoids a multi-thousand-delta catch-up.

### Buffer sizing

```
MAX_DELAY_MS   = 120_000
MAX_FRAMES     = 20_000        // hard cap; drop oldest and warn if exceeded
```

If the buffer exceeds `MAX_FRAMES`, the user's delay is larger than the retained window — clamp the delay, drop the oldest frames, and surface a warning.

### UI

- Slider 0–120 s, plus quick presets (0 / 5 / 15 / 30 / 60).
- Persist to `localStorage` under `f1:delay`.
- Show the active delay as a persistent badge in the header. **A user who forgets they set a delay and thinks the app is broken is the #1 support issue** — make it impossible to miss.
- Show buffer health: `queued frames` and `oldest frame age` in a debug panel.

---

# Part B — Replay

## What replay must support

- Load any session from 2018 onward.
- Play / pause / seek to arbitrary time / speed 0.25x–8x.
- Jump to lap N, or to a race control event (SC deployment, red flag).
- Everything the live dashboard shows, shown identically.

## Two replay modes

### Mode 1 — Raw stream replay (preferred, exact)

Replays the archived `.jsonStream` files through the same pipeline the live feed uses. Byte-for-byte the same rendering as live.

**Ingestion pipeline** (`scripts/download_archive.py`):

```
1. Resolve session path
   GET /static/{year}/Index.json  → find meeting + session, get its path
2. GET /static/{path}/Index.json  → list of Feeds
3. For each topic we care about:
     GET /static/{path}/{StreamPath}
     decode with utf-8-sig
     each line: ts = line[:12], payload = json.loads(line[12:])
     for .z topics: payload is a base64 string → inflate
4. Merge all topics into one time-ordered list of (offset_ms, topic, payload)
5. Write to archive: bundles/{year}/{round}/{type}/stream.jsonl
   each line: {"o": <offset_ms>, "t": "<topic>", "p": {...}}
```

`offset_ms` comes from parsing `HH:MM:SS.mmm` at the head of each stream line, relative to session start.

**Replay engine** (`packages/realtime/src/realtime/replay_engine.py`):

```python
class ReplayEngine:
    def __init__(self, stream: list[StreamEntry]):
        self.stream = stream            # sorted by offset
        self.cursor = 0
        self.position_ms = 0
        self.speed = 1.0
        self.playing = False
        self.state = StateAccumulator()

    async def run(self, send):
        last_wall = time.monotonic()
        while True:
            await asyncio.sleep(1 / 30)         # 30 Hz tick
            now = time.monotonic()
            if not self.playing:
                last_wall = now
                continue
            self.position_ms += (now - last_wall) * 1000 * self.speed
            last_wall = now

            batch = []
            while (self.cursor < len(self.stream)
                   and self.stream[self.cursor].offset <= self.position_ms):
                e = self.stream[self.cursor]
                delta = self.state.apply(e.topic, e.payload)
                if delta:
                    batch.append(delta)
                self.cursor += 1

            if batch:
                await send({"type": "delta", "data": coalesce(batch),
                            "ts": int(self.position_ms)})

    async def seek(self, target_ms: int, send):
        if target_ms < self.position_ms:
            self.state = StateAccumulator()      # rebuild from zero
            self.cursor = 0
        while self.cursor < len(self.stream) and self.stream[self.cursor].offset <= target_ms:
            e = self.stream[self.cursor]
            self.state.apply(e.topic, e.payload)
            self.cursor += 1
        self.position_ms = target_ms
        await send({"type": "snapshot", "data": self.state.snapshot(),
                    "ts": target_ms, "reason": "seek"})
```

**Seek is a full state rebuild**, because the state is delta-accumulated — you cannot jump backwards without replaying from a known point.

**Optimisation — keyframes.** Precompute a full state snapshot every 60 s of session time and store them alongside the stream:

```
bundles/{year}/{round}/{type}/keyframes/{offset_ms}.json
```

Seek then = load the nearest preceding keyframe + replay at most 60 s of deltas. Backward seek drops from ~2 s to under 100 ms on a 2-hour race. Build this in Phase 6, not Phase 1 — get correctness first.

**One engine per client.** Two users scrubbing the same race independently need separate engines. This means memory scales with concurrent replay viewers: cap it (`MAX_CONCURRENT_REPLAYS`, default 50) and reject beyond that with a clear message.

### Mode 2 — Precomputed frame bundle (fallback)

For sessions where raw archives are missing or malformed, precompute fixed-interval frames with FastF1:

```python
session.load(telemetry=True, laps=True, weather=True, messages=True)
# sample every 500 ms:
#   per driver: X, Y, position, gap, interval, compound, tyre life, pit status
#   plus: track status, lap count, race control messages, weather
```

Write to `bundles/{year}/{round}/{type}/replay.json`. Playback is then trivial (index by time), but the fidelity is lower — no mini-sectors, no exact timing-tower behaviour. Use as fallback only.

## Storage layout

```
archive/
  raw/{session_key}/{date}.jsonl              ← live capture, untouched wire frames
  bundles/{year}/{round}/{type}/
      meta.json          session info, driver list, duration_ms, lap markers
      stream.jsonl       time-ordered merged deltas
      keyframes/{offset_ms}.json
      track.json         MultiViewer geometry, cached
      laps.json          FastF1 lap table (for the lap-jump control)
      results.json
```

## Replay control protocol

Client → server on the replay WS:

```json
{"type":"replay-control", "action":"play"}
{"type":"replay-control", "action":"pause"}
{"type":"replay-control", "action":"seek",  "value": 1834000}
{"type":"replay-control", "action":"speed", "value": 2.0}
{"type":"replay-control", "action":"seek-lap", "value": 34}
```

Server → client, on connect:

```json
{"type":"replay-meta", "data":{
  "duration_ms": 5_412_000,
  "session": {...},
  "laps": [{"lap":1,"offset_ms":92000}, ...],
  "events": [{"offset_ms":1834000,"label":"Safety Car deployed","kind":"sc"}, ...]
}}
```

`events` drives the markers on the scrub bar. Derive it from `RaceControlMessages` + `TrackStatus` transitions during bundle precompute.

## Precompute triggering

Three paths (mirroring what similar projects do):

1. **On demand** — user opens an unprocessed session; `api` kicks off a background job and streams progress over SSE. Show a progress UI, not a spinner; cold processing takes 30–120 s.
2. **CLI bulk** — `python -m api.precompute --year 2024 --all`.
3. **Scheduled** — background task every 30 min on Fri–Mon, checks the schedule for sessions that ended and processes them automatically.

## Acceptance criteria

**Delay:**
- [ ] Setting delay to 30 s visibly holds the timing tower 30 s behind the live feed.
- [ ] Reducing delay from 60 s to 0 s does not freeze the tab for more than 500 ms.
- [ ] Clock skew of ±5 s (simulate by offsetting the client clock) does not change observed delay by more than 200 ms.
- [ ] Delay persists across reloads.

**Replay:**
- [ ] A 2024 race replays from t=0 to the chequered flag with a timing tower matching the official classification at the end.
- [ ] Seeking backwards to lap 10 and forwards to lap 50 both settle within 2 s (before keyframes) / 200 ms (after).
- [ ] Speed 4x plays without dropping deltas.
- [ ] Pausing freezes all values, including the track map.
