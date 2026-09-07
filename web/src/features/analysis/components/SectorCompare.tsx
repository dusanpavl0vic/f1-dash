import type { DriverAnalysis, SectorComparison } from "../model/types";
import { driverColor, formatDelta, formatTime } from "../model/types";
import s from "./Analysis.module.css";

/**
 * Who is faster in which sector.
 *
 * The figures are each driver's BEST sector, not the sectors of their best lap:
 * a driver's quickest S1 and S3 usually come from different laps, and the
 * question is where each is quicker.
 *
 * Bars grow from the centre outward so the faster side is obvious before any
 * number is read.
 */
export function SectorCompare({
  comparison, driverA, driverB,
}: {
  comparison: SectorComparison;
  driverA: DriverAnalysis | undefined;
  driverB: DriverAnalysis | undefined;
}) {
  const colorA = driverA ? driverColor(driverA) : "var(--accent)";
  const colorB = driverB ? driverColor(driverB) : "var(--teal)";

  const totalDelta = comparison.sectors.reduce(
    (sum, sector) => sector.delta === null ? sum : sum + sector.delta, 0);

  return (
    <div>
      <div className={s.compareGrid}>
        <div className={s.compareHead} />
        <div className={s.compareHead} style={{ color: colorA, textAlign: "right" }}>
          {comparison.driverA}
        </div>
        <div className={s.compareHead} style={{ textAlign: "center" }}>Best sector</div>
        <div className={s.compareHead} style={{ color: colorB }}>{comparison.driverB}</div>
        <div className={s.compareHead} style={{ textAlign: "right" }}>Delta</div>

        {comparison.sectors.map((sector) => {
          const a = sector.bestA;
          const b = sector.bestB;

          // The bar shows the share of the gap, not the absolute time — the
          // sectors differ in length and would otherwise be incomparable.
          const gap = a !== null && b !== null ? Math.abs(a - b) : 0;
          const scale = gap > 0 ? Math.min(1, gap / 0.5) : 0;

          return (
            <div key={sector.sector} style={{ display: "contents" }}>
              <div className={s.compareHead}>S{sector.sector}</div>

              <div className={s.compareBar}>
                {a !== null && b !== null && a < b && (
                  <div className={`${s.compareFill} ${s.compareFillRight}`}
                       style={{ width: `${scale * 100}%`, background: colorA }} />
                )}
              </div>

              <div className={s.compareValue}>
                <span style={{ color: sector.faster === comparison.driverA ? colorA : "var(--text-dim)" }}>
                  {formatTime(a)}
                </span>
                {"  "}
                <span style={{ color: sector.faster === comparison.driverB ? colorB : "var(--text-dim)" }}>
                  {formatTime(b)}
                </span>
              </div>

              <div className={s.compareBar}>
                {a !== null && b !== null && b < a && (
                  <div className={s.compareFill}
                       style={{ width: `${scale * 100}%`, background: colorB }} />
                )}
              </div>

              <div className={s.compareDelta}
                   style={{ color: sector.delta === null ? "var(--text-faintest)"
                            : sector.delta < 0 ? colorA : colorB }}>
                {formatDelta(sector.delta)}
              </div>
            </div>
          );
        })}
      </div>

      <div className={s.legend}>
        <div className={s.legendItem}>
          Best lap: <span style={{ color: colorA }}>{formatTime(comparison.bestA)}</span>
          {" vs "}
          <span style={{ color: colorB }}>{formatTime(comparison.bestB)}</span>
        </div>
        <div className={s.legendItem}>
          Sum of best sectors:{" "}
          <span style={{ color: totalDelta < 0 ? colorA : colorB }}>
            {formatDelta(totalDelta)}
          </span>{" "}
          — {totalDelta < 0 ? comparison.driverA : comparison.driverB} ahead
        </div>
      </div>
    </div>
  );
}
