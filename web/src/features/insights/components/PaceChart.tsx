import { useMemo } from "react";
import type { PaceLine } from "@/features/live/model/types";
import s from "./Insights.module.css";

const WIDTH = 520;
const HEIGHT = 170;
const PAD = { top: 10, right: 10, bottom: 20, left: 42 };

interface PaceChartProps {
  lines: PaceLine[];
  laps: number[];
}

/**
 * Lap times over the last laps, one line per focused driver.
 *
 * Drawn as SVG rather than canvas for one specific reason: the PDF export is a
 * browser print, and an SVG chart prints as vectors while a canvas prints as a
 * screenshot.
 */
export function PaceChart({ lines, laps }: PaceChartProps) {
  const scale = useMemo(() => {
    const values = lines.flatMap((l) => l.laps).filter(Number.isFinite);
    if (values.length === 0) return null;

    let min = Math.min(...values);
    let max = Math.max(...values);

    // One in-lap or a safety car lap is 20+ seconds slower than green-flag pace
    // and would flatten every real difference into a straight line. The window
    // is clamped around the median instead, and outliers simply leave the
    // frame.
    const sorted = [...values].sort((a, b) => a - b);
    const median = sorted[Math.floor(sorted.length / 2)]!;
    max = Math.min(max, median * 1.06);
    min = Math.max(min, median * 0.94);

    if (max - min < 0.5) {
      // A field running within half a second still deserves a readable axis.
      const mid = (max + min) / 2;
      min = mid - 0.25;
      max = mid + 0.25;
    }

    return { min, max };
  }, [lines]);

  if (!scale || laps.length < 2) {
    return (
      <div className={s.panel}>
        <div className={s.title}>Pace · last {laps.length || 10} laps</div>
        <div className={s.empty}>
          Lap times build up as the session runs — two completed laps are needed
          before there is a line to draw.
        </div>
      </div>
    );
  }

  const x = (i: number) =>
    PAD.left + (i / Math.max(1, laps.length - 1)) * (WIDTH - PAD.left - PAD.right);

  const y = (seconds: number) =>
    PAD.top + (1 - (seconds - scale.min) / (scale.max - scale.min)) * (HEIGHT - PAD.top - PAD.bottom);

  const ticks = [scale.min, (scale.min + scale.max) / 2, scale.max];

  return (
    <div className={s.panel}>
      <div className={s.title}>Pace · last {laps.length} laps</div>

      <svg
        className={s.chart}
        viewBox={`0 0 ${WIDTH} ${HEIGHT}`}
        preserveAspectRatio="xMidYMid meet"
        role="img"
        aria-label={`Lap times over the last ${laps.length} laps`}
      >
        {ticks.map((value) => (
          <g key={value}>
            <line className={s.gridline} x1={PAD.left} x2={WIDTH - PAD.right} y1={y(value)} y2={y(value)} />
            <text className={s.axis} x={PAD.left - 5} y={y(value) + 3} textAnchor="end">
              {value.toFixed(1)}
            </text>
          </g>
        ))}

        {laps.map((lap, i) =>
          i % Math.ceil(laps.length / 6) === 0 ? (
            <text key={lap} className={s.axis} x={x(i)} y={HEIGHT - 6} textAnchor="middle">
              {lap}
            </text>
          ) : null,
        )}

        {lines.map((line) => (
          <path
            key={line.tla}
            d={pathFor(line.laps, x, y)}
            fill="none"
            stroke={line.color}
            strokeWidth={1.6}
            strokeLinejoin="round"
          />
        ))}
      </svg>

      <div className={s.legend}>
        {lines.map((line) => (
          <span key={line.tla} className={s.legendItem}>
            <span className={s.swatch} style={{ background: line.color }} />
            {line.tla}
          </span>
        ))}
      </div>
    </div>
  );
}

/**
 * A path that BREAKS at missing laps rather than bridging them.
 *
 * A driver has no time for a lap they pitted on or a lap run before they
 * joined. Joining across the gap would draw a straight segment through data
 * that does not exist, and it would look exactly like a real lap.
 */
function pathFor(values: number[], x: (i: number) => number, y: (v: number) => number): string {
  const parts: string[] = [];
  let penDown = false;

  values.forEach((value, i) => {
    if (!Number.isFinite(value)) {
      penDown = false;
      return;
    }
    parts.push(`${penDown ? "L" : "M"}${x(i).toFixed(1)} ${y(value).toFixed(1)}`);
    penDown = true;
  });

  return parts.join(" ");
}
