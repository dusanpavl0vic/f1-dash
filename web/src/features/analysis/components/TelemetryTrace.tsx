import type { TelemetryLap } from "../model/types";
import s from "./Analysis.module.css";

const WIDTH = 1000;
const SPEED_HEIGHT = 160;
const PEDAL_HEIGHT = 70;
const PAD_LEFT = 44;

/**
 * Speed and brake for one lap.
 *
 * The x axis is elapsed time within the lap, not distance. Distance would need
 * integrating the speed trace, and an integration error accumulates along the
 * lap — a time axis is exact and is what the feed actually gives us.
 */
export function TelemetryTrace({ trace, color }: { trace: TelemetryLap; color: string }) {
  if (trace.speed.length === 0) {
    return <div className={s.empty}>NO TELEMETRY FOR THIS LAP</div>;
  }

  const duration = trace.offsetMs.at(-1) ?? 1;
  const maxSpeed = Math.max(...trace.speed, 1);

  const x = (i: number) => PAD_LEFT + ((WIDTH - PAD_LEFT - 8) * (trace.offsetMs[i] ?? 0)) / duration;

  const line = (values: number[], height: number, max: number, top: number) =>
    values.map((v, i) => `${x(i).toFixed(1)},${(top + height * (1 - v / max)).toFixed(1)}`).join(" ");

  const speedTicks = [0, 100, 200, 300].filter((t) => t <= maxSpeed + 40);

  return (
    <>
      <svg className={s.svg} viewBox={`0 0 ${WIDTH} ${SPEED_HEIGHT + PEDAL_HEIGHT + 30}`}
           role="img" aria-label={`Speed and brake trace for lap ${trace.lap}`}>
        {speedTicks.map((t) => {
          const yy = SPEED_HEIGHT * (1 - t / maxSpeed);
          return (
            <g key={t}>
              <line className={s.grid} x1={PAD_LEFT} y1={yy} x2={WIDTH - 8} y2={yy} />
              <text className={s.axis} x={PAD_LEFT - 6} y={yy + 3} textAnchor="end">{t}</text>
            </g>
          );
        })}

        <polyline points={line(trace.speed, SPEED_HEIGHT, maxSpeed, 0)}
                  fill="none" stroke={color} strokeWidth={1.6} />
        <text className={s.axis} x={PAD_LEFT} y={12}>KM/H</text>

        {/* Brake sits under the speed trace, where a braking point lines up
            with the corner it belongs to. */}
        <g transform={`translate(0, ${SPEED_HEIGHT + 22})`}>
          <line className={s.grid} x1={PAD_LEFT} y1={PEDAL_HEIGHT} x2={WIDTH - 8} y2={PEDAL_HEIGHT} />
          <polyline points={line(trace.throttle, PEDAL_HEIGHT, 100, 0)}
                    fill="none" stroke="var(--green)" strokeWidth={1.3} />
          <polyline points={line(trace.brake, PEDAL_HEIGHT, 100, 0)}
                    fill="none" stroke="var(--red)" strokeWidth={1.6} />
          <text className={s.axis} x={PAD_LEFT} y={-4}>THROTTLE / BRAKE %</text>
        </g>
      </svg>

      <div className={s.legend}>
        <div className={s.legendItem}>
          <div className={s.swatch} style={{ background: color }} /> Speed
        </div>
        <div className={s.legendItem}>
          <div className={s.swatch} style={{ background: "var(--green)" }} /> Throttle
        </div>
        <div className={s.legendItem}>
          <div className={s.swatch} style={{ background: "var(--red)" }} /> Brake
        </div>
        <div className={s.legendItem}>
          Lap {trace.lap} · {trace.speed.length} samples · top {Math.max(...trace.speed)} km/h
        </div>
      </div>
    </>
  );
}
