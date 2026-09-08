import type { Driver, TimingRow, TyreCompound } from "@/features/live/model/types";
import s from "./Insights.module.css";

const TYRE_LABEL: Record<TyreCompound, string> = {
  S: "SOFT", M: "MEDIUM", H: "HARD", I: "INTER", W: "WET",
};

const TYRE_TOKEN: Record<TyreCompound, string> = {
  S: "soft", M: "medium", H: "hard", I: "inter", W: "wet",
};

interface FocusCardsProps {
  timing: TimingRow[];
  drivers: Record<string, Driver>;
  selected: string | null;
  onSelect(tla: string | null): void;
}

/**
 * The three drivers worth watching, larger than a timing row.
 *
 * Which three is not a preference: it is the leader, plus whoever the user
 * selected, plus the closest battle. A timing tower answers "what is the order";
 * these answer "what is about to happen".
 */
export function FocusCards({ timing, drivers, selected, onSelect }: FocusCardsProps) {
  const focus = pickFocus(timing, selected);

  if (focus.length === 0) {
    return (
      <div className={s.panel}>
        <div className={s.title}>Focus</div>
        <div className={s.empty}>Waiting for timing data.</div>
      </div>
    );
  }

  return (
    <div className={s.cards}>
      {focus.map((row) => {
        const driver = drivers[row.tla];
        const color = driver?.color ?? "var(--border-strong)";

        return (
          <button
            key={row.tla}
            type="button"
            className={`${s.card} ${row.tla === selected ? s.cardActive : ""}`}
            style={{ ["--team" as string]: color }}
            onClick={() => onSelect(row.tla === selected ? null : row.tla)}
          >
            {/* The number is the graphic element, not decoration around one. */}
            <span className={s.ghost} aria-hidden="true">{driver?.number ?? ""}</span>

            <div className={s.cardTop}>
              <span className={s.pos}>P{row.position}</span>
              <span className={s.tla}>{row.tla}</span>
            </div>

            <div className={s.cardGrid}>
              <span className={s.label}>Last lap</span>
              <span className={s.label}>Gap</span>
              <span className={s.value}>{row.lastLap || "—"}</span>
              <span className={s.value}>{row.gap || "LEADER"}</span>

              <span className={s.label}>Tyre</span>
              <span className={s.label}>Stops</span>
              <span className={s.value}>
                <span
                  className={s.tyreChip}
                  style={{
                    background: `var(--tyre-${TYRE_TOKEN[row.tyre]})`,
                    color: `var(--tyre-${TYRE_TOKEN[row.tyre]}-fg)`,
                  }}
                  title={TYRE_LABEL[row.tyre]}
                >
                  {row.tyre}
                </span>
                <span className={s.value}> {row.tyreAge}L</span>
              </span>
              <span className={s.value}>{row.stops}</span>
            </div>
          </button>
        );
      })}
    </div>
  );
}

/**
 * Leader, selection, and the tightest gap — deduplicated, padded from the front
 * of the field so there are always three cards rather than a layout that
 * reflows every time the closest battle changes.
 */
function pickFocus(timing: TimingRow[], selected: string | null): TimingRow[] {
  if (timing.length === 0) return [];

  const picked: TimingRow[] = [];
  const add = (row: TimingRow | undefined) => {
    if (row && !picked.some((p) => p.tla === row.tla)) picked.push(row);
  };

  add(timing[0]);
  add(timing.find((r) => r.tla === selected));
  add(closestBattle(timing));

  for (const row of timing) {
    if (picked.length >= 3) break;
    add(row);
  }

  return picked.slice(0, 3);
}

/** The car with the smallest interval to the one ahead, ignoring lapped cars. */
function closestBattle(timing: TimingRow[]): TimingRow | undefined {
  let best: TimingRow | undefined;
  let bestGap = Number.POSITIVE_INFINITY;

  for (const row of timing) {
    // "1 L" and "" are not gaps; parsing them as numbers would make a lapped
    // car look like the closest fight on track.
    const seconds = Number(row.interval.replace("+", ""));
    if (!Number.isFinite(seconds) || seconds <= 0) continue;

    if (seconds < bestGap) {
      bestGap = seconds;
      best = row;
    }
  }
  return best;
}
