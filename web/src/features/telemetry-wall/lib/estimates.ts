import type { TyreCompound } from "@/features/live/model/types";

/**
 * Tyre condition and degradation, ESTIMATED.
 *
 * None of this is in the feed. Wear percentage, remaining life, the cliff and
 * the pit window are all team-internal telemetry that is never published, so
 * everything here is inferred from two things that ARE public: how many laps
 * the tyre has done, and how the driver's lap times are trending.
 *
 * Every value this module produces must be labelled as an estimate wherever it
 * is displayed. A number that looks like a measurement and is not is worse than
 * no number at all — see DECISIONS D-008.
 */

/**
 * Typical usable life per compound, in laps.
 *
 * Circuit-dependent in reality — Monza is gentle, Silverstone is not — so this
 * is a starting point, not a claim. It is deliberately conservative.
 */
const NOMINAL_LIFE: Record<TyreCompound, number> = {
  S: 18,
  M: 28,
  H: 40,
  I: 30,
  W: 25,
};

export interface TyreEstimate {
  /** 0-100. */
  wear: number;
  condition: "fresh" | "working" | "worn" | "critical";
  /** Laps of usable life left, at the nominal rate. */
  remaining: number;
  nominalLife: number;
}

export function estimateTyre(compound: TyreCompound, age: number): TyreEstimate {
  const life = NOMINAL_LIFE[compound];
  const wear = Math.min(100, Math.round((age / life) * 100));

  return {
    wear,
    condition:
      wear < 25 ? "fresh"
      : wear < 65 ? "working"
      : wear < 90 ? "worn"
      : "critical",
    remaining: Math.max(0, life - age),
    nominalLife: life,
  };
}

/**
 * Degradation in seconds per lap, from the trend of recent lap times.
 *
 * A least-squares slope over the last laps rather than "latest minus first":
 * one traffic lap or one slow in-lap would otherwise dominate the answer
 * completely.
 *
 * Returns null when there is not enough clean data to say anything, which is
 * the honest output far more often than a number would be.
 */
export function estimateDegradation(
  laps: { lap: number; seconds: number }[], window = 8): number | null {
  const recent = laps.slice(-window);
  if (recent.length < 4) return null;

  // Outliers are removed before fitting, not after. A safety car lap is
  // twenty seconds slow and would tilt the entire line.
  const sorted = [...recent].map((l) => l.seconds).sort((a, b) => a - b);
  const median = sorted[Math.floor(sorted.length / 2)]!;
  const clean = recent.filter((l) => l.seconds < median * 1.06 && l.seconds > median * 0.94);

  if (clean.length < 4) return null;

  const n = clean.length;
  const meanX = clean.reduce((t, l) => t + l.lap, 0) / n;
  const meanY = clean.reduce((t, l) => t + l.seconds, 0) / n;

  let numerator = 0;
  let denominator = 0;
  for (const { lap, seconds } of clean) {
    numerator += (lap - meanX) * (seconds - meanY);
    denominator += (lap - meanX) ** 2;
  }

  if (denominator === 0) return null;
  return numerator / denominator;
}

export interface UndercutThreat {
  tla: string;
  /** Seconds behind the driver in focus. */
  gap: number;
  compound: TyreCompound;
  age: number;
  /** Seconds per lap, or null when it cannot be estimated. */
  degradation: number | null;
  /** True when this car is within one stop's worth of track position. */
  inRange: boolean;
}

/**
 * Roughly what a pit stop costs in track position, in seconds.
 *
 * Circuit-dependent — Monaco is far more than Monza — so this is used only to
 * decide whether a car is worth showing as a threat, never presented as a
 * number of its own.
 */
export const PIT_LOSS_SECONDS = 22;


/**
 * Parses a gap or interval from the feed into seconds.
 *
 * `Number("")` is **0** in JavaScript, not NaN — so an empty interval, which is
 * what the feed sends before a car has one, silently reads as "zero seconds
 * behind". That put the entire field on the undercut monitor at 0.0s.
 *
 * Returns null for anything that is not an actual measurement: an empty value,
 * a lapped car ("1 L"), or a lap counter ("LAP 26").
 */
export function parseGapSeconds(value: string): number | null {
  const text = value.trim();
  if (text.length === 0) return null;

  // Anything with a letter in it is a lap marker, not a time.
  if (/[a-z]/i.test(text)) return null;

  const seconds = Number(text.replace("+", ""));
  return Number.isFinite(seconds) ? seconds : null;
}
