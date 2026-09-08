# DECISIONS

`docs/00-START-HERE.md` requires that any deviation from the specified stack is recorded here, with
its reasoning. Newest first.

---

## D-005 · Docker-first, including development

**Date** 2026-09-06 · **Status** accepted

Everything runs in containers, development included. The only host requirements are Docker and an
editor.

**Why.** Four services in two languages plus a Node build is a lot of host toolchain to install and
keep in sync, and the developer machine currently has neither the .NET SDK nor Go installed.
Containerising development removes the "works on my machine" gap entirely and makes the CI
environment and the dev environment the same thing.

**Cost.** Slightly slower inner loop than running natively, and file-watching across a bind mount
needs care on macOS.

See `docs/19-docker.md`.

---

## D-004 · Live F1 state lives outside Redux

**Date** 2026-09-06 · **Status** accepted
**Deviates from** `docs/11` (Zustand) and `REACT-APP-TEMPLATE.md` §9 (three state categories)

RTK Query holds all server state and Redux Toolkit holds settings and UI state, exactly as the
template says. The live `F1State` is the exception: a module-level object outside React, read
through `useSyncExternalStore`, with deltas coalesced and flushed once per animation frame.

**Why.** Roughly ten deltas per second against a deeply nested object. A Redux dispatch per delta
forces a top-down re-render and drops frames on mid-range hardware. This is a frequency problem,
not a criticism of Redux — at the frequency of every other piece of state in the app, Redux is the
right tool and is used.

**Scope.** One documented exception. Every other template rule — folder tree, decision table,
import direction, component folders, naming, lint enforcement — applies unchanged.

See `docs/17-frontend-architecture.md` §3.

---

## D-003 · Vite SPA instead of Next.js

**Date** 2026-09-06 · **Status** accepted · **Deviates from** `docs/00`, `docs/11`

**Why.** This is a wholly client-side realtime application. First meaningful paint depends on a
WebSocket snapshot, so server rendering contributes nothing to the metric that matters. The App
Router adds a layer that fights the socket lifecycle and the animation-frame render loop. The
landing and schedule pages are the only SSR candidates and do not justify the cost.

Also: the project already has a written frontend template (`REACT-APP-TEMPLATE.md`) built around
Vite. Using it keeps one convention instead of two.

**Cost.** No SSR for the landing page, so slightly weaker SEO on a page nobody searches for.

---

## D-002 · Python retained, but confined to FastF1 precompute

**Date** 2026-09-06 · **Status** accepted

A single Python worker builds replay bundles, lap tables and per-lap telemetry with FastF1. It is a
batch job writing files to disk and never sits in a request path.

**Why.** FastF1 has no equivalent in .NET or Go, and reimplementing its lap and telemetry parsing
would cost roughly a week for no user-visible gain. Confining it to an offline path means its
performance is irrelevant and its failure does not affect the live dashboard.

**Cost.** Two language toolchains in the repository. Contained by the fact that the Python surface
is one directory with one entry point.

---

## D-001 · .NET 9 instead of Python/FastAPI for ingest, realtime and api

**Date** 2026-09-06 · **Status** accepted · **Deviates from** `docs/00`, `docs/02`, `docs/06`, `docs/07`, `docs/09`

**Why.** Throughput was not the deciding factor — 50–200 KB/s and ~10 deltas/s is modest for any
modern runtime, and .NET, Go and Python would all be fast enough for the feed itself. The reasons
that actually decided it:

1. `System.Threading.Channels` maps 1:1 onto the bounded per-client queue design in `docs/07`, and
   the concurrency model holds up under one replay engine per client.
2. Typed records plus source-generated `System.Text.Json` for the ~30 models in `docs/05`, and the
   TypeScript types are generated from them.
3. A single toolchain across three services.

