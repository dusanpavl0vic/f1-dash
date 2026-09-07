/**
 * Mirror of backend/src/F1Dash.Core/Track/TrackTransform.cs.
 *
 * The outline is transformed on the server and the car dots on the client, so
 * the two must agree exactly — a discrepancy puts every car beside the track
 * rather than on it. Both are pinned to the same fixture in toScreen.test.ts.
 */
export interface Transform {
  rotation: number;
  flipY: boolean;
}

export function toScreen(x: number, y: number, t: Transform): [number, number] {
  const a = (t.rotation * Math.PI) / 180;
  const rx = x * Math.cos(a) + y * Math.sin(a);
  const ry = -x * Math.sin(a) + y * Math.cos(a);
  // F1 coordinates have Y increasing upward; SVG has it increasing downward.
  return [rx, t.flipY ? -ry : ry];
}

/**
 * Linear interpolation between the last two known positions.
 *
 * Linear, never spline: splines overshoot on hairpins and cars visibly cut
 * across the grass. A gap longer than STALE_MS means the driver has probably
 * pitted or the feed stalled, so the dot snaps instead of sliding.
 */
export const STALE_MS = 3000;

export function lerp(from: number, to: number, alpha: number): number {
  return from + (to - from) * alpha;
}
