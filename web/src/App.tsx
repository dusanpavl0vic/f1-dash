import { useEffect, useState } from "react";
import { AppFooter } from "./components/layout/AppFooter/AppFooter";
import { AppHeader } from "./components/layout/AppHeader/AppHeader";
import { TabBar } from "./components/layout/TabBar/TabBar";
import { useConnectionStatus, useLiveSession } from "./features/live/hooks/useLive";
import { liveStore } from "./features/live/store/liveStore";
import type { AppView, GapMode } from "./features/live/model/types";
import { RaceControlFeed } from "./features/race-control/components/RaceControlFeed";
import { TimingTower } from "./features/timing/components/TimingTower";
import { TrackMap } from "./features/track-map/components/TrackMap";
import s from "./App.module.css";

const LIVE_URL = import.meta.env["VITE_LIVE_URL"] ?? "ws://localhost:4000/ws";

const STATUS_TEXT: Record<string, { label: string; color: string }> = {
  connecting: { label: "CONNECTING TO THE SESSION FEED", color: "var(--text-faint)" },
  reconnecting: { label: "CONNECTION LOST — RECONNECTING. DATA BELOW IS STALE.", color: "var(--yellow)" },
  closed: { label: "DISCONNECTED", color: "var(--red-bright)" },
};

export function App() {
  const session = useLiveSession();
  const status = useConnectionStatus();

  const [view, setView] = useState<AppView>("timing");
  const [gapMode, setGapMode] = useState<GapMode>("gap");
  const [selected, setSelected] = useState<string | null>(null);

  useEffect(() => {
    liveStore.connect(LIVE_URL);
    return () => liveStore.disconnect();
  }, []);

  const connected = status === "open";
  const banner = connected ? null : STATUS_TEXT[status];

  return (
    <div className={s.app}>
      <AppHeader
        session={session.session}
        weather={session.weather}
        trackState={session.trackState}
        delaySeconds={0}
        connected={connected}
      />

      <TabBar
        view={view}
        messageCount={session.messages.length}
        weather={session.weather}
        onChange={setView}
      />

      {/* Connection state is stated in words, never by colour alone, and it says
          explicitly that the data below is stale — a frozen dashboard that looks
          live is the failure the user cannot detect. */}
      {banner && (
        <div className={s.banner} style={{ color: banner.color }} role="status">
          {banner.label}
        </div>
      )}

      {view === "timing" && (
        <>
          <TimingTower
            timing={session.timing}
            drivers={session.drivers}
            gapMode={gapMode}
            selected={selected}
            onGapModeChange={setGapMode}
            onSelect={setSelected}
          />
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
            <div className={s.side}>
              <div className={s.placeholder}>PACE CHART — IMPL-16</div>
            </div>
            <div className={s.side}>
              <div className={s.placeholder}>STINTS — IMPL-17</div>
            </div>
          </div>
        </>
      )}

      {view === "notifications" && (
        <div className={s.split}>
          <RaceControlFeed messages={session.messages} />
          <div className={s.side}>
            <div className={s.placeholder}>PENALTIES — IMPL-24</div>
          </div>
        </div>
      )}

      {view === "telemetry" && (
        <div className={s.placeholder}>TELEMETRY WALL — IMPL-18 TO IMPL-22</div>
      )}

      <AppFooter />
    </div>
  );
}
