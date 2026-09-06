# 12 — Build Order

**This is the work plan.** Execute phases in order. Do not start a phase until the previous phase's acceptance checks pass. Each phase should end in a commit.

---

## Phase 0 — Scaffold (half a day)

1. Monorepo skeleton exactly as laid out in `02-architecture.md`.
2. `uv` workspace for the Python packages; `pnpm` for the dashboard.
3. `packages/shared` with empty `models.py`, `merge.py`, `constants.py`; installed editable by `ingest`, `api`, `realtime`.
4. `compose.yaml` with Redis only.
5. Ruff + mypy (strict) for Python; ESLint + `tsc --noEmit` for TS. Wire into a `make check` target.
6. `DECISIONS.md` at repo root.

✅ `make check` passes on an empty repo. `docker compose up` starts Redis.

---

## Phase 1 — Get real data on disk ⭐ **do this first**

Nothing else can be verified without a fixture. This phase is the foundation.

1. `scripts/download_archive.py` — implement the static-archive download from `03-data-sources.md` §2.
2. Download two full sessions: one race and one qualifying, from 2024.
3. Parse `.jsonStream` (12-char timestamp + JSON, `utf-8-sig`), inflate `.z` topics.
4. Merge all topics into a single time-ordered `stream.jsonl`.
5. Commit a **trimmed** fixture (first 5 minutes) to `packages/shared/fixtures/` for tests; keep the full files gitignored under `data/`.

✅ `stream.jsonl` for a 2024 race exists, has >100k lines, is time-ordered, and `Position` entries inflate to valid JSON with 20 drivers.

---

## Phase 2 — Merge algorithm

1. Implement `packages/shared/src/f1shared/merge.py` per `06-ingest-service.md`.
2. Write the four unit tests listed there, plus fixture-driven tests.
3. Implement `StateAccumulator` with the per-topic special cases.
4. Write a throwaway script that replays `stream.jsonl` through the accumulator and prints the final classification.

✅ The printed final top 10 for the 2024 race fixture matches the official result exactly. This one check validates the entire parsing and merge layer.

---

## Phase 3 — Simulator

1. `packages/simulator` — FastAPI, serves `GET /ws` at port 8000.
2. Reads `stream.jsonl`, emits frames with original relative timing.
3. Query params: `?speed=1.0&start=0&loop=true`.
4. Emits frames in the **exact wire format the live F1 feed uses** (wrap in the `{"M":[{"H":"Streaming","M":"feed","A":[topic, payload, ts]}]}` envelope), so ingest cannot tell the difference.

✅ `websocat ws://localhost:8000/ws` prints frames at a realistic rate, in live-feed shape.

---

## Phase 4 — Ingest service

1. Redis publisher, recorder, config.
2. `SimulatorSource` first. Full ingest loop working against Phase 3.
3. Only then `LiveSource` — the SignalR client from `04-signalr-protocol.md`.
4. Session detection and rotation.

✅ With the simulator running, `redis-cli GET f1:state | jq '.driver_list | length'` returns 20, and `XLEN f1:deltas` grows steadily.

---

## Phase 5 — Realtime service

1. `GET /ws` — snapshot + delta fanout per `07-state-and-websocket.md`.
2. Broadcaster with one Redis reader, per-client bounded queues, backpressure drops.
3. `?since=` resumption.
4. `/health`.

✅ Two `websocat` clients both receive a snapshot then identical delta streams. Killing one does not affect the other.

---

## Phase 6 — Minimal dashboard

Goal: prove end-to-end before building UI polish.

1. Next.js app, WS client, `dataStore` with `merge.ts` (ported and fixture-tested against `merge.py`).
2. **One** component: an unstyled timing tower — position, TLA, gap, last lap.
3. Connection status indicator.

✅ Browser shows a live-updating leaderboard driven by the simulator, top to bottom of the stack.

---

## Phase 7 — Track map

