# 20 — Implementation Plan

> The work order. **One use case = one commit.** Each has a definition of done that can be checked
> without reading the diff.
>
> Product use cases live in `15-use-cases.md` and describe *what the user can do*. This document
> describes *what gets built, in what order*. The mapping column links them.

**Branching.** Work lands on `dev`. Anything spanning more than about three use cases gets a
`feat/*` branch and is merged into `dev` once it runs.

**Design source of truth.** `design/handoff/application-design-combination/project/F1 Live
Dashboard v3.dc.html`. Read it, do not screenshot it — every dimension and colour is in the source.

---

## ⚠ Design vs. available data — read before building the telemetry view

The design shows panels the public F1 feed **does not provide**. This is not a flaw in the design;
it is a scope decision that has to be made explicitly rather than discovered halfway through.

| Panel in the design | Feed reality | Decision |
|---|---|---|
| Speed, throttle, brake, gear, RPM, DRS | ✅ In `CarData.z` | Build as designed |
| Sector times, mini-sectors, gaps, tyre compound, tyre age, pit stops | ✅ In `TimingData` / `TimingAppData` | Build as designed |
| Race control messages, penalties, flags | ✅ In `RaceControlMessages` | Build as designed — penalties are a filtered projection of the message feed |
| Session timeline (green / SC / VSC bands) | ⚙️ Derivable | Compute from `TrackStatus` transitions |
| Pace chart (last 10 laps) | ⚙️ Derivable | Compute from accumulated `LastLapTime` per driver |
| Stint bars | ⚙️ Derivable | Compute from `TimingAppData.Stints` |
| Tyre **wear %**, condition, est. remaining, pit window | ❌ **Not in the feed** | **Estimated** from tyre age and compound. Must be visibly labelled as an estimate. |
| Undercut monitor — cliff, degradation rate | ❌ **Not in the feed** | **Derived** from lap-time trend. Labelled as derived. |
| Tyre **temperature** and **pressure** (4 corners) | ❌ Team-internal, never public | **Cannot be built.** Replace with data we do have — see below. |
| Brake temperatures, engine temperature, fuel % | ❌ Team-internal, never public | **Cannot be built.** |

**Resolution for the Car systems panel (IMPL-19).** The four-corner tyre/brake readout and the
engine/fuel bars are replaced with a per-corner panel driven by real channels — speed, gear,
throttle, brake, DRS state and RPM — keeping the design's exact layout, typography and gauge
treatment. The car silhouette stays. Nothing is fabricated and nothing is shown as live that is not.

This is flagged here so the substitution is a recorded decision rather than a silent divergence.
See `DECISIONS.md` D-008.

---

## Phase A — Foundation

| # | Use case | Definition of done | Product UC |
|---|---|---|---|
| **IMPL-01** | Repository scaffold | `main` and `dev` exist on the remote; two-application layout in place | — |
| **IMPL-02** | Implementation plan | This document; `DECISIONS.md` records the data-gap decision | — |
| **IMPL-03** | Design tokens | Every colour, font, size and spacing value from the v3 design extracted into `web/src/styles/tokens.css` as custom properties. No hex literal appears anywhere else in the app. | — |
| **IMPL-04** | Web app scaffold | `pnpm dev` serves a blank themed shell; TypeScript strict, ESLint, Vitest and the feature-sliced folder structure from `docs/17` all in place | — |
| **IMPL-05** | Mock session fixture | The design's own dataset (20 drivers, car positions, messages, penalties, stints) ported to typed fixtures, so every component is developable before the backend exists | — |

> **Order changed 2026-09-06.** The backend (Phase F) is now built to a working
> state *before* the remaining UI. The risk in this project is concentrated in the
> backend — the legacy SignalR handshake, the merge algorithm and the
> classification test — and building the UI against fixtures first would defer
> that risk rather than retire it. IMPL-06 (header) was completed before the
> change and stays. Phases B–E resume once Phase F reaches IMPL-33.
>
> **Working order:** A → IMPL-06 → **F** → B → C → D → E → G.

## Phase B — Application shell

| # | Use case | Definition of done | Product UC |
|---|---|---|---|
| **IMPL-06** | Header | Logo, live indicator, session name, lap counter, track/air temperature, delay badge, session-status chip. All six status states render. | UC-011, UC-021 |
| **IMPL-07** | Tab navigation | Three tabs with active states and the race-control count badge; weather strip on the right | UC-011 |
| **IMPL-08** | Toast stack | Dismissible toasts, one per session-state change plus penalties, with the slab-in animation | UC-073 |
| **IMPL-09** | Footer | Legal notice verbatim, secondary navigation | — |

