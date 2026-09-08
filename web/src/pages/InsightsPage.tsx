import { useEffect, useState } from "react";
import { TyreLoader } from "@/components/atoms/TyreLoader";
import s from "./InsightsPage.module.css";

const API_URL = import.meta.env["VITE_API_URL"] ?? "http://localhost:4000";

interface Availability { relational: boolean; telemetry: boolean }
interface CircuitRecord {
  year: number; meeting: string; sessionName: string;
  code: string; teamName: string; lapSeconds: number; lap: number;
}
interface DriverSeasonSummary {
  year: number; sessions: number; laps: number;
  bestLapSeconds: number | null; medianLapSeconds: number | null;
}
interface StrategyRow { year: number; meeting: string; code: string; stops: number; compounds: string }
interface SpeedRow { driver: string; session: string; lap: number; topSpeed: number }

type Tab = "circuit" | "driver" | "strategy" | "speed";

const TABS: { id: Tab; label: string; store: "relational" | "telemetry" }[] = [
  { id: "circuit", label: "Circuit records", store: "relational" },
  { id: "driver", label: "Driver history", store: "relational" },
  { id: "strategy", label: "Strategies", store: "relational" },
  { id: "speed", label: "Top speeds", store: "telemetry" },
];

function lapTime(seconds: number): string {
  const minutes = Math.floor(seconds / 60);
  return `${minutes}:${(seconds - minutes * 60).toFixed(3).padStart(6, "0")}`;
}

/**
 * The questions that only exist because the indexes exist.
 *
 * Every one of these spans sessions, which is precisely what the file archive
 * cannot answer without opening every file in it. With no databases configured
 * the page says so plainly rather than showing an empty table that looks like a
 * season with no races in it.
 */
export function InsightsPage() {
  const [stores, setStores] = useState<Availability | null>(null);
  const [tab, setTab] = useState<Tab>("circuit");

  useEffect(() => {
    let cancelled = false;
    void fetch(`${API_URL}/api/insights`)
      .then((r) => r.json() as Promise<Availability>)
      .then((d) => { if (!cancelled) setStores(d); })
      .catch(() => { if (!cancelled) setStores({ relational: false, telemetry: false }); });
    return () => { cancelled = true; };
  }, []);

  if (!stores) return <TyreLoader block size="lg" label="Checking the indexes" />;

  const anyStore = stores.relational || stores.telemetry;

  return (
    <div className={s.page}>
      <div className={s.head}>
        <div>
          <h1 className={s.title}>Insights</h1>
          <p className={s.subtitle}>
            Questions that span sessions. Each one needs an index — answering them from
            the archive alone would mean opening every file in it.
          </p>
        </div>
        <div className={s.stores}>
          <span className={`${s.store} ${stores.relational ? s.storeUp : s.storeDown}`}>
            PostgreSQL {stores.relational ? "up" : "off"}
          </span>
          <span className={`${s.store} ${stores.telemetry ? s.storeUp : s.storeDown}`}>
            InfluxDB {stores.telemetry ? "up" : "off"}
          </span>
        </div>
      </div>

      {!anyStore ? (
        <div className={s.offline}>
          <strong>No index is configured, and that is a supported setup.</strong>
          <br /><br />
          Everything else in Apex works without one — the archive on disk is the source of
          truth, and reading a driver&rsquo;s whole race telemetry from it takes about eleven
          milliseconds. These particular questions are the exception, because they span many
          sessions at once.
          <br /><br />
          To enable them, start the stores with{" "}
          <code>docker compose --profile stores up -d</code> and set{" "}
          <code>POSTGRES_URL</code> and <code>INFLUX_URL</code>, then run{" "}
          <code>POST /api/storage/backfill</code> once.
        </div>
      ) : (
        <>
          <div className={s.tabs} role="tablist">
            {TABS.map((t) => (
              <button
                key={t.id}
                type="button"
                role="tab"
                aria-selected={tab === t.id}
                className={`${s.tab} ${tab === t.id ? s.tabActive : ""}`}
                // A tab whose store is down would only ever show an empty
                // table; saying why is better than letting it look like no data.
                disabled={!stores[t.store]}
                title={stores[t.store] ? undefined : "Needs an index that is not running"}
                onClick={() => setTab(t.id)}
              >
                {t.label}
              </button>
            ))}
          </div>

          {tab === "circuit" && <CircuitRecords />}
          {tab === "driver" && <DriverHistory />}
          {tab === "strategy" && <Strategies />}
          {tab === "speed" && <TopSpeeds />}
        </>
      )}
    </div>
  );
}

/** Shared fetch-into-state, so each panel is only its query and its table. */
function useInsight<T>(path: string): { rows: T[]; loading: boolean } {
  const [state, setState] = useState<{ path: string; rows: T[] } | null>(null);

  useEffect(() => {
    let cancelled = false;
    void fetch(`${API_URL}${path}`)
      .then((r) => (r.ok ? (r.json() as Promise<T[]>) : []))
      .then((rows) => { if (!cancelled) setState({ path, rows }); })
      .catch(() => { if (!cancelled) setState({ path, rows: [] }); });
    return () => { cancelled = true; };
  }, [path]);

  // Derived rather than cleared in an effect: a stale result under a new query
  // would render the previous circuit's records under this one's heading.
  return state?.path === path
    ? { rows: state.rows, loading: false }
    : { rows: [], loading: true };
}

function Panel<T>({ rows, loading, empty, children }: {
  rows: T[]; loading: boolean; empty: string; children: React.ReactNode;
}) {
  if (loading) return <div className={s.panel}><TyreLoader block label="Querying" /></div>;
  if (rows.length === 0) return <div className={s.panel}><div className={s.empty}>{empty}</div></div>;
  return <div className={s.panel}>{children}</div>;
}

