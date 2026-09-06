# 15 — Use Cases

> The executable specification. Every use case below becomes one or more implementation tasks, and
> its acceptance criteria become tests. Where a use case restates a rule from another document, that
> document remains authoritative for the detail.

**Format.** Each use case gives: actor, preconditions, main flow, alternate flows, edge cases and
acceptance criteria. Edge cases are first-class here, not footnotes — in a delta-based realtime
system they are where the defects live.

**Actors.**

| Actor | Description |
|---|---|
| **Viewer** | An F1 fan using the dashboard. The only human actor; there are no accounts or roles. |
| **Ingest** | The singleton worker holding the F1 connection. |
| **Realtime** | The WebSocket fanout service. |
| **Api** | The stateless REST service. |
| **Feed** | The upstream F1 live timing service. |

---

# UC-01x — Live viewing

## UC-011 · Open the live dashboard

| | |
|---|---|
| **Actor** | Viewer |
| **Preconditions** | A session is live; `ingest` is connected and publishing. |

**Main flow**
1. Viewer navigates to `/dashboard`.
2. Client opens a WebSocket to `realtime` with no `since` cursor.
3. `realtime` reads the checkpoint hash — snapshot and cursor together, atomically — and sends
   `{"type":"snapshot", data, streamId, serverTime}` using the shared pre-compressed buffer.
4. `realtime` replays deltas from that cursor to the present, then joins the live broadcast.
5. Client seeds its store, computes clock skew from `serverTime`, and renders the full dashboard.
6. Deltas arrive continuously, accumulate in a pending buffer, and flush once per animation frame.

**Alternate flows**
- **A1 — No session live.** The snapshot has no `session_info`. The dashboard shows the designed
  "no live session" state with a countdown to the next session and links to replay. **This is the
  default state ~340 days a year and is a designed destination, not an error.**
- **A2 — Session finished but not finalised.** Data still arrives (classification corrections);
  the chequered flag banner is shown and the tower stays populated.

**Edge cases**
- Snapshot arrives before `DriverList` is populated → render skeleton rows, do not crash.
- A driver number appears in `Position` with no `DriverList` entry → skip that dot, log once.
- Fewer than 20 cars (FP1 with rookies, a test day) → no hardcoded array sizes anywhere.

**Acceptance**
- [ ] The timing tower is populated within 5 s of page load against the simulator.
- [ ] All panels render with a full recorded race.
- [ ] With no session, the "no live session" state renders — never an error or an empty grey page.
- [ ] Time to first meaningful paint under 2 s on a warm cache.

---

## UC-012 · Read the timing tower

| | |
|---|---|
| **Actor** | Viewer |
| **Preconditions** | UC-011 complete; `timing_data.Lines` populated. |

**Main flow**
1. One row per driver, sorted by position.
2. Each row shows: position, team colour bar, TLA and number, DRS, gap or interval, mini-sectors,
   S1/S2/S3, last lap, best lap, tyre compound and age, pit stop count, status.
3. Sector and lap colours come from the `PersonalFastest` and `OverallFastest` booleans on the
   timing values — **never from comparing numbers locally**.
4. A new personal best flashes green and a new overall fastest flashes purple, each settling within
   ~1.5 s.
5. On a position change, rows animate to their new order rather than jump-cutting.

**Alternate flows**
- **A1 — Lapped driver.** `GapToLeader` is `"1 L"`, not a number. Render distinctly; any gap
  parser must not assume numeric input.
- **A2 — Leader.** `GapToLeader` is empty. Render the designed leader treatment, not `+0.000`.
- **A3 — Qualifying.** Gap is to the session-best lap, not to a car ahead on track.

**Edge cases**
- Two drivers with identical gaps → the sort must be stable, keyed by driver number, or rows
  flicker between frames.
- `Sectors` arrives as a sparse patch (`{"1": {...}}` only) → sectors 0 and 2 must be untouched.
- Segment status value not in the known table → render neutral and log once.
- Mini-sector count varies by circuit (15–25) → the layout must not assume a fixed count.

