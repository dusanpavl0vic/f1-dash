# 06 — Ingest Service

Target: `packages/ingest/`. A single long-running process. No HTTP server.

## Responsibilities

1. Hold the one SignalR connection (see `04-signalr-protocol.md`).
2. Decode, normalise, and merge into authoritative state.
3. Publish snapshot + delta to Redis.
4. Record raw frames to the archive store.
5. Detect session boundaries and rotate recordings.

## Main loop

```python
async def main() -> None:
    cfg = Config.from_env()
    redis = await make_redis(cfg)
    recorder = Recorder(cfg.archive)
    state = StateAccumulator()
    publisher = Publisher(redis)

    source = SimulatorSource(cfg.f1_dev_url) if cfg.f1_dev_url else LiveSource(cfg)

    async for frame in source.frames():          # yields RawFrame(ts, text)
        await recorder.write(frame)
        for topic, payload, feed_ts in parse_frame(frame):
            delta = state.apply(topic, payload)
            if delta:
                await publisher.publish(delta, state.snapshot(), frame.ts)
```

`F1_DEV_URL` mirrors f1-dash's env var of the same name: when set, ingest reads from the simulator instead of the live feed. This must work from day one so the whole stack is developable out of season.

## `parse_frame`

```python
def parse_frame(frame: RawFrame) -> Iterator[tuple[str, dict, str | None]]:
    msg = json.loads(frame.text)

    if not msg:                       # {} keep-alive
        return

    if "R" in msg:                    # initial full state
        for topic, payload in msg["R"].items():
            yield normalise_topic(topic, payload) + (None,)
        return

    for entry in msg.get("M", []):    # deltas — ALWAYS iterate
        if entry.get("M") != "feed":
            continue
        topic, payload, ts = entry["A"][0], entry["A"][1], entry["A"][2]
        t, p = normalise_topic(topic, payload)
        yield t, p, ts


def normalise_topic(topic: str, payload) -> tuple[str, dict]:
    if topic.endswith(".z"):
        return topic[:-2], inflate(payload)
    return topic, payload
```

## State accumulator and the merge algorithm

This is the single most important piece of code in the project. Put it in `packages/shared/src/f1shared/merge.py` and unit-test it heavily.

### The rules

**Rule 1 — recursive merge, never replace.** A delta patches a nested subtree; keys absent from the delta keep their existing value.

**Rule 2 — numeric-string keys are array indices.** When an object's keys are all numeric strings (`"0"`, `"1"`, `"12"`), it represents a sparse array patch. Merge each key into the corresponding existing entry; do not drop unmentioned indices.

**Rule 3 — `_kf` and `_deleted` are control keys.** Strip `_kf`. If `_deleted` is present (a list of keys), remove those keys from the target.

**Rule 4 — lists are replaced wholesale.** Genuine JSON arrays (`RaceControlMessages.Messages` sometimes arrives as one) replace the target array. Detect and normalise arrays into index-keyed objects on ingest so downstream code only ever sees one shape.

**Rule 5 — scalars replace.**

```python
def merge(target: dict, patch: dict) -> dict:
    """Mutates and returns target. Returns the effective delta separately if needed."""
    for key, value in patch.items():
        if key == "_kf":
            continue
        if key == "_deleted":
            for dead in value:
                target.pop(dead, None)
            continue
        if isinstance(value, dict) and isinstance(target.get(key), dict):
            merge(target[key], value)
        elif isinstance(value, list) and isinstance(target.get(key), dict):
            # array arriving where we hold an index-keyed object
            for i, item in enumerate(value):
                if isinstance(item, dict) and isinstance(target[key].get(str(i)), dict):
                    merge(target[key][str(i)], item)
                else:
                    target[key][str(i)] = item
        else:
            target[key] = value
    return target
```

### Special-cased topics

| Topic | Handling |
|---|---|
| `Position` | **Replace, do not merge.** Only the newest frame matters. Overwrite `state.position` entirely. |
| `CarData` | Same — replace. |
| `RaceControlMessages` | Append-only. New messages get the next integer key. Never merge over an existing message. |
| `TeamRadio` | Append-only, same as above. |
| `SessionInfo` | Replace wholesale — it changes only between sessions. |
| everything else | Recursive merge per the rules above. |

