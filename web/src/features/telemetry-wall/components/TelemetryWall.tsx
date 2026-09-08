import { useMemo } from "react";
import { useDriverTrace } from "@/features/live/hooks/useLive";
import { liveStore } from "@/features/live/store/liveStore";
import type {
  CarChannels, Driver, OvertakeAid, SessionSnapshot, TimingRow, TyreCompound,
} from "@/features/live/model/types";
import {
  estimateDegradation, estimateTyre, parseGapSeconds, PIT_LOSS_SECONDS,
  type UndercutThreat,
} from "../lib/estimates";
import s from "./TelemetryWall.module.css";

const TYRE_TOKEN: Record<TyreCompound, string> = {
  S: "soft", M: "medium", H: "hard", I: "inter", W: "wet",
};

interface TelemetryWallProps {
  session: SessionSnapshot;
  selected: string | null;
  onSelect(tla: string): void;
}

/**
 * The telemetry view: pick a driver, see what their car is actually doing.
 *
 * What is shown is bounded by what F1 publishes. Tyre temperatures, brake
 * temperatures, pressures and fuel load are team-internal and never public, so
 * they are absent rather than invented (DECISIONS D-008). What replaces them is
 * the channel data that IS published, plus clearly-labelled estimates.
 */
export function TelemetryWall({ session, selected, onSelect }: TelemetryWallProps) {
  const focus = selected ?? session.timing[0]?.tla ?? null;
  const row = session.timing.find((r) => r.tla === focus);
  const driver = focus ? session.drivers[focus] : undefined;

  const { trace, laps } = useDriverTrace(focus);

  if (session.timing.length === 0) {
    return <div className={s.empty} style={{ padding: 40, textAlign: "center" }}>
      Waiting for timing data.
    </div>;
  }

  return (
    <>
      <DriverChips
        timing={session.timing}
        drivers={session.drivers}
        selected={focus}
        onSelect={onSelect}
      />

      <div className={s.wall}>
        <div className={s.col}>
          <DriverHead row={row} driver={driver} />
          <TyreStatus row={row} laps={laps} />
        </div>

        <div className={s.col}>
          <CarSystems channels={focus ? session.channels[focus] : undefined} aid={session.overtakeAid} />
          <Traces trace={trace} colour={driver?.color ?? "var(--accent)"} />
        </div>

        <div className={s.col}>
          <LapPerformance row={row} trace={trace} laps={laps} />
          <UndercutMonitor session={session} focus={focus} />
        </div>
      </div>
    </>
  );
}

/* ------------------------------------------------------------- IMPL-18 */

function DriverChips({ timing, drivers, selected, onSelect }: {
  timing: TimingRow[];
  drivers: Record<string, Driver>;
  selected: string | null;
  onSelect(tla: string): void;
}) {
  return (
    <div className={s.chips} role="group" aria-label="Choose a driver">
      {timing.map((row) => (
        <button
          key={row.tla}
          type="button"
          className={`${s.chip} ${row.tla === selected ? s.chipActive : ""}`}
          style={{ ["--team" as string]: drivers[row.tla]?.color ?? "var(--border-strong)" }}
          aria-pressed={row.tla === selected}
          onClick={() => onSelect(row.tla)}
        >
          <span className={s.chipPos}>{row.position}</span>
          {row.tla}
        </button>
      ))}
    </div>
  );
}

/* ------------------------------------------------------------- IMPL-19 */

function DriverHead({ row, driver }: { row: TimingRow | undefined; driver: Driver | undefined }) {
  if (!row) return null;

  return (
    <div
      className={`${s.panel} ${s.head}`}
      style={{ ["--team" as string]: driver?.color ?? "var(--accent)" }}
    >
      <span className={s.headGhost} aria-hidden="true">{driver?.number ?? ""}</span>
      <div className={s.headTla}>{row.tla}</div>
      <div className={s.headName}>
        {driver ? `${driver.firstName} ${driver.lastName}` : ""}
      </div>
      <div className={s.headTeam}>P{row.position} · {driver?.teamName ?? ""}</div>
    </div>
  );
}

