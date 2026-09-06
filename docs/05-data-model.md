# 05 — Data Model

Target file: `packages/shared/src/f1shared/models.py` — **single source of truth**. TypeScript is generated from it.

## Design rule

Keep the F1 feed's own field names wherever they arrive. Do **not** rename `Lines` to `drivers` or `GapToLeader` to `gap`. Renaming means every future feed change silently breaks a mapping layer. Convert casing only at the presentation boundary if desired.

Exceptions where we *do* normalise:
- `Position.z` / `CarData.z` → topic keys `Position` / `CarData`.
- CarData channel numbers → named fields (the numbers are meaningless to a reader).
- `_kf` markers stripped.

## Top-level state

```python
class F1State(BaseModel):
    session_info:          SessionInfo | None = None
    session_status:        SessionStatus | None = None
    session_data:          SessionData | None = None
    extrapolated_clock:    ExtrapolatedClock | None = None
    lap_count:             LapCount | None = None
    track_status:          TrackStatus | None = None
    weather_data:          WeatherData | None = None
    driver_list:           dict[str, Driver] = {}          # keyed by car number as string
    timing_data:           TimingData | None = None
    timing_app_data:       TimingAppData | None = None
    timing_stats:          TimingStats | None = None
    race_control_messages: RaceControlMessages | None = None
    top_three:             TopThree | None = None
    team_radio:            TeamRadio | None = None
    position:              PositionBatch | None = None      # latest only, not history
    car_data:              CarDataBatch | None = None       # latest only
```

> `position` and `car_data` hold only the **most recent** batch in the live state. History is the client's job (for telemetry traces) or the replay bundle's job.

## Core models

```python
class SessionInfo(BaseModel):
    Meeting: Meeting
    ArchiveStatus: dict | None = None
    Key: int | None = None
    Type: str | None = None          # "Race" | "Qualifying" | "Practice" | "Sprint"
    Name: str | None = None
    StartDate: str | None = None
    EndDate: str | None = None
    GmtOffset: str | None = None
    Path: str | None = None          # archive path — keep it, replay needs it

class Meeting(BaseModel):
    Key: int | None = None
    Name: str | None = None
    OfficialName: str | None = None
    Location: str | None = None
    Country: Country | None = None
    Circuit: Circuit | None = None

class Circuit(BaseModel):
    Key: int | None = None           # ← this is the MultiViewer circuitKey
    ShortName: str | None = None


class Driver(BaseModel):
    RacingNumber: str
    BroadcastName: str | None = None     # "M VERSTAPPEN"
    FullName: str | None = None
    Tla: str | None = None               # "VER"
    Line: int | None = None              # display order
    TeamName: str | None = None
    TeamColour: str | None = None        # hex WITHOUT leading '#'
    FirstName: str | None = None
    LastName: str | None = None
    Reference: str | None = None
    HeadshotUrl: str | None = None
    CountryCode: str | None = None


class TimingData(BaseModel):
    Lines: dict[str, TimingDataDriver] = {}
    Withheld: bool | None = None

class TimingDataDriver(BaseModel):
    GapToLeader: str | None = None            # "+1.234" | "1 L" | ""
    IntervalToPositionAhead: Interval | None = None
    Line: int | None = None
    Position: str | None = None
    ShowPosition: bool | None = None
    RacingNumber: str | None = None
    Retired: bool | None = None
    InPit: bool | None = None
    PitOut: bool | None = None
    Stopped: bool | None = None
    Status: int | None = None
    Sectors: dict[str, Sector] = {}           # "0","1","2"  ← object-as-array
    Speeds: Speeds | None = None
    BestLapTime: PersonalBestLapTime | None = None
    LastLapTime: TimingValue | None = None
    NumberOfLaps: int | None = None
    NumberOfPitStops: int | None = None

class Sector(BaseModel):
    Stopped: bool | None = None
    Value: str | None = None                  # "28.412"
    PreviousValue: str | None = None
    Status: int | None = None
    OverallFastest: bool | None = None
    PersonalFastest: bool | None = None
    Segments: dict[str, Segment] = {}         # mini-sectors, object-as-array

class Segment(BaseModel):
    Status: int | None = None                 # see mini-sector status table

class Interval(BaseModel):
    Value: str | None = None
    Catching: bool | None = None

class TimingValue(BaseModel):
    Value: str | None = None
    Status: int | None = None
    OverallFastest: bool | None = None
    PersonalFastest: bool | None = None


class TimingAppData(BaseModel):
    Lines: dict[str, TimingAppDataDriver] = {}

class TimingAppDataDriver(BaseModel):
    RacingNumber: str | None = None
    Line: int | None = None
    GridPos: str | None = None
    Stints: dict[str, Stint] = {}             # object-as-array

class Stint(BaseModel):
    Compound: str | None = None               # SOFT MEDIUM HARD INTERMEDIATE WET
    New: str | None = None                    # "true" / "false" — STRING, not bool
    TyresNotChanged: str | None = None
    TotalLaps: int | None = None
    StartLaps: int | None = None
    LapFlags: int | None = None
    LapTime: str | None = None
    LapNumber: int | None = None


class TrackStatus(BaseModel):
    Status: str | None = None                 # "1".."7"
    Message: str | None = None                # "AllClear","Yellow","SCDeployed","Red","VSCDeployed","VSCEnding"

class LapCount(BaseModel):
    CurrentLap: int | None = None
    TotalLaps: int | None = None

class ExtrapolatedClock(BaseModel):
    Utc: str | None = None
    Remaining: str | None = None              # "01:23:45"
    Extrapolating: bool | None = None

class WeatherData(BaseModel):
    AirTemp: str | None = None
    Humidity: str | None = None
    Pressure: str | None = None
    Rainfall: str | None = None               # "0" / "1"
    TrackTemp: str | None = None
    WindDirection: str | None = None
    WindSpeed: str | None = None

class RaceControlMessages(BaseModel):
    Messages: dict[str, RaceControlMessage] = {}   # object-as-array

class RaceControlMessage(BaseModel):
    Utc: str | None = None
    Lap: int | None = None
    Category: str | None = None               # "Flag","Drs","SafetyCar","Other","CarEvent"
    Flag: str | None = None                   # "GREEN","YELLOW","DOUBLE YELLOW","RED","CHEQUERED","CLEAR","BLUE"
    Scope: str | None = None                  # "Track","Sector","Driver"
    Sector: int | None = None
    Message: str | None = None
    Status: str | None = None
    RacingNumber: str | None = None
```

