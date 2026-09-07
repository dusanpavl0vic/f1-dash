/**
 * View models consumed by the components.
 *
 * These are deliberately NOT the F1 feed shapes. The feed's own field names are
 * preserved on the wire and in the backend (docs/05 rule: never rename a feed
 * field), and a selector layer maps feed state onto these view models. Keeping
 * the boundary explicit means a feed change touches the selector, not 30
 * components.
 *
 * Until the backend exists, the fixtures in ../fixtures produce these directly.
 */

export type TyreCompound = "S" | "M" | "H" | "I" | "W";

/** Mini-sector / sector colour classes, in the sport's own vocabulary. */
export type PaceClass =
  | "overall"   /* purple — fastest by anyone this session */
  | "personal"  /* green  — this driver's own best */
  | "slower"    /* yellow — slower than their best */
  | "pit"       /* blue   — traversed in the pit lane */
  | "none";     /* grey   — not yet driven this lap */

export type DriverStatus = "racing" | "lapped" | "pit" | "outlap" | "retired";

/**
 * Which overtaking-aid column the era supports.
 *
 * 2026 removed DRS from the regulations AND from the feed: CarData channel 45
 * is simply absent (docs/21). The replacement aids — active aerodynamics and
 * the Manual Override boost — are not published at all, so nothing can be shown
 * for them. What 2026 added instead is an overtake counter.
 *
 * Detected from the payload rather than the season, so a mid-season feed change
 * degrades instead of breaking.
 */
export type OvertakeAid = "drs" | "overtakes" | "none";

export type TrackState =
  | "none"        /* green flag */
  | "yellow"
  | "safety-car"
  | "vsc"
  | "red-flag"
  | "chequered";

export type GapMode = "gap" | "interval";

export type AppView = "timing" | "telemetry" | "notifications" | "analysis";

/** One timed sector. `value` is empty until the driver has set it this lap. */
export interface SectorTime {
  value: string;
  pace: PaceClass;
}

export interface Driver {
  tla: string;
  number: number;
  /** Team colour as delivered by the feed — arbitrary hex, never assumed. */
  color: string;
  teamCode: string;
  teamName: string;
  firstName: string;
  lastName: string;
}

export interface TimingRow {
  tla: string;
  position: number;
  /** Gap to the leader. May be "+12.482", "1 L" for a lapped car, or "" for the leader. */
  gap: string;
  /** Interval to the car ahead. Same shapes as `gap`. */
  interval: string;
  sectors: [SectorTime, SectorTime, SectorTime];
  /** 20 mini-sector segments. Length is circuit-dependent in the real feed (15–25). */
  segments: PaceClass[];
  lastLap: string;
  bestLap: string;
  /** True when this driver holds the session's fastest lap. */
  bestIsOverall: boolean;
  tyre: TyreCompound;
  tyreAge: number;
  stops: number;
  status: DriverStatus;
  /** Pre-2026 only. Always false once the feed stops publishing channel 45. */
  drsActive: boolean;
  /** 2026+ only. Overtakes completed this session. */
  overtakes: number;
}

export interface CarPosition {
  tla: string;
  /** Coordinates in the map's own viewBox space (see TrackMap). */
  x: number;
  y: number;
}

export type MessageCategory =
  | "Yellow flag" | "Green flag" | "Blue flag" | "Penalty"
  | "Lap deleted" | "Investigation" | "Car event" | "DRS"
  | "VSC" | "Safety car" | "Other";

export interface RaceControlMessage {
  category: MessageCategory;
  lap: number;
  time: string;
  text: string;
  /** Recent or high-severity messages get a highlighted row. */
  highlighted: boolean;
}

export type PenaltyState = "PENDING" | "NOTED" | "APPLIED" | "SERVED";

export interface Penalty {
  tla: string;
  number: number;
  teamName: string;
  lap: number;
  /** "+5.0s", "LAP DELETED", "UNDER INVESTIGATION", "REPRIMAND" */
  penalty: string;
  reason: string;
  state: PenaltyState;
}

export interface TimelineBand {
  fromLap: number;
  toLap: number;
  label: string;
  state: TrackState;
}

export interface Stint {
  tla: string;
  stops: number;
  bars: { compound: TyreCompound; laps: number }[];
}

export interface PaceLine {
  tla: string;
  color: string;
  /** Lap time in seconds, one entry per lap, oldest first. */
  laps: number[];
}

export interface Weather {
  trackTemp: number;
  airTemp: number;
  windSpeed: number;
  humidity: number;
  pressure: number;
  rainfall: boolean;
}

export interface SessionInfo {
  meetingName: string;
  circuitName: string;
  type: string;
  currentLap: number;
  totalLaps: number;
  /** MultiViewer circuitKey — the join to track geometry. */
  circuitKey: number | null;
  year: number | null;
  /** Meeting round within the season, when the feed provides it. */
  round: number | null;
  /** Session start, ISO 8601, as the feed sends it. */
  startDate: string | null;
}

/** Everything a rendered frame needs. The backend will produce this shape. */
export interface SessionSnapshot {
  session: SessionInfo;
  /** Which overtaking-aid column this session's data supports. */
  overtakeAid: OvertakeAid;
  weather: Weather;
  trackState: TrackState;
  drivers: Record<string, Driver>;
  timing: TimingRow[];
  positions: CarPosition[];
  messages: RaceControlMessage[];
  penalties: Penalty[];
  timeline: TimelineBand[];
  stints: Stint[];
  pace: PaceLine[];
  topLapSpeed: number;
  currentLapTime: string;
  currentLapDelta: string;
  bestLapTime: string;
}