## Phase C — Race dashboard

| # | Use case | Definition of done | Product UC |
|---|---|---|---|
| **IMPL-10** | Focus cards | Three driver cards with ghost position number, last lap, gap, tyre, stops; selected card highlights | UC-012 |
| **IMPL-11** | Timing tower shell | Table with all 14 column headers at the exact widths from the design; GAP/INTV toggle wired | UC-013 |
| **IMPL-12** | Driver row | All columns render: position, team bar, TLA, team, DRS, gap, sectors, laps, tyre badge, age, status. Retired, in-pit and lapped variants correct. | UC-012 |
| **IMPL-13** | Mini-sectors | 20-segment bar, five status colours, memoised | UC-012 |
| **IMPL-14** | Track map | SVG circuit, marshal-sector overlay driven by flag state, DRS zones, start/finish, 19 car dots with selection ring and pit dimming | UC-015 |
| **IMPL-15** | Race status panel | Leader block, lap counter, top speed, current and best lap, map legend | UC-017 |
| **IMPL-16** | Pace chart | Three polylines over the last 10 laps with axis labels and legend | UC-012 |
| **IMPL-17** | Stints and gap bars | Stint bars coloured by compound; gap-to-leader bars scaled correctly | UC-012 |

## Phase D — Telemetry wall

| # | Use case | Definition of done | Product UC |
|---|---|---|---|
| **IMPL-18** | Driver chip selector | Ten chips, selection drives the whole view | UC-031 |
| **IMPL-19** | Driver focus and tyre status | Name block, wear ring, condition, stint, pit-window estimate, stop history — estimates visibly labelled | UC-031 |
| **IMPL-20** | Undercut monitor | Threat cards with gap, tyre, cliff and degradation | UC-031 |
| **IMPL-21** | Car systems | Car silhouette with per-corner readouts driven by real channels (see the note above), plus RPM, throttle and brake bars | UC-031 |
| **IMPL-22** | Traces and lap performance | Three traces with ring gauges and high/low, then top speed, last and best lap, throttle, brake and DRS | UC-031, UC-032 |

## Phase E — Race control

| # | Use case | Definition of done | Product UC |
|---|---|---|---|
| **IMPL-23** | Message log | Chronological feed with category accent, lap, time, and the ALL/FLAGS/SC-VSC/DRS/CARS filters | UC-016 |
| **IMPL-24** | Penalties | Entries with driver, penalty, reason and state chip | UC-016 |
| **IMPL-25** | Session timeline | Lap-range bands coloured by track status | UC-073 |

## Phase F — Backend

> Branch: `feat/backend`. Merged into `dev` once IMPL-32 passes.

| # | Use case | Definition of done | Product UC |
|---|---|---|---|
| **IMPL-26** | Backend scaffold | One .NET 9 application; `/health` responds; Docker image builds | — |
| **IMPL-27** | Domain model | Records mirroring `docs/05`, field names verbatim; TypeScript types generated from them | — |
| **IMPL-28** | Merge algorithm | All four rules from `docs/06`; the four named unit tests plus shared fixtures pass | — |
| **IMPL-29** | State accumulator | Per-topic special cases; `.z` inflate round-trips a captured fixture | — |
| **IMPL-30** | Archive downloader | A real 2024 session downloads and merges into a time-ordered stream | UC-041 |
| **IMPL-31** | **Classification test** | **Replaying the fixture produces a final top ten identical to the official result.** The single check that validates the whole parsing and merge layer. | — |
| **IMPL-32** | Simulator source | Replays the stream in genuine live wire format, `.z` topics re-compressed | — |
| **IMPL-33** | WebSocket endpoint | Snapshot then deltas; `?since=` resumption; bounded per-client channel with backpressure drop | UC-011, UC-061, UC-063 |
| **IMPL-34** | Live SignalR client | **Spike first.** Negotiate, exact case-sensitive headers, cookie, subscribe, 10 minutes stable | UC-062 |
| **IMPL-35** | REST endpoints | Schedule, session, track geometry, standings — every response sets `Cache-Control` | UC-051, UC-053 |

## Phase G — Integration

