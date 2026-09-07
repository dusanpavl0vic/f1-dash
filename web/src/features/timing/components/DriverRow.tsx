import { memo } from "react";
import {
  DRIVER_STATUS_LABEL, PACE_COLOR, TYRE, TYRE_AGE_CRITICAL, TYRE_AGE_WARN,
} from "@/features/live/model/constants";
import { isLapCounter } from "@/features/live/model/selectors";
import type { Driver, GapMode, TimingRow } from "@/features/live/model/types";
import { MiniSectors } from "./MiniSectors";
import s from "./TimingTower.module.css";

interface DriverRowProps {
  row: TimingRow;
  driver: Driver | undefined;
  index: number;
  gapMode: GapMode;
  selected: boolean;
  onSelect(tla: string): void;
}

function ageColor(age: number): string {
  if (age > TYRE_AGE_CRITICAL) return "var(--red-bright)";
  if (age > TYRE_AGE_WARN) return "var(--yellow)";
  return "var(--text-dim)";
}

export const DriverRow = memo(function DriverRow({
  row, driver, index, gapMode, selected, onSelect,
}: DriverRowProps) {
  const retired = row.status === "retired";
  const color = driver?.color ?? "var(--neutral)";
  const tyre = TYRE[row.tyre];
  const status = DRIVER_STATUS_LABEL[row.status];

  // The leader's gap is empty in the feed. Showing "+0.000" would be a lie and
  // showing nothing reads as missing data, so it is labelled.
  const rawGap = gapMode === "interval" ? row.interval : row.gap;
  // The leader's GapToLeader is the lap counter ("LAP 26"), not a gap.
  const gapValue = row.position === 1 || isLapCounter(rawGap) ? "LEADER" : rawGap || "—";

  const gapClass =
    row.position === 1 ? s.gapLeader : row.status === "lapped" ? s.gapLapped : "";

  const rowClass = [
    s.row,
    selected ? s.rowSelected : index % 2 ? s.rowOdd : s.rowEven,
    row.status === "pit" ? s.rowPit : "",
    retired ? s.rowRetired : "",
  ].filter(Boolean).join(" ");

  return (
    <tr className={rowClass} onClick={() => onSelect(row.tla)}>
      <td className={`${s.cellPos}`}>
        <div className={s.teamBar} style={{ background: color }} />
        <span className={`${s.pos} ${retired ? s.posDim : ""}`}>{row.position}</span>
      </td>

      <td className={s.cellNum} style={{ color }}>{driver?.number ?? ""}</td>

      <td className={s.cellDriver}>
        <div className={s.tla} style={retired ? { color: "var(--text-faintest)" } : undefined}>
          {row.tla}
        </div>
        <div className={s.team}>{driver?.teamName ?? ""}</div>
      </td>

      <td className={s.cellDrs}>
        <span className={`${s.drs} ${row.drsActive ? s.drsOn : s.drsOff}`}>
          {row.drsActive ? "DRS" : "—"}
        </span>
      </td>

      <td className={s.cellGap}>
        <span className={`${s.gap} ${gapClass}`}>{gapValue}</span>
      </td>

      <td className={s.cellSegments}>
        <MiniSectors segments={row.segments} />
      </td>

      {row.sectors.map((sector, i) => (
        <td key={i} className={s.cellTime} style={{ color: PACE_COLOR[sector.pace] }}>
          {sector.value || "—"}
        </td>
      ))}

      <td className={s.cellLap} style={retired ? { color: "var(--text-faintest)" } : undefined}>
        {row.lastLap || "—"}
      </td>

      <td
        className={s.cellLap}
        style={{ color: row.bestIsOverall ? "var(--purple)" : "var(--green)" }}
      >
        {row.bestLap || "—"}
      </td>

      <td className={s.cellTyre}>
        <div
          className={`${s.tyre} ${row.tyre === "H" ? s.tyreHard : ""}`}
          style={{ background: tyre.bg, color: tyre.fg }}
          title={tyre.name}
        >
          {row.tyre}
        </div>
      </td>

      <td className={s.cellAge} style={{ color: ageColor(row.tyreAge) }}>
        {row.tyreAge}L
      </td>

      <td className={s.cellStatus}>
        <span className={s.status} style={{ color: status.fg }}>{status.label}</span>
      </td>
    </tr>
  );
});
