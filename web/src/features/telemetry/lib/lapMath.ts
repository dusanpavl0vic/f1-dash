import type { TelemetryLap } from "@/features/analysis/model/types";

/**
 * Distance travelled at each sample, in metres.
 *
 * Integrated from speed rather than from the GPS trace: the GPS is published at
 * a lower rate than the channels and is carried forward between updates, so
 * differencing it would produce a staircase. Speed over time is smooth and is
 * what the car actually did.
 */
export function distanceSeries(lap: TelemetryLap): number[] {
  const distance: number[] = [0];

  for (let i = 1; i < lap.speed.length; i++) {
    const dtSeconds = ((lap.offsetMs[i] ?? 0) - (lap.offsetMs[i - 1] ?? 0)) / 1000;
    // Trapezoidal: at 4 Hz a speed change between samples is significant, and
    // taking either endpoint alone biases the total by metres per corner.
    const metresPerSecond = (((lap.speed[i] ?? 0) + (lap.speed[i - 1] ?? 0)) / 2) / 3.6;
    distance.push((distance[i - 1] ?? 0) + metresPerSecond * dtSeconds);
  }

  return distance;
}

/** Linear interpolation of `ys` at `x`, given a sorted `xs`. */
function interpolate(xs: number[], ys: number[], x: number): number {
  if (xs.length === 0) return 0;
  if (x <= (xs[0] ?? 0)) return ys[0] ?? 0;
  if (x >= (xs[xs.length - 1] ?? 0)) return ys[ys.length - 1] ?? 0;

  // Binary search: a lap is a few hundred samples and this runs per grid point.
  let low = 0;
  let high = xs.length - 1;
  while (high - low > 1) {
    const mid = (low + high) >> 1;
    if ((xs[mid] ?? 0) <= x) low = mid; else high = mid;
  }

  const x0 = xs[low] ?? 0;
  const x1 = xs[high] ?? 0;
  const span = x1 - x0;
  const t = span === 0 ? 0 : (x - x0) / span;

  return (ys[low] ?? 0) + ((ys[high] ?? 0) - (ys[low] ?? 0)) * t;
}

export interface DeltaSeries {
  /** Distance along the lap, metres. */
  distance: number[];
  /** Seconds driver A is BEHIND driver B at that distance. Positive = A slower. */
  delta: number[];
  maxAbs: number;
  /** Delta at the end of the shorter lap — the gap the lap actually produced. */
  final: number;
}

/**
 * Cumulative time delta between two laps, against distance.
 *
 * This is the chart that actually answers "where did the lap go". Comparing two
 * speed traces side by side shows where one car was faster; only the delta
 * shows what that was worth in time, and where it was given back.
 *
 * Both laps are resampled onto a common distance grid because they have
 * different sample counts and different lap lengths.
 */
export function deltaSeries(a: TelemetryLap, b: TelemetryLap, points = 400): DeltaSeries {
  const distanceA = distanceSeries(a);
  const distanceB = distanceSeries(b);

  const timeA = a.offsetMs.map((ms) => ms / 1000);
  const timeB = b.offsetMs.map((ms) => ms / 1000);

  // Only compare the stretch both laps cover; beyond it the delta is undefined
  // rather than zero.
  const limit = Math.min(distanceA.at(-1) ?? 0, distanceB.at(-1) ?? 0);

  const distance: number[] = [];
  const delta: number[] = [];
  let maxAbs = 0;

  for (let i = 0; i <= points; i++) {
    const d = (limit * i) / points;
    const value = interpolate(distanceA, timeA, d) - interpolate(distanceB, timeB, d);

    distance.push(d);
    delta.push(value);
    maxAbs = Math.max(maxAbs, Math.abs(value));
  }

  return { distance, delta, maxAbs, final: delta.at(-1) ?? 0 };
}
