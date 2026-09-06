# 09 — REST API Contract

Target: `packages/api/`. FastAPI, port 4001. Stateless, all responses cacheable.

## Conventions

- All responses JSON, `Cache-Control` set explicitly on every endpoint.
- Errors: `{"error": {"code": "...", "message": "..."}}` with the matching HTTP status.
- Session type values: `FP1` `FP2` `FP3` `Q` `SQ` `S` `R`.

---

## Schedule

### `GET /api/schedule/{year}`
Season calendar.
```jsonc
{
  "year": 2026,
  "rounds": [
    { "round": 1, "name": "Bahrain Grand Prix", "officialName": "…",
      "country": "Bahrain", "location": "Sakhir", "circuitKey": 63,
      "sessions": [
        { "type": "FP1", "name": "Practice 1",
          "start": "2026-03-06T11:30:00Z", "end": "2026-03-06T12:30:00Z",
          "hasArchive": true, "hasBundle": true }
      ]
    }
  ]
}
```
`Cache-Control: public, max-age=3600`. Source: Jolpica + F1 static `{year}/Index.json`, merged.

### `GET /api/schedule/next`
The next upcoming session, plus a countdown. `max-age=60`.

### `GET /api/schedule/live`
```json
{ "live": true, "session": {...}, "startedAt": "..." }
```
Derived from Redis `f1:session_active` + `f1:state.session_info`. `max-age=10`.

---

## Sessions

### `GET /api/sessions/{year}/{round}/{type}`
Session metadata, driver list, final results if finished. `max-age=300` if live, `max-age=86400` if finalised.

### `GET /api/sessions/{year}/{round}/{type}/laps`
Lap table from FastF1: driver, lap number, lap time, S1/S2/S3, compound, tyre life, pit in/out, position. `max-age=86400`.

### `GET /api/sessions/{year}/{round}/{type}/results`
Final classification. `max-age=86400`.

### `GET /api/sessions/{year}/{round}/{type}/telemetry/{driver}`
Per-driver telemetry. Query: `?lap=fastest|<n>`.
```jsonc
{ "driver": "VER", "lap": 44,
  "channels": {
    "distance": [0, 12.4, ...],
    "speed":    [312, 314, ...],
    "throttle": [100, 100, ...],
    "brake":    [0, 0, ...],
    "gear":     [8, 8, ...],
    "rpm":      [11800, ...],
    "drs":      [0, 0, ...],
    "x":        [...], "y": [...]
  }
}
```
Parallel arrays, not array-of-objects — 3–5x smaller over the wire and directly consumable by uPlot. `max-age=86400`.

---

## Track geometry

### `GET /api/track/{circuit_key}/{year}`
```jsonc
{
  "circuitKey": 63,
  "circuitName": "Bahrain International Circuit",
  "rotation": 227,
  "path": "M 1234,-5678 L 1240,-5670 … Z",     // ready-to-use SVG path
  "viewBox": { "x": -8000, "y": -6000, "width": 16000, "height": 12000 },
  "corners": [ { "number": 1, "x": …, "y": …, "labelX": …, "labelY": … } ],
  "marshalSectors": [ { "number": 1, "path": "M … L …" } ],
  "drsZones": [ { "start": {"x":…,"y":…}, "end": {"x":…,"y":…} } ],
  "startFinish": { "x": …, "y": … }
}
```

The server does the rotation, Y-flip, path construction, and bbox — see `10-track-map.md`. The client renders directly with no maths. `Cache-Control: public, max-age=31536000, immutable`.

Upstream: MultiViewer. Cache the raw upstream response on disk too, so an upstream outage doesn't break the map.

---

## Replay

### `GET /api/replay/{year}/{round}/{type}/meta`
The `replay-meta` payload from `08-delay-and-replay.md`. 404 if not yet precomputed.

### `POST /api/replay/{year}/{round}/{type}/precompute`
Starts a background precompute job. `202 Accepted` with `{"jobId": "..."}`.

### `GET /api/replay/jobs/{job_id}/events`
Server-Sent Events progress stream:
```
data: {"stage":"downloading","progress":0.34}
data: {"stage":"parsing","progress":0.71}
data: {"stage":"done","bundlePath":"..."}
```

---

## Standings

### `GET /api/standings/{year}/drivers`
### `GET /api/standings/{year}/constructors`
Proxied from Jolpica, reshaped, `max-age=3600`.

---

## Caching strategy

Two layers:

1. **In-process TTL cache** (`cachetools.TTLCache`) for hot small responses — schedule, live status.
2. **Disk/S3 cache** for anything derived from FastF1 or MultiViewer, keyed by the request path. FastF1 cold loads are 30–90 s; never do one inside a request handler without a cache check first.

Add `X-Cache: HIT|MISS` to every response for debuggability.

## Rate limiting outbound

Jolpica limits to roughly 4 req/s and 500/hr unauthenticated. Wrap it in a token-bucket limiter and never call it per-request — refresh the calendar on a daily schedule into the disk cache and serve from there.

## Acceptance criteria

- [ ] `/api/schedule/2024` returns 24 rounds.
- [ ] `/api/track/63/2024` returns a valid SVG path string that renders a recognisable Bahrain layout.
- [ ] Every endpoint sets `Cache-Control`.
- [ ] A cold `/api/sessions/2024/1/R/laps` completes; a warm one returns in under 50 ms.
- [ ] Killing the MultiViewer upstream still serves `/api/track/...` from disk cache.