**Known cost, accepted.** `Microsoft.AspNetCore.SignalR.Client` speaks SignalR **Core** and cannot
connect to F1's *legacy* SignalR 1.5 feed. The client is therefore a raw `ClientWebSocket` plus a
hand-written negotiate and framing layer — about 200 lines, and the same work in any language.
**This is spiked first, in Phase 4, before the rest of the backend commits.**

**Second known cost.** `StackExchange.Redis` multiplexes and does not support blocking reads, so
`XREAD BLOCK` as written in `docs/07` will stall the multiplexer. Mitigated by the pub/sub
`f1:notify` wake-up already in the design, plus a non-blocking `XRANGE` and a 250 ms fallback poll.

**What did not change.** The F1 wire protocol, field names, the data model, the merge rules, the
delay and replay semantics, and the track-map mathematics. `docs/03`–`docs/05`, `docs/08` and
`docs/10` remain authoritative.

See `docs/16-dotnet-architecture.md`.

---

## Design deviations from the naive reading of the spec

Recorded here because they change behaviour described in `docs/06` and `docs/07`.

### D-006 · Snapshot checkpointing at 1 Hz instead of a snapshot write per delta

`docs/06` §Publisher writes the full accumulated state to `f1:state` on every delta. With a 1–2 MB
state at 10 deltas/s that is 10–20 MB/s to Redis — the largest single cost in the naive design, and
pure waste.

Instead the snapshot and its stream cursor are written atomically to one hash **once per second**.
A connecting client receives that snapshot plus every delta from its cursor to now. State is
byte-identical because merging a repeated value is idempotent, at roughly a tenth of the traffic.

### D-007 · The snapshot is compressed once and shared; deltas are never compressed

The snapshot is megabytes and sent once per connect; deltas are hundreds of bytes and sent ten
times a second. The snapshot is serialised and gzipped once per checkpoint and the identical buffer
is handed to every connecting client. Deltas are sent uncompressed, because per-message deflate
would give each client its own compression context and destroy the "serialise once, send the same
bytes to everyone" property that makes fanout nearly free.

---

## D-008 · Team-internal telemetry in the design cannot be built

**Date** 2026-09-06 · **Status** accepted

The v3 design's *Car systems* panel shows per-corner tyre temperature and pressure, brake
temperatures, engine temperature and fuel percentage. **None of these are in the public F1 feed** —
they are team-internal channels that never leave the garage.

**Decision.** Keep the panel's exact layout, typography and gauge treatment, but drive it with
channels we genuinely receive: speed, gear, throttle, brake, DRS state and RPM.

**Related.** Tyre wear percentage, condition, estimated remaining laps, the pit-window estimate and
the undercut monitor's cliff and degradation figures are also absent from the feed, but unlike
temperatures they are *derivable* from tyre age, compound and lap-time trend. Those are built as
**estimates and labelled as such in the UI** — a number presented as measured when it is inferred is
worse than no number at all.

**Why this matters enough to record.** The alternative — quietly rendering plausible values — would
make the dashboard untrustworthy in exactly the way `docs/16` §6 sets out to prevent: the user
cannot tell that it is wrong.

See `docs/20-implementation-plan.md`.

---

## D-009 · One backend application; Redis deferred

**Date** 2026-09-06 · **Status** accepted
**Supersedes** the three-service split in `docs/02` and `docs/16` §2

The user asked for two applications: `web` and `backend`. The backend is
therefore one process that owns the feed connection, the accumulated state, the
WebSocket fanout and the REST surface.

**Why this is not a compromise.** Redis existed in the original design to bridge
two incompatible requirements: ingest must be a singleton (two SignalR
connections double every delta) while realtime must be replicable (hundreds of
browser sockets). In one process that tension does not exist, and in-process
fanout is strictly faster — no serialisation to Redis, no network hop, no
`XREAD` polling workaround for `StackExchange.Redis` not supporting blocking
reads (`docs/16` §1.1, trap 2).

**What it costs.** Horizontal scaling of the socket layer. For a self-hosted
dashboard serving one household that is not a real constraint, and the seam is
preserved: `LiveSessionState` is the only file that would change.