**Acceptance**
- [ ] Information content matches a reference f1-dash screenshot side by side.
- [ ] A sparse sector patch updates exactly one sector.
- [ ] No `key={index}` anywhere in the tower — keys are driver numbers, or reorder animations tear.
- [ ] React commits per second stay under 15 in steady state.
- [ ] A row re-renders only when that driver's own slice changes (verified in the profiler).

---

## UC-013 · Toggle gap and interval

**Actor** Viewer · **Preconditions** Tower rendered.

1. Viewer clicks the gap column header.
2. All rows switch between `GapToLeader` and `IntervalToPositionAhead`.
3. The choice persists to `localStorage` and survives reload.

**Edge cases** — Interval is empty for the leader; `Catching` is advisory and must not be required.

**Acceptance** — [ ] Toggling re-renders only the gap cells. [ ] The choice survives reload.

---

## UC-014 · Choose favourite drivers

**Actor** Viewer · **Preconditions** `DriverList` populated.

1. Viewer selects drivers in settings, or clicks a row's pin affordance.
2. Their rows are highlighted and their track-map dots get a white ring.
3. The selection persists to `localStorage`.

**Edge cases** — A favourite driver absent from the current session (moved teams, not entered) →
ignore silently, do not error.

**Acceptance** — [ ] Highlight is visible without obscuring sector colours. [ ] Persists across reload.

---

## UC-015 · Watch the track map

**Actor** Viewer · **Preconditions** `session_info.Meeting.Circuit.Key` known; track geometry fetched.

**Main flow**
1. Client requests `/api/track/{circuitKey}/{year}`; `api` returns a ready-to-render SVG path,
   viewBox, corners, marshal sectors, DRS zones and the transform parameters.
2. The outline renders; 20 dots render in team colours with TLA labels.
3. `Position` updates arrive at ~4 Hz; the client linearly interpolates between the last two known
   positions inside an animation-frame loop, writing directly to DOM refs.
4. Marshal sectors colour according to race control sector flags; a global safety car or VSC
   overrides all sectors to yellow.

**Alternate flows**
- **A1 — Geometry unavailable upstream.** Serve from disk cache. If never cached, hide the map with
  an explanatory message; the rest of the dashboard is unaffected.

**Edge cases**
- Gap between position updates exceeds 3 s → stop interpolating and snap; the driver has probably
  pitted or the feed stalled. Continuing to interpolate would slide a car across the infield.
- `PositionEntry.Status != "OnTrack"` → dim or hide per user preference; pitted cars report
  positions that wander off the outline.
- Retired driver → remove the dot entirely, or a ghost sticks on the map for the rest of the race.
- Twenty cars on the starting grid → labels collide; apply the collision strategy from the design.

**Acceptance**
- [ ] Bahrain, Monaco, Suzuka and Las Vegas all render recognisably, in broadcast orientation.
- [ ] No dot leaves the track stroke across a full recorded lap.
- [ ] 60 fps with 20 cars and **zero React commits** during steady-state animation.
- [ ] A yellow flag in sector 7 colours only sector 7.
- [ ] Server and client coordinate transforms agree on a shared test fixture.

---

## UC-016 · Read race control messages

**Actor** Viewer.

1. Messages render newest first with timestamp, lap, category and text.
2. New messages arrive appended — **never merged over an existing message**.
3. Flags, safety car and penalties are visually distinguished by category.

**Edge cases** — Messages sometimes arrive as a genuine JSON array rather than an index-keyed
object; both shapes must normalise to one. Text is ALL CAPS and can exceed three lines.

**Acceptance** — [ ] A safety car in the fixture appears with the correct lap number.
[ ] Message order is stable and no message is ever overwritten.

---

## UC-017 · Read weather, lap counter and session clock

**Actor** Viewer.

1. Weather shows air and track temperature, humidity, pressure, wind speed and direction, rainfall.
2. The lap counter shows `CurrentLap / TotalLaps` for races.
3. The session clock shows `ExtrapolatedClock.Remaining` and **ticks locally between updates**,
   because the feed sends it only about once per second.

**Edge cases** — `Extrapolating: false` (session suspended) → the clock must freeze, not keep
counting down. Practice and qualifying have no lap count → hide the counter rather than showing `0/0`.

