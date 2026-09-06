# 03 — Data Sources

Every external endpoint the system touches. **Do not invent endpoints not listed here.**

---

## 1. F1 Live Timing — SignalR (live sessions)

The same feed that powers the official F1 TV and F1 app timing screens.

There are **two** SignalR variants in the wild. Implement the **legacy** one — it is the one that reliably works and the one f1-dash used.

### Legacy SignalR (ASP.NET SignalR 1.5) — PRIMARY

```
Negotiate: https://livetiming.formula1.com/signalr/negotiate
           ?clientProtocol=1.5
           &connectionData=[{"name":"Streaming"}]        (URL-encoded)

Connect:   wss://livetiming.formula1.com/signalr/connect
           ?clientProtocol=1.5
           &transport=webSockets
           &connectionToken=<url-encoded token from negotiate>
           &connectionData=[{"name":"Streaming"}]        (URL-encoded)
```

- `clientProtocol` is hardcoded to `1.5`.
- `connectionData` is the JSON-stringified hub list; only the `Streaming` hub is known.
- The negotiate response body contains `ConnectionToken`. Other fields (`KeepAliveTimeout`, `LongPollDelay`) can be ignored.
- **Grab the `Set-Cookie` header from the negotiate response** and send it on the WS upgrade. Without it the connection fails.

Required headers on the WS upgrade — **these are case-sensitive; the server returns 500 on wrong case and 400 on missing headers**:

```
User-Agent: BestHTTP
Accept-Encoding: gzip,identity
Cookie: <value from negotiate Set-Cookie>
```

### SignalR Core (newer) — FALLBACK ONLY

```
Negotiate: https://livetiming.formula1.com/signalrcore/negotiate?negotiateVersion=1
WebSocket: wss://livetiming.formula1.com/signalrcore
```

Requires the SignalR JSON protocol handshake and an `AWSALBCORS` load-balancer cookie. Implement behind a feature flag; do not make it the default path.

### Subscribing

After the socket is open, send exactly one message:

```json
{
  "H": "Streaming",
  "M": "Subscribe",
  "A": [["TimingData", "TimingAppData", "TimingStats", "DriverList",
         "RaceControlMessages", "TrackStatus", "WeatherData", "LapCount",
         "ExtrapolatedClock", "SessionInfo", "SessionStatus", "SessionData",
         "TopThree", "TeamRadio", "PitLaneTimeCollection",
         "Position.z", "CarData.z"]],
  "I": 1
}
```

Field meanings: `H` = hub, `M` = method, `A` = arguments (note: **array of array of string**), `I` = client-side request id.

### Topics — full reference

| Topic | Contents | Rate |
|---|---|---|
| `TimingData` | Per-driver gaps, intervals, sector times, mini-sector segments, lap times, pit in/out | bursty, high |
| `TimingAppData` | Stint history: tyre compound, tyre age, stint start lap | on change |
| `TimingStats` | Personal bests, best sectors, speed traps | on change |
| `DriverList` | Driver number, abbreviation (TLA), full name, team name, team colour, headshot URL | once + rare updates |
| `SessionInfo` | Meeting name, circuit key, circuit name, session type, start/end dates, **`Path`** | once |
| `SessionStatus` | `Inactive` / `Started` / `Aborted` / `Finished` / `Finalised` / `Ends` | on change |
| `SessionData` | Series of status/lap markers over the session | occasional |
| `LapCount` | `CurrentLap`, `TotalLaps` (race only) | per lap |
| `ExtrapolatedClock` | `Remaining`, `Extrapolating`, `Utc` — the session countdown | ~1 Hz |
| `TrackStatus` | `Status` code + `Message` (see table below) | on change |
| `RaceControlMessages` | Steward messages, flags, penalties, investigations, SC/VSC deployment | on event |
| `WeatherData` | `AirTemp`, `TrackTemp`, `Humidity`, `Pressure`, `Rainfall`, `WindDirection`, `WindSpeed` | ~1/min |
| `TopThree` | Top-3 widget data | on change |
| `TeamRadio` | Radio clip metadata + relative audio path | on event |
| `PitLaneTimeCollection` | Pit lane durations | on event |
| `Position.z` | **zlib-compressed** GPS X/Y/Z per car | ~4 Hz |
| `CarData.z` | **zlib-compressed** speed, RPM, gear, throttle, brake, DRS | ~4 Hz |

