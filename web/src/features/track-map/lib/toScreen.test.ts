import { describe, expect, it } from "vitest";
import { lerp, toScreen } from "./toScreen";

/**
 * The server transforms the outline and the client transforms the car dots. If
 * they disagree by even a rotation sign, every car renders beside the circuit
 * instead of on it. These are the shared fixture values.
 */
describe("toScreen", () => {
  const monza = { rotation: 95, flipY: true };

  // Real Monza outline points from the MultiViewer API, paired with what the
  // server's own transform produced for them. Taken from live responses, not
  // computed by hand — an invented fixture proves only that two guesses agree.
  it.each([
    { raw: [-1393, -874],  screen: [-749, -1464] },
    { raw: [-1384, -794],  screen: [-670, -1448] },
    { raw: [-527, 8219],   screen: [8234, 191] },
    { raw: [7368, 15113],  screen: [14413, 8657] },
  ])("matches the server transform for raw $raw", ({ raw, screen }) => {
    const [x, y] = toScreen(raw[0]!, raw[1]!, monza);
    expect(Math.round(x)).toBe(screen[0]);
    expect(Math.round(y)).toBe(screen[1]);
  });

  it("is the identity at zero rotation apart from the Y flip", () => {
    expect(toScreen(100, 200, { rotation: 0, flipY: true })).toEqual([100, -200]);
    expect(toScreen(100, 200, { rotation: 0, flipY: false })).toEqual([100, 200]);
  });

  it("rotates 90 degrees the same way the server does", () => {
    const [x, y] = toScreen(100, 0, { rotation: 90, flipY: false });
    expect(Math.round(x)).toBe(0);
    expect(Math.round(y)).toBe(-100);
  });
});

describe("lerp", () => {
  it("interpolates linearly", () => {
    expect(lerp(0, 10, 0)).toBe(0);
    expect(lerp(0, 10, 0.5)).toBe(5);
    expect(lerp(0, 10, 1)).toBe(10);
  });
});
