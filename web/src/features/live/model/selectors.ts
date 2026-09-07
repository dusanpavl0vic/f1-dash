/**
 * Maps the feed's own shapes onto the view models the components consume.
 *
 * This is the ONLY place that knows the F1 field names. The backend preserves
 * them verbatim (docs/00 rule 3) so a feed change touches this file and nothing
 * else — not thirty components.
 *
 * Every shape here was read from a real recorded session, not from the spec:
 * backend fixture data/archive/2024/italian-grand-prix/race/stream.jsonl.
 */
import type { JsonObject, JsonValue } from "../lib/merge";
import { TEAM_COLOR_FALLBACK } from "./constants";
import type {
  CarPosition, Driver, MessageCategory, PaceClass, Penalty, RaceControlMessage,
  SectorTime, SessionInfo, SessionSnapshot, Stint, TimelineBand, TimingRow,
  TrackState, TyreCompound, Weather,
} from "./types";

/* ------------------------------------------------------------------ utils */

const obj = (v: JsonValue | undefined): JsonObject | undefined =>
  typeof v === "object" && v !== null && !Array.isArray(v) ? (v as JsonObject) : undefined;

const str = (v: JsonValue | undefined): string | undefined =>
  typeof v === "string" ? v : typeof v === "number" ? String(v) : undefined;

const num = (v: JsonValue | undefined): number | undefined => {
  if (typeof v === "number") return v;
  if (typeof v === "string") {
    const n = Number(v);
    return Number.isFinite(n) ? n : undefined;
  }
  return undefined;
};

const bool = (v: JsonValue | undefined): boolean => v === true;

/**
 * The feed represents the same collection two ways and switches between them
 * mid-session: an index-keyed object ({"0":…,"1":…}) or a genuine JSON array.
 *
 * The server normalises only the TOP level of a payload, so nested collections
 * — CarData.Entries, Position.Position, TimingData Sectors/Segments — still
 * arrive either way. Handling only objects here is why DRS read "—" for the
 * whole field and the track map had no cars: both live one level down.
 */
function ordered(value: JsonValue | undefined): [number, JsonObject][] {
  if (Array.isArray(value)) {
    return value
      .map((v, i) => [i, obj(v)] as [number, JsonObject | undefined])
      .filter((e): e is [number, JsonObject] => e[1] !== undefined);
  }

  const o = obj(value);
  if (!o) return [];

  return Object.entries(o)
    .map(([k, v]) => [Number(k), obj(v)] as [number, JsonObject | undefined])
    .filter((e): e is [number, JsonObject] => Number.isFinite(e[0]) && e[1] !== undefined)
    .sort((a, b) => a[0] - b[0]);
}

/**
 * A car is lapped when the gap reads "1 L" / "2 L". The LEADER's GapToLeader is
 * "LAP 26" — the current lap, not a gap — so a substring test for "L" marks the
 * race leader as lapped. Confirmed against the recorded Monza race.
 */
export function isLapped(gap: string): boolean {
  return /^\s*\d+\s*L\s*$/.test(gap);
}

/** The leader's gap is a lap counter, not a time; the tower shows "LEADER". */
export function isLapCounter(gap: string): boolean {
  return /^\s*LAP\s+\d+/i.test(gap);
}

/* ------------------------------------------------------ mini-sector status */

/**
 * Segment.Status values, from docs/05 and confirmed against real data.
 * An unrecognised value renders neutral rather than being dropped — a feed
 * change must degrade, not crash.
 */
export function segmentPace(status: number | undefined): PaceClass {
  switch (status) {
    case 2048: return "slower";    // yellow
    case 2049: return "personal";  // green
    case 2051: return "overall";   // purple
    case 2052: return "none";      // pit lane / not applicable
    case 2064: return "pit";       // blue — in pit
    case 0:
    case undefined: return "none";
    default: return "none";
  }
}

/**
 * Sector colour comes from the OverallFastest / PersonalFastest booleans, never
 * from comparing numbers locally (docs/11).
 */
function sectorPace(sector: JsonObject | undefined): PaceClass {
  if (!sector || !str(sector.Value)) return "none";
  if (bool(sector.OverallFastest)) return "overall";
  if (bool(sector.PersonalFastest)) return "personal";
  return "slower";
}

/* ---------------------------------------------------------------- drivers */

