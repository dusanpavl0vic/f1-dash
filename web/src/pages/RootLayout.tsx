import { useEffect, useState } from "react";
import { NavLink, Outlet } from "react-router";
import { AppFooter } from "@/components/layout/AppFooter/AppFooter";
import s from "./RootLayout.module.css";

const API_URL = import.meta.env["VITE_API_URL"] ?? "http://localhost:4000";

interface LiveStatus { live: boolean; meeting: string | null; session: string | null }

/**
 * The shell around every page: primary navigation and the legal footer.
 *
 * The header is deliberately NOT here. A live session and an archived one have
 * different identities, and a shared header would have to blur them.
 */
export function RootLayout() {
  const [live, setLive] = useState<LiveStatus | null>(null);

  useEffect(() => {
    let cancelled = false;

    const check = () => {
      void fetch(`${API_URL}/api/session/live`)
        .then((r) => r.json() as Promise<LiveStatus>)
        .then((data) => { if (!cancelled) setLive(data); })
        .catch(() => undefined);
    };

    check();
    // Once a minute is enough to notice a session starting.
    const timer = window.setInterval(check, 60_000);

    return () => { cancelled = true; window.clearInterval(timer); };
  }, []);

  const className = ({ isActive }: { isActive: boolean }) =>
    `${s.link} ${isActive ? s.linkActive : ""}`;

  return (
    <div className={s.app}>
      <nav className={s.nav}>
        <NavLink to="/live" className={className}>
          {live?.live && <span className={s.liveDot} />}
          Live
        </NavLink>
        <NavLink to="/replay" className={className}>Replay</NavLink>
        <NavLink to="/schedule" className={className}>Schedule</NavLink>
        <NavLink to="/standings" className={className}>Standings</NavLink>
        <div className={s.spacer} />
        <div className={s.brandStrip}>
          {live?.live
            ? `${live.meeting} · ${live.session} running now`
            : live?.meeting
              ? `Last session: ${live.meeting} · ${live.session}`
              : " "}
        </div>
      </nav>

      <Outlet />

      <AppFooter />
    </div>
  );
}