| # | Use case | Definition of done | Product UC |
|---|---|---|---|
| **IMPL-36** | Wire web to backend | Fixtures replaced by the live WebSocket; the dashboard updates from the simulator end to end | UC-011 |
| **IMPL-37** | Merge parity in CI | `merge.ts` and `DeltaMerge.cs` run against the same fixtures; divergence fails the build | — |
| **IMPL-38** | Delay buffer | 0–120 s, clock-skew corrected, rate-capped catch-up, persisted | UC-021 |
| **IMPL-39** | Reconnection | Backoff, `?since=` resumption, cursor-expired store reset, ping/pong heartbeat | UC-061 |
| **IMPL-40** | Docker compose | `docker compose up` runs both applications; `--profile dev` adds the simulator | — |

## Phase H — Session analysis

> The dashboard shows what is happening *now*. This phase records what happened across a whole
> session and lets it be read afterwards: strategy, lap-by-lap pace, position changes, sector
> comparisons and telemetry.
>
> Branch: `feat/analysis`.

### What has to be recorded, and why it cannot be reconstructed later

The live feed is ephemeral. `TimingData` carries only a driver's *current* lap time and
`CarData` only the *latest* telemetry batch — neither keeps history, and nothing is republished.
A lap that is not captured as it passes is gone. For an archived session the stream can simply be
replayed, but for a live session there is exactly one chance.

So the recorder consumes the same `TopicUpdate` stream the dashboard does and accumulates a
session history alongside it. It runs for replay too, because a code path exercised only during
the ~24 live weekends a year is a code path that breaks on race day.

### Storage shape

| File | Contents | Size |
|---|---|---|
| `analysis/meta.json` | Session identity, drivers, lap count | KB |
| `analysis/laps.json` | Per driver, per lap: lap time, S1/S2/S3, position, compound, tyre age, pit flags | ~200 KB |
| `analysis/stints.json` | Per driver: compound, first and last lap, laps on the set, new or used | KB |
| `analysis/telemetry/{driver}.json` | Per lap, parallel channel arrays: speed, throttle, brake, gear, RPM | ~1–3 MB per driver |

Telemetry is stored **per driver in its own file** and loaded on demand. Storing it as one
document would be a 40 MB response for a chart that needs one lap, and parallel arrays are 3–5×
smaller than an array of objects (docs/09).

**Telemetry recording is limited to 2026 and later.** Older sessions still get laps, stints,
positions and sector comparisons — those are cheap. Full telemetry history for every archived
season back to 2018 is tens of gigabytes for data that is already in the archive and replayable
on demand.

| # | Use case | Definition of done | Product UC |
|---|---|---|---|
| **IMPL-43** | Analysis model and builder | Lap times, sector times, positions and stints accumulate from the topic stream; a replayed race reproduces the official lap count and stop count per driver | UC-012 |
| **IMPL-44** | Telemetry recorder | Per-lap channel traces captured for 2026+ sessions; memory stays flat across a race; older sessions skip it deliberately | UC-031 |
| **IMPL-45** | Persistence and REST | Analysis written on session end and on demand; endpoints for laps, stints, positions, telemetry and a two-driver comparison; every response sets `Cache-Control` | UC-032 |
| **IMPL-46** | Lap-time chart | Bar chart of every lap for one driver, in any session type — practice, qualifying or race. Personal best and outliers distinguishable; pit and safety-car laps marked, since they otherwise read as a collapse in pace | UC-012 |
| **IMPL-47** | Strategy timeline | Per driver, a bar per stint coloured by compound, showing lap range, laps on the set and whether the set was new | UC-012 |
| **IMPL-48** | Position progression | Line-and-dot chart of every driver's position across the race, with the selected drivers emphasised | UC-012 |
| **IMPL-49** | Two-driver sector comparison | Side-by-side S1/S2/S3 with the per-sector delta and a clear statement of who is faster where | UC-033 |
| **IMPL-50** | Telemetry trace | Speed and brake against distance for one driver and one lap; two drivers overlaid | UC-031, UC-033 |
| **IMPL-51** | PDF export | A print stylesheet renders the analysis as a paginated report — session identity, classification, strategy, lap chart, position chart. Generated in the browser, so no server-side rendering dependency | — |

## Phase I — Routing, schedule, and analysis without replay

> Branch: `feat/routes`.

### Live and replay are two separate applications

`/live` and `/replay` are not two tabs over one shared session. They answer
different questions and must not be confusable:

| | `/live` | `/replay` |
|---|---|---|
| Source | The live F1 feed only | An archived session, chosen explicitly |
| On load | Connects, or states that no session is running | Shows the picker; plays nothing until a session is clicked |
| Never | Falls back to an archived session | Silently becomes live |
| Analysis | Recorded as it streams | Precomputed from the archive, no replay needed |

