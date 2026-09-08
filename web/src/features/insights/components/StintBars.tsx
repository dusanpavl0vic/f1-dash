import type { Driver, Stint, TimingRow, TyreCompound } from "@/features/live/model/types";
import s from "./Insights.module.css";

const TYRE_TOKEN: Record<TyreCompound, string> = {
  S: "soft", M: "medium", H: "hard", I: "inter", W: "wet",
};

/**
 * Strategy at a glance: one row per driver, one bar per stint, widths in laps.
 *
 * The bars are proportional to the longest run in the field rather than to each
 * driver's own total, so a short first stint reads as short next to everyone
 * else's — which is the entire question a strategy view answers.
 */
export function StintBars({ stints }: { stints: Stint[] }) {
  const longest = Math.max(1, ...stints.map((d) => d.bars.reduce((t, b) => t + b.laps, 0)));

  if (stints.length === 0) {
    return (
      <div className={s.panel}>
        <div className={s.title}>Strategy</div>
        <div className={s.empty}>No stint data yet.</div>
      </div>
    );
  }

  return (
    <div className={s.panel}>
      <div className={s.title}>Strategy · stints by compound</div>

      {stints.map((driver) => {
        const total = driver.bars.reduce((t, b) => t + b.laps, 0);

        return (
          <div key={driver.tla} className={s.stintRow}>
            <span className={s.stintTla}>{driver.tla}</span>

            <div className={s.stintBars} style={{ width: `${(total / longest) * 100}%` }}>
              {driver.bars.map((bar, i) => (
                <span
                  key={`${bar.compound}-${i}`}
                  className={s.stintBar}
                  style={{
                    flexGrow: bar.laps,
                    background: `var(--tyre-${TYRE_TOKEN[bar.compound]})`,
                    color: `var(--tyre-${TYRE_TOKEN[bar.compound]}-fg)`,
                  }}
                  title={`${bar.compound} · ${bar.laps} laps`}
                >
                  {bar.laps >= 4 ? bar.laps : ""}
                </span>
              ))}
            </div>

            <span className={s.stintStops}>{driver.stops}</span>
          </div>
        );
      })}
    </div>
  );
}

/**
 * Gap to the leader as bars.
 *
 * Lapped cars are shown at full width rather than being scaled: a car a lap
 * down is not "far behind by N seconds", and drawing it on the same axis would
 * compress everyone still on the lead lap into nothing.
 */
export function GapBars({ timing, drivers }: { timing: TimingRow[]; drivers: Record<string, Driver> }) {
  const rows = timing.slice(0, 10);
  const numeric = rows
    .map((r) => Number(r.gap.replace("+", "")))
    .filter((n) => Number.isFinite(n) && n > 0);

  const widest = Math.max(1, ...numeric);

  if (rows.length === 0) {
    return (
      <div className={s.panel}>
        <div className={s.title}>Gap to leader</div>
        <div className={s.empty}>Waiting for timing data.</div>
      </div>
    );
  }

  return (
    <div className={s.panel}>
      <div className={s.title}>Gap to leader · top 10</div>

      {rows.map((row) => {
        const seconds = Number(row.gap.replace("+", ""));
        const lapped = !Number.isFinite(seconds);
        const width = lapped ? 100 : (seconds / widest) * 100;

        return (
          <div
            key={row.tla}
            className={s.gapRow}
            style={{ ["--team" as string]: drivers[row.tla]?.color ?? "var(--accent)" }}
          >
            <span className={s.stintTla}>{row.tla}</span>
            <div className={s.gapTrack}>
              <div className={s.gapFill} style={{ width: `${row.position === 1 ? 0 : width}%` }} />
            </div>
            <span className={s.gapValue}>{row.gap || "—"}</span>
          </div>
        );
      })}
    </div>
  );
}
