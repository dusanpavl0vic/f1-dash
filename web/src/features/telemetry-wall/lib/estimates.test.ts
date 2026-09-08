import { describe, expect, it } from "vitest";
import { estimateDegradation, estimateTyre, parseGapSeconds } from "./estimates";

describe("estimateTyre", () => {
  it("reports a fresh tyre as fresh and a spent one as critical", () => {
    expect(estimateTyre("S", 1).condition).toBe("fresh");
    expect(estimateTyre("S", 18).condition).toBe("critical");
  });

  it("never reports more than fully worn", () => {
    // A driver can run well past nominal life on a one-stop. The estimate has
    // to degrade gracefully rather than claim 180% wear.
    const long = estimateTyre("M", 60);
    expect(long.wear).toBe(100);
    expect(long.remaining).toBe(0);
  });

  it("treats compounds differently", () => {
    // The same age on a hard is a much younger tyre than on a soft.
    expect(estimateTyre("H", 18).wear).toBeLessThan(estimateTyre("S", 18).wear);
  });
});

describe("estimateDegradation", () => {
  const laps = (times: number[]) =>
    times.map((seconds, i) => ({ lap: i + 1, seconds }));

  it("returns null rather than guessing from too little data", () => {
    expect(estimateDegradation(laps([90, 90.2, 90.4]))).toBeNull();
  });

  it("finds the slope of a steady drop-off", () => {
    // 0.1 s per lap, exactly.
    const result = estimateDegradation(laps([90, 90.1, 90.2, 90.3, 90.4, 90.5]));
    expect(result).toBeCloseTo(0.1, 5);
  });

  it("reports no degradation for consistent laps", () => {
    expect(estimateDegradation(laps([91, 91, 91, 91, 91]))).toBeCloseTo(0, 5);
  });

  it("is not dragged around by a safety car lap", () => {
    // Without outlier rejection this single lap turns a flat trend into a
    // claimed four seconds a lap of degradation.
    const clean = estimateDegradation(laps([90, 90.1, 90.2, 90.3, 90.4, 90.5]))!;
    const withSafetyCar = estimateDegradation(
      laps([90, 90.1, 90.2, 110, 90.3, 90.4, 90.5]))!;

    expect(Math.abs(withSafetyCar - clean)).toBeLessThan(0.05);
  });

  it("handles an improving driver without inverting the sign", () => {
    // Fuel burn-off makes lap times fall through a stint; that is negative
    // degradation and must read as such, not as an error.
    expect(estimateDegradation(laps([91, 90.9, 90.8, 90.7, 90.6]))!).toBeLessThan(0);
  });
});

describe("parseGapSeconds", () => {
  it("reads a real interval", () => {
    expect(parseGapSeconds("+2.500")).toBe(2.5);
    expect(parseGapSeconds("12.482")).toBe(12.482);
  });

  it("rejects an empty value rather than reading it as zero", () => {
    // Number("") is 0 in JavaScript. Before this, an empty interval put every
    // car in the field on the undercut monitor at 0.0 seconds.
    expect(parseGapSeconds("")).toBeNull();
    expect(parseGapSeconds("   ")).toBeNull();
  });

  it("rejects lap markers", () => {
    expect(parseGapSeconds("1 L")).toBeNull();
    expect(parseGapSeconds("LAP 26")).toBeNull();
  });
});