The failure this prevents is the one the whole project guards against
elsewhere: a user cannot tell a replay from live by looking at the numbers, so
the two must be separated by the URL and by the page, not by a badge alone.

### Why analysis no longer needs a replay

Building a session's analysis by replaying it through the paced ingest loop
takes minutes even at 50×, and for a session that already finished the pacing
buys nothing — the stream is on disk and can be read at full speed. Reading the
whole 2024 Monza race through the accumulator takes **1.5 seconds**.

So the two cases separate cleanly:

| Session | How the analysis is produced |
|---|---|
| **Finished** | Read `stream.jsonl` at full speed once, write `analysis/`, never replay again. Subsequent requests load the saved documents. |
| **Live** | Recorded as it streams, because there is no second chance. |

### On the storage question

MongoDB was offered as an option "if it is easiest". It is not. The analysis is
a handful of whole documents per session, read in full and written once —
exactly the shape a file already handles. Mongo would add a container, a
driver, connection lifecycle and a backup story, for data that is derived and
can always be rebuilt from the archive in 1.5 seconds. **The on-disk store
stays.** Revisit this only if analysis needs querying *across* sessions —
"every driver's Monza stint history since 2018" is a database question; "this
session's laps" is not.

| # | Use case | Definition of done | Product UC |
|---|---|---|---|
| **IMPL-52** | Analysis without replay | `POST /api/analysis/precompute` reads a finished session's stream at full speed and writes the analysis; a second request loads from disk instead of recomputing | UC-042 |
| **IMPL-53** | Saved-analysis lookup | `GET /api/analysis/{year}/{meeting}/{session}` serves a stored analysis with no session running | UC-041 |
| **IMPL-54** | Season schedule | `GET /api/schedule/{year}` merges the Jolpica calendar with what the archive holds; `GET /api/schedule/next` gives the next session and a countdown | UC-051, UC-052 |
| **IMPL-55** | Routing | React Router with distinct pages rather than tabs on one screen | — |
| **IMPL-56** | Live page | `/live` — bound to the live feed ONLY. Never plays an archived session, and shows the no-session state when nothing is running rather than silently falling back to a replay. | UC-011, UC-065 |
| **IMPL-57** | Replay page | `/replay` — session picker; `/replay/{year}/{meeting}/{session}` plays one. A replay starts only on an explicit choice, never on page load. | UC-041, UC-043 |
| **IMPL-60** | Weather panel | Track and air temperature, humidity, pressure, wind speed with a direction arrow, and rainfall — with icons, laid out to be read at a glance rather than as a row of numbers | UC-017 |
| **IMPL-58** | Schedule page | `/schedule` — the 2026 calendar with rounds completed, running and upcoming, and a countdown to the next | UC-051, UC-052 |
| **IMPL-59** | Analysis page | `/analysis/{year}/{meeting}/{session}` — readable without the session being loaded into the live dashboard | UC-032 |

## Phase J — Results, drivers and teams

> Branch: `feat/results`. Modelled on what `app.formula1dashboard.com` does well:
> a results page per race with session tabs, and card-based driver and team
> pages that carry real identity rather than being another table.

### ⚠ Logos and photographs cannot be shipped

The reference site's visual richness comes largely from **team logos** (the
Mercedes star, the Ferrari shield, the McLaren speedmark) used as large
background art, and **driver portraits**. Those are trademarked and copyrighted
assets. This project already refuses the F1 wordmark for the same reason
(`docs/DESIGN-BRIEF.md` §11), and taking a competitor's approach does not change
what the marks are.

What can be built instead, carrying most of the same visual weight:

| Their approach | Ours |
|---|---|
| Team logo as background art | Oversized team **name** as typographic art, in the team colour |
| Team-coloured gradient card | Same — colour is not a trademark |
| Driver portrait | Driver **number** as the graphic element, in the team colour, at card scale |
| Constructor mark beside each result row | Team colour bar, as the timing tower already does |

**Driver headshots are a separate decision.** `DriverList.HeadshotUrl` arrives
in the feed itself and points at F1's media server. Hot-linking it is a grey
area for a self-hosted dashboard and a licensing risk for a public one, so it
is behind a setting that is **off by default**, and the layout must be complete
without it.

