import { memo } from "react";
import { PACE_COLOR } from "@/features/live/model/constants";
import type { PaceClass } from "@/features/live/model/types";
import s from "./TimingTower.module.css";

/**
 * The mini-sector bar — the densest element in the application.
 *
 * Segment count is circuit-dependent (Monza delivers 19, other circuits 15-25),
 * so the bar flexes rather than assuming a fixed count.
 *
 * Memoised on the joined pace string: this re-renders up to ten times a second
 * across twenty rows otherwise.
 */
export const MiniSectors = memo(function MiniSectors({ segments }: { segments: PaceClass[] }) {
  return (
    <div className={s.segments} aria-hidden="true">
      {segments.map((pace, i) => (
        <div key={i} className={s.segment} style={{ background: PACE_COLOR[pace] }} />
      ))}
    </div>
  );
}, (prev, next) =>
  prev.segments.length === next.segments.length &&
  prev.segments.every((p, i) => p === next.segments[i]),
);
