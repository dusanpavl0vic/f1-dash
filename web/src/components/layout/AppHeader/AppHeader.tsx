import { memo } from "react";
import { TRACK_STATE } from "@/features/live/model/constants";
import type { SessionInfo, TrackState, Weather } from "@/features/live/model/types";
import s from "./AppHeader.module.css";

/** Delay above this reads as a distinct, more insistent state. */
const HIGH_DELAY_SECONDS = 60;

export interface AppHeaderProps {
  session: SessionInfo;
  weather: Weather;
  trackState: TrackState;
  /** Client-side broadcast delay in seconds, 0–120. */
  delaySeconds: number;
  /** False once the socket drops — the live dot goes grey and data is stale. */
  connected: boolean;
}

export const AppHeader = memo(function AppHeader({
  session, weather, trackState, delaySeconds, connected,
}: AppHeaderProps) {
  const status = TRACK_STATE[trackState];
  const highDelay = delaySeconds > HIGH_DELAY_SECONDS;

  return (
    <header className={s.header}>
      <div className={s.accentRule} />
      <div className={s.inner}>
        <div className={s.left}>
          <div>
            <div className={s.brandName}>Apex</div>
            <div className={s.brandSub}>Live timing</div>
          </div>

          <div className={`${s.divider} ${s.dividerTall}`} />

          <div className={s.session}>
            <div className={s.liveGroup}>
              <div className={`${s.liveDot} ${connected ? "" : s.liveDotStale}`} />
              <div className={s.liveLabel}>{connected ? "LIVE" : "STALE"}</div>
            </div>
            <div className={`${s.divider} ${s.dividerShort}`} />
            <div>
              <div className={s.meetingName}>{session.meetingName}</div>
              <div className={s.meetingSub}>
                {session.circuitName} · {session.type}
              </div>
            </div>
          </div>
        </div>

        {/* Lap counter is hidden outside races — the feed sends no lap count for
            practice or qualifying, and "0/0" reads as broken. */}
        {session.totalLaps > 0 && (
          <div className={s.lapBlock}>
            <div className={s.lapValue}>
              {session.currentLap}
              <span className={s.lapTotal}>/{session.totalLaps}</span>
            </div>
            <div className={s.lapLabel}>Lap</div>
          </div>
        )}

        <div className={s.right}>
          <div className={s.temps}>
            <span className={s.tempsKey}>TRACK </span>
            {weather.trackTemp.toFixed(1)}C
            <span className={s.tempsKey}>{"  AIR "}</span>
            {weather.airTemp.toFixed(1)}C
          </div>

          {delaySeconds > 0 && (
            <div
              className={`${s.delayBadge} ${highDelay ? s.delayBadgeHigh : ""}`}
              title="Broadcast delay — the dashboard is intentionally held behind the live feed"
            >
              <div className={s.delayDot} />
              <div className={s.delayLabel}>DELAY {delaySeconds}s</div>
            </div>
          )}

          {/* Status carries a text label, never colour alone. */}
          <div
            className={s.statusChip}
            style={{ borderColor: status.bd, background: status.bg }}
          >
            <div className={s.statusMark} style={{ background: status.fg }} />
            <div className={s.statusLabel} style={{ color: status.fg }}>
              {status.label}
            </div>
          </div>
        </div>
      </div>
    </header>
  );
});
