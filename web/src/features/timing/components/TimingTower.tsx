import { PACE_COLOR } from "@/features/live/model/constants";
import type { Driver, GapMode, TimingRow } from "@/features/live/model/types";
import { DriverRow } from "./DriverRow";
import s from "./TimingTower.module.css";

interface TimingTowerProps {
  timing: TimingRow[];
  drivers: Record<string, Driver>;
  gapMode: GapMode;
  selected: string | null;
  onGapModeChange(mode: GapMode): void;
  onSelect(tla: string): void;
}

const LEGEND: { label: string; pace: keyof typeof PACE_COLOR }[] = [
  { label: "Session", pace: "overall" },
  { label: "Personal", pace: "personal" },
  { label: "Slower", pace: "slower" },
  { label: "Pit", pace: "pit" },
];

const COLUMNS: { key: string; label: string; width?: number; align?: "left" | "right" | "center" }[] = [
  { key: "pos", label: "POS", width: 44, align: "center" },
  { key: "num", label: "NUM", width: 42, align: "center" },
  { key: "driver", label: "DRIVER", width: 158, align: "left" },
  { key: "drs", label: "DRS", width: 44, align: "center" },
  { key: "gap", label: "GAP", width: 92, align: "right" },
  { key: "segments", label: "MINI-SECTORS", align: "left" },
  { key: "s1", label: "S1", width: 70, align: "right" },
  { key: "s2", label: "S2", width: 70, align: "right" },
  { key: "s3", label: "S3", width: 70, align: "right" },
  { key: "last", label: "LAST LAP", width: 86, align: "right" },
  { key: "best", label: "BEST LAP", width: 86, align: "right" },
  { key: "tyre", label: "TIRE", width: 52, align: "center" },
  { key: "age", label: "AGE", width: 52, align: "right" },
  { key: "status", label: "STATUS", width: 92, align: "left" },
];

export function TimingTower({
  timing, drivers, gapMode, selected, onGapModeChange, onSelect,
}: TimingTowerProps) {
  return (
    <section>
      <div className={s.header}>
        <div className={s.marker} />
        <h2 className={s.title}>Timing tower</h2>
        <div className={s.count}>{timing.length} ACTIVE</div>
        <div className={s.rule} />

        <div className={s.legend}>
          {LEGEND.map((item) => (
            <div key={item.label} className={s.legendItem}>
              <div className={s.swatch} style={{ background: PACE_COLOR[item.pace] }} />
              {item.label}
            </div>
          ))}
        </div>

        {/* Gap and interval answer different questions; which one a viewer wants
            changes through a race, so it is a toggle rather than a setting. */}
        <div className={s.toggle} role="group" aria-label="Gap or interval">
          {(["gap", "interval"] as const).map((mode) => (
            <button
              key={mode}
              type="button"
              className={`${s.toggleButton} ${gapMode === mode ? s.toggleActive : ""}`}
              aria-pressed={gapMode === mode}
              onClick={() => onGapModeChange(mode)}
            >
              {mode === "gap" ? "GAP" : "INTV"}
            </button>
          ))}
        </div>
      </div>

      {timing.length === 0 ? (
        <div className={s.empty}>WAITING FOR TIMING DATA</div>
      ) : (
        <table className={s.table}>
          <colgroup>
            {COLUMNS.map((c) => (
              <col key={c.key} style={c.width ? { width: c.width } : undefined} />
            ))}
          </colgroup>
          <thead>
            <tr>
              {COLUMNS.map((c) => (
                <th
                  key={c.key}
                  scope="col"
                  className={c.key === "gap" ? s.thGap : undefined}
                  style={{
                    textAlign: c.align ?? "left",
                    paddingLeft: c.align === "left" ? 12 : undefined,
                    paddingRight: c.align === "right" ? 14 : undefined,
                  }}
                  onClick={c.key === "gap"
                    ? () => onGapModeChange(gapMode === "gap" ? "interval" : "gap")
                    : undefined}
                >
                  {c.key === "gap" ? (gapMode === "interval" ? "INTV" : "GAP") : c.label}
                </th>
              ))}
            </tr>
          </thead>
          <tbody>
            {timing.map((row, i) => (
              // Keyed by driver, never by index — index keys tear the reorder
              // animation the moment two cars swap places.
              <DriverRow
                key={row.tla}
                row={row}
                driver={drivers[row.tla]}
                index={i}
                gapMode={gapMode}
                selected={selected === row.tla}
                onSelect={onSelect}
              />
            ))}
          </tbody>
        </table>
      )}
    </section>
  );
}
