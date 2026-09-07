import { useEffect, useState } from "react";
import { TyreLoader } from "@/components/atoms/TyreLoader";
import { flag } from "@/lib/countries";
import { teamColour } from "@/lib/teams";
import s from "./Pages.module.css";

const API_URL = import.meta.env["VITE_API_URL"] ?? "http://localhost:4000";
const REVALIDATE: RequestInit = { cache: "no-cache" };

interface DriverStanding {
  position: number; code: string; driver: string; nationality: string;
  constructor: string; points: number; wins: number;
}
interface ConstructorStanding {
  position: number; constructor: string; nationality: string; points: number; wins: number;
}
interface Standings { season: number; round: number; drivers: DriverStanding[]; constructors: ConstructorStanding[] }
interface PodiumEntry { code: string; position: number }
interface ScheduledRound { round: number; podium?: PodiumEntry[] }

/**
 * Driver and constructor cards.
 *
 * Team logos and driver portraits are trademarked and cannot be shipped
 * (docs/20 Phase J), so the card's graphic element is the driver's NUMBER at
 * display scale in the team colour. The number is as recognisable to a fan as a
 * badge and it is ours to draw.
 */
export function DriversPage({ mode }: { mode: "drivers" | "teams" }) {
  const currentYear = new Date().getUTCFullYear();

  const [year, setYear] = useState(currentYear);
  const [standings, setStandings] = useState<Standings | null>(null);
  const [podiums, setPodiums] = useState<Record<string, number>>({});
  const [numbers, setNumbers] = useState<Record<string, string>>({});
  const [failed, setFailed] = useState(false);

  useEffect(() => {
    let cancelled = false;

    void fetch(`${API_URL}/api/standings/${year}`, REVALIDATE)
      .then((r) => (r.ok ? (r.json() as Promise<Standings>) : null))
      .then((data) => {
        if (cancelled) return;
        setStandings(data);
        setFailed(data === null);
      })
      .catch(() => { if (!cancelled) setFailed(true); });

    // Podium counts come from the calendar, which already carries each finished
    // round's top three — no extra request per driver.
    void fetch(`${API_URL}/api/schedule/${year}`, REVALIDATE)
      .then((r) => r.json() as Promise<ScheduledRound[]>)
      .then((list) => {
        if (cancelled) return;
        const counts: Record<string, number> = {};
        for (const round of list) {
          for (const entry of round.podium ?? []) {
            counts[entry.code] = (counts[entry.code] ?? 0) + 1;
          }
        }
        setPodiums(counts);
      })
      .catch(() => undefined);

    // Car numbers come from the most recent race result.
    void fetch(`${API_URL}/api/schedule/${year}`, REVALIDATE)
      .then((r) => r.json() as Promise<ScheduledRound[]>)
      .then(async (list) => {
        const last = list.filter((r) => (r.podium ?? []).length > 0).at(-1);
        if (!last || cancelled) return;

        const race = await fetch(`${API_URL}/api/results/${year}/${last.round}`)
          .then((r) => (r.ok ? r.json() : null))
          .catch(() => null) as { sessions: { session: string; rows: { code: string; number: string }[] }[] } | null;

        const rows = race?.sessions.find((x) => x.session === "Race")?.rows ?? [];
        if (!cancelled) {
          setNumbers(Object.fromEntries(rows.map((r) => [r.code, r.number])));
        }
      })
      .catch(() => undefined);

    return () => { cancelled = true; };
  }, [year]);

  const title = mode === "drivers" ? "Drivers" : "Teams";

  return (
    <>
      <div className={s.pickerHeader}>
        <div className={s.marker} />
        <h1 className={s.pickerTitle}>{title}</h1>
        <div className={s.rule} />
        <div className={s.meta}>
          {standings ? `${year} · after round ${standings.round}` : year}
        </div>
      </div>

      <div className={s.controls}>
        <div className={s.controlGroup}>
          <span className={s.controlLabel}>Season</span>
          <select className={s.select} value={year} aria-label="Season"
                  onChange={(e) => { setYear(Number(e.target.value)); setStandings(null); }}>
            {Array.from({ length: currentYear - 1958 + 1 }, (_, i) => currentYear - i)
              .map((y) => <option key={y} value={y}>{y}</option>)}
          </select>
        </div>
      </div>

      {failed ? (
        <div className={s.empty}>
          <div className={s.emptyTitle}>No data for {year}</div>
          <p className={s.emptyBody}>The season may not have started.</p>
        </div>
      ) : !standings ? (
        <TyreLoader block size="lg" label={`Loading ${year}`} />
      ) : mode === "drivers" ? (
        <div className={s.cardGrid}>
          {standings.drivers.map((d) => {
            const colour = teamColour(d.constructor);
            return (
              <article key={d.code + d.driver} className={s.driverCard}>
                <span className={s.driverStripe} style={{ background: colour }} />
                <span className={s.driverGhost} style={{ color: colour, opacity: 0.14 }}>
                  {numbers[d.code] ?? d.position}
                </span>
                <div className={s.driverName} style={{ color: colour }}>
                  {d.driver.split(" ").at(-1)}
                </div>
                <div className={s.driverTeam}>
                  {flag(d.nationality)} {d.constructor}
                </div>
                <div className={s.driverStats}>
                  <div className={s.driverStat}>
                    <span className={s.driverStatValue}>{d.points}</span>
                    <span className={s.driverStatLabel}>Points</span>
                  </div>
                  <div className={s.driverStat}>
                    <span className={s.driverStatValue}>{d.position}</span>
                    <span className={s.driverStatLabel}>Position</span>
                  </div>
                  <div className={s.driverStat}>
                    <span className={s.driverStatValue}>{d.wins}</span>
                    <span className={s.driverStatLabel}>Wins</span>
                  </div>
                  <div className={s.driverStat}>
                    <span className={s.driverStatValue}>{podiums[d.code] ?? 0}</span>
                    <span className={s.driverStatLabel}>Podiums</span>
                  </div>
                </div>
              </article>
            );
          })}
        </div>
      ) : (
        <div className={s.cardGrid}>
          {standings.constructors.map((c) => {
            const colour = teamColour(c.constructor);
            const drivers = standings.drivers.filter((d) => d.constructor === c.constructor);

            return (
              <article key={c.constructor} className={s.driverCard}>
                <span className={s.driverStripe} style={{ background: colour }} />
                <span className={s.driverGhost} style={{ color: colour, opacity: 0.14 }}>
                  {c.position}
                </span>
                <div className={s.driverName} style={{ color: colour }}>{c.constructor}</div>
                <div className={s.driverTeam}>
                  {flag(c.nationality)} {drivers.map((d) => d.code).join(" · ") || "—"}
                </div>
                <div className={s.driverStats}>
                  <div className={s.driverStat}>
                    <span className={s.driverStatValue}>{c.points}</span>
                    <span className={s.driverStatLabel}>Points</span>
                  </div>
                  <div className={s.driverStat}>
                    <span className={s.driverStatValue}>{c.position}</span>
                    <span className={s.driverStatLabel}>Position</span>
                  </div>
                  <div className={s.driverStat}>
                    <span className={s.driverStatValue}>{c.wins}</span>
                    <span className={s.driverStatLabel}>Wins</span>
                  </div>
                </div>
              </article>
            );
          })}
        </div>
      )}
    </>
  );
}
