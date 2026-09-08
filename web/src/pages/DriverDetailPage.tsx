import { useEffect, useMemo, useState } from "react";
import { Link, useParams, useSearchParams } from "react-router";
import { DriverPhoto } from "@/components/atoms/DriverPhoto/DriverPhoto";
import { TyreLoader } from "@/components/atoms/TyreLoader";
import { useDrivers } from "@/features/drivers/lib/useDrivers";
import s from "./DriverDetailPage.module.css";

const API_URL = import.meta.env["VITE_API_URL"] ?? "http://localhost:4000";

interface DriverRound {
  round: number;
  raceName: string;
  circuit: string;
  date: string;
  grid: number | null;
  finish: number | null;
  status: string;
  points: number;
  cumulativePoints: number;
  qualifyingPosition: number | null;
  qualifyingTime: string | null;
}

interface DriverSeason {
  season: number;
  code: string;
  givenName: string;
  familyName: string;
  nationality: string;
  constructor: string;
  points: number;
  wins: number;
  podiums: number;
  bestFinish: number | null;
  retirements: number;
  rounds: DriverRound[];
}

const SEASONS = [2026, 2025, 2024, 2023, 2022];

/** One driver's season: results per round, points progression, quali vs race. */
export function DriverDetailPage() {
  const { driverId = "" } = useParams();
  const [params, setParams] = useSearchParams();

  const year = Number(params.get("year")) || SEASONS[0]!;
  const [loaded, setLoaded] = useState<{ year: number; id: string; season: DriverSeason | null } | null>(null);

  const profiles = useDrivers(year);

  useEffect(() => {
    let cancelled = false;

    void fetch(`${API_URL}/api/drivers/${year}/${encodeURIComponent(driverId)}/season`)
      .then((r) => (r.ok ? (r.json() as Promise<DriverSeason>) : null))
      .then((season) => { if (!cancelled) setLoaded({ year, id: driverId, season }); })
      .catch(() => { if (!cancelled) setLoaded({ year, id: driverId, season: null }); });

    return () => { cancelled = true; };
  }, [year, driverId]);

  // Derived, not cleared in an effect: a stale season under a new year would
  // render last year's results under this year's heading for one frame.
  const fresh = loaded?.year === year && loaded.id === driverId ? loaded : null;
  const season = fresh?.season ?? null;

  const profile = season ? profiles.get(season.code) : undefined;
  const colour = profile ? `#${profile.teamColour}` : "var(--accent)";

  const progression = useMemo(() => season?.rounds ?? [], [season]);

  if (!fresh) return <TyreLoader block size="lg" label="Loading season" />;

  if (!season) {
    return (
      <div className={s.page}>
        <Link to="/drivers" className={s.back}>← All drivers</Link>
        <h1 className={s.name}>No {year} season</h1>
        <p className={s.statLabel}>
          This driver has no results for {year}. Try another season below.
        </p>
        <div className={s.years}>
          {SEASONS.map((y) => (
            <button
              key={y}
              type="button"
              className={`${s.year} ${y === year ? s.yearActive : ""}`}
              onClick={() => setParams({ year: String(y) })}
            >
              {y}
            </button>
          ))}
        </div>
      </div>
    );
  }

  return (
    <div className={s.page} style={{ ["--team" as string]: colour, ["--team-wash" as string]: `${colour}22` }}>
      <div className={s.years}>
        {SEASONS.map((y) => (
          <button
            key={y}
            type="button"
            className={`${s.year} ${y === year ? s.yearActive : ""}`}
            onClick={() => setParams({ year: String(y) })}
          >
            {y}
          </button>
        ))}
      </div>

      <div className={s.hero}>
        <div className={s.heroText}>
          <Link to="/drivers" className={s.back}>← All drivers</Link>
          <h1 className={s.name}>
            <span>{season.givenName}</span>
            {season.familyName}
          </h1>
          <div className={s.team}>{season.constructor} · {season.nationality}</div>
        </div>

        <span className={s.heroGhost} aria-hidden="true">{profile?.racingNumber ?? season.code}</span>

        {profile?.headshotUrl && (
          <div className={s.portrait}>
            <DriverPhoto
              url={profile.headshotUrl}
              tla={season.code}
              colour={colour}
              size={186}
              cutout
            />
          </div>
        )}
      </div>

      <div className={s.stats}>
        <div className={s.stat}>
          <div className={s.statValue}>{season.points}</div>
          <div className={s.statLabel}>Points</div>
        </div>
        <div className={s.stat}>
          <div className={s.statValue}>{season.wins}</div>
          <div className={s.statLabel}>Wins</div>
        </div>
        <div className={s.stat}>
          <div className={s.statValue}>{season.podiums}</div>
          <div className={s.statLabel}>Podiums</div>
        </div>
        <div className={s.stat}>
          <div className={s.statValue}>{season.bestFinish ? `P${season.bestFinish}` : "—"}</div>
          <div className={s.statLabel}>Best finish</div>
        </div>
        <div className={s.stat}>
          <div className={s.statValue}>{season.retirements}</div>
          <div className={s.statLabel}>Retirements</div>
        </div>
      </div>

      <div className={s.section}>
        <div className={s.sectionTitle}>Points progression</div>
        <div className={s.panel}>
          <PointsChart rounds={progression} colour={colour} />
        </div>
      </div>

      <div className={s.section}>
        <div className={s.sectionTitle}>Qualifying against race result</div>
        <div className={s.panel}>
          <table className={s.table}>
            <thead>
              <tr>
                <th>Round</th>
                <th>Grand Prix</th>
                <th className={s.num}>Quali</th>
                <th>Best lap</th>
                <th className={s.num}>Grid</th>
                <th className={s.num}>Finish</th>
                <th className={s.num}>Gained</th>
                <th className={s.num}>Points</th>
              </tr>
            </thead>
            <tbody>
              {season.rounds.map((round) => {
                // Positions gained is grid minus finish; a retirement has no
                // finishing position, so it has no gain either — showing 0
                // would read as "held station", which is the opposite.
                const gained =
                  round.grid && round.finish ? round.grid - round.finish : null;

                return (
                  <tr key={round.round}>
                    <td className={s.num}>{round.round}</td>
                    <td>{round.raceName}</td>
                    <td className={s.num}>{round.qualifyingPosition ? `P${round.qualifyingPosition}` : "—"}</td>
                    <td className={s.num}>{round.qualifyingTime ?? "—"}</td>
                    <td className={s.num}>{round.grid || "—"}</td>
                    <td className={s.num}>
                      {round.finish ? `P${round.finish}` : <span className={s.dnf}>DNF</span>}
                    </td>
                    <td className={`${s.num} ${gained === null ? "" : gained > 0 ? s.gain : gained < 0 ? s.loss : ""}`}>
                      {gained === null ? "—" : gained > 0 ? `+${gained}` : gained}
                    </td>
                    <td className={s.num}>{round.points || "—"}</td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
      </div>
    </div>
  );
}

const W = 720;
const H = 200;
const PAD = { top: 12, right: 12, bottom: 22, left: 40 };

/** Cumulative points across the season, as a step-free line with round markers. */
function PointsChart({ rounds, colour }: { rounds: DriverRound[]; colour: string }) {
  if (rounds.length < 2) {
    return <div className={s.statLabel}>Two rounds are needed before there is a line to draw.</div>;
  }

  const max = Math.max(1, rounds.at(-1)!.cumulativePoints);

  const x = (i: number) => PAD.left + (i / (rounds.length - 1)) * (W - PAD.left - PAD.right);
  const y = (points: number) =>
    PAD.top + (1 - points / max) * (H - PAD.top - PAD.bottom);

  const line = rounds.map((r, i) => `${i === 0 ? "M" : "L"}${x(i).toFixed(1)} ${y(r.cumulativePoints).toFixed(1)}`).join(" ");
  const area = `${line} L${x(rounds.length - 1).toFixed(1)} ${y(0).toFixed(1)} L${x(0).toFixed(1)} ${y(0).toFixed(1)} Z`;

  const ticks = [0, max / 2, max];

  return (
    <svg
      className={s.chart}
      viewBox={`0 0 ${W} ${H}`}
      preserveAspectRatio="xMidYMid meet"
      role="img"
      aria-label={`Cumulative points across ${rounds.length} rounds`}
    >
      {ticks.map((value) => (
        <g key={value}>
          <line className={s.gridline} x1={PAD.left} x2={W - PAD.right} y1={y(value)} y2={y(value)} />
          <text className={s.axis} x={PAD.left - 5} y={y(value) + 3} textAnchor="end">
            {Math.round(value)}
          </text>
        </g>
      ))}

      <path d={area} fill={colour} opacity={0.14} />
      <path d={line} fill="none" stroke={colour} strokeWidth={2} strokeLinejoin="round" />

      {rounds.map((round, i) => (
        <circle key={round.round} cx={x(i)} cy={y(round.cumulativePoints)} r={round.points > 0 ? 3 : 2}
                fill={round.points > 0 ? colour : "var(--surface-4)"} stroke={colour} strokeWidth={1} />
      ))}

      {rounds.map((round, i) =>
        i % Math.ceil(rounds.length / 8) === 0 ? (
          <text key={round.round} className={s.axis} x={x(i)} y={H - 6} textAnchor="middle">
            R{round.round}
          </text>
        ) : null,
      )}
    </svg>
  );
}
