import type { TelemetryLap } from "@/features/analysis/model/types";
import { distanceSeries } from "../lib/lapMath";
import s from "./Telemetry.module.css";

const WIDTH = 1000;
const PAD_LEFT = 52;

type Channel = "speed" | "throttle" | "brake" | "gear" | "rpm";

const CHANNELS: Record<Channel, { label: string; unit: string; max: number | null; step: boolean }> = {
  speed:    { label: "Speed",    unit: "km/h", max: null, step: false },
  throttle: { label: "Throttle", unit: "%",    max: 100,  step: false },
  brake:    { label: "Brake",    unit: "%",    max: 100,  step: false },
  // Gear is a discrete state, so it is drawn as steps: interpolating between
  // 4th and 5th implies a gear that does not exist.
  gear:     { label: "Gear",     unit: "",     max: 8,    step: true },
  rpm:      { label: "RPM",      unit: "",     max: null, step: false },
};

interface Trace { lap: TelemetryLap; colour: string; tla: string }

/**
 * One channel, one or two drivers, plotted against DISTANCE rather than time.
 *
 * Distance is what makes two laps comparable: at the same distance both cars
 * are at the same point on the circuit, so a difference in the trace is a
 * difference in driving. Against time they drift apart and the same corner
 * appears at two different x positions.
 */
export function ChannelChart({
  channel, traces, height = 130,
}: {
  channel: Channel; traces: Trace[]; height?: number;
}) {
  const config = CHANNELS[channel];
  const withData = traces.filter((t) => t.lap[channel].length > 1);

  if (withData.length === 0) {
    return <div className={s.empty}>NO {config.label.toUpperCase()} DATA</div>;
  }

  const series = withData.map((t) => ({
    ...t,
    distance: distanceSeries(t.lap),
    values: t.lap[channel],
  }));

  const limit = Math.min(...series.map((t) => t.distance.at(-1) ?? 1));
  const max = config.max ?? Math.max(1, ...series.flatMap((t) => t.values));

  const x = (d: number) => PAD_LEFT + ((WIDTH - PAD_LEFT - 8) * d) / limit;
  const y = (v: number) => (height - 16) * (1 - v / max) + 6;

  const ticks = config.max === 100 ? [0, 50, 100]
    : config.max === 8 ? [0, 4, 8]
    : [0, max / 2, max];

  return (
    <>
      <svg className={s.svg} viewBox={`0 0 ${WIDTH} ${height}`} role="img"
           aria-label={`${config.label} against distance`}>
        {ticks.map((t) => (
          <g key={t}>
            <line className={s.grid} x1={PAD_LEFT} y1={y(t)} x2={WIDTH - 8} y2={y(t)} />
            <text className={s.axis} x={PAD_LEFT - 6} y={y(t) + 3} textAnchor="end">
              {Math.round(t)}
            </text>
          </g>
        ))}

        {series.map((trace) => {
          const points: string[] = [];

          for (let i = 0; i < trace.values.length; i++) {
            const d = trace.distance[i] ?? 0;
            if (d > limit) break;

            const px = x(d).toFixed(1);
            const py = y(trace.values[i] ?? 0).toFixed(1);

            // A step chart repeats the previous height before moving.
            if (config.step && i > 0) {
              points.push(`${px},${y(trace.values[i - 1] ?? 0).toFixed(1)}`);
            }
            points.push(`${px},${py}`);
          }

          return (
            <polyline key={trace.tla} points={points.join(" ")} fill="none"
                      stroke={trace.colour} strokeWidth={1.5} />
          );
        })}

        <text className={s.axis} x={PAD_LEFT} y={12}>
          {config.label.toUpperCase()}{config.unit ? ` (${config.unit})` : ""}
        </text>
      </svg>
    </>
  );
}
