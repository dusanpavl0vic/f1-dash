import { useMemo } from "react";
import type { TelemetryLap } from "@/features/analysis/model/types";
import { toScreen } from "@/features/track-map/lib/toScreen";
import { useTrackGeometry } from "@/features/track-map/lib/useTrackGeometry";
import s from "./Telemetry.module.css";

/** Slow to fast; the values live in tokens.css beside every other colour. */
const RAMP = [
  "var(--speed-1)", "var(--speed-2)", "var(--speed-3)",
  "var(--speed-4)", "var(--speed-5)", "var(--speed-6)",
] as const;

function speedColour(kmh: number, min: number, max: number): string {
  const t = max === min ? 0 : (kmh - min) / (max - min);
  const index = Math.min(RAMP.length - 1, Math.max(0, Math.floor(t * RAMP.length)));
  return RAMP[index]!;
}

/**
 * The lap's racing line, drawn on the circuit and coloured by speed.
 *
 * This is the one telemetry view that shows WHERE on the track something
 * happened. A speed trace against distance tells you a car slowed at 1,850 m,
 * which means nothing without knowing the circuit; the same information on the
 * outline is immediately a corner.
 *
 * Drawn as one segment per sample rather than a single gradient-stroked path:
 * SVG gradients follow the bounding box, not the path, so a gradient would
 * colour by position on screen instead of by speed.
 */
export function RacingLine({
  lap, circuitKey, year, tla,
}: {
  lap: TelemetryLap; circuitKey: number | null; year: number | null; tla: string;
}) {
  const geometry = useTrackGeometry(circuitKey, year);

  const segments = useMemo(() => {
    if (!geometry || lap.x.length < 2) return [];

    const speeds = lap.speed.filter((v) => v > 0);
    const min = speeds.length ? Math.min(...speeds) : 0;
    const max = speeds.length ? Math.max(...speeds) : 1;

    const transform = { rotation: geometry.rotation, flipY: true };
    const result: { d: string; colour: string }[] = [];

    for (let i = 1; i < lap.x.length; i++) {
      // Samples before the first GPS update sit at the origin; they are not on
      // the circuit and would draw a spoke to the middle of the map.
      if (!lap.x[i] || !lap.y[i] || !lap.x[i - 1] || !lap.y[i - 1]) continue;

      const [x0, y0] = toScreen(lap.x[i - 1]!, lap.y[i - 1]!, transform);
      const [x1, y1] = toScreen(lap.x[i]!, lap.y[i]!, transform);

      result.push({
        d: `M ${x0.toFixed(0)},${y0.toFixed(0)} L ${x1.toFixed(0)},${y1.toFixed(0)}`,
        colour: speedColour(lap.speed[i] ?? 0, min, max),
      });
    }

    return result;
  }, [geometry, lap]);

  if (!geometry) {
    return <div className={s.empty}>CIRCUIT GEOMETRY UNAVAILABLE</div>;
  }

  if (segments.length === 0) {
    return <div className={s.empty}>NO GPS RECORDED FOR THIS LAP</div>;
  }

  const vb = geometry.viewBox;
  const speeds = lap.speed.filter((v) => v > 0);

  return (
    <>
      <svg className={s.svg}
           viewBox={`${vb.x} ${vb.y} ${vb.width} ${vb.height}`}
           preserveAspectRatio="xMidYMid meet"
           role="img" aria-label={`Racing line for ${tla}, lap ${lap.lap}, coloured by speed`}>
        {/* The circuit underneath, so the line is read against the track. */}
        <path d={geometry.path} fill="none" stroke="var(--surface-3)"
              strokeWidth={300} strokeLinejoin="round" strokeLinecap="round" />

        {segments.map((segment, i) => (
          <path key={i} d={segment.d} stroke={segment.colour}
                strokeWidth={130} strokeLinecap="round" fill="none" />
        ))}
      </svg>

      <div className={s.legend}>
        {RAMP.map((colour, i) => (
          <div key={colour} className={s.legendItem}>
            <span className={s.swatch} style={{ background: colour, height: 8 }} />
            {i === 0 ? "Slow" : i === RAMP.length - 1 ? "Fast" : ""}
          </div>
        ))}
        <div className={s.legendItem}>
          {tla} lap {lap.lap} · {speeds.length ? Math.min(...speeds) : 0}–
          {speeds.length ? Math.max(...speeds) : 0} km/h
        </div>
      </div>
    </>
  );
}