export function selectDrivers(state: JsonObject): Record<string, Driver> {
  const list = obj(state.DriverList);
  const out: Record<string, Driver> = {};
  if (!list) return out;

  for (const [number, raw] of Object.entries(list)) {
    const d = obj(raw);
    // The feed carries a "_kf" sibling and occasionally other scalars.
    if (!d || !str(d.Tla)) continue;

    // TeamColour arrives as hex WITHOUT a leading '#', e.g. "E80020".
    const colour = str(d.TeamColour);

    out[str(d.Tla)!] = {
      tla: str(d.Tla)!,
      number: num(d.RacingNumber) ?? Number(number),
      color: colour ? `#${colour}` : TEAM_COLOR_FALLBACK,
      teamCode: str(d.TeamName) ?? "",
      teamName: str(d.TeamName) ?? "",
      firstName: str(d.FirstName) ?? "",
      lastName: str(d.LastName) ?? "",
    };
  }
  return out;
}

/* ----------------------------------------------------------------- timing */

const COMPOUND: Record<string, TyreCompound> = {
  SOFT: "S", MEDIUM: "M", HARD: "H", INTERMEDIATE: "I", WET: "W",
};

/** Latest stint = current tyre. Stops = stints beyond the first. */
function stintInfo(appLine: JsonObject | undefined): { tyre: TyreCompound; age: number; stops: number } {
  const stints = ordered(appLine?.Stints);
  if (stints.length === 0) return { tyre: "H", age: 0, stops: 0 };

  const current = stints[stints.length - 1]![1];

  // The feed often omits Compound on a stint it has already announced, so the
  // newest stint can carry an age with no compound. Walking back to the most
  // recent stint that names one beats silently defaulting to HARD.
  let compound: TyreCompound | undefined;
  for (let i = stints.length - 1; i >= 0 && !compound; i--) {
    compound = COMPOUND[str(stints[i]![1].Compound)?.toUpperCase() ?? ""];
  }

  return {
    tyre: compound ?? "H",
    age: num(current.TotalLaps) ?? 0,
    stops: Math.max(0, stints.length - 1),
  };
}

export function selectTiming(state: JsonObject, drsByNumber: Record<string, boolean>): TimingRow[] {
  const lines = obj(obj(state.TimingData)?.Lines);
  const appLines = obj(obj(state.TimingAppData)?.Lines);
  const driverList = obj(state.DriverList);
  if (!lines) return [];

  const rows: TimingRow[] = [];

  for (const [number, raw] of Object.entries(lines)) {
    const t = obj(raw);
    if (!t) continue;

    const position = num(t.Position);
    if (position === undefined) continue;

    const tla = str(obj(driverList?.[number])?.Tla) ?? number;
    const gap = str(t.GapToLeader) ?? "";
    const interval = str(obj(t.IntervalToPositionAhead)?.Value) ?? "";
    const lapped = isLapped(gap);

    const sectorsRaw = ordered(t.Sectors);
    const sectors = [0, 1, 2].map<SectorTime>((i) => {
      const s = sectorsRaw.find(([k]) => k === i)?.[1];
      return { value: str(s?.Value) ?? "", pace: sectorPace(s) };
    }) as [SectorTime, SectorTime, SectorTime];

    // Mini-sectors are stored per sector; the tower shows them as one bar, so
    // they are flattened in sector order.
    const segments: PaceClass[] = [];
    for (const [, sector] of sectorsRaw) {
      for (const [, seg] of ordered(sector.Segments)) {
        segments.push(segmentPace(num(seg.Status)));
      }
    }

    const { tyre, age, stops } = stintInfo(obj(appLines?.[number]));

    // Explicit booleans are the source of truth for driver state; the Status
    // bitfield is advisory only (docs/05).
    const status =
      bool(t.Retired) || bool(t.Stopped) ? "retired"
      : bool(t.InPit) ? "pit"
      : bool(t.PitOut) ? "outlap"
      : lapped ? "lapped"
      : "racing";

    rows.push({
      tla,
      position,
      gap,
      interval,
      sectors,
      segments,
      lastLap: str(obj(t.LastLapTime)?.Value) ?? "",
      bestLap: str(obj(t.BestLapTime)?.Value) ?? "",
      bestIsOverall: bool(obj(t.BestLapTime)?.OverallFastest),
      tyre,
      tyreAge: age,
      stops,
      status,
      drsActive: drsByNumber[number] ?? false,
    });
  }

  rows.sort((a, b) => a.position - b.position);
  return rows;
}

/* ---------------------------------------------------------------- CarData */

