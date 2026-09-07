import { describe, expect, it } from "vitest";
import type { TelemetryLap } from "@/features/analysis/model/types";
import { deltaSeries, distanceSeries } from "./lapMath";

/** A synthetic lap at a constant speed, sampled at 4 Hz. */
function constantLap(kmh: number, seconds: number, lap = 1): TelemetryLap {
  const n = seconds * 4;
  const offsetMs = Array.from({ length: n }, (_, i) => i * 250);
  const speed = Array.from({ length: n }, () => kmh);
  const zeros = Array.from({ length: n }, () => 0);

  return { lap, offsetMs, speed, throttle: zeros, brake: zeros, gear: zeros, rpm: zeros, x: zeros, y: zeros };
}

describe("distanceSeries", () => {
  it("integrates constant speed to the right distance", () => {
    // n samples span n-1 intervals, so 144 samples at 250 ms cover 35.75 s,
    // not 36. At 100 km/h that is 993.06 m — the expectation is derived rather
    // than rounded, because a loose tolerance here would hide a real drift.
    const lap = constantLap(100, 36);
    const intervals = lap.offsetMs.length - 1;
    const expected = (100 / 3.6) * (intervals * 0.25);

    expect(distanceSeries(lap).at(-1)!).toBeCloseTo(expected, 6);
  });

  it("starts at zero and never goes backwards", () => {
    const d = distanceSeries(constantLap(200, 10));
    expect(d[0]).toBe(0);
    for (let i = 1; i < d.length; i++) expect(d[i]!).toBeGreaterThanOrEqual(d[i - 1]!);
  });
});

describe("deltaSeries", () => {
  it("is zero for two identical laps", () => {
    const lap = constantLap(200, 20);
    const { maxAbs } = deltaSeries(lap, lap);
    expect(maxAbs).toBeCloseTo(0, 6);
  });

  it("reports the slower car as positive", () => {
    // Over the same distance, 180 km/h takes longer than 200 km/h.
    const { final } = deltaSeries(constantLap(180, 20), constantLap(200, 20));
    expect(final).toBeGreaterThan(0);
  });

  it("compares only the distance both laps cover", () => {
    // A lap cut short must not produce a delta over ground it never covered.
    const long = constantLap(200, 30);
    const short = constantLap(200, 10);
    const { distance } = deltaSeries(long, short);

    expect(distance.at(-1)!).toBeCloseTo(distanceSeries(short).at(-1)!, 0);
  });
});