1. `api` service scaffold + `GET /api/track/{key}/{year}` with the full server-side transform from `10-track-map.md`.
2. MultiViewer fetch + disk cache.
3. `TrackMap.tsx` — outline, viewBox, car dots.
4. rAF interpolation.
5. Marshal sectors and flag colouring.

✅ Bahrain, Monaco, Suzuka render correctly; car dots stay on the track for a full lap; smooth motion at 60 fps.

---

## Phase 8 — Full timing tower

Everything in `11-frontend.md` §"Timing tower behaviour details": mini-sectors, tyre badges, driver status, purple/green flashing, FLIP reorder animation, gap/interval toggle, favourites.

✅ Side-by-side with an f1-dash screenshot, the information content matches.

---

## Phase 9 — Supporting panels

Race control feed, weather, lap counter, session clock, track status banner, DRS indicators.

✅ A safety car in the fixture triggers the banner, colours all marshal sectors, and appears in the race control feed.

---

## Phase 10 — Broadcast delay

Per `08-delay-and-replay.md` Part A: buffer, clock skew estimation, slider, catch-up handling, persistent badge.

✅ All delay acceptance criteria in that document pass.

---

## Phase 11 — Telemetry

1. `GET /api/sessions/.../telemetry/{driver}` backed by FastF1.
2. uPlot speed/throttle/brake/gear traces.
3. Two-driver comparison with delta.
4. Live telemetry from `CarData` for the selected driver.

✅ Fastest-lap speed trace for VER at Monza 2024 matches FastF1's own plot.

---

## Phase 12 — Replay

1. Bundle precompute pipeline + `meta.json` with lap and event markers.
2. `ReplayEngine`, `GET /ws/replay/...`, control protocol.
3. Replay UI: scrub bar with markers, play/pause, speed, lap jump.
4. Keyframes optimisation.

✅ All replay acceptance criteria pass.

---

## Phase 13 — Schedule, standings, results

Jolpica integration, schedule page, standings pages, session picker for replay.

---

## Phase 14 — Production hardening

1. Dockerfiles for all four services; multi-stage, non-root.
2. `compose.yaml` complete, `.env.example` documented.
3. Structured JSON logging, Prometheus `/metrics` on each service.
4. Graceful shutdown everywhere.
5. Outbound proxy support on ingest (`F1_HTTP_PROXY`) for the datacentre-IP problem.
6. README with setup, screenshots, and the legal notice.
7. Load test: 500 concurrent WS clients.

---

## Phase 15 — Live validation

Only doable during an actual session weekend. Reserve a full session for this.

1. Run ingest against the live feed during FP1.
2. Compare the timing tower against the official F1 live timing page, side by side.
3. Record the whole session raw — that recording becomes the next fixture.
4. Log every unrecognised field, topic, and status code encountered.
5. Fix, then validate again on qualifying.

⚠️ Expect surprises here. The live feed carries edge cases that archives don't: mid-session driver changes, red-flag restarts, feed dropouts, sessions abandoned. Budget a full race weekend for this phase.

---

## Estimated effort

| Phase | Effort |
|---|---|
| 0–2 (foundation, merge) | 3–4 days |
| 3–5 (simulator, ingest, realtime) | 4–5 days |
| 6–7 (minimal UI, track map) | 4–5 days |
| 8–9 (full tower, panels) | 5–6 days |
| 10–11 (delay, telemetry) | 4–5 days |
| 12 (replay) | 5–7 days |
| 13–14 (schedule, hardening) | 4–5 days |
| 15 (live validation) | 1 race weekend + fixes |

Roughly **6–8 weeks** of focused solo work for full parity. A usable dashboard exists at the end of Phase 9 — around 3 weeks.

## What to cut if time is short

In order of what to drop first:
1. Telemetry comparison (Phase 11) — nice but not core.
2. Replay (Phase 12) — the biggest single chunk of work.
3. Standings (Phase 13).

Never cut: the merge algorithm tests, the simulator, or the delay feature. Those are load-bearing.
