import { useEffect, useState } from "react";
import { Link } from "react-router";
import { flag } from "@/lib/countries";
import s from "./HomePage.module.css";

const API_URL = import.meta.env["VITE_API_URL"] ?? "http://localhost:4000";
const REVALIDATE: RequestInit = { cache: "no-cache" };

interface LiveStatus { live: boolean; meeting: string | null; session: string | null }
interface ScheduledRound {
  round: number; name: string; circuit: string; country: string;
  locality: string; startUtc: string | null; status: string;
}
interface NextSession { round: ScheduledRound | null; hoursUntil: number | null }

function countdown(hours: number | null): { value: string; label: string } {
  if (hours === null) return { value: "—", label: "next race" };
  if (hours <= 0) return { value: "NOW", label: "racing" };
  if (hours < 48) return { value: hours.toFixed(0), label: "hours to go" };
  return { value: Math.round(hours / 24).toString(), label: "days to go" };
}

/**
 * The landing page.
 *
 * Out of season this is where most visits end, so it has to be a destination
 * rather than a redirect: what is next, and the four things worth doing when
 * nothing is running.
 */
export function HomePage() {
  const [live, setLive] = useState<LiveStatus | null>(null);
  const [next, setNext] = useState<NextSession | null>(null);

  useEffect(() => {
    const year = new Date().getUTCFullYear();
    let cancelled = false;

    void fetch(`${API_URL}/api/session/live`)
      .then((r) => r.json() as Promise<LiveStatus>)
      .then((d) => { if (!cancelled) setLive(d); })
      .catch(() => undefined);

    void fetch(`${API_URL}/api/schedule/${year}/next`, REVALIDATE)
      .then((r) => r.json() as Promise<NextSession>)
      .then((d) => { if (!cancelled) setNext(d); })
      .catch(() => undefined);

    return () => { cancelled = true; };
  }, []);

  const remaining = countdown(next?.hoursUntil ?? null);
  const upcoming = next?.round;

  const cards = [
    {
      to: "/live",
      kicker: "Session in progress",
      name: "Live",
      body: "The live timing feed: timing tower, track map, race control and weather. Connects only when a session is actually running.",
      accent: "var(--accent)",
      badge: live?.live
        ? { text: "Running now", className: s.badgeLive }
        : { text: "No session", className: s.badgeIdle },
    },
    {
      to: "/replay",
      kicker: "Every session since 2018",
      name: "Replay",
      body: "Play back any archived practice, qualifying or race through the same dashboard. Nothing starts until you pick one.",
      accent: "var(--yellow)",
      badge: { text: "2018 — now", className: s.badgeIdle },
    },
    {
      to: "/telemetry",
      kicker: "Speed, brake, gear, GPS",
      name: "Telemetry",
      body: "Compare two drivers across a lap: channel traces against distance, the time delta, and the racing line coloured by speed.",
      accent: "var(--teal)",
      badge: { text: "2026 onward", className: s.badgeSoon },
    },
    {
      to: "/schedule",
      kicker: "The season calendar",
      name: "Schedule",
      body: "Every round with its date, circuit and podium, and a countdown to the next race.",
      accent: "var(--blue-light)",
      badge: upcoming
        ? { text: `Next: R${upcoming.round}`, className: s.badgeIdle }
        : { text: "Calendar", className: s.badgeIdle },
    },
    {
      to: "/standings",
      kicker: "Drivers and constructors",
      name: "Standings",
      body: "The championship for any season — and as it stood after any round, not just at the end.",
      accent: "var(--purple)",
      badge: { text: "Any season", className: s.badgeIdle },
    },
    {
      to: "/replay",
      kicker: "Laps, strategy, sectors",
      name: "Analysis",
      body: "Lap-time charts, stint strategy, position progression and sector comparisons — with a PDF export. Open a session from Replay.",
      accent: "var(--orange)",
      badge: { text: "Per session", className: s.badgeIdle },
    },
  ];

  return (
    <>
      <div className={s.hero}>
        <div className={s.heroBody}>
          <div className={s.heroKicker}>
            {live?.live ? "A session is running now" : "Unofficial F1 timing console"}
          </div>
          <h1 className={s.heroTitle}>
            {live?.live && live.meeting
              ? `${live.meeting} · ${live.session}`
              : "Live timing, replay and telemetry"}
          </h1>
          <p className={s.heroBlurb}>
            Sub-second live timing straight from the Formula 1 feed, every archived session back to
            2018, and per-lap telemetry with two-driver comparison. Self-hosted, no account, no
            tracking.
          </p>
        </div>

        {upcoming && !live?.live && (
          <div className={s.heroSide}>
            <div className={s.heroStat}>{remaining.value}</div>
            <div className={s.heroStatLabel}>{remaining.label}</div>
            <div className={s.heroStatLabel} style={{ color: "var(--text-dim)", marginTop: 10 }}>
              {flag(upcoming.country)} {upcoming.name}
            </div>
          </div>
        )}
      </div>

      <div className={s.cards}>
        {cards.map((card) => (
          <Link key={card.name} to={card.to} className={s.card}>
            <span className={s.cardAccent} style={{ background: card.accent }} />
            <span className={s.cardKicker}>{card.kicker}</span>
            <span className={s.cardName}>{card.name}</span>
            <span className={s.cardBody}>{card.body}</span>
            <span className={`${s.cardBadge} ${card.badge.className}`}>{card.badge.text}</span>
          </Link>
        ))}
      </div>
    </>
  );
}
