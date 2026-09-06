# 07 — Realtime Service & WebSocket Fanout

Target: `packages/realtime/`. FastAPI, port 4000.

## Endpoints

| Path | Purpose |
|---|---|
| `GET /ws` | Live session stream |
| `GET /ws/replay/{year}/{round}/{session}` | Replay stream with controls |
| `GET /health` | Liveness — returns `{"ok": true, "live": bool, "clients": int}` |

## Live WebSocket flow

```
client connects
    ↓
server: GET f1:state + XINFO STREAM f1:deltas (last-generated-id)
    ↓
server → client: {"type":"snapshot","data":{...},"streamId":"1718…-0","ts":…}
    ↓
loop: XREAD BLOCK 1000 STREAMS f1:deltas <last-id>
    ↓
server → client: {"type":"delta","data":{...},"streamId":"…","ts":…}
```

The snapshot's `streamId` must be captured **before or atomically with** reading `f1:state`, otherwise a delta can slip between the two and be lost. Order the reads: read the stream's last ID first, then read the state — a duplicated delta is harmless (merges are idempotent for the same value), a missing one is not.

```python
async def snapshot_and_cursor(redis):
    last_id = (await redis.xinfo_stream("f1:deltas"))["last-generated-id"]
    state = await redis.get("f1:state")
    return orjson.loads(state or b"{}"), last_id
```

## Connection handling and backpressure

Each client gets a bounded `asyncio.Queue`. A writer task drains it.

```python
MAX_QUEUE = 500        # ~2 minutes of deltas at typical rate

class Connection:
    def __init__(self, ws: WebSocket):
        self.ws = ws
        self.queue: asyncio.Queue[bytes] = asyncio.Queue(maxsize=MAX_QUEUE)

    def offer(self, payload: bytes) -> bool:
        try:
            self.queue.put_nowait(payload)
            return True
        except asyncio.QueueFull:
            return False        # caller closes the connection
```

**A slow client is dropped, not accommodated.** It reconnects and gets a fresh snapshot. Never let one stalled browser tab back up the shared reader.

## Fanout

Do **not** open one Redis `XREAD` per client. One reader task per process broadcasts to all connections:

```python
class Broadcaster:
    def __init__(self):
        self.connections: set[Connection] = set()
        self.latest_id = "$"

    async def run(self, redis):
        while True:
            resp = await redis.xread({"f1:deltas": self.latest_id}, block=1000, count=100)
            if not resp:
                continue
            for _, entries in resp:
                for entry_id, fields in entries:
                    self.latest_id = entry_id
                    payload = build_delta_message(entry_id, fields)
                    for conn in list(self.connections):
                        if not conn.offer(payload):
                            self.connections.discard(conn)
                            await conn.close(code=1013)   # try again later
```

Serialise the outgoing message **once** and send the same bytes to every client.

## Reconnect resumption

If a client reconnects with `?since=<streamId>` and that ID is still within the stream's retained window, skip the snapshot and resume from that ID:

```
GET /ws?since=1718234567890-3
→ {"type":"delta", …}  {"type":"delta", …}  …
```

If the ID has fallen out of the window (XREAD returns nothing and the requested ID is older than the stream's first entry), fall back to a full snapshot. Always tell the client which happened, so it knows whether to reset its store:

```json
{"type":"snapshot", "reason":"cursor-expired", …}
```

## Client-side state application

The browser mirrors the server's merge algorithm exactly. Port `merge.py` to `packages/dashboard/src/lib/merge.ts` — **same rules, same tests**. Divergence between the two merge implementations is the most likely source of subtle display bugs; keep the test fixtures shared (JSON files under `packages/shared/fixtures/merge/`, loaded by both the pytest and vitest suites).

## Zustand store shape

```typescript
interface DataStore {
  state: F1State | null;
  connected: boolean;
  lastStreamId: string | null;

  applySnapshot(data: F1State, streamId: string): void;
  applyDelta(data: DeepPartial<F1State>, streamId: string): void;
  reset(): void;
}
```

### Performance requirement

Deltas arrive at up to ~10/s and touch a deeply nested object. Naive `set({state: {...}})` on every delta re-renders the whole tree and will drop frames on mid-range hardware.

Required approach:
- Mutate a module-level state object outside React; use `useSyncExternalStore` (or Zustand with `subscribeWithSelector`) so components subscribe to **narrow slices**.
- Timing tower rows subscribe to `state.timing_data.Lines[driverNumber]` only.
- Track map subscribes to `state.position` only.
- Coalesce: apply incoming deltas into a pending buffer and flush to React on `requestAnimationFrame`, not per message. At 4–10 deltas/s this alone cuts re-renders by ~3x during bursts.

Acceptance: with a recorded race playing at 1x, Chrome DevTools Performance shows **no long tasks over 50 ms** and a stable 60 fps on the dashboard page.

## CORS and origin

```
ORIGIN=http://localhost:3000        # comma-separated list in prod
```

Reject WS upgrades whose `Origin` header is not in the list. This is the only access control in v1.

## Acceptance criteria

- [ ] 100 simultaneous WS clients on one process, fed by a recorded race at 1x, with CPU under 50% of one core.
- [ ] Killing Redis mid-stream: clients receive an `error` message, then reconnect cleanly once Redis returns.
- [ ] A client that stops reading is dropped within 30 s and does not affect other clients.
- [ ] `?since=` resumption skips the snapshot when the cursor is valid.
- [ ] `merge.ts` and `merge.py` produce identical output for every fixture in `packages/shared/fixtures/merge/`.
