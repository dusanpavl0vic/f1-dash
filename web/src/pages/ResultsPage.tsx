import { useEffect, useState } from "react";
import { DriverPhoto } from "@/components/atoms/DriverPhoto/DriverPhoto";
import { TyreLoader } from "@/components/atoms/TyreLoader";
import { useDrivers } from "@/features/drivers/lib/useDrivers";
import { flag } from "@/lib/countries";
import { teamColour } from "@/lib/teams";
import s from "./Pages.module.css";

const API_URL = import.meta.env["VITE_API_URL"] ?? "http://localhost:4000";
const REVALIDATE: RequestInit = { cache: "no-cache" };

interface ResultRow {
  position: number; number: string; code: string; driver: string;
  constructor: string; nationality: string; time: string; gap: string;
  points: number; laps: number; status: string; grid: number | null;
}
interface SessionResults { session: string; rows: ResultRow[] }
interface Highlight { code: string; driver: string; constructor: string; value: string; detail: string | null }
interface RaceResults {
  season: number; round: number; raceName: string; circuit: string;
  country: string; locality: string; date: string | null;
  winner: Highlight | null; pole: Highlight | null; fastestLap: Highlight | null;
  sessions: SessionResults[];
}
interface ScheduledRound { round: number; name: string; country: string; status: string }

function HighlightCard({ label, entry }: { label: string; entry: Highlight | null }) {
  if (!entry) {
    return (
      <div className={s.highlight}>
        <div className={s.highlightLabel}>{label}</div>
        <div className={s.highlightMeta} style={{ marginTop: 8 }}>Not available</div>
      </div>
    );
  }

  return (
    <div className={s.highlight}>
      <span className={s.driverStripe} style={{ background: teamColour(entry.constructor) }} />
      <div className={s.highlightLabel}>{label}</div>
      <div className={s.highlightName}>{entry.driver}</div>
      <div className={s.highlightMeta}>{entry.constructor}</div>
      <div className={s.highlightValue}>
        {entry.value}{entry.detail ? ` · ${entry.detail}` : ""}
      </div>
    </div>
  );
}

