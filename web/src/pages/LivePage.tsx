import { useEffect, useState } from "react";
import { Link } from "react-router";
import { TyreLoader } from "@/components/atoms/TyreLoader";
import { SessionDashboard } from "@/features/dashboard/components/SessionDashboard";
import { liveStore } from "@/features/live/store/liveStore";
import s from "./Pages.module.css";

const API_URL = import.meta.env["VITE_API_URL"] ?? "http://localhost:4000";
const LIVE_URL = import.meta.env["VITE_LIVE_URL"] ?? "ws://localhost:4000/ws";

interface LiveStatus { live: boolean; meeting: string | null; session: string | null; startDate: string | null }

/**
 * The live feed, and only the live feed.
 *
 * It never falls back to an archived session. Out of season that means this
 * page mostly shows "no session running" — which is the honest answer, and
 * quietly replaying an old race instead would be the single most misleading
 * thing this application could do.
 */
export function LivePage() {
  const [status, setStatus] = useState<LiveStatus | null>(null);
  const [switching, setSwitching] = useState(false);

  useEffect(() => {
    liveStore.connect(LIVE_URL);
    return () => liveStore.disconnect();
  }, []);

  useEffect(() => {
    let cancelled = false;

    void fetch(`${API_URL}/api/session/live`)
      .then((r) => r.json() as Promise<LiveStatus>)
      .then(async (data) => {
        if (cancelled) return;
        setStatus(data);

        if (!data.live) return;

        // Only bind the backend to the live feed when a session is genuinely
        // running. Requesting live out of session would tear down whatever the
        // replay page had loaded for no benefit.
        setSwitching(true);
        try {
          await fetch(`${API_URL}/api/session`, {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({ mode: "live" }),
          });
        } finally {
          if (!cancelled) setSwitching(false);
        }
      })
      .catch(() => undefined);

    return () => { cancelled = true; };
  }, []);

  if (status === null) {
    return <TyreLoader block size="lg" label="Checking for a live session" />;
  }

  if (!status.live) {
    return (
      <div className={s.empty}>
        <div className={s.emptyTitle}>No session running</div>
        <p className={s.emptyBody}>
          Formula 1 runs on roughly 24 weekends a year. This page connects to the live timing
          feed and shows nothing else — it will not quietly play an old race instead.
          {status.meeting && (
            <> The last session was <strong>{status.meeting} · {status.session}</strong>.</>
          )}
        </p>
        <div className={s.emptyActions}>
          <Link to="/schedule" className={`${s.button} ${s.buttonPrimary}`}>SEE THE SCHEDULE</Link>
          <Link to="/replay" className={s.button}>REPLAY A PAST SESSION</Link>
        </div>
      </div>
    );
  }

  if (switching) {
    return <TyreLoader block size="lg" label="Connecting to the live feed" detail={`${status.meeting} · ${status.session}`} />;
  }

  return (
    <SessionDashboard
      mode="live"
      emptyState={<TyreLoader block size="lg" label="Waiting for the first frame" />}
    />
  );
}