## Position / CarData

```python
class PositionBatch(BaseModel):
    Position: list[PositionFrame] = []

class PositionFrame(BaseModel):
    Timestamp: str
    Entries: dict[str, PositionEntry] = {}

class PositionEntry(BaseModel):
    Status: str | None = None       # "OnTrack" | "OffTrack"
    X: int
    Y: int
    Z: int


class CarDataBatch(BaseModel):
    Entries: list[CarDataFrame] = []

class CarDataFrame(BaseModel):
    Utc: str
    Cars: dict[str, CarChannels] = {}

class CarChannels(BaseModel):
    """Normalised from the numeric channel map."""
    rpm: int | None = None          # channel 0
    speed: int | None = None        # channel 2
    gear: int | None = None         # channel 3
    throttle: int | None = None     # channel 4
    brake: int | None = None        # channel 5
    drs: int | None = None          # channel 45

    @classmethod
    def from_channels(cls, ch: dict[str, int]) -> "CarChannels":
        return cls(rpm=ch.get("0"), speed=ch.get("2"), gear=ch.get("3"),
                   throttle=ch.get("4"), brake=ch.get("5"), drs=ch.get("45"))
```

## Status code tables

### Mini-sector `Segment.Status`

| Value | Meaning | Colour |
|---|---|---|
| `0` | Not yet set | neutral / grey |
| `2048` | Yellow sector | yellow |
| `2049` | Green — personal best or normal | green |
| `2051` | Purple — overall fastest | purple |
| `2052` | Pitlane / not applicable | grey |
| `2064` | Blue — in pit | blue |

Anything unrecognised → render neutral and log once.

### Timing `Status` bitfield (`TimingDataDriver.Status`)

Not fully reverse-engineered. Use the explicit booleans (`InPit`, `PitOut`, `Retired`, `Stopped`) as the source of truth for driver state and treat `Status` as advisory only.

Driver display state, in priority order:
1. `Retired` → **OUT**
2. `Stopped` → **STOP**
3. `InPit` → **PIT**
4. `PitOut` → **OUT LAP**
5. otherwise → normal

## The wire protocol to the browser

```typescript
type ServerMessage =
  | { type: "snapshot"; data: F1State; streamId: string; ts: number }
  | { type: "delta";    data: DeepPartial<F1State>; streamId: string; ts: number }
  | { type: "replay-meta"; data: ReplayMeta }
  | { type: "error";    message: string };

type ClientMessage =
  | { type: "ping" }
  | { type: "replay-control"; action: "play" | "pause" | "seek" | "speed";
      value?: number };
```

`ts` is the **wall-clock unix ms at which the ingest service observed the message**. The client delay buffer keys off this, not off the F1 timestamps (which drift and are session-relative).

## Constants to hardcode

`packages/shared/src/f1shared/constants.py`:

- Tyre compound → colour: `SOFT #da291c`, `MEDIUM #ffd12e`, `HARD #f0f0ec`, `INTERMEDIATE #43b02a`, `WET #0067ad`.
- Team colours come from `DriverList.TeamColour` at runtime — **do not hardcode team colours**, they change per season and mid-season. Hardcode only a fallback grey.
- Prefix `TeamColour` with `#` when rendering; the feed omits it.