export function ResultsPage() {
  const currentYear = new Date().getUTCFullYear();

  const [year, setYear] = useState(currentYear);
  const [rounds, setRounds] = useState<ScheduledRound[]>([]);
  const [round, setRound] = useState<number | null>(null);
  const [results, setResults] = useState<RaceResults | null>(null);
  const [session, setSession] = useState<string | null>(null);
  const [failed, setFailed] = useState(false);

  const profiles = useDrivers(year);

  useEffect(() => {
    let cancelled = false;
    void fetch(`${API_URL}/api/schedule/${year}`, REVALIDATE)
      .then((r) => r.json() as Promise<ScheduledRound[]>)
      .then((list) => {
        if (cancelled) return;
        const finished = list.filter((r) => r.status === "finished");
        setRounds(finished);
        // Open on the most recent race, which is what anyone came to look at.
        setRound(finished.at(-1)?.round ?? null);
      })
      .catch(() => { if (!cancelled) setRounds([]); });

    return () => { cancelled = true; };
  }, [year]);

  useEffect(() => {
    if (round === null) return;

    let cancelled = false;
    void fetch(`${API_URL}/api/results/${year}/${round}`)
      .then((r) => (r.ok ? (r.json() as Promise<RaceResults>) : null))
      .then((data) => {
        if (cancelled) return;
        setResults(data);
        setFailed(data === null);
        setSession(data?.sessions[0]?.session ?? null);
      })
      .catch(() => { if (!cancelled) setFailed(true); });

    return () => { cancelled = true; };
  }, [year, round]);

  const active = results?.sessions.find((x) => x.session === session) ?? results?.sessions[0];
  const isQualifying = active?.session === "Qualifying";
  const isGrid = active?.session === "Starting grid";

  return (
    <>
      <div className={s.pickerHeader}>
        <div className={s.marker} />
        <h1 className={s.pickerTitle}>
          {results ? `${flag(results.country)} ${results.raceName} ${results.season}` : "Results"}
        </h1>
        <div className={s.rule} />
        <div className={s.meta}>{results ? `${results.circuit} · ${results.date ?? ""}` : ""}</div>
      </div>

      <div className={s.controls}>
        <div className={s.controlGroup}>
          <span className={s.controlLabel}>Season</span>
          <select className={s.select} value={year} aria-label="Season"
                  onChange={(e) => { setYear(Number(e.target.value)); setRound(null); setResults(null); }}>
            {Array.from({ length: currentYear - 1950 + 1 }, (_, i) => currentYear - i)
              .map((y) => <option key={y} value={y}>{y}</option>)}
          </select>
        </div>

        <div className={s.controlGroup}>
          <span className={s.controlLabel}>Grand Prix</span>
          <select className={s.select} value={round ?? ""} aria-label="Grand Prix"
                  onChange={(e) => setRound(Number(e.target.value))}>
            {rounds.map((r) => (
              <option key={r.round} value={r.round}>R{r.round} — {r.name}</option>
            ))}
          </select>
        </div>
      </div>

      {failed ? (
        <div className={s.empty}>
          <div className={s.emptyTitle}>No results</div>
          <p className={s.emptyBody}>That round has not run, or the results service is unavailable.</p>
        </div>
      ) : !results ? (
        <TyreLoader block size="lg" label="Loading results" />
      ) : (
        <>
          <div className={s.highlights}>
            <HighlightCard label="Race winner" entry={results.winner} />
            <HighlightCard label="Pole position" entry={results.pole} />
            <HighlightCard label="Fastest lap" entry={results.fastestLap} />
          </div>

          <div className={s.tabs}>
            {results.sessions.map((x) => (
              <button key={x.session} type="button"
                      className={`${s.tab} ${x.session === active?.session ? s.tabActive : ""}`}
                      onClick={() => setSession(x.session)}>
                {x.session}
              </button>
            ))}
          </div>

          <table className={s.resultsTable}>
            <thead>
              <tr>
                <th>Pos</th><th>No.</th><th>Driver</th><th>Team</th>
                <th>{isQualifying ? "Best" : "Time"}</th>
                {!isQualifying && !isGrid && <><th>Gap</th><th>Points</th><th>Laps</th></>}
              </tr>
            </thead>
            <tbody>
              {(active?.rows ?? []).map((row) => (
                <tr key={`${row.code}-${row.position}`}>
                  <td className={s.resPos}>{row.position}</td>
                  <td className={s.resNum}>{row.number}</td>
                  <td>
                    <span className={s.resDriverCell}>
                      <DriverPhoto
                        url={profiles.get(row.code)?.headshotUrl}
                        tla={row.code}
                        colour={teamColour(row.constructor)}
                        size={28}
                      />
                      <span>
                        <span className={s.resDriver}>{row.code || row.driver}</span>{" "}
                        <span className={s.resStatus} style={{ fontSize: "var(--fs-10)" }}>
                          {flag(row.nationality)} {row.driver}
                        </span>
                      </span>
                    </span>
                  </td>
                  <td>
                    <span className={s.resTeam}>
                      <span className={s.resTeamBar} style={{ background: teamColour(row.constructor) }} />
                      {row.constructor}
                    </span>
                  </td>
                  <td>{row.time || "—"}</td>
                  {!isQualifying && !isGrid && (
                    <>
                      {/* Ergast puts a gap and a retirement reason in the same
                          field; a status is not a time and is dimmed. */}
                      <td className={/^[+\d]/.test(row.gap) ? undefined : s.resStatus}>
                        {row.gap || "—"}
                      </td>
                      <td>{row.points || "—"}</td>
                      <td>{row.laps || "—"}</td>
                    </>
                  )}
                </tr>
              ))}
            </tbody>
          </table>
        </>
      )}
    </>
  );
}