**Acceptance** — [ ] The clock ticks smoothly at 1 Hz and re-syncs on each feed update without
jumping backwards. [ ] It freezes when extrapolation stops.

---

# UC-02x — Broadcast delay

## UC-021 · Set a broadcast delay

| | |
|---|---|
| **Actor** | Viewer |
| **Preconditions** | Connected and receiving live data. |
| **Rationale** | The data feed runs 3–40 s ahead of every TV broadcast. Without this the dashboard spoils overtakes before they appear on screen. |

**Main flow**
1. Viewer opens the delay control from the header badge or settings.
2. They set a value between 0 and 120 s, by slider or preset (0 / 5 / 15 / 30 / 60).
3. Incoming frames are buffered client-side and released when `(now + skew) ≥ frame.ts + delay`.
4. A persistent header badge shows the active delay.
5. The value persists to `localStorage`.

**Alternate flows**
- **A1 — Increasing the delay while running.** Frames simply sit longer. The display appears to
  freeze for the increment and then resumes. No special handling.
- **A2 — Decreasing the delay while running.** Many frames become due at once. Drain at a capped
  rate (roughly 200 frames per animation frame) with a "catching up" indicator. **Never dump 30 s
  of deltas into React in one tick.**
- **A3 — Setting delay to 0 from a high value.** Discard the buffer, request a fresh snapshot, reset
  the store. Faster and simpler than a multi-thousand-delta catch-up.

**Edge cases**
- Client and server clocks differ by seconds → all comparisons use `now + skew`, re-estimated every
  30 s from the minimum-RTT ping sample.
- Buffer exceeds `MAX_FRAMES` (20 000) → clamp the delay, drop the oldest frames, warn the viewer.
- Feed dropout longer than the delay → the buffer starves and the display stalls; show the stale
  state honestly rather than pretending it is live.
- **A viewer who forgets they set a delay and reports the app as broken is the single most common
  support issue.** The badge must be impossible to miss.

**Acceptance**
- [ ] A 30 s delay visibly holds the tower 30 s behind live.
- [ ] Reducing 60 s → 0 does not freeze the tab for more than 500 ms.
- [ ] ±5 s of simulated clock skew changes observed delay by less than 200 ms.
- [ ] The delay persists across reloads and the badge is visible on every page.
- [ ] A debug panel reports queued frame count and oldest frame age.

---

# UC-03x — Telemetry

## UC-031 · View live telemetry for a driver

**Actor** Viewer · **Preconditions** `CarData` arriving.

1. Viewer selects a driver in the tower.
2. Live speed, throttle, brake, gear, RPM and DRS render for that driver.
3. A rolling trace shows roughly the last 30 s.

**Edge cases** — Channel numbers are the field names (`0` RPM, `2` speed, `3` gear, `4` throttle,
`5` brake, `45` DRS); an unmapped channel is logged once, not dropped. DRS is active at `>= 10`.
Values are absent while a car is in the garage → render the empty state, not zeros.

**Acceptance** — [ ] Readouts update at feed rate without digit jitter. [ ] Deselecting stops the
trace and frees its buffer.

## UC-032 · View a fastest-lap telemetry trace

**Actor** Viewer · **Preconditions** Session has finished and lap data is available.

1. Viewer opens telemetry for a driver and lap (`?lap=fastest` or a lap number).
2. `api` returns parallel channel arrays — 3–5× smaller than array-of-objects and directly
   consumable by uPlot.
3. Speed, throttle, brake, gear and RPM render against distance.

**Alternate flows** — **A1 — Cold FastF1 load** takes 30–90 s: return `202` and show progress, and
never perform a cold load inside a request handler without a cache check first.

**Acceptance** — [ ] A fastest-lap speed trace for a known session matches FastF1's own plot.
[ ] A warm request returns in under 50 ms.

## UC-033 · Compare two drivers

**Actor** Viewer.

1. Viewer picks two drivers and a lap each.
2. Traces overlay in team colours with a delta channel between them.

**Edge cases** — Laps of different lengths (a driver ran wide) → align on distance, not on time.
Two teammates share a colour → differentiate by line style, not colour alone.

**Acceptance** — [ ] The delta trace is zero at the start and matches the lap-time difference at the end.

