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

## Quick start

```bash
docker compose up
```

Everything runs in containers. The only host requirements are Docker and an editor —
no .NET SDK, no Node installation needed.

For development against a recorded session (no live race required):

```bash
docker compose --profile dev up
```

## Documentation

| Document | Contents |
|---|---|
| `docs/00-START-HERE.md` | Entry point and document map |
| `docs/15-use-cases.md` | Product use cases — the executable specification |
| `docs/20-implementation-plan.md` | Implementation use cases — the actual work order |
| `docs/16-dotnet-architecture.md` | Backend architecture |
| `docs/17-frontend-architecture.md` | Frontend architecture |
| `docs/F1-Dash-Architecture-Report.pdf` | Technical report with diagrams |
| `DECISIONS.md` | Every deviation from the original specification, with reasoning |

## Branching

- `main` — released, working state only
- `dev` — integration branch, all work lands here
- `feat/*` — larger pieces of work, merged into `dev` once they run

## Licence

AGPL-3.0
