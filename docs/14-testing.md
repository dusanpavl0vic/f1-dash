# 14 — Testing & The Simulator

## Why this document comes before most of the code

F1 sessions run on roughly 24 weekends a year. If the app is only testable during a live session, development is impossible. **The simulator is infrastructure, not a nice-to-have.** Build it in Phase 3, before ingest.

f1-dash made the same call — it shipped a `simulator/` package and a `F1_DEV_URL` env var on the realtime service specifically so the stack runs off recorded data.

---

## Fixtures

### Getting them

```bash
python scripts/download_archive.py --year 2024 --round 1  --session R
python scripts/download_archive.py --year 2024 --round 8  --session Q
python scripts/download_archive.py --year 2024 --round 6  --session R   # Monaco: SC + red flag
```

Pick sessions deliberately:

| Fixture | Why |
|---|---|
| A normal dry race | baseline |
| A qualifying session | different session semantics (segments, elimination) |
| A race with SC + VSC + red flag | flag handling, restarts |
| A wet race | intermediates/wets, changing conditions |
| A sprint weekend | the `S` / `SQ` session types |

### Committing them

Full sessions are 50–300 MB — do not commit those. Commit **trimmed** 5-minute slices under `packages/shared/fixtures/`, and keep full sessions in gitignored `data/`. Provide `make fixtures` to download the full set.

---

## The simulator

`packages/simulator/src/simulator/main.py`

```
GET /ws?speed=1.0&start=0&loop=false&session=2024_1_R
```

Requirements:

1. Emits frames in the **exact wire format the live F1 SignalR feed uses** — the `{"M":[{"H":"Streaming","M":"feed","A":[topic, payload, timestamp]}]}` envelope. The ingest service must not be able to tell the difference.
2. Sends an initial `{"R": {...}}` full-state message first, exactly like the live feed does.
3. Honours original inter-frame timing, scaled by `speed`.
4. `start` skips ahead (offset in ms) — essential for testing a specific incident without waiting an hour.
5. `loop=true` restarts from the beginning — for long-running soak tests.
6. Re-compresses `Position` and `CarData` back to base64+deflate, so the ingest decompression path is genuinely exercised.

Point 6 matters: if the simulator sends already-decoded position data, the `.z` handling never runs in dev and breaks silently on race day.

---

## Test layers

### 1. Unit — the merge algorithm

Highest value tests in the project. `packages/shared/tests/test_merge.py` plus the mirrored `merge.test.ts`.

Shared fixture format, `packages/shared/fixtures/merge/*.json`:

```json
{
  "name": "sparse sector patch",
  "initial": { ... },
  "patch":   { ... },
  "expected":{ ... }
}
```

Both pytest and vitest load the same files. **If the two implementations diverge, the build fails.** This is the guard against the whole class of "the browser shows something different from the server" bugs.

### 2. Integration — full session replay

```python
def test_full_race_produces_correct_classification():
    stream = load_fixture("2024_1_R/stream.jsonl")
    state = StateAccumulator()
    for entry in stream:
        state.apply(entry.topic, entry.payload)
    top10 = extract_classification(state.snapshot())[:10]
    assert top10 == EXPECTED_2024_BAHRAIN_TOP10
```

This single test validates parsing, decompression, merging, and the state model in one shot. **Write it in Phase 2 and never let it break.**

Add variants:
- Final lap count matches the official lap count.
- Every driver's total pit stops matches the official record.
- The safety car in the Monaco fixture appears in race control with the right lap number.

### 3. Contract — API responses

Snapshot-test every REST endpoint against a recorded upstream response. Use `respx` to stub MultiViewer and Jolpica so tests never hit the network.

### 4. E2E — Playwright

Against the simulator:

- Dashboard loads and the timing tower populates within 5 s.
- Position changes reorder rows.
- The track map renders 20 dots.
- Setting a 30 s delay visibly holds the tower back.
- Reconnect after a forced socket close.
- Replay: play, seek to lap 30, pause.

### 5. Load

```bash
# 500 concurrent WS clients against realtime, fed by the simulator
python scripts/loadtest.py --clients 500 --duration 300
```

Assert: no client dropped for backpressure, p99 delta latency under 200 ms, realtime process RSS under 1 GB.

### 6. Soak

Run the simulator with `loop=true` for 24 h against the full stack. Assert: no memory growth beyond 10%, no unhandled exceptions, no Redis stream unbounded growth.

---

## Live-session validation checklist

To run during Phase 15, on an actual session:

- [ ] Ingest connects to the live SignalR endpoint and stays connected for the full session.
- [ ] Timing tower positions match the official F1 live timing page at 10 random moments.
- [ ] Gaps match to within 0.1 s.
- [ ] Sector colours (purple/green/yellow) match.
- [ ] Tyre compounds and ages match.
- [ ] The track map shows cars in plausible positions vs the TV feed.
- [ ] A safety car or VSC period is reflected within 2 s.
- [ ] The full session is recorded to raw archive without gaps.
- [ ] Every unrecognised field/topic/status logged during the session is reviewed afterwards.

## Known edge cases to test explicitly

These are the ones that bite in production:

| Case | What breaks |
|---|---|
| Red flag → session restart | lap counter, tyre state, driver ordering |
| Driver retires mid-lap | ghost dot stuck on the map |
| A driver number appears in `Position` before `DriverList` | undefined lookup crash |
| Sprint weekend | session type strings the schedule parser doesn't know |
| Session with fewer than 20 cars (testing, FP1 rookies) | hardcoded array sizes |
| Feed dropout for 30+ seconds | delay buffer starvation, stale positions |
| Qualifying segment transitions (Q1→Q2→Q3) | timing tower reset behaviour |
| Two drivers with identical gaps | unstable sort, rows flickering |
| Lapped traffic (`GapToLeader` = `"1 L"`) | gap parsing assuming a number |
| Mid-session driver replacement | `DriverList` update mid-stream |
