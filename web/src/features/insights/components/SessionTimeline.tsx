import type { TimelineBand, TrackState } from "@/features/live/model/types";
import s from "./Insights.module.css";

const BAND_CLASS: Record<TrackState, string> = {
  "none": s.bandNone!,
  "yellow": s.bandYellow!,
  "safety-car": s.bandSafetyCar!,
  "vsc": s.bandVsc!,
  "red-flag": s.bandRedFlag!,
  "chequered": s.bandChequered!,
};

const BAND_LABEL: Record<TrackState, string> = {
  "none": "Green",
  "yellow": "Yellow flag",
  "safety-car": "Safety car",
  "vsc": "Virtual safety car",
  "red-flag": "Red flag",
  "chequered": "Chequered",
};

/**
 * The session so far as coloured bands over laps.
 *
 * Built from track-status transitions watched as the stream goes past, because
 * the feed publishes only the status right now and keeps no history. Joining a
 * session halfway means the timeline starts halfway — stated rather than
 * disguised, since a timeline that silently omits the first safety car is worse
 * than one that admits it was not watching.
 */
export function SessionTimeline({ bands, currentLap }: { bands: TimelineBand[]; currentLap: number }) {
  if (bands.length === 0) {
    return (
      <div className={s.panel}>
        <div className={s.title}>Session timeline</div>
        <div className={s.empty}>Nothing recorded yet.</div>
      </div>
    );
  }

  const first = bands[0]!.fromLap;
  const last = Math.max(currentLap, bands.at(-1)!.toLap);
  const span = Math.max(1, last - first);

  return (
    <div className={s.panel}>
      <div className={s.title}>Session timeline · from lap {first}</div>

      <div className={s.timeline} role="img" aria-label="Track status over the session">
        {bands.map((band, i) => (
          <span
            key={`${band.fromLap}-${i}`}
            className={`${s.band} ${BAND_CLASS[band.state]}`}
            style={{ flexGrow: Math.max(0.25, band.toLap - band.fromLap) / span }}
            title={`${BAND_LABEL[band.state]} · laps ${band.fromLap}–${band.toLap}`}
          />
        ))}
      </div>

      <div className={s.timelineScale}>
        <span>LAP {first}</span>
        <span>LAP {last}</span>
      </div>
    </div>
  );
}
