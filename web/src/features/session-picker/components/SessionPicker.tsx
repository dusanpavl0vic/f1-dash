import { useCallback, useEffect, useMemo, useState } from "react";
import s from "./SessionPicker.module.css";

const API_URL = import.meta.env["VITE_API_URL"] ?? "http://localhost:4000";

interface CatalogSession {
  year: number;
  meeting: string;
  session: string;
  type: string;
  downloaded: boolean;
}

interface LiveStatus {
  live: boolean;
  meeting: string | null;
  session: string | null;
  startDate: string | null;
}

export interface CurrentSession {
  mode: number;              // 0 none, 1 replay, 2 live
  description: string;
  year: number | null;
  meeting: string | null;
  session: string | null;
  error: string | null;
}

interface SessionPickerProps {
  current: CurrentSession | null;
  onSwitched(): void;
}

/**
 * Chooses what the dashboard plays: any archived session back to 2018, or the
 * live feed when a session is running.
 *
 * Sessions that are not on disk need a multi-megabyte download before playback,
 * which can take a minute. That is stated in the UI rather than presented as an
 * unexplained wait.
 */
export function SessionPicker({ current, onSwitched }: SessionPickerProps) {
  const [open, setOpen] = useState(false);
  const [seasons, setSeasons] = useState<number[]>([]);
  const [year, setYear] = useState<number | null>(null);
  // Stored WITH the year it belongs to, so "loading" is derived rather than
  // set synchronously in the effect — clearing state in an effect body causes a
  // cascading render, which the React lint rule correctly refuses.
  const [loaded, setLoaded] = useState<{ year: number; list: CatalogSession[] } | null>(null);
  const [live, setLive] = useState<LiveStatus | null>(null);
  const [status, setStatus] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    if (!open) return;

    void fetch(`${API_URL}/api/seasons`)
      .then((r) => r.json() as Promise<number[]>)
      .then((list) => {
        setSeasons(list);
        setYear((y) => y ?? current?.year ?? list[0] ?? null);
      })
      .catch(() => setStatus("Could not reach the server."));

    void fetch(`${API_URL}/api/session/live`)
      .then((r) => r.json() as Promise<LiveStatus>)
      .then(setLive)
      .catch(() => undefined);
  }, [open, current?.year]);

  useEffect(() => {
    if (!open || year === null) return;

    let cancelled = false;
    void fetch(`${API_URL}/api/seasons/${year}/sessions`)
      .then((r) => r.json() as Promise<CatalogSession[]>)
      .then((list) => { if (!cancelled) setLoaded({ year, list }); })
      .catch(() => { if (!cancelled) setStatus(`Could not list ${year}.`); });

    return () => { cancelled = true; };
  }, [open, year]);

  const switchTo = useCallback(async (body: Record<string, unknown>, label: string) => {
    setBusy(true);
    setStatus(`Loading ${label}…`);
    try {
      const response = await fetch(`${API_URL}/api/session`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(body),
      });
      const result = (await response.json()) as CurrentSession;

      if (!response.ok || result.error) {
        setStatus(result.error ?? "Could not switch session.");
        return;
      }

      setStatus(null);
      setOpen(false);
      onSwitched();
    } catch {
      setStatus("Could not reach the server.");
    } finally {
      setBusy(false);
    }
  }, [onSwitched]);

  const grouped = useMemo(() => {
    // Derived inside the memo: the loaded list belongs to a specific year, and
    // showing last year's sessions under this year's heading would be worse
    // than showing a moment of "loading".
    const sessions = loaded?.year === year ? loaded.list : [];

    const map = new Map<string, CatalogSession[]>();
    for (const session of sessions) {
      const list = map.get(session.meeting);
      if (list) list.push(session);
      else map.set(session.meeting, [session]);
    }
    return [...map.entries()];
  }, [loaded, year]);

  const label =
    current?.mode === 2 ? "LIVE FEED"
    : current?.meeting ? `${current.meeting} · ${current.session}`
    : "CHOOSE SESSION";

  return (
    <div className={s.wrap}>
      <button
        type="button"
        className={s.trigger}
        aria-expanded={open}
        onClick={() => setOpen((v) => !v)}
      >
        {label.length > 30 ? label.slice(0, 30) + "…" : label}
        <span className={s.caret}>▾</span>
      </button>

      {open && (
        <div className={s.panel} role="dialog" aria-label="Choose a session">
          <div className={s.section}>
            <div className={s.sectionTitle}>Live</div>
            <div className={s.liveRow}>
              <div className={s.liveText}>
                {live?.live
                  ? `${live.meeting} · ${live.session} is running now`
                  : live?.meeting
                    ? `No session running. Last: ${live.meeting} · ${live.session}`
                    : "Checking…"}
              </div>
              <button
                type="button"
                className={s.goLive}
                disabled={busy || !live?.live}
                onClick={() => void switchTo({ mode: "live" }, "the live feed")}
              >
                GO LIVE
              </button>
            </div>
          </div>

          <div className={s.section}>
            <div className={s.sectionTitle}>Season</div>
            <div className={s.years}>
              {seasons.map((y) => (
                <button
                  key={y}
                  type="button"
                  className={`${s.year} ${y === year ? s.yearActive : ""}`}
                  onClick={() => setYear(y)}
                >
                  {y}
                </button>
              ))}
            </div>
          </div>

          <div className={s.list}>
            {grouped.length === 0 ? (
              <div className={s.empty}>
                {year === null ? "Pick a season" : `Loading ${year}…`}
              </div>
            ) : (
              grouped.map(([meeting, list]) => (
                <div key={meeting}>
                  <div className={s.meeting}>{meeting}</div>
                  <div className={s.sessions}>
                    {list.map((session) => {
                      const active =
                        current?.year === session.year &&
                        current?.meeting === session.meeting &&
                        current?.session === session.session;

                      return (
                        <button
                          key={`${session.meeting}-${session.session}`}
                          type="button"
                          className={`${s.session} ${active ? s.sessionActive : ""}`}
                          disabled={busy}
                          title={session.downloaded
                            ? "On disk — starts immediately"
                            : "Not downloaded yet; this will fetch it first"}
                          onClick={() => void switchTo({
                            mode: "replay",
                            year: session.year,
                            meeting: session.meeting,
                            session: session.session,
                            speed: 1,
                            loop: true,
                          }, `${session.meeting} ${session.session}`)}
                        >
                          <span className={`${s.dot} ${session.downloaded ? "" : s.dotRemote}`} />
                          {session.session}
                        </button>
                      );
                    })}
                  </div>
                </div>
              ))
            )}
          </div>

          {status && (
            <div className={`${s.status} ${status.startsWith("Could") ? s.statusError : ""}`}>
              {status}
            </div>
          )}
        </div>
      )}
    </div>
  );
}
