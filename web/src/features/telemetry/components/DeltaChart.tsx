import { useMemo } from "react";
import type { TelemetryLap } from "@/features/analysis/model/types";
import { deltaSeries } from "../lib/lapMath";
import s from "./Telemetry.module.css";

const WIDTH = 1000;
const HEIGHT = 150;
const PAD_LEFT = 52;
const PAD_BOTTOM = 20;

/**
 * Cumulative time delta between two laps, against distance.
 *
 * This is the chart that answers "where did the lap go". Two speed traces show
 * where one car was quicker; only the delta shows what that was worth in time,
 * and where it was handed back.
 *
 * The line crossing zero means the advantage changed hands at that point on the
 * circuit, which is exactly the moment worth looking at.
 */
export function DeltaChart({
  lapA, lapB, tlaA, tlaB, colorA, colorB,
}: {
  lapA: TelemetryLap; lapB: TelemetryLap;
  tlaA: string; tlaB: string;
  colorA: string; colorB: string;
}) {
  const series = useMemo(() => deltaSeries(lapA, lapB), [lapA, lapB]);

  if (series.distance.length === 0) {
    return <div className={s.empty}>NOT ENOUGH DATA TO COMPARE</div>;
  }

  const limit = series.distance.at(-1) ?? 1;
  // A floor on the scale, or a two-thousandth difference fills the chart.
  const scale = Math.max(0.25, series.maxAbs * 1.15);

  const x = (d: number) => PAD_LEFT + ((WIDTH - PAD_LEFT - 8) * d) / limit;
  const y = (v: number) => (HEIGHT - PAD_BOTTOM) / 2 - (v / scale) * ((HEIGHT - PAD_BOTTOM) / 2 - 6);

  const points = series.distance
    .map((d, i) => `${x(d).toFixed(1)},${y(series.delta[i] ?? 0).toFixed(1)}`)
    .join(" ");

  // Filled to the zero line, split by sign, so "who is ahead here" reads
  // without following the line.
  const area = `${PAD_LEFT},${y(0)} ${points} ${x(limit)},${y(0)}`;

  const ticks = [-scale, -scale / 2, 0, scale / 2, scale];

  return (
    <>
      <svg className={s.svg} viewBox={`0 0 ${WIDTH} ${HEIGHT}`} role="img"
           aria-label={`Time delta between ${tlaA} and ${tlaB}`}>
        <defs>
          <linearGradient id="deltaFill" x1="0" y1="0" x2="0" y2="1">
            <stop offset="0%" stopColor={colorB} stopOpacity="0.28" />
            <stop offset="50%" stopColor={colorB} stopOpacity="0.02" />
            <stop offset="50%" stopColor={colorA} stopOpacity="0.02" />
            <stop offset="100%" stopColor={colorA} stopOpacity="0.28" />
          </linearGradient>
        </defs>

        {ticks.map((t) => (
          <g key={t}>
            <line className={t === 0 ? s.zeroLine : s.grid}
                  x1={PAD_LEFT} y1={y(t)} x2={WIDTH - 8} y2={y(t)} />
            <text className={s.axis} x={PAD_LEFT - 6} y={y(t) + 3} textAnchor="end">
              {t === 0 ? "0" : `${t > 0 ? "+" : ""}${t.toFixed(2)}`}
            </text>
          </g>
        ))}

        <polygon points={area} fill="url(#deltaFill)" />
        <polyline points={points} fill="none" stroke="var(--text)" strokeWidth={1.6} />

        {Array.from({ length: 6 }, (_, i) => (limit * i) / 5).map((d) => (
          <text key={d} className={s.axis} x={x(d)} y={HEIGHT - 4} textAnchor="middle">
            {(d / 1000).toFixed(1)} km
          </text>
        ))}
      </svg>

      <div className={s.legend}>
        <div className={s.deltaSummary}>
          <span className={s.deltaValue}
                style={{ color: series.final > 0 ? colorB : colorA }}>
            {series.final >= 0 ? "+" : ""}{series.final.toFixed(3)}s
          </span>
          <span className={s.deltaWho}>
            {series.final > 0 ? `${tlaB} ahead over the lap` : `${tlaA} ahead over the lap`}
          </span>
        </div>
        <div className={s.legendItem}>
          Above the line: {tlaB} faster · below: {tlaA} faster
        </div>
      </div>
    </>
  );
}
