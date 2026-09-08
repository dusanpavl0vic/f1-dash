# 22 · Data architecture

Three stores, each doing the job it is actually good at, with the file archive
still underneath as the source of truth.

Supersedes the "no database" position in `DECISIONS.md` D-011 by decision of the
project owner. D-011's measurements are not withdrawn — reading a driver's whole
race telemetry from disk really does take 11 ms — and the design below is shaped
so that those measurements keep being true for the paths that already work.

---

## 1 · The rule that makes this safe

**`stream.jsonl` remains authoritative. Every database is a derived index.**

Any store can be dropped and rebuilt from the archive. That single rule decides
almost everything else:

- A corrupt or lost database is an inconvenience, never data loss.
- A schema change is a rebuild, not a migration script that must be perfect.
- The databases can be added to an existing deployment without a backfill window
  — they fill in as sessions are read.
- **No database sits in the live ingest path.** Delta merging writes to memory
  and to `stream.jsonl`, exactly as now. Everything else is written afterwards.

If that rule is ever broken, the reasoning in this document stops holding.

---

## 2 · Which store gets what, and why

| Store | Holds | Why this one |
|---|---|---|
| **PostgreSQL** | Sessions, drivers, teams, results, standings, laps, stints, pit stops | This data is relational and the questions asked of it are joins. "Every driver's Monza stint history since 2018" is one query with indexes, and a nightmare over files. |
| **InfluxDB** | Per-lap telemetry channels — speed, throttle, brake, RPM, gear, DRS | It is a time series by any definition: ~20,000 samples per channel per driver per race. Downsampling on read and retention policies are the two things Influx does that a file cannot. |
| **MongoDB** | Analysis documents, race control messages, session metadata | Variable-shape JSON that changes between eras. `analysis.json` gained `overtakes` in 2026 and lost `drs`; a relational table would need a migration for that, a document store does not. |

### What each one is NOT for

- **Postgres is not for telemetry.** 400,000 rows per race per driver is a
  time-series workload wearing a relational costume.
- **Influx is not for anything with a join.** It has no joins.
- **Mongo is not for the leaderboard.** Standings are relational; putting them
  in documents means recomputing aggregations the database could have done.

---

## 3 · Schemas

### PostgreSQL

```sql
-- The catalogue. One row per session that exists, whether or not it is
-- downloaded, so the picker can list a season without touching the network.
CREATE TABLE sessions (
    id            BIGSERIAL PRIMARY KEY,
    year          SMALLINT     NOT NULL,
    round         SMALLINT,
    meeting_name  TEXT         NOT NULL,
    meeting_slug  TEXT         NOT NULL,
    session_name  TEXT         NOT NULL,
    session_slug  TEXT         NOT NULL,
    session_type  TEXT         NOT NULL,
    circuit_key   INTEGER,
    circuit_name  TEXT,
    country_code  TEXT,
    start_utc     TIMESTAMPTZ,
    end_utc       TIMESTAMPTZ,
    -- Whether stream.jsonl is on disk. The archive is still the truth; this
    -- column only saves a filesystem walk.
    downloaded    BOOLEAN      NOT NULL DEFAULT FALSE,
    total_laps    SMALLINT,
    ingested_at   TIMESTAMPTZ,
    UNIQUE (year, meeting_slug, session_slug)
);
CREATE INDEX sessions_year_idx    ON sessions (year, round);
CREATE INDEX sessions_circuit_idx ON sessions (circuit_key, year);

CREATE TABLE drivers (
    id            BIGSERIAL PRIMARY KEY,
    -- Jolpica's stable id. The racing number is NOT a key: it changes between
    -- seasons and is reused.
    driver_ref    TEXT         NOT NULL UNIQUE,
    code          TEXT,
    first_name    TEXT,
    last_name     TEXT,
    nationality   TEXT,
    country_code  TEXT
);

CREATE TABLE session_entries (
    session_id    BIGINT       NOT NULL REFERENCES sessions(id) ON DELETE CASCADE,
    driver_id     BIGINT       NOT NULL REFERENCES drivers(id),
    racing_number SMALLINT     NOT NULL,
    tla           TEXT         NOT NULL,
    team_name     TEXT,
    team_colour   TEXT,
    PRIMARY KEY (session_id, driver_id)
);

CREATE TABLE laps (
    session_id    BIGINT       NOT NULL REFERENCES sessions(id) ON DELETE CASCADE,
    driver_id     BIGINT       NOT NULL REFERENCES drivers(id),
    lap           SMALLINT     NOT NULL,
    lap_time_ms   INTEGER,
    sector1_ms    INTEGER,
    sector2_ms    INTEGER,
    sector3_ms    INTEGER,
    position      SMALLINT,
    -- The track status DURING the lap, so safety-car laps can be excluded from
    -- pace analysis rather than skewing it.
    track_status  SMALLINT,
    is_pit_out    BOOLEAN      NOT NULL DEFAULT FALSE,
    is_pit_in     BOOLEAN      NOT NULL DEFAULT FALSE,
    PRIMARY KEY (session_id, driver_id, lap)
);
-- The index that makes "fastest laps at this circuit" fast.
CREATE INDEX laps_time_idx ON laps (session_id, lap_time_ms)
    WHERE lap_time_ms IS NOT NULL;

CREATE TABLE stints (
    session_id    BIGINT       NOT NULL REFERENCES sessions(id) ON DELETE CASCADE,
    driver_id     BIGINT       NOT NULL REFERENCES drivers(id),
    stint         SMALLINT     NOT NULL,
    compound      TEXT         NOT NULL,
    is_new        BOOLEAN,
    start_lap     SMALLINT     NOT NULL,
    end_lap       SMALLINT,
    laps_on_tyre  SMALLINT,
    PRIMARY KEY (session_id, driver_id, stint)
);

CREATE TABLE results (
    session_id    BIGINT       NOT NULL REFERENCES sessions(id) ON DELETE CASCADE,
    driver_id     BIGINT       NOT NULL REFERENCES drivers(id),
    position      SMALLINT,
    grid          SMALLINT,
    points        NUMERIC(5,2) NOT NULL DEFAULT 0,
    status        TEXT,
    laps_completed SMALLINT,
    PRIMARY KEY (session_id, driver_id)
);
```

