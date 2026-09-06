# 18 — Prerequisites: what you need to provide

> Grouped by when it becomes necessary. Verification commands are included so each item can be
> confirmed rather than assumed.

---

## A · Required to start (blocks all work)

### A1 — Docker

The only tool needed on the host. Everything else — .NET SDK, Python, Node — runs inside
containers and does not need to be installed.

```bash
docker --version          # expect 24+
docker compose version    # expect v2.20+
```

Allocate at least **6 GB of memory and 40 GB of disk** to Docker Desktop. Building four .NET images
plus a Node build with the default 2 GB will fail or crawl.

> Checked on this machine: `dotnet` and `go` are **not** installed locally. That is fine and
> expected — the Docker-first setup means they are never needed on the host.

### A2 — Nothing else

There are **no API keys, no accounts and no paid services** in this project. Every upstream — the
F1 live feed, the F1 static archive, MultiViewer and Jolpica — is public and unauthenticated. There
is no third-party dependency that can be revoked or priced out of existence.

---

## B · Required for the first real milestone (Phase 1)

### B1 — Disk space for recorded sessions

| Item | Size |
|---|---|
| One full session, raw | 50–300 MB |
| One full race weekend, all sessions | ~2 GB |
| A full season | 40–60 GB |
| Trimmed fixtures committed to git | < 20 MB total |

**Do not commit full sessions.** Commit trimmed five-minute slices under `shared-fixtures/`; keep
full sessions in a gitignored `data/`.

### B2 — Outbound internet from the container

Phase 1 downloads real sessions from `livetiming.formula1.com/static/`. This path is **not** IP
blocked — only the live feed is — so it works from anywhere, including a cloud machine.

Choose the fixtures deliberately; the whole project is verified against them:

| Fixture | Why it is needed |
|---|---|
| A normal dry race (2024 R1) | Baseline; the classification test |
| A qualifying session | Different session semantics — segments, elimination |
| A race with SC, VSC and a red flag (Monaco 2024) | Flag handling and restarts |
| A wet race | Intermediates and wets, changing conditions |
| A sprint weekend | The `S` and `SQ` session types |

---

## C · Required before going live (Phase 15)

### C1 — A machine for `ingest` that is not in a datacentre ⚠️ the one real constraint

Formula 1 applies IP blocking against known cloud provider ranges. An ingest process on a typical
VPS may simply never connect.

Options, in order of preference:

| Option | Notes |
|---|---|
| **A home server, mini PC or Raspberry Pi** on a residential connection | The reliable answer. It needs only outbound access to F1 and a route to Redis — a few hundred KB/s. Everything else deploys to the cloud normally. |
| A VPS from a provider outside the blocked ranges | Works until it does not; ranges change without notice. |
| An outbound proxy via `F1_HTTP_PROXY` | Adds a dependency and a failure mode. |
| Replay-only mode | Always available as a fallback. Schedule, standings, results and replay all keep working; only the live dashboard is affected. |

**This is worth deciding early**, because it shapes the deployment topology. The architecture is
built so that only `ingest` is affected — it is the one component with this constraint.

### C2 — Storage for the archive

Local disk via the `./data` volume, or S3-compatible object storage (MinIO, Cloudflare R2, AWS S3).
If object storage: an endpoint, a bucket, an access key and a secret. R2 is the cheapest sensible
option because it has no egress fees.

### C3 — A race weekend reserved for validation

Phase 15 cannot be done from archives. The live feed carries edge cases recordings do not:
mid-session driver changes, red-flag restarts, feed dropouts, abandoned sessions.

Budget a **full session weekend**: run against the live feed during FP1, compare the timing tower
against the official F1 live timing page side by side, record the whole session, review every
unrecognised field logged, fix, then validate again on qualifying.

---

## D · Required for public deployment

| Item | Notes |
|---|---|
| Domain name | |
| TLS certificate | Let's Encrypt is fine |
| Reverse proxy | nginx, Caddy or Traefik. **Must set `proxy_buffering off` on the WebSocket route** — with buffering on, frames are batched and the entire latency design is wasted. |
| A host for redis, realtime, api, web | Any VPS. These never touch an F1 endpoint at request time, so IP blocking does not affect them. |

---

## E · Optional

| Item | Value |
|---|---|
| Prometheus + Grafana | Metrics are exposed at `/metrics` on every service whether or not anything scrapes them |
| A GitHub repository | For CI: merge-parity tests, the classification test, and the generated-types freshness check |
| Sentry or similar | Frontend error tracking |

---

## F · Decisions still needed from you

| # | Decision | Why it matters | Default if you say nothing |
|---|---|---|---|
| 1 | Where `ingest` will run (§C1) | Shapes the deployment topology | Local development only; live mode deferred |
| 2 | Filesystem or S3 for the archive | Affects config and cost | Local filesystem via `./data` |
| 3 | Which fixture sessions to download first | Determines what gets tested first | 2024 R1 race + one qualifying |
| 4 | Public deployment, or self-hosted for yourself only | Affects hardening priority | Self-hosted, single user |
| 5 | Licence | `docs/01` recommends AGPL-3.0, matching f1-dash | AGPL-3.0 |

None of these block the start of work. Items 1 and 2 are needed by Phase 13; the rest can be
decided at any point.

---

## Summary

**To start today you need Docker and nothing else.** Everything through Phase 12 — including the
complete dashboard, the track map, telemetry and replay — is built and verified against recorded
data with no live session, no API key and no account.

The only genuine external constraint is C1: a non-datacentre connection for the ingest process,
and only when you want live sessions rather than replay.