/** DRS is active at channel 45 >= 10 (docs/04 §5). */
export function selectDrs(state: JsonObject): Record<string, boolean> {
  const frames = ordered(obj(state.CarData)?.Entries);
  const latest = frames[frames.length - 1]?.[1];
  const cars = obj(latest?.Cars);
  const out: Record<string, boolean> = {};
  if (!cars) return out;

  for (const [number, raw] of Object.entries(cars)) {
    const channels = obj(obj(raw)?.Channels);
    out[number] = (num(channels?.["45"]) ?? 0) >= 10;
  }
  return out;
}

export interface CarChannels {
  rpm: number; speed: number; gear: number; throttle: number; brake: number; drs: number;
}

export function selectChannels(state: JsonObject, racingNumber: string): CarChannels | undefined {
  const frames = ordered(obj(state.CarData)?.Entries);
  const latest = frames[frames.length - 1]?.[1];
  const channels = obj(obj(obj(latest?.Cars)?.[racingNumber])?.Channels);
  if (!channels) return undefined;

  return {
    rpm: num(channels["0"]) ?? 0,
    speed: num(channels["2"]) ?? 0,
    gear: num(channels["3"]) ?? 0,
    throttle: num(channels["4"]) ?? 0,
    brake: num(channels["5"]) ?? 0,
    drs: num(channels["45"]) ?? 0,
  };
}

/* --------------------------------------------------------------- Position */

export function selectPositions(state: JsonObject, drivers: Record<string, Driver>): CarPosition[] {
  const frames = ordered(obj(state.Position)?.Position);
  const entries = obj(frames[frames.length - 1]?.[1]?.Entries);
  if (!entries) return [];

  const byNumber = new Map(Object.values(drivers).map((d) => [String(d.number), d.tla]));
  const out: CarPosition[] = [];

  for (const [number, raw] of Object.entries(entries)) {
    const e = obj(raw);
    const tla = byNumber.get(number);
    // A car number with no DriverList entry is skipped, not a crash (docs/14).
    if (!e || !tla) continue;
    if (str(e.Status) !== "OnTrack") continue;

    out.push({ tla, x: num(e.X) ?? 0, y: num(e.Y) ?? 0 });
  }
  return out;
}

/* --------------------------------------------------------- session / misc */

const TRACK_STATE_BY_CODE: Record<string, TrackState> = {
  "1": "none", "2": "yellow", "3": "yellow", "4": "safety-car",
  "5": "red-flag", "6": "vsc", "7": "vsc",
};

export function selectTrackState(state: JsonObject): TrackState {
  const status = obj(state.TrackStatus);
  const sessionStatus = str(obj(state.SessionStatus)?.Status);
  if (sessionStatus === "Finished" || sessionStatus === "Finalised" || sessionStatus === "Ends") {
    return "chequered";
  }
  return TRACK_STATE_BY_CODE[str(status?.Status) ?? "1"] ?? "none";
}

export function selectSession(state: JsonObject): SessionInfo {
  const info = obj(state.SessionInfo);
  const meeting = obj(info?.Meeting);
  const lap = obj(state.LapCount);

  // StartDate is "2024-09-01T15:00:00"; the year is the join key for geometry.
  const year = num(str(info?.StartDate)?.slice(0, 4)) ?? null;

  return {
    meetingName: str(meeting?.Name) ?? "No session",
    circuitName: str(obj(meeting?.Circuit)?.ShortName) ?? "",
    type: (str(info?.Type) ?? "").toUpperCase(),
    currentLap: num(lap?.CurrentLap) ?? 0,
    totalLaps: num(lap?.TotalLaps) ?? 0,
    circuitKey: num(obj(meeting?.Circuit)?.Key) ?? null,
    year,
  };
}

export function selectWeather(state: JsonObject): Weather {
  const w = obj(state.WeatherData);
  return {
    trackTemp: num(w?.TrackTemp) ?? 0,
    airTemp: num(w?.AirTemp) ?? 0,
    windSpeed: num(w?.WindSpeed) ?? 0,
    humidity: num(w?.Humidity) ?? 0,
    pressure: num(w?.Pressure) ?? 0,
    rainfall: (num(w?.Rainfall) ?? 0) > 0,
  };
}

/* -------------------------------------------------------- race control */

const CATEGORY: Record<string, MessageCategory> = {
  Flag: "Yellow flag", Drs: "DRS", SafetyCar: "Safety car", CarEvent: "Car event", Other: "Other",
};