**When to revisit.** If a single instance is genuinely serving hundreds of
concurrent viewers. The load test in IMPL-40 is what should trigger that
conversation, not a guess.

---

## D-010 · Bounded channels use `Wait`, never `DropWrite`

**Date** 2026-09-06 · **Status** accepted

A subtle one, caught by a test rather than by reading.

`BoundedChannelFullMode.DropWrite` silently discards the incoming item and
returns **true** from `TryWrite`. A slow client would therefore keep its
connection open while quietly losing deltas — its state would drift out of sync
with the server and neither side would know. That is the exact failure the
backpressure design exists to prevent: the dashboard would show plausible,
wrong data, and the user could not tell.

`BoundedChannelFullMode.Wait` makes `TryWrite` return **false** when the queue
is full, which is what lets the broadcaster drop the client and force a clean
resync. The write never actually blocks, because the code only ever calls
`TryWrite`.

## D-011 · InfluxDB and MongoDB: measured, and not adopted yet

Both were offered as free choices — "use them if they make the app faster or the
data easier to get". So they were measured against the data the app actually
holds rather than against the shape they are usually recommended for.

**What the data looks like on disk, 2026 Monza race:**

| | Size | Read cost |
|---|---|---|
| `stream.jsonl` (the raw feed) | 80 MB | streamed, never held whole |
| `analysis.json` (derived) | 1.4 MB | one read, whole document |
| `telemetry/*.jsonl` (21 drivers) | 10 MB | 11 ms for one driver's whole race |

**InfluxDB** is a real fit for the *shape* of telemetry — 20,920 samples per
channel per driver is a time series by any definition. It is not a fit for the
*access pattern*. Every telemetry screen in this app asks for one driver, one
session, one lap. That query is a file read of a few hundred kilobytes, and it
already completes in 11 ms for an entire race. Influx would replace an 11 ms
file read with a network round-trip to a container that has to be running,
backed up and version-matched, and would win nothing.

**MongoDB** is a worse fit still. The analysis documents are written once when
a session ends and read whole. That is what a file is. Mongo would add a
container, a driver, connection lifecycle and a backup story for data that is
derived and can be rebuilt from the archive in about a second and a half.

**What would change this.** Both answers are about *per-session* access. The
moment a screen asks a question that spans sessions — "every lap VER exceeded
330 km/h this season", "Monza sector 2 across 2024–2026", "all two-stop races in
2026" — the file layout stops working, because answering means opening every
file in the archive. A season is roughly 120 sessions; at 10 MB of telemetry
each that is over a gigabyte to scan for one question.

So the trigger is written down rather than the technology: **when the first
cross-session query ships, InfluxDB goes in for telemetry and the analysis
documents get indexed** — Mongo if the query is document-shaped, Postgres if it
turns out to be relational. Until then the archive on disk *is* the database,
and it is the faster one.

Recorded because "we considered it" is worth less than "we measured it and here
are the numbers".

## D-012 · Three live sources, ordered cheapest-first

The live feed is the only part of this system that depends on someone else's
server being willing to talk to yours, so it has three implementations of one
interface rather than one implementation and a hope.

Measured from a development machine on 2026-09-08:

| Endpoint | Response |
|---|---|
| `signalr/negotiate` (legacy 1.5) | **401** |
| `signalrcore/negotiate` | 200 |
| `static/…` archive | 200 — `AmazonS3` via `CloudFront` |

The static archive is a plain CDN. It is therefore **not** subject to whatever
the SignalR origin is doing to refuse requests, which is what turns polling from
a consolation prize into a real answer. The files support HTTP `Range` (verified
with a `206`), so a poller re-reads only what was appended — kilobytes a second
against a 5.6 MB file — and the format is byte-identical to the archive the
replay source already parses.

**Failover is judged on data, not on connection.** The SignalR endpoint will
complete a handshake and then send nothing; that is the failure actually
observed here. A source that has connected but produced no update inside a
probation window is treated as failed. Once it produces one update it is trusted
for the rest of the session, because falling back mid-race over a quiet minute
under a red flag would be worse than the problem.

