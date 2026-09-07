import { TYRE } from "@/features/live/model/constants";
import type { TyreCompound } from "@/features/live/model/types";
import type { DriverAnalysis } from "../model/types";
import s from "./Analysis.module.css";

const COMPOUND: Record<string, TyreCompound> = {
  SOFT: "S", MEDIUM: "M", HARD: "H", INTERMEDIATE: "I", WET: "W",
};

/**
 * One bar per stint, coloured by compound and sized by laps.
 *
 * A used set is hatched rather than given a separate colour: compound colour is
 * fixed sport vocabulary and must not be spent on a second meaning.
 */
export function StrategyTimeline({ drivers, totalLaps }: { drivers: DriverAnalysis[]; totalLaps: number }) {
  const withStints = drivers.filter((d) => d.stints.length > 0);

  if (withStints.length === 0) {
    return <div className={s.empty}>NO STINT DATA YET</div>;
  }

  // Every driver is drawn against the same lap span, so stops line up
  // vertically and a strategy divergence is visible at a glance.
  const span = Math.max(totalLaps, ...withStints.map((d) => d.stints.at(-1)?.endLap ?? 0));

  return (
    <div>
      {withStints.map((driver) => (
        <div key={driver.racingNumber} className={s.strategyRow}>
          <div className={s.strategyTla}>{driver.tla}</div>

          <div className={s.strategyBars}>
            {driver.stints.map((stint) => {
              const compound = COMPOUND[stint.compound.toUpperCase()] ?? "H";
              const tyre = TYRE[compound];

              return (
                <div
                  key={stint.index}
                  className={`${s.stint} ${stint.newTyres ? "" : s.stintUsed}`}
                  style={{
                    flexGrow: stint.laps,
                    flexBasis: 0,
                    background: tyre.bg,
                    color: tyre.fg,
                    // Near-white HARD needs an edge or it reads as a gap.
                    boxShadow: compound === "H" ? "inset 0 0 0 1px var(--border-strong)" : undefined,
                  }}
                  title={
                    `${stint.compound} · laps ${stint.startLap}-${stint.endLap} ` +
                    `(${stint.laps}) · ${stint.newTyres ? "new" : "used"}`
                  }
                >
                  {stint.laps >= 4 ? `${compound}${stint.laps}` : ""}
                </div>
              );
            })}
            {/* Pads a retirement to the full width so bars stay comparable. */}
            {(() => {
              const covered = driver.stints.reduce((n, st) => n + st.laps, 0);
              return covered < span
                ? <div style={{ flexGrow: span - covered, flexBasis: 0 }} />
                : null;
            })()}
          </div>

          <div className={s.strategyStops}>
            {driver.stints.length - 1} stop{driver.stints.length === 2 ? "" : "s"}
          </div>
        </div>
      ))}

      <div className={s.legend}>
        {(["S", "M", "H", "I", "W"] as const).map((c) => (
          <div key={c} className={s.legendItem}>
            <div className={s.swatch} style={{ background: TYRE[c].bg }} /> {TYRE[c].name}
          </div>
        ))}
        <div className={s.legendItem}>
          <div className={`${s.swatch} ${s.stintUsed}`} style={{ background: "var(--text-fainter)" }} /> Used set
        </div>
      </div>
    </div>
  );
}
