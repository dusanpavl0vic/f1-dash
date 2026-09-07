# 21 — What changed in the 2026 feed

> Every statement here was verified against the recorded **2026 Italian Grand
> Prix** (Monza, 6 September 2026) and compared against the **2024 Italian Grand
> Prix** on the same circuit — same track, same session type, two seasons apart,
> so differences are regulation changes rather than circuit quirks.

## The headline: DRS is gone from the feed

```
CarData channels, 2024 Monza : 0, 2, 3, 4, 5, 45
CarData channels, 2026 Monza : 0, 2, 3, 4, 5
                                          ^^ channel 45 (DRS) absent
```

Race control confirms it:

```
2024:  2 of  59 messages mention DRS   (e.g. "DRS DISABLED")
2026:  0 of 154 messages mention DRS
```

**The 2026 regulations replaced DRS with active aerodynamics (X-mode / Z-mode
wings) and a Manual Override electrical boost. The public timing feed exposes
neither.** There is no new channel for wing state or override deployment — the
data simply is not published. Anything a dashboard showed for "override" would
be invented, so this project shows nothing.

## What was added instead

Four new topics appear in 2026 and in no 2024 session:

| Topic | Contents | Useful? |
|---|---|---|
| **`OvertakeSeries`** | Per-driver overtake counter with timestamps: `{"Overtakes":{"81":[{"Timestamp":"…","count":1}]}}` | **Yes** — this is what fills the column DRS used to occupy |
| **`PitStop`** | One stop: `{"RacingNumber":"27","PitStopTime":"2.4","PitLaneTime":"24.246","Lap":"27"}` | **Yes** — stationary time was never published before |
| `PitStopSeries` | The same, accumulated | Redundant with `PitStop` |
| `DriverTracker` | A parallel timing projection: `Position`, `LapState`, `DiffToAhead`, `DiffToLeader` | Duplicates `TimingData`; not adopted |

## A correction worth recording

A first pass at this analysis reported that channel 3 had also changed, because
its range went from `0–8` in 2024 to `0–128` in 2026. That was wrong. The
distribution shows it is still the gear:

```
speed   0- 49 km/h : mean ch3 = 0.05
speed  50- 99 km/h : mean ch3 = 1.13
speed 100-149 km/h : mean ch3 = 2.67
speed 150-199 km/h : mean ch3 = 4.26
speed 200-249 km/h : mean ch3 = 5.51
speed 250-299 km/h : mean ch3 = 6.95
speed 300-349 km/h : mean ch3 = 7.83
```

99.99% of samples are 0–8 and the mean rises cleanly with speed. The `max=128`
came from a handful of corrupt readings — fewer than 40 samples out of 330,000.
**Reading a maximum instead of a distribution produced a confident wrong
answer**, which is the argument for looking at distributions before concluding
anything about this feed.

## How the application handles both eras

The dashboard must render 2018 through 2026 from the same code, so the era is
detected from the data rather than from the season number:

| Condition | Column shown |
|---|---|
| Any car reports CarData channel 45 | **DRS** — active at `>= 10` |
| No channel 45, `OvertakeSeries` present | **OVT** — overtakes completed |
| Neither | Column hidden |

Detecting from the payload rather than from the year means a mid-season feed
change degrades instead of breaking, and a 2026 session replayed from an
archive behaves identically to a live one.
