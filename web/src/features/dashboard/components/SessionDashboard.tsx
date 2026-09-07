import { useState } from "react";
import { TyreLoader } from "@/components/atoms/TyreLoader";
import { AppHeader } from "@/components/layout/AppHeader/AppHeader";
import { TabBar } from "@/components/layout/TabBar/TabBar";
import { useConnectionStatus, useLiveSession } from "@/features/live/hooks/useLive";
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
              <div className={s.placeholder}>STINTS — IMPL-17</div>
            </div>
          </div>
        </>
      )}

      {view === "notifications" && (
        <div className={s.split}>
          {session.messages.length === 0 && connected
            ? <div className={s.side}><TyreLoader block label="Waiting for race control" /></div>
            : <RaceControlFeed messages={session.messages} />}
          <div className={s.side}>
            <div className={s.placeholder}>PENALTIES — IMPL-24</div>
          </div>
        </div>
      )}

      {view === "telemetry" && (
        <div className={s.placeholder}>TELEMETRY WALL — IMPL-18 TO IMPL-22</div>
      )}
    </>
  );
}
