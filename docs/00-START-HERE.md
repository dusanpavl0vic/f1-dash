# F1 Live Timing & Telemetry — Web App Specification

> **This is the entry point. Read this file first, then follow the build order in `12-build-order.md`.**

> ### ⚠ Stack decisions below are superseded
> The **Tech stack** table in this file describes the original Python/FastAPI + Next.js plan.
> Those choices changed on 2026-09-06 and the reasoning is recorded in `/DECISIONS.md`.
>
> | Superseded here | Now authoritative |
> |---|---|
> | Python/FastAPI backend | `16-dotnet-architecture.md` — .NET 9, plus a Python worker for FastF1 only |
> | Next.js + Zustand frontend | `17-frontend-architecture.md` — Vite SPA per `REACT-APP-TEMPLATE.md` |
> | `compose.yaml` sketch in `13` | `19-docker.md` — Docker-first, development included |
>
> **Unchanged and still authoritative:** the F1 wire protocol (`03`, `04`), field names and the
> data model (`05`), the merge algorithm's rules (`06`), delay and replay semantics (`08`), and
> the track-map mathematics (`10`). Everything below about *what the product is* still holds.

## What we are building

An open-source, self-hostable **web** dashboard for Formula 1 live timing and telemetry — functionally equivalent to [f1-dash](https://github.com/slowlydev/f1-dash) (now sunset) and [Nitrous](https://nitrous.software/) (desktop-only), but browser-based.

Core capability set:

| Feature | Description |
|---|---|
| Timing tower | Live leaderboard: position, gap, interval, sector times, mini-sectors, tyre compound & age, pit status, driver status (OUT/PIT/RETIRED) |
| Track map | 2D circuit outline with live car dots, driver colours, DRS zones, marshal sectors coloured by flag state |
| Telemetry | Speed / throttle / brake / gear / RPM / DRS traces per driver, two-driver comparison |
| Race control | Flags, safety car / VSC, investigations, penalties, session status |
| Weather | Air & track temp, humidity, wind, rainfall |
| Broadcast delay | User-configurable client-side buffer (0–120s) to sync the dashboard with a TV stream that lags the data feed |
| Replay | Full session replay from archived data with play / pause / seek / speed control |
| Schedule | Past and upcoming sessions, results |

## Tech stack (decided)

| Layer | Choice | Why |
|---|---|---|
| Ingest + API | **Python 3.12 + FastAPI** | Developer already works in FastAPI; the F1 ecosystem (FastF1) is Python-native |
| Realtime transport | **WebSocket** (FastAPI native) | Push deltas to browser |
| State store | **Redis** | Session state snapshot + pub/sub fanout across API workers |
| Archive store | **Filesystem or S3-compatible** (MinIO / Cloudflare R2) | Recorded raw streams + precomputed replay bundles |
| Frontend | **Next.js 15 (App Router) + TypeScript** | Same as f1-dash |
| State mgmt | **Zustand** | Lightweight, works well with a high-frequency delta stream |
| Styling | **Tailwind CSS v4** | |
| Charts | **uPlot** (telemetry traces) | Far faster than Recharts at 10 Hz+ |
| Track map | **Raw SVG** (no library) | See `10-track-map.md` |
| Container | **Docker Compose** | |

> If Claude Code is told to substitute any of these, it must record the deviation in `DECISIONS.md` at repo root.

## Document map

| File | Contents |
|---|---|
| `01-goals-and-scope.md` | Product goals, non-goals, legal notice |
| `02-architecture.md` | Services, process boundaries, data flow diagram |
| `03-data-sources.md` | **Every external endpoint, with exact URLs** |
| `04-signalr-protocol.md` | How to connect to and decode the F1 live feed |
| `05-data-model.md` | Canonical internal state shape (TypeScript + Pydantic) |
| `06-ingest-service.md` | Step-by-step: the ingest worker |
| `07-state-and-websocket.md` | Step-by-step: state manager + WS fanout |
| `08-delay-and-replay.md` | Broadcast delay buffer + full session replay engine |
| `09-rest-api.md` | REST endpoint contract |
| `10-track-map.md` | Track geometry + rendering live car positions |
| `11-frontend.md` | Next.js app structure, components, stores |
| `12-build-order.md` | **Phased implementation plan — the actual work order** |
| `13-deployment.md` | Docker, compose, env vars, k8s notes |
| `14-testing.md` | Recording fixtures, the simulator, test strategy |
| `15-use-cases.md` | **Formal use cases — the executable specification** |
| `16-dotnet-architecture.md` | .NET solution layout, primitive mapping, performance and reliability design |
| `17-frontend-architecture.md` | Vite SPA structure, applied from `REACT-APP-TEMPLATE.md` |
| `18-prerequisites.md` | What the operator must provide, and when |
| `19-docker.md` | Docker topology, commands, build discipline |
| `DESIGN-BRIEF.md` | Self-contained brief for the visual design |
| `F1-Dash-Architecture-Report.pdf` | Technical report with architecture diagrams |
| `REACT-APP-TEMPLATE.md` | Frontend code-organisation template |

## Rules for the implementing agent

1. **Follow `12-build-order.md` phase by phase.** Do not start Phase 3 before Phase 2 has a passing smoke test.
2. **Build the simulator before the live client is verifiable.** F1 sessions happen ~20 days a year. Everything must be developable from recorded data. See `14-testing.md`.
3. **Never invent field names.** The F1 feed's field names are given verbatim in `05-data-model.md`. If a field is not documented here, log it and surface it — do not silently drop or rename it.
4. **The feed is delta-based, not snapshot-based.** Every design decision follows from this. Read `07-state-and-websocket.md` before writing any state code.
5. **Rate-limit nothing on the ingest side, buffer everything on the client side.** Dropping frames server-side breaks the delay feature.
6. Write tests as you go. Each phase in the build order lists its own acceptance criteria.
