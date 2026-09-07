import { describe, expect, it } from "vitest";
import type { JsonObject } from "../lib/merge";
import {
  isLapCounter, isLapped, segmentPace, selectDrivers, selectDrs,
  selectPositions, selectTiming, selectTrackState,
} from "./selectors";

/**
 * Every case here is a bug that reached the screen once. The shapes are copied
 * from the recorded 2024 Italian GP, not from the specification — the spec
 * describes some of these differently from how the feed actually behaves.
 */

describe("lapped detection", () => {
  it("treats '1 L' and '2 L' as lapped", () => {
    expect(isLapped("1 L")).toBe(true);
    expect(isLapped("2 L")).toBe(true);
  });

  it("does NOT treat the leader's 'LAP 26' as lapped", () => {
    // The leader's GapToLeader is the lap counter. A substring test for "L"
    // marked the race leader as LAPPED on screen.
    expect(isLapped("LAP 26")).toBe(false);
    expect(isLapCounter("LAP 26")).toBe(true);
  });

  it("does not treat a time gap as lapped", () => {
    expect(isLapped("+12.482")).toBe(false);
    expect(isLapped("")).toBe(false);
  });
});

describe("collections arriving as arrays instead of index-keyed objects", () => {
  // The server normalises only the TOP level of a payload, so nested
  // collections still arrive either way. Reading only objects meant DRS showed
  // "—" for the whole field and the track map had no cars.
  const carDataAsArray: JsonObject = {
    CarData: { Entries: [{ Utc: "t", Cars: { "1": { Channels: { "45": 12 } }, "4": { Channels: { "45": 0 } } } }] },
  };
  const carDataAsObject: JsonObject = {
    CarData: { Entries: { "0": { Utc: "t", Cars: { "1": { Channels: { "45": 12 } }, "4": { Channels: { "45": 0 } } } } } },
  };

  it("reads DRS when Entries is an array", () => {
    expect(selectDrs(carDataAsArray)).toEqual({ "1": true, "4": false });
  });

  it("reads DRS identically when Entries is an index-keyed object", () => {
    expect(selectDrs(carDataAsObject)).toEqual(selectDrs(carDataAsArray));
  });

  it("reads car positions when Position is an array", () => {
    const drivers = selectDrivers({ DriverList: { "1": { Tla: "VER", RacingNumber: "1", TeamColour: "3671C6" } } });
    const state: JsonObject = {
      Position: { Position: [{ Entries: { "1": { Status: "OnTrack", X: 100, Y: 200, Z: 0 } } }] },
    };
    expect(selectPositions(state, drivers)).toEqual([{ tla: "VER", x: 100, y: 200 }]);
  });
});

describe("tyre stints", () => {
  const base = (stints: JsonObject): JsonObject => ({
    DriverList: { "81": { Tla: "PIA", RacingNumber: "81", TeamColour: "FF8000" } },
    TimingData: { Lines: { "81": { Position: "1", GapToLeader: "LAP 26" } } },
    TimingAppData: { Lines: { "81": { Stints: stints } } },
  });

  it("uses the newest stint's compound and age", () => {
    const row = selectTiming(base({
      "0": { Compound: "MEDIUM", TotalLaps: 16 },
      "1": { Compound: "HARD", TotalLaps: 9 },
    }), {})[0]!;

    expect(row.tyre).toBe("H");
    expect(row.tyreAge).toBe(9);
    expect(row.stops).toBe(1);
  });

  it("walks back when the newest stint omits its compound", () => {
    // The feed frequently omits Compound on a stint it already announced.
    // Defaulting to HARD silently showed the wrong tyre for the whole field.
    const row = selectTiming(base({
      "0": { Compound: "SOFT", TotalLaps: 14 },
      "1": { TotalLaps: 3 },
    }), {})[0]!;

    expect(row.tyre).toBe("S");
    expect(row.tyreAge).toBe(3);
  });

  it("does not mark the leader as lapped", () => {
    const row = selectTiming(base({ "0": { Compound: "HARD", TotalLaps: 9 } }), {})[0]!;
    expect(row.status).toBe("racing");
  });
});

describe("mini-sector status codes", () => {
  it("maps the documented values", () => {
    expect(segmentPace(2051)).toBe("overall");
    expect(segmentPace(2049)).toBe("personal");
    expect(segmentPace(2048)).toBe("slower");
    expect(segmentPace(2064)).toBe("pit");
    expect(segmentPace(0)).toBe("none");
  });

  it("renders an unknown code neutral rather than throwing", () => {
    // A feed change must degrade, never crash (docs/13).
    expect(segmentPace(9999)).toBe("none");
    expect(segmentPace(undefined)).toBe("none");
  });
});

describe("driver state", () => {
  it("prefixes the team colour, which the feed sends without '#'", () => {
    const drivers = selectDrivers({ DriverList: { "16": { Tla: "LEC", RacingNumber: "16", TeamColour: "E80020" } } });
    expect(drivers["LEC"]!.color).toBe("#E80020");
  });

  it("falls back to neutral when a colour has not arrived", () => {
    const drivers = selectDrivers({ DriverList: { "16": { Tla: "LEC", RacingNumber: "16" } } });
    expect(drivers["LEC"]!.color).toBe("var(--neutral)");
  });

  it("uses the explicit booleans, not the advisory Status bitfield", () => {
    const state: JsonObject = {
      DriverList: { "22": { Tla: "TSU", RacingNumber: "22" } },
      TimingData: { Lines: { "22": { Position: "20", Status: 64, Retired: true } } },
    };
    expect(selectTiming(state, {})[0]!.status).toBe("retired");
  });
});

describe("track state", () => {
  it("maps status codes", () => {
    expect(selectTrackState({ TrackStatus: { Status: "1" } })).toBe("none");
    expect(selectTrackState({ TrackStatus: { Status: "4" } })).toBe("safety-car");
    expect(selectTrackState({ TrackStatus: { Status: "5" } })).toBe("red-flag");
  });

  it("lets a finished session override the flag state", () => {
    expect(selectTrackState({
      TrackStatus: { Status: "1" },
      SessionStatus: { Status: "Finalised" },
    })).toBe("chequered");
  });
});
