import { describe, expect, it } from "vitest";
import { buildMockSession } from "./mockSession";
import { TEAM_COLOR_FALLBACK } from "../model/constants";

describe("mock session fixture", () => {
  const s = buildMockSession();

  it("has a full 20-car field", () => {
    expect(s.timing).toHaveLength(20);
    expect(Object.keys(s.drivers)).toHaveLength(20);
  });

  it("is ordered by position with no gaps", () => {
    expect(s.timing.map((r) => r.position)).toEqual(
      Array.from({ length: 20 }, (_, i) => i + 1),
    );
  });

  it("gives every driver a timing row and vice versa", () => {
    for (const row of s.timing) expect(s.drivers[row.tla]).toBeDefined();
  });

  it("leaves the leader's gap empty rather than showing +0.000", () => {
    expect(s.timing[0]!.gap).toBe("");
  });

  it("represents a lapped car as '1 L', not a number", () => {
    const lapped = s.timing.filter((r) => r.status === "lapped");
    expect(lapped.length).toBeGreaterThan(0);
    for (const r of lapped) expect(r.gap).toMatch(/L$/);
  });

  it("marks the retired driver and gives it no sector times", () => {
    const retired = s.timing.find((r) => r.status === "retired");
    expect(retired?.tla).toBe("SAR");
    expect(retired!.sectors.every((x) => x.value === "")).toBe(true);
  });

  it("renders pit-lane mini-sectors in blue for a car in the pits", () => {
    const pit = s.timing.find((r) => r.status === "pit")!;
    expect(pit.segments).toContain("pit");
  });

  it("gives every driver exactly 20 mini-sector segments", () => {
    for (const r of s.timing) expect(r.segments).toHaveLength(20);
  });

  it("falls back to a neutral colour when a team has none", () => {
    // The awkward palette deliberately omits Alpine's colour.
    const awkward = buildMockSession("yellow", "awkward");
    expect(awkward.drivers["GAS"]!.color).toBe(TEAM_COLOR_FALLBACK);
  });

  it("reflects the requested track state in the final timeline band", () => {
    expect(buildMockSession("red-flag").timeline.at(-1)!.state).toBe("red-flag");
  });
});
