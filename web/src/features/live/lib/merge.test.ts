import { readFileSync, readdirSync } from "node:fs";
import { dirname, join, resolve } from "node:path";
import { describe, expect, it } from "vitest";
import { merge, normalise, type JsonObject } from "./merge";

/**
 * shared-fixtures/ sits at the repository root so the C# and TypeScript suites
 * read the same bytes. Walk up rather than hardcoding a depth.
 */
function fixtureDir(): string {
  let dir = resolve(__dirname);
  for (let i = 0; i < 10; i++) {
    try {
      const candidate = join(dir, "shared-fixtures", "merge");
      readdirSync(candidate);
      return candidate;
    } catch {
      dir = dirname(dir);
    }
  }
  throw new Error("shared-fixtures/merge not found");
}

interface Fixture {
  name: string;
  why: string;
  initial: JsonObject;
  patch: JsonObject;
  expected: JsonObject;
}

describe("merge — shared fixtures (parity with DeltaMerge.cs)", () => {
  const dir = fixtureDir();
  const files = readdirSync(dir).filter((f) => f.endsWith(".json")).sort();

  it("actually finds the fixtures", () => {
    // Guards against a green suite that tested nothing.
    expect(files.length).toBeGreaterThanOrEqual(9);
  });

  for (const file of files) {
    const fixture = JSON.parse(readFileSync(join(dir, file), "utf8")) as Fixture;

    it(fixture.name, () => {
      const state = structuredClone(fixture.initial);
      merge(state, fixture.patch);
      expect(state).toEqual(fixture.expected);
    });
  }
});

describe("merge — the four cases docs/06 names", () => {
  it("a sparse sector patch leaves the other sectors untouched", () => {
    const state: JsonObject = {
      Lines: { "44": { Sectors: { "0": { Value: "28.1" }, "1": { Value: "31.2" }, "2": { Value: "24.0" } } } },
    };
    merge(state, { Lines: { "44": { Sectors: { "1": { Value: "30.9" } } } } });

    const sectors = (state.Lines as JsonObject)["44"] as JsonObject;
    expect((sectors.Sectors as JsonObject)["0"]).toEqual({ Value: "28.1" });
    expect((sectors.Sectors as JsonObject)["1"]).toEqual({ Value: "30.9" });
    expect((sectors.Sectors as JsonObject)["2"]).toEqual({ Value: "24.0" });
  });

  it("patching one driver does not wipe the others", () => {
    const state: JsonObject = { Lines: { "1": { Position: "1" }, "44": { Position: "2" } } };
    merge(state, { Lines: { "44": { Position: "1" } } });
    expect((state.Lines as JsonObject)["1"]).toEqual({ Position: "1" });
  });

  it("strips the _kf keyframe marker", () => {
    const state: JsonObject = {};
    merge(state, { Status: "1", _kf: true });
    expect(state).toEqual({ Status: "1" });
  });

  it("removes keys listed in _deleted", () => {
    const state: JsonObject = { Lines: { "1": {}, "44": {} } };
    merge(state, { Lines: { _deleted: ["1"] } });
    expect(Object.keys(state.Lines as JsonObject)).toEqual(["44"]);
  });
});

describe("merge — properties that must hold for every patch", () => {
  it("is idempotent for a repeated value", () => {
    // The reconnect path deliberately replays deltas that may already have been
    // applied. Duplicating one must be harmless; missing one is not.
    const patch = { Lines: { "44": { Position: "1", Sectors: { "0": { Value: "28.1" } } } } };
    const once: JsonObject = { Lines: { "44": { Position: "2" } } };
    const twice: JsonObject = { Lines: { "44": { Position: "2" } } };

    merge(once, structuredClone(patch));
    merge(twice, structuredClone(patch));
    merge(twice, structuredClone(patch));

    expect(twice).toEqual(once);
  });

  it("does not alias nodes from the patch", () => {
    // A shared reference means mutating state later silently rewrites the delta
    // that produced it — a bug that only surfaces on the second application.
    const state: JsonObject = {};
    const patch: JsonObject = { Lines: { "44": { Position: "1" } } };

    merge(state, patch);
    ((state.Lines as JsonObject)["44"] as JsonObject).Position = "9";

    expect(((patch.Lines as JsonObject)["44"] as JsonObject).Position).toBe("1");
  });

  it("normalises a top-level array into an index-keyed object", () => {
    expect(normalise([{ a: 1 }, { a: 2 }])).toEqual({ "0": { a: 1 }, "1": { a: 2 } });
  });
});