`TrackStatus.Status` codes:

| Code | Meaning |
|---|---|
| `1` | Track clear (green) |
| `2` | Yellow flag |
| `3` | (unused / SC deployed on some feeds) |
| `4` | Safety Car |
| `5` | Red flag |
| `6` | Virtual Safety Car deployed |
| `7` | VSC ending |

### `.z` decompression

Topics ending in `.z` arrive as a base64 string. Decode base64, then **raw DEFLATE with no zlib header** — in Python:

```python
import base64, zlib
raw = zlib.decompress(base64.b64decode(payload), -zlib.MAX_WBITS)
data = json.loads(raw)
```

The `-zlib.MAX_WBITS` (negative window bits) is essential. Using plain `zlib.decompress` will raise `zlib.error: incorrect header check`.

---

## 2. F1 Live Timing — Static Archive (replay + historical)

Every session since 2018 is available as static files. This is the backbone of the replay feature.

Base: `https://livetiming.formula1.com/static/`

### Discovery hierarchy

```
/static/Index.json
    → current year(s)

/static/{year}/Index.json
    → list of Meetings, each with its Sessions and their `Path`

/static/{year}/{meeting_path}/{session_path}/Index.json
    → { "Feeds": { "<Topic>": { "KeyFramePath": "<Topic>.json",
                                "StreamPath": "<Topic>.jsonStream" } } }
```

Concrete examples (these paths are real and valid):

```
https://livetiming.formula1.com/static/2024/Index.json
https://livetiming.formula1.com/static/2024/2024-06-23_Spanish_Grand_Prix/2024-06-23_Race/Index.json
https://livetiming.formula1.com/static/2024/2024-06-23_Spanish_Grand_Prix/2024-06-23_Race/SessionInfo.json
https://livetiming.formula1.com/static/2024/2024-06-23_Spanish_Grand_Prix/2024-06-23_Race/TrackStatus.jsonStream
```

There is also a "what's live right now" pointer:

```
https://livetiming.formula1.com/static/SessionInfo.json
```

Its `Path` field gives the current session's directory, e.g. `2021/2021-10-24_United_States_Grand_Prix/2021-10-24_Race/`.

### Feed file formats

- **`.json` (KeyFrame)** — a full snapshot of that topic at archive time.
- **`.jsonStream` (Stream)** — line-delimited. Each line is `<12-char timestamp><json payload>`, e.g.:

  ```
  00:00:12.345{"Status":"1","Message":"AllClear"}
  ```

  The first 12 characters are `HH:MM:SS.mmm` **relative to session start**. Everything after is the JSON payload. This is exactly the delta stream we need for replay.

- Files are served with a **UTF-8 BOM**. Decode with `utf-8-sig`, not `utf-8`.
- `.z` topics in the archive (`Position.z.jsonStream`, `CarData.z.jsonStream`) use the same base64+raw-deflate encoding.

### Team radio audio

`TeamRadio.jsonStream` entries contain relative paths. Full URL:

```
https://livetiming.formula1.com/static/{session_path}/{relative_path}
e.g. .../2021-04-18_Race/TeamRadio/MAXVER01_33_20210418_162030.mp3
```

(v2 feature — spec it but do not build in v1.)

---

## 3. MultiViewer Circuits API — track geometry

**This is how the track map is drawn.** Do not draw the track from a driver's telemetry lap — that produces a wobbly line that differs per driver.

```
GET https://api.multiviewer.app/api/v1/circuits/{circuitKey}/{year}
```

`circuitKey` comes from `SessionInfo.Meeting.Circuit.Key` in the live feed, or from the archived `SessionInfo.json`.

Response shape (key fields):