function TyreStatus({ row, laps }: { row: TimingRow | undefined; laps: { lap: number; seconds: number }[] }) {
  const estimate = row ? estimateTyre(row.tyre, row.tyreAge) : null;
  const degradation = estimateDegradation(laps);

  if (!row || !estimate) return null;

  return (
    <div className={s.panel}>
      <div className={s.title}>
        <span>Tyre</span>
        <span className={s.estimate}>Estimated</span>
      </div>

      <div className={s.tyreRow}>
        <WearRing wear={estimate.wear} compound={row.tyre} />

        <div className={s.tyreFacts}>
          <div>
            <div className={s.factLabel}>Age</div>
            <div className={s.factValue}>{row.tyreAge}L</div>
          </div>
          <div>
            <div className={s.factLabel}>Condition</div>
            <div className={`${s.factValue} ${s[estimate.condition]}`}>
              {estimate.condition.toUpperCase()}
            </div>
          </div>
          <div>
            <div className={s.factLabel}>Est. left</div>
            <div className={s.factValue}>{estimate.remaining}L</div>
          </div>
          <div>
            <div className={s.factLabel}>Deg</div>
            <div className={s.factValue}>
              {degradation === null ? "—" : `${degradation >= 0 ? "+" : ""}${degradation.toFixed(2)}s`}
            </div>
          </div>
        </div>
      </div>

      <div className={s.empty} style={{ paddingBottom: 0 }}>
        Wear and remaining life are inferred from tyre age against a nominal
        life of {estimate.nominalLife} laps for this compound. F1 does not
        publish tyre data.
      </div>
    </div>
  );
}

function WearRing({ wear, compound }: { wear: number; compound: TyreCompound }) {
  const R = 30;
  const circumference = 2 * Math.PI * R;

  return (
    <svg className={s.ring} width="76" height="76" viewBox="0 0 76 76" role="img"
         aria-label={`Estimated tyre wear ${wear} percent`}>
      <circle cx="38" cy="38" r={R} fill="none" stroke="var(--surface-4)" strokeWidth="7" />
      <circle
        cx="38" cy="38" r={R} fill="none"
        stroke={`var(--tyre-${TYRE_TOKEN[compound]})`}
        strokeWidth="7"
        strokeDasharray={`${(wear / 100) * circumference} ${circumference}`}
        // Start at twelve o'clock rather than three, which is where a gauge
        // reads from.
        transform="rotate(-90 38 38)"
      />
      <text x="38" y="36" textAnchor="middle" fontFamily="var(--font-mono)"
            fontSize="15" fill="var(--text)">{compound}</text>
      <text x="38" y="50" textAnchor="middle" fontFamily="var(--font-mono)"
            fontSize="10" fill="var(--text-faintest)">{wear}%</text>
    </svg>
  );
}

/* ------------------------------------------------------------- IMPL-21 */

