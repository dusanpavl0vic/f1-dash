# 19 — Docker

> Everything runs in Docker, development included. The only things required on the host are Docker
> and an editor — no .NET SDK, no Python, no Node installation.
>
> Supersedes `13-deployment.md` §compose. The rest of `13` — ports, replica rules, the reverse
> proxy configuration, the IP-blocking discussion and the observability table — still applies.

---

## 1. Services

| Service | Base image | Port | Replicas | Profile |
|---|---|---|---|---|
| `redis` | `redis:7-alpine` | 6379, internal only | 1 | default |
| `ingest` | `dotnet/runtime:9.0-alpine` | none | **exactly 1** | default |
| `realtime` | `dotnet/aspnet:9.0-alpine` | 4000 | 1–N | default |
| `api` | `dotnet/aspnet:9.0-alpine` | 4001 | 1–N | default |
| `web` | `nginx:alpine` (built SPA) | 3000 | 1–N | default |
| `simulator` | `dotnet/aspnet:9.0-alpine` | 8000 | 1 | `dev` |
| `precompute` | `python:3.12-slim` | none | on demand | `tools` |

`ingest` uses `strategy: Recreate`, never a rolling update. Two instances must not overlap even for
the seconds of a deployment — two SignalR connections double every delta and risk rate-limiting.
The Redis singleton lease (`docs/16` §5.1) is the runtime backstop for when this rule is violated
by accident.

---

## 2. Commands

| Command | Result |
|---|---|
| `docker compose up` | Production-shaped stack: redis, ingest, realtime, api, web |
| `docker compose --profile dev up` | Adds the simulator and points ingest at it via `F1_DEV_URL` |
| `docker compose --profile tools run --rm precompute --year 2024 --round 1 --session R` | Build one replay bundle |
| `docker compose run --rm tests` | Full test suite, identical to CI |
| `docker compose run --rm archive --year 2024 --round 1 --session R` | Download a session for fixtures |

**The second command is the one used for most development**, since sessions run on roughly 24
weekends a year and the whole stack must be developable on the other 340 days.

---

## 3. Build discipline

- **Multi-stage.** SDK image compiles, runtime image ships. No SDK, source or build cache in the
  published image.
- **Non-root user** in every image.
- **Pinned base image digests**, not floating tags, so a rebuild in six months produces the same image.
- `InvariantGlobalization=true` on the .NET images — the app has no locale-dependent formatting.
- `ServerGarbageCollection=true` for `realtime`; leave it off for `ingest`, whose workload is
  effectively single-threaded.
- Consider `PublishAot` for `ingest`: viable because serialisation is source-generated and needs no
  reflection.
- **Health checks on every service**, with `depends_on: condition: service_healthy`, so start-up
  ordering is deterministic rather than racy.
- `.dockerignore` excludes `data/`, `bin/`, `obj/`, `node_modules/` — otherwise the build context
  includes gigabytes of recorded sessions.

---

## 4. Volumes

| Mount | Contents | Note |
|---|---|---|
| `./data:/data` | `archive/raw/**`, `archive/bundles/**` | ~300 MB per full session; plan 40–60 GB per season, or use S3/R2 instead |
| `fastf1-cache` | FastF1 disk cache | Cold loads take 30–90 s; this cache is what makes them bearable |
| `redis-data` | RDB snapshots | Optional. State is disposable — losing it costs one snapshot cycle |

---

## 5. Environment

Full variable reference is in `13-deployment.md` §"Environment variables". The ones that matter
most in daily use:

| Variable | Service | Effect |
|---|---|---|
| `F1_DEV_URL` | ingest | When set, reads from the simulator instead of the live feed. **The switch that makes out-of-season development possible.** |
| `F1_HTTP_PROXY` | ingest | Outbound proxy for the F1 endpoints, for the datacentre IP-blocking problem |
| `ORIGIN` | realtime, api | Comma-separated allowed origins; WS upgrades from other origins are rejected. This is the only access control in v1. |
| `MAX_CONCURRENT_REPLAYS` | realtime | Default 50 |
| `ARCHIVE_BACKEND` | ingest, api | `fs` or `s3` |

---

## 6. Production notes

- Put a reverse proxy in front and terminate TLS there.
- **`proxy_buffering off` on the WebSocket route is mandatory.** With buffering on, nginx batches
  frames and the entire latency design is wasted.
- `proxy_read_timeout 3600s` on the WS route.
- Enable gzip or brotli for the REST API and static assets, **not** for the WS route.
- `terminationGracePeriodSeconds: 60` so in-flight WebSocket connections drain on deploy.
- Ingest may need to run outside the cloud entirely — see `13-deployment.md` §"The IP blocking
  problem". It only needs outbound access to F1 and a route to Redis; everything else deploys
  normally.
