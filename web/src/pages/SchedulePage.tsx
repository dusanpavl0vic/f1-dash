import { useEffect, useState } from "react";
import { Link } from "react-router";
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

interface PodiumEntry { position: number; code: string; driver: string; constructor: string }

/**
 * Anything optional on a response is read defensively.
 *
 * /api/schedule is served with a one-hour Cache-Control, so after a deploy that
 * adds a field the browser keeps serving the OLD shape until the cache expires.
 * A client that assumes the new shape crashes on its own cache — which is
 * exactly what happened when `podium` was added.
 */
function podiumOf(round: ScheduledRound): PodiumEntry[] {
  return Array.isArray(round.podium) ? round.podium : [];
}

interface ScheduledRound {
  round: number;
  name: string;
  circuit: string;
  country: string;
  locality: string;
  startUtc: string | null;
  status: "finished" | "live" | "upcoming";
  podium?: PodiumEntry[];
}

interface NextSession { round: ScheduledRound | null; hoursUntil: number | null }

function formatDate(iso: string | null): string {
  if (!iso) return "TBC";
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) return "TBC";

  return date.toLocaleDateString(undefined, { day: "2-digit", month: "short", year: "numeric" });
}

/** Days when the race is far off, hours when it is close — a countdown in 700 hours is unreadable. */
function countdown(hours: number): { value: string; unit: string } {
  if (hours <= 0) return { value: "NOW", unit: "" };
  if (hours < 48) return { value: hours.toFixed(1), unit: "hours" };
  return { value: Math.round(hours / 24).toString(), unit: "days" };
}

export function SchedulePage() {
  // The current season, not a hardcoded year — this page must not need editing
  // in January.
  const year = new Date().getUTCFullYear();

  const [rounds, setRounds] = useState<ScheduledRound[] | null>(null);
  const [next, setNext] = useState<NextSession | null>(null);

  useEffect(() => {
    let cancelled = false;

    void fetch(`${API_URL}/api/schedule/${year}`, REVALIDATE)
      .then((r) => r.json() as Promise<ScheduledRound[]>)
      .then((data) => { if (!cancelled) setRounds(data); })
      .catch(() => { if (!cancelled) setRounds([]); });

    void fetch(`${API_URL}/api/schedule/${year}/next`, REVALIDATE)
      .then((r) => r.json() as Promise<NextSession>)
      .then((data) => { if (!cancelled) setNext(data); })
      .catch(() => undefined);

    return () => { cancelled = true; };
  }, [year]);

  if (rounds === null) {
    return <TyreLoader block size="lg" label={`Loading the ${year} calendar`} />;
  }

  if (rounds.length === 0) {
    return (
      <div className={s.empty}>
        <div className={s.emptyTitle}>No calendar available</div>
        <p className={s.emptyBody}>
          The {year} calendar could not be fetched. Replay and analysis are unaffected —
          they read the archive, not the calendar.
        </p>
        <Link to="/replay" className={`${s.button} ${s.buttonPrimary}`}>GO TO REPLAY</Link>
      </div>
    );
  }

  const upcoming = next?.round;
  const remaining = next?.hoursUntil !== null && next?.hoursUntil !== undefined
    ? countdown(next.hoursUntil)
    : null;

  return (
    <>
      {upcoming && (
        <div className={s.next}>
          <div>
            <div className={s.nextLabel}>
              {upcoming.status === "live" ? "Running now" : "Next race"}
            </div>
            <div className={s.nextName}>
              <span className={s.flagLarge}>{flag(upcoming.country)}</span>{" "}
              {upcoming.name}
            </div>
            <div className={s.nextWhere}>
              R{upcoming.round} · {upcoming.circuit} · {upcoming.locality}, {upcoming.country}
              {" · "}{formatDate(upcoming.startUtc)}
            </div>
          </div>

          {remaining && (
            <div className={s.countdown}>
              <div className={s.countdownValue}>{remaining.value}</div>
              <div className={s.countdownUnit}>{remaining.unit || "live"}</div>
            </div>
          )}
        </div>
      )}

      <div className={s.pickerHeader}>
        <div className={s.marker} />
        <h1 className={s.pickerTitle}>{year} season</h1>
        <div className={s.rule} />
        <div className={s.meta}>
          {rounds.filter((r) => r.status === "finished").length} of {rounds.length} run
        </div>
      </div>

      <div className={s.rounds}>
        {rounds.map((round) => (
          <div key={round.round}
               className={`${s.round} ${round.status === "finished" ? s.roundPast : ""}`}>
            <div className={s.roundNumber}>R{round.round}</div>
            <div className={s.standingName}>
              <span className={s.flag}>{flag(round.country)}</span>
              <div>
                <div className={s.roundName}>{round.name}</div>
                <div className={s.roundWhere}>{round.locality}, {round.country}</div>
              </div>
            </div>

            {/* A finished round shows who won it — a calendar of dates alone
                tells you nothing about the season. */}
            {podiumOf(round).length > 0 ? (
              <div className={s.podium}>
                {podiumOf(round).map((p) => (
                  <div key={p.position}
                       className={`${s.podiumEntry} ${
                         p.position === 1 ? s.podium1 : p.position === 2 ? s.podium2 : s.podium3}`}
                       title={`P${p.position} ${p.driver} — ${p.constructor}`}>
                    <span className={s.podiumBar} style={{ background: teamColour(p.constructor) }} />
                    <span className={s.podiumPos}>{p.position}</span>
                    <span className={s.podiumCode}>{p.code}</span>
                  </div>
                ))}
              </div>
            ) : (
              <div className={s.roundWhere}>{round.circuit}</div>
            )}

            <div className={s.roundDate}>{formatDate(round.startUtc)}</div>
            <div className={`${s.status} ${
              round.status === "live" ? s.statusLive
              : round.status === "upcoming" ? s.statusUpcoming
              : s.statusFinished}`}>
              {round.status}
            </div>
          </div>
        ))}
      </div>
    </>
  );
}