function CarSystems({ channels, aid }: { channels: CarChannels | undefined; aid: OvertakeAid }) {
  if (!channels) {
    return (
      <div className={s.panel}>
        <div className={s.title}><span>Car systems</span></div>
        <div className={s.empty}>
          No car data for this driver yet. Channels arrive only while the car is
          on track.
        </div>
      </div>
    );
  }

  return (
    <div className={s.panel}>
      <div className={s.title}><span>Car systems · live channels</span></div>

      <div className={s.gearRow}>
        <div>
          <span className={s.bigValue}>{channels.speed}</span>{" "}
          <span className={s.bigUnit}>km/h</span>
        </div>
        <div>
          <span className={s.bigValue}>{channels.gear || "N"}</span>{" "}
          <span className={s.bigUnit}>gear</span>
        </div>
        {/* Whether DRS exists at all is an ERA question, not a value question.
            In a DRS season 0 means closed; from 2026 channel 45 is absent
            entirely and every reading is 0, which would otherwise render as a
            permanently closed wing that never existed. */}
        {aid === "drs" && (
          <div>
            <span className={`${s.bigUnit} ${channels.drs > 9 ? s.drsOn : s.drsOff}`}>
              DRS {channels.drs > 9 ? "OPEN" : "CLOSED"}
            </span>
          </div>
        )}
      </div>

      <div className={s.bars}>
        <Bar label="Throttle" value={channels.throttle} max={100} unit="%" colour="var(--green)" />
        <Bar label="Brake" value={channels.brake} max={100} unit="%" colour="var(--red-bright)" />
        <Bar label="RPM" value={channels.rpm} max={15000} unit="" colour="var(--blue)" />
      </div>
    </div>
  );
}

function Bar({ label, value, max, unit, colour }: {
  label: string; value: number; max: number; unit: string; colour: string;
}) {
  return (
    <div className={s.bar}>
      <span className={s.factLabel}>{label}</span>
      <div className={s.barTrack}>
        <div
          className={s.barFill}
          style={{ width: `${Math.min(100, (value / max) * 100)}%`, background: colour }}
        />
      </div>
      <span className={s.barValue}>{value}{unit}</span>
    </div>
  );
}

/* ------------------------------------------------------------- IMPL-22 */

function Traces({ trace, colour }: { trace: CarChannels[]; colour: string }) {
  if (trace.length < 3) {
    return (
      <div className={s.panel}>
        <div className={s.title}><span>Traces</span></div>
        <div className={s.empty}>
          Traces build from the live channel stream — a few seconds of running
          are needed before there is a line.
        </div>
      </div>
    );
  }

  return (
    <div className={s.panel}>
      <div className={s.title}><span>Traces · last {trace.length} samples</span></div>
      <Trace label="Speed" unit="km/h" values={trace.map((c) => c.speed)} colour={colour} />
      <Trace label="Throttle" unit="%" values={trace.map((c) => c.throttle)} colour="var(--green)" max={100} />
      <Trace label="Brake" unit="%" values={trace.map((c) => c.brake)} colour="var(--red-bright)" max={100} />
    </div>
  );
}

function Trace({ label, unit, values, colour, max }: {
  label: string; unit: string; values: number[]; colour: string; max?: number | undefined;
}) {
  const W = 320;
  const H = 46;

  const high = max ?? Math.max(1, ...values);
  const low = max ? 0 : Math.min(...values);
  const span = Math.max(1, high - low);

  const d = values
    .map((v, i) => {
      const x = (i / Math.max(1, values.length - 1)) * W;
      const y = H - ((v - low) / span) * H;
      return `${i === 0 ? "M" : "L"}${x.toFixed(1)} ${y.toFixed(1)}`;
    })
    .join(" ");

  return (
    <div style={{ marginTop: 10 }}>
      <div className={s.traceHead}>
        <span>{label}</span>
        <span className={s.traceHigh}>
          {Math.round(low)}–{Math.round(high)} {unit}
        </span>
      </div>
      <svg className={s.trace} viewBox={`0 0 ${W} ${H}`} preserveAspectRatio="none"
           role="img" aria-label={`${label} trace`}>
        <path d={d} fill="none" stroke={colour} strokeWidth="1.4" vectorEffect="non-scaling-stroke" />
      </svg>
    </div>
  );
}