---

# UC-04x — Replay

## UC-041 · Choose a session to replay

**Actor** Viewer.

1. Viewer opens `/replay`, picks a year (2018 → current), a round and a session type.
2. Each entry shows whether a bundle already exists.
3. Selecting a ready session opens the replay dashboard.

**Alternate flows** — **A1 — Not yet processed** → UC-042.

**Acceptance** — [ ] All rounds for a chosen season list correctly, including sprint weekends.

## UC-042 · Precompute a session bundle

**Actor** Viewer, Api.

1. Viewer opens an unprocessed session.
2. `api` starts a background job and returns `202` with a job id.
3. Progress streams over server-sent events with **named stages**: downloading → parsing → building
   keyframes → done.
4. On completion the replay dashboard opens automatically.

**Alternate flows**
- **A1 — Raw archive missing or malformed** → fall back to a FastF1 precomputed frame bundle at
  reduced fidelity (no mini-sectors, no exact tower behaviour), and say so in the UI.
- **A2 — Already running for this session** → attach to the existing job rather than starting a second.

**Edge cases** — Upstream fetch fails mid-download → the partial bundle must not be marked ready.
The viewer navigates away → the job continues; it is not tied to the connection.

**Acceptance** — [ ] Cold processing completes in 30–120 s with visible staged progress.
[ ] A spinner is never shown for more than 3 s without stage information.
[ ] An interrupted job leaves no half-written bundle that later reads as valid.

## UC-043 · Play, pause and seek

**Actor** Viewer.

1. Play, pause, seek to any point, and set speed from 0.25× to 8×.
2. Seeking forward advances the accumulator through intervening deltas.
3. Seeking **backward rebuilds state** — it loads the nearest preceding keyframe and replays at most
   60 s of deltas.
4. On seek, the server sends a full snapshot with `reason: "seek"`, not a delta.

**Edge cases** — Seeking to before the first keyframe rebuilds from zero. Pausing must freeze
everything **including the track map** — position interpolation must stop, or cars keep drifting.
At 8× the delta rate exceeds the render budget → coalesce per frame; dropping deltas is not
permitted, dropping *renders* is.

**Acceptance**
- [ ] A 2024 race replays from t=0 to the chequered flag with a final tower matching the official
      classification.
- [ ] Backward seek to lap 10 and forward to lap 50 both settle within 2 s (before keyframes) and
      200 ms (after).
- [ ] 4× plays without dropping deltas.
- [ ] Pausing freezes every value, the map included.
- [ ] Controls reflect seek, pause and speed within 200 ms.

## UC-044 · Jump to a lap or an event

**Actor** Viewer.

1. The scrub bar shows lap ticks and event markers (safety car, red flag, incidents).
2. Clicking a marker seeks to that offset.
3. A lap number can be entered directly.

**Edge cases** — Lap markers do not exist for practice sessions → show time markers instead.
Red-flag restarts produce non-monotonic lap timing → markers must derive from the recorded
`RaceControlMessages` and `TrackStatus` transitions, not from an assumed constant lap duration.

**Acceptance** — [ ] Every safety car period in the Monaco fixture appears as a marker at the right
offset. [ ] The scrub bar is usable at 375 px width.

## UC-045 · Replay capacity

**Actor** Realtime.

1. Each viewer gets an engine; the parsed stream is shared, only cursor and state are per-client.
2. Beyond `MAX_CONCURRENT_REPLAYS` (default 50) new requests are refused with a clear message.

**Acceptance** — [ ] 50 concurrent replays of the same session hold one copy of the parsed stream.
[ ] The 51st request gets an explanatory refusal, not a timeout or a degraded experience for everyone.

---

# UC-05x — Schedule, results and standings

## UC-051 · View the season calendar

1. Viewer opens `/schedule`; `api` serves rounds merged from Jolpica and the F1 static index.
2. Each round shows its sessions with local start times, and its state: completed, live, upcoming.
3. Completed sessions link to replay.

**Edge cases** — Sprint weekends have a different session set (`SQ`, `S`) and a parser that assumes
FP1/FP2/FP3/Q/R will break. Time zones: sessions are stored UTC and rendered in the viewer's local
zone. A cancelled round must render as cancelled, not as perpetually upcoming.