```jsonc
{
  "circuitKey": 63,
  "circuitName": "Bahrain International Circuit",
  "rotation": 227,            // degrees; rotate to match official map orientation
  "x": [ ... ],               // track centreline X coordinates
  "y": [ ... ],               // track centreline Y coordinates
  "corners":        [ { "trackPosition": {"x":…, "y":…}, "number": 1, "angle": …, "length": … } ],
  "marshalLights":  [ … ],
  "marshalSectors": [ { "trackPosition": {"x":…,"y":…}, "number": 1, "angle": …, "length": … } ],
  "candidateLaps":  [ … ]
}
```

**Critical property:** these X/Y coordinates are in the **same coordinate system** as `Position.z` from the live feed (units of ~1/10 metre). That is why live car dots land exactly on the outline with no fitting, scaling, or georeferencing.

Send a polite `User-Agent` identifying your app. Cache the response indefinitely per `(circuitKey, year)` — it never changes mid-season.

---

## 4. Jolpica F1 API (Ergast successor) — schedule & results

Ergast was retired; Jolpica is the drop-in community replacement.

```
Base: https://api.jolpi.ca/ergast/f1/

GET /{year}.json                       season calendar
GET /{year}/{round}/results.json       race results
GET /{year}/{round}/qualifying.json    qualifying results
GET /{year}/driverStandings.json       championship standings
GET /{year}/constructorStandings.json
```

Used for: the schedule page, championship standings, and cross-referencing round numbers.
Rate limits apply (roughly 4 req/s, 500/hr unauthenticated) — **cache aggressively**, refresh the calendar daily at most.

---

## 5. FastF1 (Python library) — precomputed replay bundles

`fastf1 >= 3.8` wraps the same F1 endpoints and does the hard parsing work. Use it in the `api` service for **precomputing replay bundles** and historical analysis, not for live ingest.

```python
import fastf1
fastf1.Cache.enable_cache(FASTF1_CACHE_DIR)

session = fastf1.get_session(year, round_num, session_type)  # 'FP1','Q','R','S'
session.load(telemetry=True, laps=True, weather=True, messages=True)

session.results                  # driver metadata, final positions, team colours
session.laps                     # lap times, sectors, compound, tyre life, pit flags
session.get_circuit_info()       # corners, marshal sectors, rotation
lap.get_telemetry()              # Speed, Throttle, Brake, nGear, RPM, DRS, Distance, X, Y
session.race_control_messages
session.weather_data
fastf1.get_event_schedule(year)
```

Enable its disk cache (`FASTF1_CACHE_DIR`) — cold loads take 30–90 s per session.

---

## 6. Summary table

| # | Source | Endpoint | Used by | Required |
|---|---|---|---|---|
| 1 | F1 SignalR (live) | `wss://livetiming.formula1.com/signalr/connect` | `ingest` | live mode |
| 2 | F1 Static archive | `https://livetiming.formula1.com/static/` | `api`, `scripts` | replay |
| 3 | MultiViewer circuits | `https://api.multiviewer.app/api/v1/circuits/{key}/{year}` | `api` | track map |
| 4 | Jolpica | `https://api.jolpi.ca/ergast/f1/` | `api` | schedule |
| 5 | FastF1 (lib) | wraps 1 & 2 | `api` precompute | replay bundles |

## Reference implementations worth reading

| Project | Why |
|---|---|
| https://github.com/slowlydev/f1-dash | The reference. Rust + Next.js. AGPL. Read `signalr/` and `realtime/`. |
| https://github.com/theOehrly/Fast-F1 | Canonical parsing logic for every feed. Read `fastf1/api.py` and `fastf1/livetiming/`. |
| https://github.com/matteocelani/f1-telemetry | Node/Next monorepo doing the same thing; has good `docs/replay-mode.md` and payload-type docs. |
| https://dweik.xyz/post/f1-signalr-endpoint/ | Clearest written walkthrough of the legacy SignalR handshake. |
| https://github.com/GoktugOcal/LiveF1 | Documents the static archive URL hierarchy thoroughly. |
| https://docs.fastf1.dev/circuit_info.html | Circuit rotation / corner data semantics. |