function LapPerformance({ row, trace, laps }: {
  row: TimingRow | undefined;
  trace: CarChannels[];
  laps: { lap: number; seconds: number }[];
}) {
  const topSpeed = trace.length > 0 ? Math.max(...trace.map((c) => c.speed)) : null;

  return (
    <div className={s.panel}>
      <div className={s.title}><span>Lap performance</span></div>

      <div className={s.tyreFacts}>
        <div>
          <div className={s.factLabel}>Last lap</div>
          <div className={s.factValue}>{row?.lastLap || "—"}</div>
        </div>
        <div>
          <div className={s.factLabel}>Best lap</div>
          <div className={`${s.factValue} ${row?.bestIsOverall ? s.fresh : ""}`}>
            {row?.bestLap || "—"}
          </div>
        </div>
        <div>
          <div className={s.factLabel}>Top speed</div>
          <div className={s.factValue}>{topSpeed === null ? "—" : `${topSpeed} km/h`}</div>
        </div>
        <div>
          <div className={s.factLabel}>Laps seen</div>
          <div className={s.factValue}>{laps.length}</div>
        </div>
      </div>

      {topSpeed !== null && (
        <div className={s.empty} style={{ paddingBottom: 0 }}>
          Top speed is the highest value in the samples held for this driver, not
          a session record.
        </div>
      )}
    </div>
  );
}

/* ------------------------------------------------------------- IMPL-20 */

function UndercutMonitor({ session, focus }: { session: SessionSnapshot; focus: string | null }) {
  const threats = useMemo(() => {
    const history = liveStore.getHistory();

    // Degradation is filled in here rather than inside findThreats: the
    // threat search is pure, and the lap history is accumulated state.
    return findThreats(session, focus).map((threat) => ({
      ...threat,
      degradation: estimateDegradation(history.lapsFor(threat.tla)),
    }));
  }, [session, focus]);

  return (
    <div className={s.panel}>
      <div className={s.title}>
        <span>Undercut threats</span>
        <span className={s.estimate}>Derived</span>
      </div>

      {threats.length === 0 ? (
        <div className={s.empty}>
          Nobody is close enough behind for a stop to change position.
        </div>
      ) : (
        threats.map((threat) => (
          <div
            key={threat.tla}
            className={s.threat}
            style={{ ["--team" as string]: session.drivers[threat.tla]?.color ?? "var(--border-strong)" }}
          >
            <span className={s.threatTla}>{threat.tla}</span>
            <span className={s.threatMeta}>
              {threat.compound} · {threat.age}L
              {threat.degradation !== null && ` · ${threat.degradation >= 0 ? "+" : ""}${threat.degradation.toFixed(2)}s/lap`}
            </span>
            <span className={`${s.threatGap} ${threat.gap < 2 ? s.threatDanger : ""}`}>
              {threat.gap.toFixed(1)}s
            </span>
          </div>
        ))
      )}

      <div className={s.empty} style={{ paddingBottom: 0 }}>
        Cars within {PIT_LOSS_SECONDS}s behind, whose stop could gain track
        position. Degradation is inferred from lap-time trend.
      </div>
    </div>
  );
}

function findThreats(session: SessionSnapshot, focus: string | null): UndercutThreat[] {
  if (!focus) return [];

  const index = session.timing.findIndex((r) => r.tla === focus);
  if (index < 0) return [];

  const threats: UndercutThreat[] = [];
  let cumulative = 0;

  // Walk BACKWARDS through the field, adding intervals. Interval is to the car
  // ahead, so the gap to our driver is the running sum — reading each car's own
  // interval would say every car behind is a fraction of a second away.
  for (let i = index + 1; i < session.timing.length; i++) {
    const row = session.timing[i]!;
    const interval = parseGapSeconds(row.interval);

    // No interval yet, or a lapped car — neither is an undercut threat, and
    // neither can be summed. Stop rather than guess: the cars further back are
    // measured relative to this one.
    if (interval === null) break;

    cumulative += interval;
    if (cumulative > PIT_LOSS_SECONDS) break;

    threats.push({
      tla: row.tla,
      gap: cumulative,
      compound: row.tyre,
      age: row.tyreAge,
      degradation: null,
      inRange: true,
    });
  }

  return threats;
}