**The relay collector dials outward.** When neither source works from the server
at all, a collector runs somewhere F1 accepts — a home connection, being a
residential address, is the most reliable — and connects *to* the backend. The
direction is the whole design: a server-initiated link would need a static
address, port forwarding and a firewall rule wherever the collector runs, and
would break the first time a home connection was renumbered.

It forwards **raw topic updates, never merged state**. Merged state would be
1-2 MB per update instead of a few kilobytes, and would put a second copy of the
merge algorithm in production where it could drift from the first.

## D-013 · PostgreSQL, InfluxDB and MongoDB adopted

Supersedes D-011 by decision of the project owner, who asked for the full
architectural package. D-011's measurements are not withdrawn and the design
below is shaped so they keep being true.

**The rule that makes this safe: `stream.jsonl` remains authoritative and every
database is a derived index.** Any store can be dropped and rebuilt. That single
rule means a lost database is an inconvenience rather than data loss, a schema
change is a re-run rather than a migration that must be perfect, and no store
sits in the live ingest path.

| Store | Holds | Why this one |
|---|---|---|
| PostgreSQL | Sessions, drivers, laps, stints, results | Relational data asked relational questions. "Every driver's Monza history since 2018" is one indexed query. |
| InfluxDB | Per-lap telemetry channels | A genuine time series: ~20,000 samples per channel per driver per race. |
| MongoDB | Analysis documents, race control | Variable-shape JSON that changes between eras — 2026 gained an overtake counter and lost DRS. |

**Every one is optional.** Unconfigured they report themselves unavailable and
every read falls back to the archive, so a small self-hosted box still runs two
containers and no databases. They start under `--profile stores`.

**The cost, stated plainly.** This backend had zero NuGet dependencies; it now
has two. PostgreSQL and MongoDB speak binary wire protocols with no HTTP
surface, so there was nothing to hand-roll against. InfluxDB deliberately has no
package: its API is line protocol over HTTP, which is two requests.

**Verified, not assumed.** Backfilling the archive indexed 2 sessions and 21
telemetry files in 13.7 s across all three. The cross-season query that files
could not answer — a driver's best lap at one circuit across two seasons —
returns instantly, and Influx reports 148 laps above 330 km/h with per-driver top
speeds of 344–351 km/h at Monza, which is right for that circuit.

**Two bugs this work exposed, both in existing code.**

*Telemetry files are named by racing number.* Tagging the time series with it
would have made a query for one driver return several people's careers, since
numbers are reassigned between seasons — the exact trap the schema notes warn
about for `drivers`. The analysis carries the mapping, so the code resolves the
driver code before writing.

*`Slug` did not fold diacritics.* F1 publishes "São Paulo Grand Prix" with the
accent, and `char.IsLetterOrDigit` keeps 'ã', so a user typing "Sao Paulo" —
the natural thing to type — matched nothing. The first fix used Unicode
normalisation and a test proved it did nothing at all: this project builds with
`InvariantGlobalization`, where `string.Normalize` silently returns the accented
character unchanged. The fold is now an explicit table.

## D-014 · Redis still not adopted

Asked again alongside the three databases, and the answer is still no, for a
different reason than D-009's.

Redis cannot speed up the live path because the live path has no network hop to
remove. Fan-out is one serialise and N in-process socket writes; a round trip to
Redis would make it slower, not faster. The measurements stand: 0.52% CPU and
58 MB resident for a full session.

There is now exactly one place it would help — caching the results of the
cross-session Postgres aggregations and Flux queries added in D-013, which are
the only genuinely expensive reads in the system. That is a real use, but it is
premature: those queries are new and none has yet been shown to be slow. The
trigger is written down instead — **when a cross-session query is measured above
roughly 200 ms and is requested repeatedly, a Redis cache in front of it is the
right answer** — so the decision is waiting on a number rather than on taste.
