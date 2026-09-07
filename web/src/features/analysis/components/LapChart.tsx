import { useMemo } from "react";
import type { DriverAnalysis, LapRecord } from "../model/types";
import { driverColor, formatTime, isOutlier, paceLaps } from "../model/types";
import s from "./Analysis.module.css";

const WIDTH = 1000;
const HEIGHT = 220;
const PAD_LEFT = 52;
const PAD_BOTTOM = 22;
const PAD_TOP = 10;

/** Why a lap is not a clean measure of pace. */
function lapKind(lap: LapRecord, outlier: boolean): { label: string; color: string } | null {
  if (lap.inPit) return { label: "IN PIT", color: "var(--blue)" };
  if (lap.pitOut) return { label: "OUT LAP", color: "var(--blue-lighter)" };
  if (lap.trackStatus === "5") return { label: "RED FLAG", color: "var(--red)" };
  if (lap.trackStatus === "4") return { label: "SAFETY CAR", color: "var(--yellow)" };
  if (lap.trackStatus === "6") return { label: "VSC", color: "var(--yellow)" };
  // A stoppage or restart lap whose flag had already cleared by the time it
  // completed — see paceLaps.
  if (outlier) return { label: "STOPPED / RESTART", color: "var(--orange)" };
  return null;
}

/**
 * Every lap of one driver's session as a bar.
 *
 * Pit, out, safety-car and VSC laps are drawn in their own colour rather than
 * hidden. They are 20–40 seconds slower and would otherwise read as a collapse
 * in pace — but removing them entirely would leave unexplained gaps in the lap
 * sequence, which is worse.
 *
 * The scale is built from representative laps only, so one 2-minute safety-car
 * lap cannot flatten the other fifty.
 */
export function LapChart({ driver }: { driver: DriverAnalysis }) {
  const model = useMemo(() => {
    const laps = driver.laps.filter((l) => l.timeSeconds !== null && l.timeSeconds > 0);
    if (laps.length === 0) return null;

    const clean = paceLaps(driver).map((l) => l.timeSeconds!);
    const best = clean.length > 0 ? Math.min(...clean) : Math.min(...laps.map((l) => l.timeSeconds!));
    const worst = clean.length > 0 ? Math.max(...clean) : Math.max(...laps.map((l) => l.timeSeconds!));

    // A little headroom, and a floor on the span so a very consistent stint
    // does not turn tenths into full-height bars.
    const span = Math.max(1.5, (worst - best) * 1.25);
    const floor = best - span * 0.08;
    const ceiling = floor + span * 1.16;

    return { laps, best, floor, ceiling };
  }, [driver]);

  if (!model) {
    return <div className={s.empty}>NO COMPLETED LAPS</div>;
  }

  const { laps, best, floor, ceiling } = model;
  const plotWidth = WIDTH - PAD_LEFT - 8;
  const plotHeight = HEIGHT - PAD_TOP - PAD_BOTTOM;
  const barWidth = Math.max(2, plotWidth / laps.length - 2);

  const y = (seconds: number) =>
    PAD_TOP + plotHeight * (1 - (Math.min(ceiling, Math.max(floor, seconds)) - floor) / (ceiling - floor));

  const ticks = [0, 0.25, 0.5, 0.75, 1].map((f) => floor + (ceiling - floor) * f);

  return (
    <>
      <svg className={s.svg} viewBox={`0 0 ${WIDTH} ${HEIGHT}`} role="img"
           aria-label={`Lap times for ${driver.tla}`}>
        {ticks.map((t) => (
          <g key={t}>
            <line className={s.grid} x1={PAD_LEFT} y1={y(t)} x2={WIDTH - 8} y2={y(t)} />
            <text className={s.axis} x={PAD_LEFT - 6} y={y(t) + 3} textAnchor="end">
              {formatTime(t)}
            </text>
          </g>
        ))}

        {laps.map((lap, i) => {
          const kind = lapKind(lap, isOutlier(driver, lap));
          const top = y(lap.timeSeconds!);
          const isBest = lap.timeSeconds === best && !kind;

          return (
            <rect
              key={lap.lap}
              x={PAD_LEFT + (i * plotWidth) / laps.length}
              y={top}
              width={barWidth}
              height={Math.max(1, HEIGHT - PAD_BOTTOM - top)}
              fill={kind ? kind.color : isBest ? "var(--purple)" : driverColor(driver)}
              opacity={kind ? 0.75 : 1}
            >
              <title>
                {`Lap ${lap.lap} · ${formatTime(lap.timeSeconds)}`}
                {kind ? ` · ${kind.label}` : isBest ? " · fastest" : ""}
                {lap.compound ? ` · ${lap.compound}${lap.tyreAge !== null ? ` ${lap.tyreAge}L` : ""}` : ""}
              </title>
            </rect>
          );
        })}

        <line className={s.grid} x1={PAD_LEFT} y1={HEIGHT - PAD_BOTTOM}
              x2={WIDTH - 8} y2={HEIGHT - PAD_BOTTOM} />

        {laps.filter((_, i) => i % Math.ceil(laps.length / 14) === 0).map((lap, i, shown) => {
          const index = laps.indexOf(lap);
          return (
            <text key={lap.lap} className={s.axis}
                  x={PAD_LEFT + (index * plotWidth) / laps.length + barWidth / 2}
                  y={HEIGHT - PAD_BOTTOM + 12}
                  textAnchor={i === shown.length - 1 ? "end" : "middle"}>
              {lap.lap}
            </text>
          );
        })}
      </svg>

      <div className={s.legend}>
        <div className={s.legendItem}>
          <div className={s.swatch} style={{ background: "var(--purple)" }} /> Fastest
        </div>
        <div className={s.legendItem}>
          <div className={s.swatch} style={{ background: driverColor(driver) }} /> Green-flag lap
        </div>
        <div className={s.legendItem}>
          <div className={s.swatch} style={{ background: "var(--yellow)" }} /> SC / VSC
        </div>
        <div className={s.legendItem}>
          <div className={s.swatch} style={{ background: "var(--blue)" }} /> Pit / out lap
        </div>
        <div className={s.legendItem}>
          <div className={s.swatch} style={{ background: "var(--orange)" }} /> Stopped / restart
        </div>
        <div className={s.legendItem}>{laps.length} laps · best {formatTime(best)}</div>
      </div>
    </>
  );
}
