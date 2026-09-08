import type { Driver, Penalty, PenaltyState } from "@/features/live/model/types";
import s from "./Insights.module.css";

const CHIP: Record<PenaltyState, string> = {
  SERVED: s.chipServed!,
  APPLIED: s.chipPending!,
  PENDING: s.chipPending!,
  NOTED: s.chipInvestigation!,
};

/**
 * Penalties, investigations and deleted laps.
 *
 * Separated from the general race control feed because they are the messages
 * that change the result. A five-second penalty scrolling past between two
 * blue-flag notices is a message the user will miss.
 */
export function PenaltyList({ penalties, drivers }: { penalties: Penalty[]; drivers: Record<string, Driver> }) {
  if (penalties.length === 0) {
    return (
      <div className={s.panel}>
        <div className={s.title}>Penalties</div>
        <div className={s.empty}>No penalties or investigations so far.</div>
      </div>
    );
  }

  return (
    <div className={s.panel}>
      <div className={s.title}>Penalties · {penalties.length}</div>

      {/* Newest first: the one that just happened is the one being looked for. */}
      {[...penalties].reverse().map((penalty, i) => (
        <div
          key={`${penalty.tla}-${penalty.lap}-${i}`}
          className={s.penalty}
          style={{ ["--team" as string]: drivers[penalty.tla]?.color ?? "var(--border-strong)" }}
        >
          <span className={s.penaltyDriver}>{penalty.tla}</span>

          <div>
            <div className={s.penaltyWhat}>{penalty.penalty}</div>
            <div className={s.penaltyWhy}>{penalty.reason}</div>
            {penalty.lap > 0 && <div className={s.penaltyLap}>LAP {penalty.lap}</div>}
          </div>

          <span
            className={`${s.chip} ${penalty.penalty === "LAP DELETED" ? s.chipDeleted : CHIP[penalty.state]}`}
          >
            {penalty.penalty === "LAP DELETED" ? "DELETED" : penalty.state}
          </span>
        </div>
      ))}
    </div>
  );
}
