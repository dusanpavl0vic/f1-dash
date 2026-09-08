import { useMemo, useState } from "react";
import { TyreLoader } from "@/components/atoms/TyreLoader";
import { AppHeader } from "@/components/layout/AppHeader/AppHeader";
import { TabBar } from "@/components/layout/TabBar/TabBar";
import { DelayControl } from "@/features/delay/components/DelayControl";
import { FocusCards } from "@/features/insights/components/FocusCards";
import { PaceChart } from "@/features/insights/components/PaceChart";
import { PenaltyList } from "@/features/insights/components/PenaltyList";
import { SessionTimeline } from "@/features/insights/components/SessionTimeline";
import { GapBars, StintBars } from "@/features/insights/components/StintBars";
import { useConnectionStatus, useLiveSession, useSessionHistory } from "@/features/live/hooks/useLive";
import { ToastStack } from "@/features/toasts/components/ToastStack";
import { useToasts } from "@/features/toasts/lib/useToasts";
import { useIsCompact } from "@/hooks/useBreakpoint";
import type { AppView, GapMode } from "@/features/live/model/types";
import { RaceControlFeed } from "@/features/race-control/components/RaceControlFeed";
import { TimingTower } from "@/features/timing/components/TimingTower";
import { TrackMap } from "@/features/track-map/components/TrackMap";
import { WeatherPanel } from "@/features/weather/components/WeatherPanel";
import s from "./SessionDashboard.module.css";

const STATUS_TEXT: Record<string, { label: string; color: string }> = {
  connecting: { label: "CONNECTING TO THE SESSION FEED", color: "var(--text-faint)" },
  reconnecting: { label: "CONNECTION LOST — RECONNECTING. DATA BELOW IS STALE.", color: "var(--yellow)" },
  closed: { label: "DISCONNECTED", color: "var(--red-bright)" },
};

export interface SessionDashboardProps {
  /** Shown in the header so a replay is never mistakable for live. */
  mode: "replay" | "live" | "none";
  /** Rendered in the header's right-hand slot — the picker, or nothing. */
  headerControl?: React.ReactNode;
  /** What to show before any data has arrived. */
  emptyState?: React.ReactNode;
}

export function SessionDashboard({ mode, headerControl, emptyState }: SessionDashboardProps) {
  const session = useLiveSession();
  const status = useConnectionStatus();

  const [view, setView] = useState<AppView>("timing");
  const [gapMode, setGapMode] = useState<GapMode>("gap");
  const [selected, setSelected] = useState<string | null>(null);

  const compact = useIsCompact();
  const { toasts, dismiss } = useToasts(session);

  // The pace chart follows the focus cards rather than the whole field: twenty
  // overlapping lines is not a chart, it is a texture.
  const focus = useMemo(() => {
    const top = session.timing.slice(0, 3).map((r) => r.tla);
    return selected && !top.includes(selected) ? [selected, ...top.slice(0, 2)] : top;
  }, [session.timing, selected]);

  const { pace, paceLaps, timeline } = useSessionHistory(focus);

  const connected = status === "open";
  const banner = connected ? null : STATUS_TEXT[status];
  const hasData = session.timing.length > 0;

  if (!hasData && emptyState) {
    return <>{emptyState}</>;
  }

  return (
    <>
      <AppHeader
        session={session.session}
        mode={mode}
        weather={session.weather}
        trackState={session.trackState}
        delaySeconds={0}
        connected={connected}
      >
        {headerControl}
      </AppHeader>

      <TabBar
        view={view}
        messageCount={session.messages.length}
        weather={session.weather}
        onChange={setView}
      />

      {/* Connection state is stated in words and says explicitly that the data
          below is stale — a frozen dashboard that still looks live is the
          failure a user cannot detect. */}
      {banner && (
        <div className={s.banner} style={{ color: banner.color }} role="status">
          {banner.label}
        </div>
      )}

      {view === "timing" && (
        <>
          {hasData ? (
            <TimingTower
              timing={session.timing}
              drivers={session.drivers}
              overtakeAid={session.overtakeAid}
              gapMode={gapMode}
              selected={selected}
              onGapModeChange={setGapMode}
              onSelect={setSelected}
            />
          ) : (
            <TyreLoader block size="lg" label="Waiting for timing data" />
          )}

          <div className={s.lower}>
            <TrackMap
              circuitKey={session.session.circuitKey}
              year={session.session.year}
              circuitName={session.session.circuitName}
              positions={session.positions}
              drivers={session.drivers}
              timing={session.timing}
              trackState={session.trackState}
              selected={selected}
            />
            <WeatherPanel weather={session.weather} />
            <div className={s.side}>
              <StintBars stints={session.stints} />
            </div>
          </div>

          <div className={s.insights}>
            <FocusCards
              timing={session.timing}
              drivers={session.drivers}
              selected={selected}
              onSelect={setSelected}
            />
            {/* The pace chart is the widest element here and the first thing
                worth dropping on a phone, where ten laps of three lines is
                thinner than the axis labels. */}
            {!compact && <PaceChart lines={pace} laps={paceLaps} />}
            <SessionTimeline bands={timeline} currentLap={session.session.currentLap} />
            <GapBars timing={session.timing} drivers={session.drivers} />
          </div>
        </>
      )}

      {view === "notifications" && (
        <div className={s.split}>
          {session.messages.length === 0 && connected
            ? <div className={s.side}><TyreLoader block label="Waiting for race control" /></div>
            : <RaceControlFeed messages={session.messages} />}
          <div className={s.side}>
            <PenaltyList penalties={session.penalties} drivers={session.drivers} />
          </div>
        </div>
      )}

      {view === "telemetry" && (
        <div className={s.placeholder}>TELEMETRY WALL — IMPL-18 TO IMPL-22</div>
      )}

      {/* Only live sessions can be behind a broadcast; a replay is already
          wherever the transport bar put it. */}
      {mode === "live" && (
        <div className={s.side}>
          <DelayControl />
        </div>
      )}

      <ToastStack toasts={toasts} onDismiss={dismiss} />
    </>
  );
}
