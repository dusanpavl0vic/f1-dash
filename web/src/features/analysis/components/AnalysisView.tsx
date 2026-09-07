import { useEffect, useMemo, useState } from "react";
import { TyreLoader } from "@/components/atoms/TyreLoader";
import { useAnalysis, useSectorComparison, useTelemetry } from "../lib/useAnalysis";
import { bestLap, driverColor, formatTime } from "../model/types";
import { LapChart } from "./LapChart";
import { PositionChart } from "./PositionChart";
import { SectorCompare } from "./SectorCompare";
import { StrategyTimeline } from "./StrategyTimeline";
import { TelemetryTrace } from "./TelemetryTrace";
import s from "./Analysis.module.css";

interface PanelProps {
  title: string;
  meta?: string | undefined;
  actions?: React.ReactNode | undefined;
  children: React.ReactNode;
}

function Panel({ title, meta, actions, children }: PanelProps) {
  return (
    <section className={s.panel}>
      <div className={s.header}>
        <div className={s.marker} />
        <h2 className={s.title}>{title}</h2>
        <div className={s.rule} />
        {meta && <div className={s.meta}>{meta}</div>}
        {actions}
      </div>
      {children}
    </section>
  );
}

export function AnalysisView({ active }: { active: boolean }) {
  const { analysis, loading, error, refresh } = useAnalysis(active);

  const [chosenPrimary, setPrimary] = useState<string | null>(null);
  const [chosenSecondary, setSecondary] = useState<string | null>(null);
  const [chosenLap, setLap] = useState<number | null>(null);

  const drivers = useMemo(() => analysis?.drivers ?? [], [analysis]);

  // Defaults are DERIVED, not set in an effect: the view is useful before
  // anything is clicked, without a cascading render on mount.
  const primary = chosenPrimary ?? drivers[0]?.tla ?? null;
  const secondary = chosenSecondary ?? drivers[1]?.tla ?? null;

  const driverA = drivers.find((d) => d.tla === primary);
  const driverB = drivers.find((d) => d.tla === secondary);

  const comparison = useSectorComparison(primary, secondary);
  const telemetry = useTelemetry(driverA?.racingNumber ?? null);

  // A driver's fastest lap is the one worth opening on.
  const defaultLap = useMemo(() => {
    if (telemetry.laps.length === 0) return null;

    const fastest = driverA?.laps
      .filter((l) => telemetry.laps.includes(l.lap) && l.timeSeconds)
      .sort((x, y) => (x.timeSeconds ?? 0) - (y.timeSeconds ?? 0))[0];

    return fastest?.lap ?? telemetry.laps.at(-1) ?? null;
  }, [telemetry.laps, driverA]);

  const lap = chosenLap ?? defaultLap;
  const loadTelemetry = telemetry.load;

  // Fetching is a side effect on an external system, which is what an effect is
  // for. No state is set here.
  useEffect(() => {
    if (lap !== null) loadTelemetry(lap);
  }, [lap, loadTelemetry]);

  if (!analysis) {
    return error
      ? <div className={s.empty}>{error.toUpperCase()}</div>
      : <TyreLoader block size="lg" label="Building session analysis" />;
  }

  const { meta } = analysis;
  const sessionLabel =
    `${meta.meeting} ${meta.sessionName} ${meta.year ?? ""} · ${meta.circuit}`.trim();

  return (
    <div className={s.view} data-analysis-report>
      <Panel
        title="Session analysis"
        meta={`${sessionLabel} · ${drivers.length} drivers${loading ? " · updating" : ""}`}
        actions={
          <div className={s.actions}>
            <button type="button" className={s.action} onClick={refresh}>REFRESH</button>
            {/* The browser's own print-to-PDF: no server-side rendering
                dependency, and the user picks the destination. */}
            <button type="button" className={s.action} onClick={() => window.print()}>
              EXPORT PDF
            </button>
          </div>
        }
      >
        <div className={s.chips}>
          {drivers.map((d) => (
            <button
              key={d.racingNumber}
              type="button"
              className={`${s.chip} ${d.tla === primary ? s.chipActive : d.tla === secondary ? s.chipB : ""}`}
              onClick={() => {
                // First click sets the primary driver; clicking the primary
                // again promotes it to the comparison slot.
                if (d.tla === primary) setSecondary(d.tla);
                else if (d.tla === secondary) setSecondary(null);
                else setPrimary(d.tla);
                setLap(null);   // the new driver's own fastest lap is derived
              }}
              title={`Best lap ${formatTime(bestLap(d))}`}
            >
              <span className={s.chipBar} style={{ background: driverColor(d) }} />
              {d.tla}
            </button>
          ))}
        </div>
        <div className={s.legend}>
          <div className={s.legendItem}>
            Click a driver to select · click it again to add it as the comparison
          </div>
        </div>
      </Panel>

      {driverA && (
        <Panel
          title={`Lap times — ${driverA.tla}`}
          meta={`${driverA.teamName} · best ${formatTime(bestLap(driverA))}`}
        >
          <LapChart driver={driverA} />
        </Panel>
      )}

      <Panel title="Strategy" meta={`${meta.totalLaps} laps`}>
        <StrategyTimeline drivers={drivers} totalLaps={meta.totalLaps} />
      </Panel>

      <Panel
        title="Position progression"
        meta={primary ? `Highlighting ${[primary, secondary].filter(Boolean).join(", ")}` : undefined}
      >
        <PositionChart
          drivers={drivers}
          totalLaps={meta.totalLaps}
          highlighted={[primary, secondary].filter((x): x is string => x !== null)}
        />
      </Panel>

      <Panel
        title="Sector comparison"
        meta={primary && secondary ? `${primary} vs ${secondary}` : "Pick a second driver"}
      >
        {comparison
          ? <SectorCompare comparison={comparison} driverA={driverA} driverB={driverB} />
          : <div className={s.empty}>SELECT TWO DIFFERENT DRIVERS TO COMPARE</div>}
      </Panel>

      <Panel
        title={`Telemetry — ${driverA?.tla ?? ""}`}
        meta={meta.hasTelemetry ? `${telemetry.laps.length} laps recorded` : undefined}
        actions={
          telemetry.enabled && telemetry.laps.length > 0 ? (
            <select
              className={s.action}
              value={lap ?? ""}
              onChange={(e) => {
                const chosen = Number(e.target.value);
                setLap(chosen);
                telemetry.load(chosen);
              }}
              aria-label="Lap"
            >
              {telemetry.laps.map((l) => <option key={l} value={l}>LAP {l}</option>)}
            </select>
          ) : undefined
        }
      >
        {!telemetry.enabled ? (
          <div className={s.empty}>
            TELEMETRY IS RECORDED FOR 2026 SESSIONS ONWARD
          </div>
        ) : telemetry.trace ? (
          <TelemetryTrace trace={telemetry.trace} color={driverA ? driverColor(driverA) : "var(--teal)"} />
        ) : (
          <TyreLoader block label="Loading telemetry" />
        )}
      </Panel>
    </div>
  );
}