**Acceptance** — [ ] `/api/schedule/2024` returns 24 rounds including sprints.
[ ] Jolpica is never called per request; the calendar refreshes on a daily schedule into a cache.

## UC-052 · See the next session and countdown

1. The landing page shows the next session with a live countdown.
2. When a session is live, a prominent entry point replaces the countdown.

**Acceptance** — [ ] The countdown is correct across a DST boundary. [ ] The live state appears
within 60 s of a session starting.

## UC-053 · View results and standings

1. `/dashboard/standings` shows driver and constructor tables with points and wins.
2. A finished session shows its final classification.

**Edge cases** — Before round 1 of a season, standings are empty → render the empty state. Shared
points and countback rules → display the order the upstream provides; do not re-sort locally.

**Acceptance** — [ ] Standings match the official tables for a completed season.

---

# UC-06x — Resilience

> These are the flows that decide whether the product is trustworthy. They are specified with the
> same rigour as the happy path.

## UC-061 · Browser connection drops and recovers

**Actor** Viewer, Realtime.

1. The socket closes unexpectedly.
2. The client shows a clear reconnecting state and **marks the on-screen data as stale**.
3. It reconnects with exponential backoff 1 s → 30 s plus jitter, passing `?since=<lastStreamId>`.
4. If the cursor is still retained, deltas resume with no snapshot and the store is preserved.

**Alternate flows**
- **A1 — Cursor expired.** The server sends a full snapshot carrying `reason: "cursor-expired"`.
  The client **resets** its store rather than merging onto stale state. Merging here would produce
  a plausible-looking but wrong tower — the worst possible outcome.
- **A2 — Dead-but-open socket.** The client pings every 20 s; no pong within 10 s forces a
  reconnect. Common on mobile networks and behind corporate proxies, and invisible without this.
- **A3 — Tab hidden.** Keep the socket, stop the animation-frame loop. On becoming visible, resync.

**Acceptance**
- [ ] Killing the network shows a reconnecting state and recovers with no page reload.
- [ ] `?since=` resumption skips the snapshot when the cursor is valid.
- [ ] An expired cursor resets the store and the tower is correct afterwards.
- [ ] A forced socket close in Playwright recovers within 5 s.

## UC-062 · Ingest loses the F1 feed

**Actor** Ingest.

1. The socket drops or 30 s pass with no frame of any kind.
2. Ingest backs off 1 s → 30 s with ±20% jitter and restarts from **negotiate**, since the token
   expires.
3. **Accumulated state is not cleared** — the fresh snapshot overwrites it anyway, and keeping it
   means the dashboard does not blank out during a brief blip.
4. Each reconnect logs at WARNING; the connected metric reflects reality throughout.

**Acceptance** — [ ] Killing the network for 5 s produces exactly one reconnect log line and state
resumes. [ ] The dashboard does not blank during a 5 s outage.

## UC-063 · A client cannot keep up

**Actor** Realtime.

1. A client's bounded channel (500 frames) fills.
2. It is closed with code 1013 and reconnects with a fresh snapshot.
3. Other clients are unaffected; the shared reader never blocks.

**Acceptance** — [ ] A client that stops reading is dropped within 30 s. [ ] Other clients see no
interruption. [ ] The drop is counted in `f1_realtime_dropped_clients_total`.

## UC-064 · Redis becomes unavailable

1. Ingest keeps recording raw frames to the archive and buffers; **recording never stops**, because
   data loss is the only unrecoverable failure.
2. Realtime sends clients an error message and retries.
3. When Redis returns, clients reconnect and receive a fresh snapshot.

**Acceptance** — [ ] Killing Redis mid-stream produces an error message then a clean recovery.
[ ] No raw frames are lost during the outage.

## UC-065 · The live feed is unreachable at all

**Rationale** — F1 blocks datacentre IP ranges; a cloud-hosted ingest may never connect.

1. Ingest reports disconnected; no live data is published.
2. The dashboard shows "no live session" — **not an error**. It is the same state as any ordinary
   day without a race, and it is honest.