function CircuitRecords() {
  const [slug, setSlug] = useState("italian-grand-prix");
  const { rows, loading } = useInsight<CircuitRecord>(`/api/insights/circuit/${slug}?limit=25`);

  return (
    <>
      <div className={s.controls}>
        <span className={s.label}>Circuit</span>
        <input
          className={s.input}
          value={slug}
          aria-label="Circuit slug"
          onChange={(e) => setSlug(e.target.value.trim().toLowerCase())}
        />
        <span className={s.label}>fastest laps, every indexed season</span>
      </div>

      <Panel rows={rows} loading={loading} empty="Nothing indexed for that circuit yet.">
        <table className={s.table}>
          <thead>
            <tr>
              <th /><th>Year</th><th>Session</th><th>Driver</th><th>Team</th>
              <th className={s.num}>Lap time</th><th className={s.num}>Lap</th>
            </tr>
          </thead>
          <tbody>
            {rows.map((r, i) => (
              <tr key={`${r.year}-${r.code}-${r.lap}`}>
                <td className={s.rank}>{i + 1}</td>
                <td className={s.num}>{r.year}</td>
                <td>{r.sessionName}</td>
                <td className={s.code}>{r.code}</td>
                <td>{r.teamName}</td>
                <td className={s.num}>{lapTime(r.lapSeconds)}</td>
                <td className={s.num}>{r.lap}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </Panel>
    </>
  );
}

function DriverHistory() {
  const [code, setCode] = useState("VER");
  const { rows, loading } = useInsight<DriverSeasonSummary>(`/api/insights/driver/${code}`);

  return (
    <>
      <div className={s.controls}>
        <span className={s.label}>Driver</span>
        <input
          className={s.input}
          value={code}
          maxLength={3}
          aria-label="Driver code"
          onChange={(e) => setCode(e.target.value.toUpperCase())}
        />
        <span className={s.label}>pace by season</span>
      </div>

      <Panel rows={rows} loading={loading} empty="No indexed laps for that driver.">
        <table className={s.table}>
          <thead>
            <tr>
              <th>Season</th><th className={s.num}>Sessions</th><th className={s.num}>Laps</th>
              <th className={s.num}>Best lap</th><th className={s.num}>Median lap</th>
            </tr>
          </thead>
          <tbody>
            {rows.map((r) => (
              <tr key={r.year}>
                <td className={s.num}>{r.year}</td>
                <td className={s.num}>{r.sessions}</td>
                <td className={s.num}>{r.laps}</td>
                <td className={s.num}>{r.bestLapSeconds ? lapTime(r.bestLapSeconds) : "—"}</td>
                {/* Median, not mean: one safety-car lap moves a mean by seconds
                    and a median not at all. */}
                <td className={s.num}>{r.medianLapSeconds ? lapTime(r.medianLapSeconds) : "—"}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </Panel>
    </>
  );
}

function Strategies() {
  const [year, setYear] = useState(2026);
  const [stops, setStops] = useState<string>("");
  const query = `/api/insights/strategies/${year}${stops ? `?stops=${stops}` : ""}`;
  const { rows, loading } = useInsight<StrategyRow>(query);

  return (
    <>
      <div className={s.controls}>
        <span className={s.label}>Season</span>
        <select className={s.select} value={year} aria-label="Season"
                onChange={(e) => setYear(Number(e.target.value))}>
          {[2026, 2025, 2024, 2023, 2022].map((y) => <option key={y} value={y}>{y}</option>)}
        </select>
        <span className={s.label}>Stops</span>
        <select className={s.select} value={stops} aria-label="Number of stops"
                onChange={(e) => setStops(e.target.value)}>
          <option value="">any</option>
          {[0, 1, 2, 3].map((n) => <option key={n} value={n}>{n}</option>)}
        </select>
      </div>

      <Panel rows={rows} loading={loading} empty="No indexed races for that season.">
        <table className={s.table}>
          <thead>
            <tr><th>Grand Prix</th><th>Driver</th><th className={s.num}>Stops</th><th>Compounds</th></tr>
          </thead>
          <tbody>
            {rows.map((r) => (
              <tr key={`${r.meeting}-${r.code}`}>
                <td>{r.meeting}</td>
                <td className={s.code}>{r.code}</td>
                <td className={s.num}>{r.stops}</td>
                <td className={s.compounds}>{r.compounds}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </Panel>
    </>
  );
}

function TopSpeeds() {
  const [above, setAbove] = useState(320);
  const { rows, loading } = useInsight<SpeedRow>(`/api/insights/speed?above=${above}&limit=40`);

  return (
    <>
      <div className={s.controls}>
        <span className={s.label}>Above</span>
        <select className={s.select} value={above} aria-label="Speed threshold"
                onChange={(e) => setAbove(Number(e.target.value))}>
          {[300, 320, 330, 340, 350].map((v) => <option key={v} value={v}>{v} km/h</option>)}
        </select>
        <span className={s.label}>peak speed per lap, from the telemetry index</span>
      </div>

      <Panel rows={rows} loading={loading} empty="No telemetry indexed above that speed.">
        <table className={s.table}>
          <thead>
            <tr>
              <th /><th>Driver</th><th className={s.num}>Top speed</th>
              <th className={s.num}>Lap</th><th>Session</th>
            </tr>
          </thead>
          <tbody>
            {rows.map((r, i) => (
              <tr key={`${r.session}-${r.driver}-${r.lap}`}>
                <td className={s.rank}>{i + 1}</td>
                <td className={s.code}>{r.driver}</td>
                <td className={s.num}>{r.topSpeed} km/h</td>
                <td className={s.num}>{r.lap}</td>
                <td className={s.compounds}>{r.session}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </Panel>
    </>
  );
}
