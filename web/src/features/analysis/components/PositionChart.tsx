import type { DriverAnalysis } from "../model/types";
import { driverColor } from "../model/types";
import s from "./Analysis.module.css";

const WIDTH = 1000;
const HEIGHT = 300;
const PAD_LEFT = 34;
const PAD_RIGHT = 44;
const PAD_TOP = 10;
const PAD_BOTTOM = 22;

/**
 * Every driver's position across the session, as a line with a dot per lap.
 *
 * Position 1 is at the TOP: it is the ordering everyone already reads on a
 * timing tower, and inverting it makes gaining places look like falling.
 *
 * Unselected drivers stay on the chart at low opacity rather than being hidden.
 * A position chart is about relative movement, and removing the field removes
 * the thing the selected driver moved through.
 */
export function PositionChart({
  drivers, totalLaps, highlighted,
}: {
  drivers: DriverAnalysis[];
  totalLaps: number;
  highlighted: string[];
}) {
  const withPositions = drivers.filter((d) => d.laps.some((l) => l.position !== null));

  if (withPositions.length === 0) {
    return <div className={s.empty}>NO POSITION DATA YET</div>;
  }

  const lastLap = Math.max(totalLaps, ...withPositions.map((d) => d.laps.at(-1)?.lap ?? 0));
  const maxPosition = Math.max(
    ...withPositions.flatMap((d) => d.laps.map((l) => l.position ?? 0)),
  );

  const plotWidth = WIDTH - PAD_LEFT - PAD_RIGHT;
  const plotHeight = HEIGHT - PAD_TOP - PAD_BOTTOM;

  const x = (lap: number) => PAD_LEFT + (plotWidth * (lap - 1)) / Math.max(1, lastLap - 1);
  const y = (position: number) => PAD_TOP + (plotHeight * (position - 1)) / Math.max(1, maxPosition - 1);

  const gridPositions = [1, 5, 10, 15, 20].filter((p) => p <= maxPosition);

  return (
    <>
      <svg className={s.svg} viewBox={`0 0 ${WIDTH} ${HEIGHT}`} role="img"
           aria-label="Position progression">
        {gridPositions.map((p) => (
          <g key={p}>
            <line className={s.grid} x1={PAD_LEFT} y1={y(p)} x2={WIDTH - PAD_RIGHT} y2={y(p)} />
            <text className={s.axis} x={PAD_LEFT - 6} y={y(p) + 3} textAnchor="end">P{p}</text>
          </g>
        ))}

        {withPositions.map((driver) => {
          const points = driver.laps
            .filter((l) => l.position !== null)
            .map((l) => `${x(l.lap).toFixed(1)},${y(l.position!).toFixed(1)}`);

          if (points.length === 0) return null;

          const active = highlighted.length === 0 || highlighted.includes(driver.tla);
          const last = driver.laps.filter((l) => l.position !== null).at(-1)!;

          return (
            <g key={driver.racingNumber} opacity={active ? 1 : 0.16}>
              <polyline
                points={points.join(" ")}
                fill="none"
                stroke={driverColor(driver)}
                strokeWidth={active ? 2 : 1.2}
                strokeLinejoin="round"
                strokeLinecap="round"
              />
              {active && driver.laps.filter((l) => l.position !== null).map((l) => (
                <circle key={l.lap} cx={x(l.lap)} cy={y(l.position!)} r={2.1}
                        fill={driverColor(driver)}>
                  <title>{`${driver.tla} · lap ${l.lap} · P${l.position}`}</title>
                </circle>
              ))}
              {active && (
                <text className={s.axis} x={x(last.lap) + 6} y={y(last.position!) + 3}
                      fill={driverColor(driver)} fontWeight={700}>
                  {driver.tla}
                </text>
              )}
            </g>
          );
        })}

        <line className={s.grid} x1={PAD_LEFT} y1={HEIGHT - PAD_BOTTOM}
              x2={WIDTH - PAD_RIGHT} y2={HEIGHT - PAD_BOTTOM} />
        {Array.from({ length: 6 }, (_, i) => Math.round(1 + (lastLap - 1) * (i / 5))).map((lap) => (
          <text key={lap} className={s.axis} x={x(lap)} y={HEIGHT - PAD_BOTTOM + 12} textAnchor="middle">
            {lap}
          </text>
        ))}
      </svg>

      <div className={s.legend}>
        <div className={s.legendItem}>Position 1 at the top · lap number along the bottom</div>
        <div className={s.legendItem}>
          {highlighted.length === 0 ? "All drivers" : `Highlighting ${highlighted.join(", ")}`}
        </div>
      </div>
    </>
  );
}
