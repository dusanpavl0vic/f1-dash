/** Mirrors the backend's Analysis records (backend/src/F1Dash.Core/Analysis). */

export interface LapRecord {
  lap: number;
  timeSeconds: number | null;
  sector1: number | null;
  sector2: number | null;
  sector3: number | null;
  position: number | null;
  compound: string | null;
  tyreAge: number | null;
  inPit: boolean;
  pitOut: boolean;
  /** "1" clear, "4" safety car, "6" VSC, "5" red. */
  trackStatus: string | null;
}

export interface StintRecord {
  index: number;
  compound: string;
  startLap: number;
  endLap: number;
  laps: number;
  newTyres: boolean;
}

export interface DriverAnalysis {
  racingNumber: string;
  tla: string;
  teamName: string;
  /** Hex without a leading '#', as the feed sends it. */
  teamColour: string;
  laps: LapRecord[];
  stints: StintRecord[];
}

export interface AnalysisMeta {
  year: number | null;
  meeting: string;
  sessionName: string;
  sessionType: string;
  circuit: string;
  circuitKey: number | null;
  startDate: string | null;
  totalLaps: number;
  hasTelemetry: boolean;
  recordedAtUtc: string;
}

export interface SessionAnalysis {
  meta: AnalysisMeta;
  drivers: DriverAnalysis[];
}

export interface SectorDelta {
  sector: number;
  bestA: number | null;
  bestB: number | null;
  /** Positive means A is slower. */
  delta: number | null;
  faster: string | null;
}

export interface SectorComparison {
  driverA: string;
  driverB: string;
  bestA: number | null;
  bestB: number | null;
  sectors: SectorDelta[];
}

export interface TelemetryLap {
  lap: number;
  offsetMs: number[];
  speed: number[];
  throttle: number[];
  brake: number[];
  gear: number[];
  rpm: number[];
  /** Track coordinates in native F1 units — the same system as the circuit outline. */
  x: number[];
  y: number[];
}

/**
 * Laps whose time says nothing about the car's pace, by flag alone.
 *
 * Not sufficient on its own — see paceLaps.
 */
export function isRepresentative(lap: LapRecord): boolean {
  return lap.timeSeconds !== null
    && lap.timeSeconds > 0
    && !lap.inPit
    && !lap.pitOut
    // Safety car and VSC laps are 20-40 seconds slower and would flatten the
    // scale of every other lap on the chart.
    && lap.trackStatus !== "4"
    && lap.trackStatus !== "6"
    && lap.trackStatus !== "5";
}

/** A lap slower than this multiple of the median is not racing. */
const OUTLIER_FACTOR = 1.6;

/**
 * Laps that actually represent pace.
 *
 * Flags are not enough. A red flag on lap 3 of the 2026 Italian GP produced a
 * lap 4 of 1,958 seconds — the 32-minute suspension — and its trackStatus read
 * "1", because by the time the lap completed the track WAS clear. The flag at
 * the moment of completion says nothing about what happened during the lap.
 *
 * So anything beyond OUTLIER_FACTOR times the median is rejected as well. The
 * median is used rather than the mean precisely because one 32-minute lap would
 * drag a mean far enough to let itself through.
 */
export function paceLaps(driver: DriverAnalysis): LapRecord[] {
  const candidates = driver.laps.filter(isRepresentative);
  if (candidates.length < 3) return candidates;

  const sorted = [...candidates].map((l) => l.timeSeconds!).sort((a, b) => a - b);
  const median = sorted[Math.floor(sorted.length / 2)]!;

  return candidates.filter((l) => l.timeSeconds! <= median * OUTLIER_FACTOR);
}

/** True when a lap is an outlier for this driver — a stoppage or a restart. */
export function isOutlier(driver: DriverAnalysis, lap: LapRecord): boolean {
  if (lap.timeSeconds === null) return false;

  const candidates = driver.laps.filter(isRepresentative);
  if (candidates.length < 3) return false;

  const sorted = [...candidates].map((l) => l.timeSeconds!).sort((a, b) => a - b);
  const median = sorted[Math.floor(sorted.length / 2)]!;

  return lap.timeSeconds > median * OUTLIER_FACTOR;
}

export function bestLap(driver: DriverAnalysis): number | null {
  const times = paceLaps(driver).map((l) => l.timeSeconds!);
  return times.length === 0 ? null : Math.min(...times);
}

/** "1:23.456" for a lap, "23.456" for a sector. */
export function formatTime(seconds: number | null | undefined): string {
  if (seconds === null || seconds === undefined || seconds <= 0) return "—";

  const minutes = Math.floor(seconds / 60);
  const rest = seconds - minutes * 60;

  return minutes > 0
    ? `${minutes}:${rest.toFixed(3).padStart(6, "0")}`
    : rest.toFixed(3);
}

export function formatDelta(delta: number | null): string {
  if (delta === null) return "—";
  return `${delta >= 0 ? "+" : ""}${delta.toFixed(3)}`;
}

export function driverColor(driver: { teamColour: string }): string {
  return driver.teamColour ? `#${driver.teamColour}` : "var(--neutral)";
}
