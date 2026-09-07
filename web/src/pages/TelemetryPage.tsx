import { useCallback, useEffect, useMemo, useState } from "react";
import { TyreLoader } from "@/components/atoms/TyreLoader";
import type { SessionAnalysis, TelemetryLap } from "@/features/analysis/model/types";
import { bestLap, driverColor, formatTime } from "@/features/analysis/model/types";
import { ChannelChart } from "@/features/telemetry/components/ChannelChart";
import { DeltaChart } from "@/features/telemetry/components/DeltaChart";
import { RacingLine } from "@/features/telemetry/components/RacingLine";
import s from "@/features/telemetry/components/Telemetry.module.css";
import p from "./Pages.module.css";

const API_URL = import.meta.env["VITE_API_URL"] ?? "http://localhost:4000";

interface CatalogSession {
  year: number; meeting: string; meetingSlug: string;
  session: string; sessionSlug: string; downloaded: boolean;
}

/**
 * Telemetry for any archived session, independent of what the dashboard is
 * playing.
 *
 * The cascade is season → session → driver → lap. A session is precomputed on
 * demand — reading the stream at full speed rather than replaying it — so
 * picking a race here does not disturb /live or /replay.
 */
export function TelemetryPage() {
  const [seasons, setSeasons] = useState<number[]>([]);
  const [year, setYear] = useState<number | null>(null);
  const [sessions, setSessions] = useState<CatalogSession[]>([]);
  const [chosen, setChosen] = useState<CatalogSession | null>(null);

  const [analysis, setAnalysis] = useState<SessionAnalysis | null>(null);
  const [status, setStatus] = useState<string | null>(null);

  const [tlaA, setTlaA] = useState<string | null>(null);
  const [tlaB, setTlaB] = useState<string | null>(null);
  const [lapNumber, setLapNumber] = useState<number | null>(null);

  const [traceA, setTraceA] = useState<TelemetryLap | null>(null);
  const [traceB, setTraceB] = useState<TelemetryLap | null>(null);

  useEffect(() => {
    void fetch(`${API_URL}/api/seasons`)
      .then((r) => r.json() as Promise<number[]>)
      .then((list) => {
        setSeasons(list);
        // Telemetry only exists from 2026, so that is where the picker opens.
        setYear((current) => current ?? list.find((y) => y >= 2026) ?? list[0] ?? null);
      })
      .catch(() => setStatus("Could not reach the server."));
  }, []);

  useEffect(() => {
    if (year === null) return;

    let cancelled = false;
    void fetch(`${API_URL}/api/seasons/${year}/sessions`)
      .then((r) => r.json() as Promise<CatalogSession[]>)
      .then((list) => { if (!cancelled) setSessions(list); })
      .catch(() => undefined);

    return () => { cancelled = true; };
  }, [year]);

  const load = useCallback(async (session: CatalogSession) => {
    setChosen(session);
    setAnalysis(null);
    setTraceA(null);
    setTraceB(null);
    setStatus(session.downloaded
      ? "Reading the session…"
      : "Downloading the session first — this takes a minute…");

    try {
      const precompute = await fetch(`${API_URL}/api/analysis/precompute`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ year: session.year, meeting: session.meeting, session: session.session }),
      }).then((r) => r.json() as Promise<{ ok: boolean; error: string | null }>);

      if (!precompute.ok) {
        setStatus(precompute.error ?? "Could not read that session.");
        return;
      }

      const data = await fetch(
        `${API_URL}/api/analysis/${session.year}/${session.meetingSlug}/${session.sessionSlug}`,
      ).then((r) => (r.ok ? (r.json() as Promise<SessionAnalysis>) : null));

      if (!data) { setStatus("Analysis is missing after precompute."); return; }

      setAnalysis(data);
      setStatus(null);
      setTlaA(data.drivers[0]?.tla ?? null);
      setTlaB(data.drivers[1]?.tla ?? null);
      setLapNumber(null);
    } catch {
      setStatus("Could not reach the server.");
    }
  }, []);

  const driverA = analysis?.drivers.find((d) => d.tla === tlaA);
  const driverB = analysis?.drivers.find((d) => d.tla === tlaB);

  // Default to each driver's own fastest lap: comparing a flying lap against an
  // out lap tells you nothing.
  const fastestLap = useCallback((tla: string | null) => {
    const driver = analysis?.drivers.find((d) => d.tla === tla);
    if (!driver) return null;

    const best = bestLap(driver);
    return driver.laps.find((l) => l.timeSeconds === best)?.lap ?? null;
  }, [analysis]);

  const lapA = lapNumber ?? fastestLap(tlaA);
  const lapB = fastestLap(tlaB);

  const fetchTrace = useCallback(async (racingNumber: string | undefined, lap: number | null) => {
    if (!chosen || !racingNumber || lap === null) return null;

    const url = `${API_URL}/api/analysis/${chosen.year}/${chosen.meetingSlug}/${chosen.sessionSlug}` +
      `/telemetry/${racingNumber}/${lap}`;

    return fetch(url).then((r) => (r.ok ? (r.json() as Promise<TelemetryLap>) : null)).catch(() => null);
  }, [chosen]);

  useEffect(() => {
    let cancelled = false;
    void fetchTrace(driverA?.racingNumber, lapA).then((t) => { if (!cancelled) setTraceA(t); });
    return () => { cancelled = true; };
  }, [fetchTrace, driverA?.racingNumber, lapA]);

  useEffect(() => {
    let cancelled = false;
    void fetchTrace(driverB?.racingNumber, lapB).then((t) => { if (!cancelled) setTraceB(t); });
    return () => { cancelled = true; };
  }, [fetchTrace, driverB?.racingNumber, lapB]);

  const meetings = useMemo(() => [...new Set(sessions.map((x) => x.meeting))], [sessions]);

  const colourA = driverA ? driverColor(driverA) : "var(--teal)";
  const colourB = driverB ? driverColor(driverB) : "var(--orange)";
  const traces = [
    traceA && driverA ? { lap: traceA, colour: colourA, tla: driverA.tla } : null,
    traceB && driverB ? { lap: traceB, colour: colourB, tla: driverB.tla } : null,
  ].filter((t): t is NonNullable<typeof t> => t !== null);

  return (
    <>
      <div className={s.panel}>
        <div className={s.header}>
          <div className={s.marker} />
          <h1 className={s.title}>Telemetry</h1>
          <div className={s.rule} />
          <div className={s.meta}>Recorded for 2026 sessions onward</div>
        </div>

        <div className={s.selectors}>
          <label className={s.field}>
            <span className={s.fieldLabel}>Season</span>
            <select className={s.select} value={year ?? ""}
                    onChange={(e) => { setYear(Number(e.target.value)); setChosen(null); setAnalysis(null); }}>
              {seasons.map((y) => <option key={y} value={y}>{y}</option>)}
            </select>
          </label>

          <label className={s.field}>
            <span className={s.fieldLabel}>Session</span>
            <select
              className={s.select}
              value={chosen ? `${chosen.meetingSlug}/${chosen.sessionSlug}` : ""}
              onChange={(e) => {
                const match = sessions.find((x) => `${x.meetingSlug}/${x.sessionSlug}` === e.target.value);
                if (match) void load(match);
              }}
            >
              <option value="">Select a session…</option>
              {meetings.map((meeting) => (
                <optgroup key={meeting} label={meeting}>
                  {sessions.filter((x) => x.meeting === meeting).map((x) => (
                    <option key={`${x.meetingSlug}/${x.sessionSlug}`}
                            value={`${x.meetingSlug}/${x.sessionSlug}`}>
                      {x.session}{x.downloaded ? "" : " — downloads first"}
                    </option>
                  ))}
                </optgroup>
              ))}
            </select>
          </label>

          <label className={s.field}>
            <span className={s.fieldLabel}>Driver</span>
            <select className={`${s.select} ${s.selectA}`} value={tlaA ?? ""} disabled={!analysis}
                    onChange={(e) => { setTlaA(e.target.value); setLapNumber(null); }}>
              {analysis?.drivers.map((d) => (
                <option key={d.tla} value={d.tla}>{d.tla} — {d.teamName}</option>
              ))}
            </select>
          </label>

          <label className={s.field}>
            <span className={s.fieldLabel}>Compare with</span>
            <select className={`${s.select} ${s.selectB}`} value={tlaB ?? ""} disabled={!analysis}
                    onChange={(e) => setTlaB(e.target.value || null)}>
              <option value="">None</option>
              {analysis?.drivers.filter((d) => d.tla !== tlaA).map((d) => (
                <option key={d.tla} value={d.tla}>{d.tla} — {d.teamName}</option>
              ))}
            </select>
          </label>

          <label className={s.field}>
            <span className={s.fieldLabel}>Lap</span>
            <select className={s.select} value={lapA ?? ""} disabled={!driverA}
                    onChange={(e) => setLapNumber(Number(e.target.value))}>
              {driverA?.laps.filter((l) => l.timeSeconds).map((l) => (
                <option key={l.lap} value={l.lap}>
                  L{l.lap} — {formatTime(l.timeSeconds)}
                </option>
              ))}
            </select>
          </label>
        </div>
      </div>

      {status && <TyreLoader block size="lg" label={status} />}

      {!status && !analysis && (
        <div className={p.empty}>
          <div className={p.emptyTitle}>Pick a session</div>
          <p className={p.emptyBody}>
            Choose a season and session above. Telemetry is recorded for 2026 onward; older
            sessions still give lap times, strategy and sector comparisons on the analysis page.
          </p>
        </div>
      )}

      {analysis && !status && (
        <>
          {traces.length > 0 ? (
            <div className={s.two}>
              <div>
                <div className={s.panel}>
                  <div className={s.header}>
                    <div className={s.marker} />
                    <h2 className={s.title}>Speed</h2>
                    <div className={s.rule} />
                    <div className={s.meta}>Against distance</div>
                  </div>
                  <ChannelChart channel="speed" traces={traces} height={190} />
                  <ChannelChart channel="throttle" traces={traces} height={90} />
                  <ChannelChart channel="brake" traces={traces} height={90} />
                  <ChannelChart channel="gear" traces={traces} height={90} />

                  <div className={s.legend}>
                    {traces.map((t) => (
                      <div key={t.tla} className={s.legendItem}>
                        <span className={s.swatch} style={{ background: t.colour }} />
                        {t.tla} · lap {t.lap.lap}
                      </div>
                    ))}
                  </div>
                </div>

                {traceA && traceB && driverA && driverB && (
                  <div className={s.panel}>
                    <div className={s.header}>
                      <div className={s.marker} />
                      <h2 className={s.title}>Time delta</h2>
                      <div className={s.rule} />
                      <div className={s.meta}>Where the lap was won</div>
                    </div>
                    <DeltaChart lapA={traceA} lapB={traceB}
                                tlaA={driverA.tla} tlaB={driverB.tla}
                                colorA={colourA} colorB={colourB} />
                  </div>
                )}
              </div>

              <div className={s.panel}>
                <div className={s.header}>
                  <div className={s.marker} />
                  <h2 className={s.title}>Racing line</h2>
                  <div className={s.rule} />
                </div>
                {traceA && driverA && (
                  <RacingLine lap={traceA} tla={driverA.tla}
                              circuitKey={analysis.meta.circuitKey} year={analysis.meta.year} />
                )}
              </div>
            </div>
          ) : (
            <div className={p.empty}>
              <div className={p.emptyTitle}>No telemetry for this session</div>
              <p className={p.emptyBody}>
                {analysis.meta.hasTelemetry
                  ? "The selected lap has no recorded trace. Try another lap."
                  : `Telemetry is only recorded for 2026 sessions onward. ${analysis.meta.meeting} ${analysis.meta.year} still has lap times, strategy and sector data on the analysis page.`}
              </p>
            </div>
          )}
        </>
      )}
    </>
  );
}
