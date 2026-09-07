import { useEffect, useMemo, useState } from "react";
import { TyreLoader } from "@/components/atoms/TyreLoader";
import { flag } from "@/lib/countries";
import { teamColour } from "@/lib/teams";
import s from "./Pages.module.css";

const API_URL = import.meta.env["VITE_API_URL"] ?? "http://localhost:4000";

/**
 * Always revalidate these.
 *
 * They are small documents whose SHAPE changes as features land, and a client
 * that trusts its own cache across a deploy renders the old shape — which is
 * how the calendar crashed the moment a podium field was added. The server
 * still caches the upstream call for a day, so revalidating costs a 304.
 */
const REVALIDATE: RequestInit = { cache: "no-cache" };
const FIRST_SEASON = 1950;

interface DriverStanding {
  position: number; code: string; driver: string; nationality: string;
  constructor: string; points: number; wins: number;
}
interface ConstructorStanding {
  position: number; constructor: string; nationality: string; points: number; wins: number;
}
interface Standings {
  season: number; round: number;
  drivers: DriverStanding[]; constructors: ConstructorStanding[];
}
interface ScheduledRound { round: number; name: string; status: string }

/**
 * Championship tables, for any season and as they stood after any round.
 *
 * "After round N" is the interesting question — a final table hides the whole
 * season's shape. Ergast addresses standings by round, so this is a lookup
 * rather than a reconstruction from results.
 */
export function StandingsPage() {
  const currentYear = new Date().getUTCFullYear();

  const [year, setYear] = useState(currentYear);
  const [round, setRound] = useState(0);        // 0 = latest
  const [standings, setStandings] = useState<Standings | null>(null);
  const [rounds, setRounds] = useState<ScheduledRound[]>([]);
  const [failed, setFailed] = useState(false);

  const seasons = useMemo(
    () => Array.from({ length: currentYear - FIRST_SEASON + 1 }, (_, i) => currentYear - i),
    [currentYear],
  );

  // The round selector only offers rounds that have actually run: asking for
  // the table after a race that has not happened returns nothing useful.
  useEffect(() => {
    let cancelled = false;
    void fetch(`${API_URL}/api/schedule/${year}`, REVALIDATE)
      .then((r) => r.json() as Promise<ScheduledRound[]>)
      .then((list) => { if (!cancelled) setRounds(list.filter((r) => r.status === "finished")); })
      .catch(() => { if (!cancelled) setRounds([]); });

    return () => { cancelled = true; };
  }, [year]);

  useEffect(() => {
    let cancelled = false;
    const query = round > 0 ? `?round=${round}` : "";

    void fetch(`${API_URL}/api/standings/${year}${query}`, REVALIDATE)
      .then((r) => (r.ok ? (r.json() as Promise<Standings>) : null))
      .then((data) => {
        if (cancelled) return;
        setStandings(data);
        setFailed(data === null);
      })
      .catch(() => { if (!cancelled) setFailed(true); });

    return () => { cancelled = true; };
  }, [year, round]);

  const maxDriverPoints = standings?.drivers[0]?.points ?? 1;
  const maxTeamPoints = standings?.constructors[0]?.points ?? 1;

  return (
    <>
      <div className={s.pickerHeader}>
        <div className={s.marker} />
        <h1 className={s.pickerTitle}>Championship</h1>
        <div className={s.rule} />
        <div className={s.meta}>
          {standings ? `After round ${standings.round}` : ""}
        </div>
      </div>

      <div className={s.controls}>
        <div className={s.controlGroup}>
          <span className={s.controlLabel}>Season</span>
          <select
            className={s.select}
            value={year}
            aria-label="Season"
            onChange={(e) => { setYear(Number(e.target.value)); setRound(0); }}
          >
            {seasons.map((y) => <option key={y} value={y}>{y}</option>)}
          </select>
        </div>

        <div className={s.controlGroup}>
          <span className={s.controlLabel}>After round</span>
          <select
            className={s.select}
            value={round}
            aria-label="After round"
            onChange={(e) => setRound(Number(e.target.value))}
          >
            <option value={0}>Latest</option>
            {rounds.map((r) => (
              <option key={r.round} value={r.round}>R{r.round} — {r.name}</option>
            ))}
          </select>
        </div>
      </div>

      {failed ? (
        <div className={s.empty}>
          <div className={s.emptyTitle}>No standings for {year}</div>
          <p className={s.emptyBody}>
            The season may not have started, or the upstream results service is unavailable.
          </p>
        </div>
      ) : !standings ? (
        <TyreLoader block size="lg" label={`Loading ${year} standings`} />
      ) : (
        <div className={s.tables}>
          <div>
            <div className={s.pickerHeader}>
              <div className={s.marker} />
              <h2 className={s.pickerTitle} style={{ fontSize: "var(--fs-13)" }}>Drivers</h2>
              <div className={s.rule} />
            </div>
            {standings.drivers.map((d) => (
              <div key={d.code + d.driver} className={s.standingRow}>
                <div className={s.teamStripe} style={{ background: teamColour(d.constructor) }} />
                <div className={`${s.standingPos} ${d.position === 1 ? s.standingLeader : ""}`}>
                  {d.position}
                </div>
                <div>
                  <div className={s.standingName}>
                    <span className={s.flag}>{flag(d.nationality)}</span>
                    <span className={s.standingCode}>{d.code || d.driver.slice(0, 3).toUpperCase()}</span>
                    <span className={s.standingFull}>{d.driver}</span>
                  </div>
                  <div className={s.standingTeam}>{d.constructor}</div>
                </div>
                <div className={s.standingPoints}>{d.points}</div>
                <div className={s.standingWins}>{d.wins ? `${d.wins} W` : "—"}</div>
                <div className={s.pointsBar}
                     style={{ width: `${(d.points / maxDriverPoints) * 100}%`,
                              background: teamColour(d.constructor) }} />
              </div>
            ))}
          </div>

          <div>
            <div className={s.pickerHeader}>
              <div className={s.marker} />
              <h2 className={s.pickerTitle} style={{ fontSize: "var(--fs-13)" }}>Constructors</h2>
              <div className={s.rule} />
            </div>
            {standings.constructors.map((c) => (
              <div key={c.constructor} className={`${s.standingRow} ${s.standingCon}`}>
                <div className={s.teamStripe} style={{ background: teamColour(c.constructor) }} />
                <div className={`${s.standingPos} ${c.position === 1 ? s.standingLeader : ""}`}>
                  {c.position}
                </div>
                <div className={s.standingName}>
                  <span className={s.flag}>{flag(c.nationality)}</span>
                  <span className={s.standingCode} style={{ fontSize: "var(--fs-15)" }}>
                    {c.constructor}
                  </span>
                </div>
                <div className={s.standingPoints}>{c.points}</div>
                <div className={s.pointsBar}
                     style={{ width: `${(c.points / maxTeamPoints) * 100}%`,
                              background: teamColour(c.constructor) }} />
              </div>
            ))}
          </div>
        </div>
      )}
    </>
  );
}