function categorise(m: JsonObject): MessageCategory {
  const text = (str(m.Message) ?? "").toUpperCase();
  const flag = (str(m.Flag) ?? "").toUpperCase();

  if (text.includes("PENALTY")) return "Penalty";
  if (text.includes("DELETED")) return "Lap deleted";
  if (text.includes("UNDER INVESTIGATION") || text.includes("NOTED")) return "Investigation";
  if (flag === "BLUE") return "Blue flag";
  if (flag === "GREEN" || flag === "CLEAR") return "Green flag";
  if (flag.includes("YELLOW")) return "Yellow flag";
  if (text.includes("VIRTUAL SAFETY CAR") || text.includes("VSC")) return "VSC";
  if (text.includes("SAFETY CAR")) return "Safety car";
  return CATEGORY[str(m.Category) ?? "Other"] ?? "Other";
}

export function selectMessages(state: JsonObject): RaceControlMessage[] {
  const messages = ordered(obj(state.RaceControlMessages)?.Messages);

  return messages
    .map(([, m]) => ({
      category: categorise(m),
      lap: num(m.Lap) ?? 0,
      time: (str(m.Utc) ?? "").slice(11, 19),
      text: str(m.Message) ?? "",
      highlighted: false,
    }))
    .filter((m) => m.text.length > 0)
    .reverse()                                  // newest first
    .map((m, i) => ({ ...m, highlighted: i < 2 }));
}

/** Penalties are a filtered projection of the race control feed, not a topic. */
export function selectPenalties(state: JsonObject, drivers: Record<string, Driver>): Penalty[] {
  const byNumber = new Map(Object.values(drivers).map((d) => [String(d.number), d]));

  return selectMessages(state)
    .filter((m) => m.category === "Penalty" || m.category === "Lap deleted" || m.category === "Investigation")
    .map((m) => {
      // Messages name the car as "CAR 11 (PER)".
      const car = /CAR (\d+)/.exec(m.text)?.[1];
      const driver = car ? byNumber.get(car) : undefined;
      const seconds = /(\d+) SECOND TIME PENALTY/.exec(m.text)?.[1];

      const penalty =
        seconds ? `+${seconds}.0s`
        : m.category === "Lap deleted" ? "LAP DELETED"
        : m.category === "Investigation" ? "UNDER INVESTIGATION"
        : "PENALTY";

      return {
        tla: driver?.tla ?? "—",
        number: driver?.number ?? 0,
        teamName: driver?.teamName ?? "",
        lap: m.lap,
        penalty,
        reason: m.text,
        state: m.category === "Investigation" ? "NOTED" as const : "APPLIED" as const,
      };
    });
}

/* ------------------------------------------------------------ derived UI */

export function selectStints(state: JsonObject, timing: TimingRow[]): Stint[] {
  const appLines = obj(obj(state.TimingAppData)?.Lines);
  const lines = obj(obj(state.TimingData)?.Lines);
  if (!appLines || !lines) return [];

  const numberByTla = new Map<string, string>();
  for (const [number, raw] of Object.entries(lines)) {
    const t = obj(raw);
    if (t) numberByTla.set(str(t.RacingNumber) ?? number, number);
  }

  return timing.slice(0, 5).map((row) => {
    const number = Object.entries(obj(state.DriverList) ?? {})
      .find(([, d]) => str(obj(d)?.Tla) === row.tla)?.[0];

    const stints = ordered(obj(appLines[number ?? ""])?.Stints);
    return {
      tla: row.tla,
      stops: row.stops,
      bars: stints.map(([, s]) => ({
        compound: COMPOUND[str(s.Compound)?.toUpperCase() ?? ""] ?? "H",
        laps: Math.max(1, num(s.TotalLaps) ?? 1),
      })),
    };
  });
}

/** Session timeline bands, derived from TrackStatus transitions. */
export function selectTimeline(state: JsonObject, currentLap: number): TimelineBand[] {
  const trackState = selectTrackState(state);
  return [{ fromLap: 1, toLap: Math.max(1, currentLap), label: "", state: trackState }];
}

/* ------------------------------------------------------------- snapshot */

export function selectSnapshot(state: JsonObject): SessionSnapshot {
  const drivers = selectDrivers(state);
  const drs = selectDrs(state);
  const timing = selectTiming(state, drs);
  const session = selectSession(state);

  return {
    session,
    weather: selectWeather(state),
    trackState: selectTrackState(state),
    drivers,
    timing,
    positions: selectPositions(state, drivers),
    messages: selectMessages(state),
    penalties: selectPenalties(state, drivers),
    timeline: selectTimeline(state, session.currentLap),
    stints: selectStints(state, timing),
    pace: [],
    topLapSpeed: 0,
    currentLapTime: timing[0]?.lastLap ?? "",
    currentLapDelta: "",
    bestLapTime: timing.find((r) => r.bestIsOverall)?.bestLap ?? "",
  };
}