### Test cases that must pass

```python
def test_sparse_sector_patch():
    state = {"Lines": {"44": {"Sectors": {"0": {"Value": "28.1"},
                                          "1": {"Value": "31.2"},
                                          "2": {"Value": "24.0"}}}}}
    merge(state, {"Lines": {"44": {"Sectors": {"1": {"Value": "30.9"}}}}})
    assert state["Lines"]["44"]["Sectors"]["0"]["Value"] == "28.1"   # untouched
    assert state["Lines"]["44"]["Sectors"]["1"]["Value"] == "30.9"   # updated
    assert state["Lines"]["44"]["Sectors"]["2"]["Value"] == "24.0"   # untouched

def test_single_driver_patch_does_not_wipe_others():
    state = {"Lines": {"1": {"Position": "1"}, "44": {"Position": "2"}}}
    merge(state, {"Lines": {"44": {"Position": "1"}}})
    assert state["Lines"]["1"]["Position"] == "1"

def test_kf_marker_stripped():
    state = {}
    merge(state, {"Status": "1", "_kf": True})
    assert "_kf" not in state

def test_deleted_key_removed():
    state = {"Lines": {"1": {}, "44": {}}}
    merge(state, {"Lines": {"_deleted": ["1"]}})
    assert "1" not in state["Lines"]
```

## Publisher

```python
class Publisher:
    async def publish(self, delta: dict, snapshot: dict, ts_ms: int) -> None:
        pipe = self.redis.pipeline()
        pipe.set("f1:state", orjson.dumps(snapshot))
        pipe.xadd("f1:deltas",
                  {"d": orjson.dumps(delta), "ts": ts_ms},
                  maxlen=50_000, approximate=True)
        pipe.publish("f1:notify", "1")
        await pipe.execute()
```

Notes:
- `maxlen=50_000` at ~4 Hz on multiple topics covers roughly the last 30–60 minutes — enough for the 120 s delay buffer plus a large reconnect window, without unbounded growth.
- `f1:state` is overwritten every time. That is fine; it is the join point for new clients.
- Use `orjson`, not `json` — this is the hot path.
- Batch: if more than one delta is produced within a single frame, coalesce them into one XADD.

## Session detection and rotation

Watch `SessionStatus.Status`:

| Value | Action |
|---|---|
| `Inactive` | idle; keep connection, no recording |
| `Started` | begin a new recording; `SET f1:session_active 1` |
| `Aborted` | red flag — keep recording |
| `Finished` | session over; keep recording (finalisation deltas still arrive) |
| `Finalised` | flush and close recording; `DEL f1:session_active` |
| `Ends` | as Finalised |

Derive `session_key` from `SessionInfo`: `{year}_{round}_{Type}` — used as the archive folder name.

Also: when `SessionInfo.Key` changes, force-rotate. That is the definitive "new session" signal.

## Config (`packages/ingest/src/ingest/config.py`)

```
REDIS_URL=redis://localhost:6379/0
F1_DEV_URL=                        # if set, use the simulator instead of live
F1_HTTP_PROXY=                     # optional outbound proxy for the F1 endpoints
ARCHIVE_BACKEND=fs                 # fs | s3
ARCHIVE_PATH=./data/archive
S3_ENDPOINT= S3_BUCKET= S3_ACCESS_KEY_ID= S3_SECRET_ACCESS_KEY=
LOG_LEVEL=INFO
KEEPALIVE_TIMEOUT=30
```

## Acceptance criteria

- [ ] Runs against the simulator with `F1_DEV_URL` set, no live network needed.
- [ ] `f1:state` in Redis contains a `driver_list` with 20 entries after processing a recorded race fixture.
- [ ] All merge unit tests pass.
- [ ] Processing a 2-hour recorded race completes without an unhandled exception and produces monotonically increasing stream IDs.
- [ ] Raw recording is byte-identical to the input when replayed through the simulator (round-trip test).
- [ ] Memory usage stays flat over a full race — no unbounded accumulation of Position/CarData history.
