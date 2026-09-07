import { useCallback, useEffect, useMemo, useState } from "react";
import { Link, useNavigate, useParams } from "react-router";
import { TyreLoader } from "@/components/atoms/TyreLoader";
import { SessionDashboard } from "@/features/dashboard/components/SessionDashboard";
import { TransportBar } from "@/features/replay/components/TransportBar";
import { liveStore } from "@/features/live/store/liveStore";
import s from "./Pages.module.css";

const API_URL = import.meta.env["VITE_API_URL"] ?? "http://localhost:4000";
const LIVE_URL = import.meta.env["VITE_LIVE_URL"] ?? "ws://localhost:4000/ws";

interface CatalogSession {
  year: number; meeting: string; meetingSlug: string;
  session: string; sessionSlug: string; type: string; downloaded: boolean;
}

/**
 * Archived sessions, and only archived sessions.
 *
 * Nothing plays on load: /replay shows the picker, and a session starts only
 * when one is clicked. The URL carries the choice, so a replay is shareable and
 * a reload returns to the same race rather than to whatever the backend
 * happened to be playing.
 */
export function ReplayPage() {
  const { year: yearParam, meeting, session } = useParams();
  const navigate = useNavigate();

  const [seasons, setSeasons] = useState<number[]>([]);
  const [year, setYear] = useState<number | null>(null);
  const [loaded, setLoaded] = useState<{ year: number; list: CatalogSession[] } | null>(null);
  const [error, setError] = useState<string | null>(null);

  const playing = Boolean(yearParam && meeting && session);
  const sessionKey = `${yearParam}/${meeting}/${session}`;

  // Which session the backend has confirmed. "Starting" is DERIVED from the
  // difference rather than set at the top of the effect, which would cause a
  // cascading render on every navigation.
  const [ready, setReady] = useState<string | null>(null);
  const starting = playing && ready !== sessionKey && error === null;

  useEffect(() => {
    if (!playing) return;
    liveStore.connect(LIVE_URL);
    return () => liveStore.disconnect();
  }, [playing]);

  useEffect(() => {
    void fetch(`${API_URL}/api/seasons`)
      .then((r) => r.json() as Promise<number[]>)
      .then((list) => {
        setSeasons(list);
        setYear((current) => current ?? (yearParam ? Number(yearParam) : list[0] ?? null));
      })
      .catch(() => setError("Could not reach the server."));
  }, [yearParam]);

  useEffect(() => {
    if (year === null) return;

    let cancelled = false;
    void fetch(`${API_URL}/api/seasons/${year}/sessions`)
      .then((r) => r.json() as Promise<CatalogSession[]>)
      .then((list) => { if (!cancelled) setLoaded({ year, list }); })
      .catch(() => { if (!cancelled) setError(`Could not list ${year}.`); });

    return () => { cancelled = true; };
  }, [year]);

  // Starting the replay is driven by the URL, so a shared link works and a
  // reload resumes the same session.
  useEffect(() => {
    if (!playing) return;

    let cancelled = false;

    void fetch(`${API_URL}/api/session`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        mode: "replay",
        year: Number(yearParam),
        meeting,
        session,
        speed: 1,
        loop: true,
      }),
    })
      .then((r) => r.json())
      .then((result: { error: string | null }) => {
        if (cancelled) return;
        if (result.error) setError(result.error);
      })
      .catch(() => { if (!cancelled) setError("Could not start the replay."); })
      .finally(() => { if (!cancelled) setReady(sessionKey); });

    return () => { cancelled = true; };
  }, [playing, sessionKey, yearParam, meeting, session]);

  const start = useCallback((chosen: CatalogSession) => {
    navigate(`/replay/${chosen.year}/${encodeURIComponent(chosen.meetingSlug)}/${encodeURIComponent(chosen.sessionSlug)}`);
  }, [navigate]);

  const grouped = useMemo(() => {
    const list = loaded?.year === year ? loaded.list : [];
    const map = new Map<string, CatalogSession[]>();
    for (const item of list) {
      const existing = map.get(item.meeting);
      if (existing) existing.push(item);
      else map.set(item.meeting, [item]);
    }
    return [...map.entries()];
  }, [loaded, year]);

  if (playing) {
    if (starting) {
      return (
        <TyreLoader
          block size="lg"
          label={`Loading ${meeting} ${session}`}
          detail="A session that is not already on disk is downloaded first — this can take a minute."
        />
      );
    }

    if (error) {
      return (
        <div className={s.empty}>
          <div className={s.emptyTitle}>Could not load that session</div>
          <p className={s.emptyBody}>{error}</p>
          <Link to="/replay" className={`${s.button} ${s.buttonPrimary}`}>BACK TO THE PICKER</Link>
        </div>
      );
    }

    return (
      <>
        <SessionDashboard
          mode="replay"
          headerControl={<Link to="/replay" className={s.button}>CHANGE SESSION</Link>}
          emptyState={<TyreLoader block size="lg" label="Rebuilding session state" />}
        />
        <TransportBar />
      </>
    );
  }

  return (
    <>
      <div className={s.pickerHeader}>
        <div className={s.marker} />
        <h1 className={s.pickerTitle}>Replay a session</h1>
        <div className={s.rule} />
        <div className={s.meta}>
          {grouped.length > 0 ? `${grouped.length} meetings` : ""}
        </div>
      </div>

      <div className={s.legendBar}>
        <span className={s.legendItem}>
          <span className={s.dot} /> On disk — starts immediately
        </span>
        <span className={s.legendItem}>
          <span className={`${s.dot} ${s.dotRemote}`} /> Not downloaded — fetched first, about a minute
        </span>
      </div>

      <div className={s.years}>
        {seasons.map((y) => (
          <button key={y} type="button"
                  className={`${s.year} ${y === year ? s.yearActive : ""}`}
                  onClick={() => setYear(y)}>
            {y}
          </button>
        ))}
      </div>

      {error && <div className={s.empty}><p className={s.emptyBody}>{error}</p></div>}

      {grouped.length === 0 && !error ? (
        <TyreLoader block label={year === null ? "Loading seasons" : `Loading ${year}`} />
      ) : (
        <div className={s.meetings}>
          {grouped.map(([name, list]) => (
            <div key={name} className={s.meeting}>
              <div className={s.meetingName}>{name}</div>
              <div className={s.sessions}>
                {list.map((item) => (
                  <button
                    key={`${item.meetingSlug}-${item.sessionSlug}`}
                    type="button"
                    className={s.sessionButton}
                    title={item.downloaded
                      ? "On disk — starts immediately"
                      : "Not downloaded yet; this will fetch it first"}
                    onClick={() => start(item)}
                  >
                    <span className={`${s.dot} ${item.downloaded ? "" : s.dotRemote}`} />
                    {item.session}
                  </button>
                ))}
              </div>
            </div>
          ))}
        </div>
      )}
    </>
  );
}
