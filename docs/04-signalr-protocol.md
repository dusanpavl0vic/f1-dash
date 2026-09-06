# 04 — SignalR Protocol Implementation

Target file: `packages/ingest/src/ingest/signalr/`

## Connection lifecycle

```
negotiate (HTTP GET)
    ↓  ConnectionToken + Set-Cookie
open WebSocket with token + cookie + exact headers
    ↓
send Subscribe invocation
    ↓
receive initial full-state response (message with "R" key)
    ↓
receive delta messages (messages with "M" key) ──┐
    ↑                                             │
    └──── ping/pong keep-alive ───────────────────┘
    ↓ on disconnect
reconnect with exponential backoff 1s → 30s, then re-negotiate
```

## Step 1 — Negotiate

```python
import json, urllib.parse, httpx

HUB = json.dumps([{"name": "Streaming"}], separators=(",", ":"))

async def negotiate(client: httpx.AsyncClient) -> tuple[str, str]:
    params = {"clientProtocol": "1.5", "connectionData": HUB}
    r = await client.get(
        "https://livetiming.formula1.com/signalr/negotiate",
        params=params,
        headers={"User-Agent": "BestHTTP", "Accept-Encoding": "gzip,identity"},
        timeout=15.0,
    )
    r.raise_for_status()
    token = r.json()["ConnectionToken"]
    cookie = r.headers.get("set-cookie", "")
    return token, cookie
```

Return both. The cookie is mandatory on the WS upgrade.

## Step 2 — WebSocket connect

```python
import websockets

async def connect(token: str, cookie: str):
    qs = urllib.parse.urlencode({
        "clientProtocol": "1.5",
        "transport": "webSockets",
        "connectionToken": token,
        "connectionData": HUB,
    })
    url = f"wss://livetiming.formula1.com/signalr/connect?{qs}"
    return await websockets.connect(
        url,
        additional_headers={          # websockets >= 14; older: extra_headers
            "User-Agent": "BestHTTP",
            "Accept-Encoding": "gzip,identity",
            "Cookie": cookie,
        },
        max_size=None,                # payloads can exceed the 1 MiB default
        ping_interval=None,           # we handle keep-alive ourselves
    )
```

> **Header case matters.** `User-Agent`, `Accept-Encoding`, `Cookie` exactly as written. Wrong case → HTTP 500. Missing → HTTP 400. Do not let a library normalise them to lowercase in a way the server rejects; if `websockets` misbehaves here, fall back to `aiohttp`.

> `max_size=None` — the initial state dump on a race can be several megabytes.

## Step 3 — Subscribe

```python
await ws.send(json.dumps({
    "H": "Streaming",
    "M": "Subscribe",
    "A": [TOPICS],       # TOPICS is a list[str]; note the extra nesting
    "I": 1,
}))
```

## Step 4 — Message shapes

Three shapes arrive on the socket.

### (a) Keep-alive
```json
{}
```
An empty object. Respond by doing nothing; just reset your idle timer. If no message of any kind arrives for `KEEPALIVE_TIMEOUT` (default 30 s), treat the connection as dead and reconnect.

### (b) Initial state response — has key `R`
```json
{
  "R": {
    "Heartbeat":   {...},
    "DriverList":  {"1": {...}, "44": {...}, "_kf": true},
    "TimingData":  {"Lines": {...}, "_kf": true},
    "Position.z":  "<base64 zlib>",
    ...
  },
  "I": "1"
}
```
This is the **full snapshot** for every subscribed topic. Seed your state accumulator from it. The `_kf: true` marker means "keyframe"; strip it.

### (c) Delta message — has key `M`
```json
{
  "C": "d-ABC123-...",
  "M": [
    { "H": "Streaming", "M": "feed", "A": ["TimingData", {"Lines": {"44": {...}}}, "2024-06-23T15:04:22.123Z"] },
    { "H": "Streaming", "M": "feed", "A": ["TrackStatus", {"Status": "2"}, "2024-06-23T15:04:22.456Z"] }
  ]
}
```

**A single message can carry multiple topic updates.** Iterate `M`; for each entry, `A[0]` is the topic name, `A[1]` is the payload, `A[2]` is a UTC timestamp string. Never assume `M` has length 1.

## Step 5 — Decoding `.z` topics

```python
import base64, zlib, json

def inflate(payload: str) -> dict:
    return json.loads(zlib.decompress(base64.b64decode(payload), -zlib.MAX_WBITS))
```

Applies to `Position.z` and `CarData.z`, in both the `R` snapshot and `feed` deltas.

After inflating, normalise the topic name by stripping the `.z` suffix — internally the topics are `Position` and `CarData`.

`Position` decoded shape:
```json
{"Position": [
  {"Timestamp": "2024-06-23T15:04:22.123Z",
   "Entries": {"1":  {"Status": "OnTrack", "X": 1234, "Y": -5678, "Z": 101},
               "44": {"Status": "OnTrack", "X": 2345, "Y": -6789, "Z": 99}}}
]}
```

`CarData` decoded shape:
```json
{"Entries": [
  {"Utc": "2024-06-23T15:04:22.123Z",
   "Cars": {"1": {"Channels": {"0": 11500, "2": 287, "3": 7, "4": 100, "5": 0, "45": 8}}}}
]}
```

**Channel numbers are the field names.** Map them:

| Channel | Meaning | Range |
|---|---|---|
| `0` | RPM | 0–15000 |
| `2` | Speed (km/h) | 0–360 |
| `3` | Gear (`nGear`) | 0–8 |
| `4` | Throttle (%) | 0–100 |
| `5` | Brake | 0 or 100 |
| `45` | DRS status | see below |

DRS values: `0,1` = off; `8` = eligible/detected; `10,12,14` = DRS open. Treat `>= 10` as active.

## Step 6 — Reconnection

```python
BACKOFF_INITIAL = 1.0
BACKOFF_MAX     = 30.0
BACKOFF_FACTOR  = 2.0
```

On any disconnect or exception: sleep `backoff`, then start again from **negotiate** (the token expires). Reset backoff to initial on a successful subscribe. Add ±20% jitter. Log every reconnect at `WARNING`.

Do **not** clear accumulated state on reconnect — the fresh `R` snapshot will overwrite it wholesale anyway, and keeping the old state means the dashboard doesn't blank out during a brief blip.

## Step 7 — Recording raw traffic

Every raw frame, before any parsing, is appended to the archive:

```
raw/{session_key}/{iso_date}.jsonl
```
Each line: `{"t": <unix_ms>, "raw": <the exact string received>}`

This gives us: regression fixtures, the simulator's input, and a rebuild path if our parser has a bug.

## Acceptance criteria for this module

- [ ] `negotiate()` returns a non-empty token against the live endpoint.
- [ ] A connection stays open for 10 minutes against a live or test-day session without an unhandled exception.
- [ ] `inflate()` round-trips a captured `Position.z` fixture to valid JSON with an `Entries` key.
- [ ] A synthetic `M` message containing 3 feed entries produces 3 separate topic updates.
- [ ] Killing the network for 5 s produces exactly one reconnect log line, and state resumes.
- [ ] Every raw frame lands in the `.jsonl` recording, byte-identical.
