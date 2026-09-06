# 13 — Deployment

## Services and ports

| Service | Port | Replicas | Notes |
|---|---|---|---|
| `redis` | 6379 | 1 | AOF off, RDB every 5 min; state is disposable |
| `ingest` | — | **exactly 1** | never scale this |
| `api` | 4001 | 1–N | stateless |
| `realtime` | 4000 | 1–N | stateless; each holds its own client set |
| `dashboard` | 3000 | 1–N | Next.js standalone |
| `simulator` | 8000 | dev only | |

> `ingest` must be a singleton. Two SignalR connections double every delta and risk being rate-limited by F1. In k8s use a `Deployment` with `replicas: 1` and `strategy: Recreate` (not RollingUpdate — you do not want two running during a rollout).

## `compose.yaml`

```yaml
services:
  redis:
    image: redis:7-alpine
    command: redis-server --save 300 1 --appendonly no
    ports: ["6379:6379"]
    healthcheck:
      test: ["CMD", "redis-cli", "ping"]
      interval: 5s

  ingest:
    build: { context: ., dockerfile: packages/ingest/Dockerfile }
    environment:
      REDIS_URL: redis://redis:6379/0
      F1_DEV_URL: ${F1_DEV_URL:-}
      ARCHIVE_BACKEND: fs
      ARCHIVE_PATH: /data/archive
      LOG_LEVEL: INFO
    volumes: ["./data:/data"]
    depends_on: { redis: { condition: service_healthy } }
    restart: unless-stopped

  api:
    build: { context: ., dockerfile: packages/api/Dockerfile }
    environment:
      ADDRESS: 0.0.0.0:4001
      ORIGIN: http://localhost:3000
      REDIS_URL: redis://redis:6379/0
      ARCHIVE_PATH: /data/archive
      FASTF1_CACHE_DIR: /data/fastf1-cache
    volumes: ["./data:/data"]
    ports: ["4001:4001"]
    depends_on: { redis: { condition: service_healthy } }

  realtime:
    build: { context: ., dockerfile: packages/realtime/Dockerfile }
    environment:
      ADDRESS: 0.0.0.0:4000
      ORIGIN: http://localhost:3000
      REDIS_URL: redis://redis:6379/0
      ARCHIVE_PATH: /data/archive
      MAX_CONCURRENT_REPLAYS: 50
    volumes: ["./data:/data"]
    ports: ["4000:4000"]
    depends_on: { redis: { condition: service_healthy } }

  dashboard:
    build:
      context: .
      dockerfile: packages/dashboard/Dockerfile
      args: { SKIP_ENV_VALIDATION: "1", NEXT_STANDALONE: "1" }
    environment:
      NEXT_PUBLIC_LIVE_URL: http://localhost:4000
      NEXT_PUBLIC_API_URL: http://localhost:4001
    ports: ["3000:3000"]
```

Dev override (`compose.dev.yaml`) adds the `simulator` service and sets `F1_DEV_URL=ws://simulator:8000/ws`.

## Environment variables — complete reference

### ingest
```
REDIS_URL=redis://localhost:6379/0
F1_DEV_URL=                        # simulator WS; when set, live feed is not used
F1_HTTP_PROXY=                     # outbound proxy for livetiming.formula1.com
ARCHIVE_BACKEND=fs                 # fs | s3
ARCHIVE_PATH=./data/archive
S3_ENDPOINT= S3_BUCKET= S3_ACCESS_KEY_ID= S3_SECRET_ACCESS_KEY= S3_REGION=
KEEPALIVE_TIMEOUT=30
LOG_LEVEL=INFO
```

### api
```
ADDRESS=0.0.0.0:4001
ORIGIN=http://localhost:3000       # comma-separated
REDIS_URL=redis://localhost:6379/0
ARCHIVE_PATH=./data/archive
FASTF1_CACHE_DIR=./data/fastf1-cache
JOLPICA_BASE=https://api.jolpi.ca/ergast/f1
MULTIVIEWER_BASE=https://api.multiviewer.app/api/v1
PRECOMPUTE_ENABLED=true
PRECOMPUTE_CRON_DAYS=fri,sat,sun,mon
```

### realtime
```
ADDRESS=0.0.0.0:4000
ORIGIN=http://localhost:3000
REDIS_URL=redis://localhost:6379/0
ARCHIVE_PATH=./data/archive
MAX_CONCURRENT_REPLAYS=50
MAX_CLIENT_QUEUE=500
```

### dashboard
```
NEXT_PUBLIC_LIVE_URL=http://localhost:4000
NEXT_PUBLIC_API_URL=http://localhost:4001
```
Build-time: `SKIP_ENV_VALIDATION=1`, `NEXT_STANDALONE=1`, `NEXT_NO_COMPRESS=1` (set the last one when a reverse proxy handles compression).

## Reverse proxy

WebSockets need explicit upgrade handling and a long read timeout.

```nginx
location /ws {
    proxy_pass http://realtime:4000;
    proxy_http_version 1.1;
    proxy_set_header Upgrade $http_upgrade;
    proxy_set_header Connection "upgrade";
    proxy_set_header Host $host;
    proxy_set_header Origin $http_origin;
    proxy_read_timeout 3600s;
    proxy_buffering off;          # critical — buffering destroys latency
}
```

`proxy_buffering off` is not optional. With it on, nginx batches frames and the whole point of the system is lost.

Enable gzip/brotli for the REST API and the Next.js assets, **not** for the WS route.

## The IP blocking problem

Formula 1 has applied blocks against cloud provider IP ranges. Mitigations, in order of preference:

1. Host `ingest` on a residential or non-datacentre connection (a home server, a small VPS from a provider not in the blocked ranges) and let it write to a Redis instance in the cloud.
2. Route ingest's outbound traffic through a proxy via `F1_HTTP_PROXY`.
3. Fall back to replay-only mode. `api` and `realtime` do not touch F1 endpoints at request time, so the rest of the app works fine.

Design the app to degrade cleanly: if `ingest` cannot connect, the dashboard shows "no live session" rather than an error, and the replay and schedule sections stay fully functional.

## Observability

Each service exposes `/metrics` (prometheus-client):

| Metric | Service |
|---|---|
| `f1_ingest_frames_total{topic}` | ingest |
| `f1_ingest_connected` (0/1) | ingest |
| `f1_ingest_reconnects_total` | ingest |
| `f1_realtime_clients` | realtime |
| `f1_realtime_dropped_clients_total{reason}` | realtime |
| `f1_realtime_delta_lag_seconds` | realtime |
| `f1_api_cache_hits_total{endpoint}` | api |

Log JSON to stdout. During a session, log every unrecognised topic name and every unknown status code **once each** (dedupe by key) — this is how you discover feed changes before users report broken UI.

## Kubernetes notes

- `ingest`: `replicas: 1`, `strategy: Recreate`, PVC for the archive if using `fs` backend (or use S3 and skip the PVC).
- `realtime`: HPA on `f1_realtime_clients`, `sessionAffinity: ClientIP` on the Service is unnecessary (clients can hit any replica), but set `terminationGracePeriodSeconds: 60` so in-flight WS connections drain.
- Ingress must have WebSocket support and generous timeouts — for nginx-ingress: `nginx.ingress.kubernetes.io/proxy-read-timeout: "3600"`.