**Why `driver_ref` and not the racing number.** Numbers are reassigned between
seasons and drivers change them. Keying on a number silently merges two people's
careers, and the join looks correct while it does it.

### InfluxDB

One measurement per channel rather than one wide measurement, because the
channels are queried independently and Influx charges for cardinality:

```
measurement: speed | throttle | brake | rpm | gear | drs
tags:        session (year-meeting-session slug)
             driver  (TLA)
             lap     (as a tag, not a field — it is what queries group by)
fields:      value   (integer)
timestamp:   the sample's own time within the session
```

Retention: telemetry for the current season at full resolution, older seasons
downsampled to per-lap aggregates (min, max, mean). The raw samples stay in
`telemetry/*.jsonl` regardless, so downsampling loses nothing permanently.

### MongoDB

```
sessions_analysis   one document per session, the whole analysis.json
race_control        one document per message, indexed by session and lap
session_meta        era detection, feed quirks, which topics were published
```

Documents keyed by the same `year/meeting-slug/session-slug` triple Postgres
uses, so the two can be joined by the application without a shared transaction.

---

## 4 · Write paths

```
                       ┌──────────────────────────────────────────┐
  live feed / replay → │ StateAccumulator  (memory)               │ ← unchanged
                       └───────────────┬──────────────────────────┘
                                       │ writes, as now
                                       ▼
                            stream.jsonl  (authoritative)
                                       │
                    ┌──────────────────┴──────────────────┐
                    │  StorageIndexer — after the session  │
                    └──────┬───────────┬───────────┬───────┘
                           ▼           ▼           ▼
                      PostgreSQL   InfluxDB     MongoDB
```

The indexer runs when a session ends, and can be run again at any time over the
whole archive. It is idempotent: every write is an upsert keyed on the natural
key, so re-running it repairs rather than duplicates.

**Nothing in this diagram is on the live path.** A database being down delays
indexing; it cannot delay a position update.

---

## 5 · Read paths

| Question | Answered by | Built |
|---|---|---|
| Play this session | `stream.jsonl` — no database involved | ✅ unchanged |
| This session's analysis | Mongo, falling back to the documents on disk | ✅ |
| This driver's telemetry, this lap | `telemetry/*.jsonl` — 11 ms, no database | ✅ unchanged |
| Fastest laps at a circuit, every season | Postgres | ✅ `/api/insights/circuit/{slug}` |
| A driver's pace season by season | Postgres | ✅ `/api/insights/driver/{code}` |
| Every two-stop race in a season | Postgres | ✅ `/api/insights/strategies/{year}` |
| Every lap above a speed | InfluxDB | ✅ `/api/insights/speed` |
| Which sessions exist | **The archive index, not Postgres** | see below |

### Why the catalogue is not read from Postgres

An earlier draft of this document said the session list would come from Postgres
with a filesystem fallback. That was wrong, and it is recorded rather than
quietly deleted.

Postgres only knows sessions that have been **indexed**, which means sessions
that have a saved analysis. The archive index lists every session that exists,
including ones never downloaded. Making Postgres primary would therefore *hide*
sessions from the picker — the database would be authoritative about something
it has an incomplete view of, which is exactly the failure the "archive is
authoritative" rule exists to prevent.

The fallbacks are the point. Every read that works today keeps working with all
three containers stopped.

---

## 6 · Configuration

All three are optional and off unless configured:

| Variable | Effect when unset |
|---|---|
| `POSTGRES_URL` | Catalogue reads fall back to the filesystem walk |
| `INFLUX_URL`, `INFLUX_TOKEN`, `INFLUX_BUCKET` | Cross-session telemetry queries return "not indexed" |
| `MONGO_URL` | Analysis reads fall back to `analysis.json` |

A deployment that sets none of them behaves exactly as the application did
before this document existed. That is deliberate: self-hosting on a small box
should not require running three databases.
