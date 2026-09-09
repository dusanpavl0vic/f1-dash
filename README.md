# f1-dash

Self-hostable web dashboard for Formula 1 live timing and telemetry — live timing tower, track map,
telemetry, race control, broadcast delay and full session replay.

> **Unofficial.** This project is not associated in any way with the Formula 1 companies.
> F1, FORMULA ONE, FORMULA 1, FIA FORMULA ONE WORLD CHAMPIONSHIP, GRAND PRIX and related marks are
> trade marks of Formula One Licensing B.V.

---

## Two applications

```
f1-dash/
├── web/       Frontend  — React 19 + Vite + TypeScript
├── backend/   Backend   — .NET 9 (feed ingest, WebSocket fanout, REST)
├── docs/      Specification, use cases, architecture report
├── design/    Claude Design handoff — the visual source of truth
└── compose.yaml
```

| | `web` | `backend` |
|---|---|---|
| Stack | React 19, Vite, TypeScript, Tailwind | .NET 9, Kestrel |
| Port | 3000 | 4000 |
| Owns | Rendering, delay buffer, position interpolation | F1 feed connection, state merge, WebSocket fanout, REST, replay |

## Running it

The only host requirement is Docker. No .NET SDK, no Node install — even the
build toolchain runs in containers.

```bash
docker compose up -d          # backend on :4000, web on :3000
```

Open <http://localhost:3000>. A fresh clone has **no data at all** — `data/` is
gitignored — and that is fine: the session picker lists every session back to
2018 from F1's own archive index, and downloading one takes a few seconds.

Verified from an empty directory: 75 sessions listed for 2024, none local;
choosing one downloaded 45 MB in 9 seconds and it replayed with 20 drivers and
real lap times.

### With the databases

```bash
docker compose --profile stores up -d       # + postgres, influxdb, mongo
cp .env.example .env                        # connection strings
```

Then fill the indexes once:

```bash
curl -XPOST localhost:4000/api/storage/backfill   # laps, stints, analysis, telemetry
curl -XPOST localhost:4000/api/storage/streams    # the raw sessions themselves
```

Both are idempotent. `GET /api/storage` says which stores are live.

Every store is **optional**. With none configured the application behaves exactly
as it did before they existed — the archive on disk is the only store, and
`/insights` explains how to turn them on rather than showing an empty table.

### Useful environment variables

| Variable | Default | Effect |
|---|---|---|
| `ARCHIVE_PATH` | `/data/archive` | Where sessions are stored |
| `REPLAY_STREAM` | 2024 Monza race | Session to play on startup; absent, it waits for a choice |
| `F1_LIVE` | unset | `1` connects to the live feed instead |
| `F1_RELAY` | unset | `1` waits for a collector (see `docs/22`) |
| `POSTGRES_URL` / `INFLUX_URL` / `MONGO_URL` | unset | Enable each index |

## Where the data comes from

Four sources, none of them requiring an account or a key.

| Source | Provides | Notes |
|---|---|---|
| **F1 live timing** — `livetiming.formula1.com/static/` | Every session since 2018, and live sessions as they run | S3 behind CloudFront. This is the backbone: the session archive and the replay fixtures both come from here |
| **F1 SignalR** — `livetiming.formula1.com/signalrcore/` | The live feed as a socket | Lowest latency. The origin can refuse a server's address, which is why the static path above is also a live source — see `docs/22` |
| **Jolpica** — `api.jolpi.ca/ergast/f1` | Schedule, results, championship standings | The Ergast successor. Only these pages depend on it |
| **MultiViewer** — `api.multiviewer.app` | Circuit outlines for the track map | Cached on disk after the first fetch |

Nothing is scraped and nothing is fabricated. Every number on screen came from
one of these four, and where a value is estimated rather than measured — tyre
wear, degradation — the interface says so on the panel.

**This project is unofficial and unaffiliated with the Formula 1 companies.**

## Documentation

| Document | Contents |
|---|---|
| `docs/00-START-HERE.md` | Entry point and document map |
| `docs/15-use-cases.md` | Product use cases — the executable specification |
| `docs/20-implementation-plan.md` | Implementation use cases — the actual work order |
| `docs/16-dotnet-architecture.md` | Backend architecture |
| `docs/17-frontend-architecture.md` | Frontend architecture |
| `docs/Apex-Architecture.pdf` | Technical report — 17 pages, 8 diagrams |
| `docs/22-data-architecture.md` | The three stores, schemas, and the two deployment modes |
| `DECISIONS.md` | Every deviation from the original specification, with reasoning |

## Branching

- `main` — released, working state only
- `dev` — integration branch, all work lands here
- `feat/*` — larger pieces of work, merged into `dev` once they run

## Licence

AGPL-3.0