3. Schedule, standings, results and the entire replay experience remain fully functional, because
   `api` and `realtime` touch no F1 endpoint at request time.

**Acceptance** — [ ] With ingest stopped, every route except the live dashboard works normally.
[ ] The live dashboard shows the no-session state, never a stack trace or a spinner.

## UC-066 · Two ingest instances start by mistake

1. Each attempts `SET f1:ingest:lock <id> NX PX 15000`.
2. The loser runs in standby and publishes nothing.
3. If the holder dies, the standby acquires the lease within 15 s.

**Acceptance** — [ ] Starting a second instance does not double any delta. [ ] Killing the primary
promotes the standby within 15 s. [ ] `f1_ingest_lease_held` is 1 on exactly one instance.

---

# UC-07x — Session scenarios

> Real conditions that archives underrepresent and that break naive implementations. Each is a test.

## UC-071 · Red flag and restart

1. `TrackStatus` becomes `5`; `SessionStatus` becomes `Aborted`.
2. The red flag banner appears; recording continues throughout.
3. On restart the lap counter, tyre state and driver ordering must all remain correct.

**Acceptance** — [ ] A red-flag fixture replays with a correct final classification.
[ ] The lap counter does not reset or double-count across the stoppage.

## UC-072 · Qualifying segment transitions

1. Q1 → Q2 → Q3 each reset the timing tower's frame of reference.
2. Eliminated drivers must be presented as eliminated, not as slow.

**Acceptance** — [ ] A qualifying fixture shows the correct five eliminations after Q1 and Q2.

## UC-073 · Safety car and VSC

1. `TrackStatus` `4` (SC) or `6` (VSC) raises the banner and overrides all marshal sectors to yellow.
2. `7` (VSC ending) is a distinct transitional state.
3. The event also appears in the race control feed.

**Acceptance** — [ ] A safety car in the fixture triggers banner, sector colouring and feed entry
together, within 2 s. [ ] SC and VSC are visually distinguishable.

## UC-074 · A driver retires mid-lap

1. `Retired` becomes true.
2. The row moves to the bottom and is dimmed; **the map dot is removed**.
3. The driver is excluded from gap and interval calculations.

**Acceptance** — [ ] No ghost dot remains anywhere on the map for the rest of the session.

## UC-075 · Mid-session driver replacement

1. `DriverList` updates mid-stream with a changed name or number.
2. Rows and dots update without a remount or a lost row.

**Acceptance** — [ ] A `DriverList` delta mid-session does not blank the tower or duplicate a row.

## UC-076 · Sprint weekend

1. Session types `SQ` and `S` appear in the schedule and replay picker.
2. All session-type parsing handles them.

**Acceptance** — [ ] A sprint weekend lists all five sessions with correct types and times.

## UC-077 · Feed dropout of 30 seconds or more

1. Positions go stale; the delay buffer starves.
2. The UI shows the data as stale rather than presenting frozen values as live.
3. Position interpolation stops after 3 s rather than sliding cars across the infield.

**Acceptance** — [ ] A 30 s gap injected into a fixture produces a visible stale indicator and no
car leaves the track surface.

## UC-078 · An unknown field, topic or status code appears

1. Unknown values are rendered neutrally and **never dropped**.
2. Each distinct unknown key is logged exactly once, deduplicated.

**Rationale** — This is how a feed change is discovered before users report a broken UI, and the
feed does change.

**Acceptance** — [ ] An injected unknown segment status renders neutral and logs one line, not one
line per delta.

---

# Coverage check

Every row of `14-testing.md` §"Known edge cases to test explicitly" maps to a use case:

| Edge case | Use case |
|---|---|
| Red flag → session restart | UC-071 |
| Driver retires mid-lap | UC-074 |
| Driver number in `Position` before `DriverList` | UC-011 |
| Sprint weekend | UC-076, UC-051 |
| Fewer than 20 cars | UC-011 |
| Feed dropout 30 s+ | UC-077, UC-021 |
| Qualifying segment transitions | UC-072 |
| Two drivers with identical gaps | UC-012 |
| Lapped traffic (`GapToLeader` = `"1 L"`) | UC-012 |
| Mid-session driver replacement | UC-075 |