| # | Use case | Definition of done | Product UC |
|---|---|---|---|
| **IMPL-64** | Race results API | Per round: results for every session type, plus pole, fastest lap and the winner | UC-053 |
| **IMPL-65** | Results page | Season and Grand Prix selectors; winner, pole and fastest-lap cards; session tabs (FP1–3, Qualifying, Grid, Race); a table with position, number, driver, team, time, gap, interval, points and laps | UC-053 |
| **IMPL-66** | Drivers page | A card per driver in team colours with number, team and season totals — points, wins, podiums, best finish | UC-053 |
| **IMPL-67** | Teams page | A card per constructor with its drivers, points, wins and season position | UC-053 |
| **IMPL-68** | Driver detail | One driver's season: results per round, points progression, qualifying against race pace | UC-053 |
| **IMPL-69** | Headshot setting | Off by default, with the licensing position stated where it is toggled | — |

## Phase K · Storage, when the archive stops being enough

The archive is currently a directory tree, and for everything the app does today
that is the right database (see `DECISIONS.md` D-011 for the measurements —
11 ms to read a driver's entire race telemetry). This phase exists so the
migration is designed before it is needed, not during an outage.

**The trigger is a feature, not a size.** Phase K starts when the first screen
asks a question spanning more than one session.

| # | Use case | Definition of done | Trigger |
|---|---|---|---|
| **IMPL-70** | Session index | A single queryable index of every archived session — year, meeting, type, circuit, drivers, whether telemetry exists — rebuildable from the tree in one pass, so it is a cache and never the source of truth | Any cross-session listing slower than a page load |
| **IMPL-71** | Telemetry in InfluxDB | Per-lap channels written to Influx alongside the JSONL, with the file remaining authoritative; measurement per channel, tags for driver, session and lap | The first query spanning sessions or seasons |
| **IMPL-72** | Cross-season analysis | Circuit records, a driver's pace across years, sector bests over a season — the screens the index and Influx exist to serve | Follows IMPL-70/71 |

Two rules hold whichever store is chosen. **The archive stays authoritative:**
every store is a derived index that can be dropped and rebuilt, so a corrupt
database is an inconvenience rather than data loss. **No database sits in the
ingest hot path:** live delta merging writes to memory and to `stream.jsonl`,
and adding a network write per delta would trade the one thing this app cannot
afford to lose — latency during a live session — for a convenience it does not
need.

## Phase L · Ingest resilience

Live data is the only genuinely fragile part of this system, because it is the
only part that depends on someone else's server being willing to talk to yours.
Measured from a development machine on 2026-09-07:

| Endpoint | Response |
|---|---|
| `signalr/negotiate` (legacy 1.5) | **401** |
| `signalrcore/negotiate` | 200 |
| `static/…` archive | 200 — `AmazonS3` via `CloudFront` |

The static path is an ordinary CDN, so it is **not** subject to the origin's
IP filtering. That makes polling a genuine fallback rather than a consolation
prize, and it is why these are ordered cheapest-first.

| # | Use case | Definition of done |
|---|---|---|
| **IMPL-73** ✅ | Static polling source | `StaticPollingSource` re-reads each topic's `.jsonStream` with HTTP `Range` from the last byte offset, once a second. Verified against F1's real archive: 16 topics, `OvertakeSeries` and `CurrentTyres` included. |
| **IMPL-74** ✅ | Source failover | `FailoverSessionSource` tries SignalR, falls back to polling, and `/api/session` reports which won. The dashboard says when it is following the polled feed. |
| **IMPL-75** ✅ | Relay collector | `RelayCollector` runs the same binary in a different mode and **dials outward** to the backend over authenticated WSS, forwarding raw `TopicUpdate`s. |
| **IMPL-76** ✅ | Relay source | `RelaySessionSource` plus `/relay`, with sequence numbers, resume-on-reconnect and duplicate rejection. Verified end to end: frames sent by a collector arrive at a browser as deltas. |

Two rules. The collector forwards **raw updates, never merged state**: merged
state is 1–2 MB per update instead of a few kilobytes, and it would put a second
copy of the merge algorithm in production where it could drift from the first.
And every source stays behind `ISessionSource`, so nothing downstream — analysis,
telemetry, fan-out — can tell which one is running.

### What failover is judged on

"Delivers data", not "connects". The SignalR endpoint will complete a handshake
and then send nothing — that is the failure this project actually hit — so a
source that has connected but produced no update inside a probation window is
treated as failed. Once it has produced one update it is trusted for the rest of
the session; falling back mid-race because of a quiet minute under a red flag
would be worse than the problem.

## Not in this plan yet

Everything in this document is built. Responsive layout is verified by
measurement rather than by eye: every page reports `scrollWidth == clientWidth`
at 375, 768 and 1024 px under device emulation. It has still not been opened on
a physical handset.
