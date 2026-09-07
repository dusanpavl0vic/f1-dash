import { useEffect, useRef } from "react";
import { TyreLoader } from "@/components/atoms/TyreLoader";
import { TRACK_STATE } from "@/features/live/model/constants";
import type { CarPosition, Driver, TimingRow, TrackState } from "@/features/live/model/types";
import { lerp, STALE_MS, toScreen } from "../lib/toScreen";
import { useTrackGeometry } from "../lib/useTrackGeometry";
import s from "./TrackMap.module.css";

/** Track units are roughly 1/10 m, so these are metres, not pixels. */
const TRACK_STROKE = 260;
const MARSHAL_STROKE = 340;
const DOT_RADIUS = 220;
const DOT_RADIUS_SELECTED = 300;
const LABEL_SIZE = 420;

interface TrackMapProps {
  circuitKey: number | null;
  year: number | null;
  circuitName: string;
  positions: CarPosition[];
  drivers: Record<string, Driver>;
  timing: TimingRow[];
  trackState: TrackState;
  selected: string | null;
}

interface Sample { x: number; y: number; t: number }

export function TrackMap({
  circuitKey, year, circuitName, positions, drivers, timing, trackState, selected,
}: TrackMapProps) {
  const geometry = useTrackGeometry(circuitKey, year);

  // Position updates arrive at ~4 Hz. Rendering them raw looks like stop-motion,
  // so dots are interpolated on every animation frame — and written straight to
  // DOM refs, never through React state. Twenty cars at 60fps through setState
  // would melt the main thread.
  const groupRefs = useRef(new Map<string, SVGGElement | null>());
  const previous = useRef(new Map<string, Sample>());
  const target = useRef(new Map<string, Sample>());

  const transform = { rotation: geometry?.rotation ?? 0, flipY: true };

  useEffect(() => {
    if (!geometry) return;
    const now = performance.now();

    for (const p of positions) {
      const [x, y] = toScreen(p.x, p.y, { rotation: geometry.rotation, flipY: true });
      const current = target.current.get(p.tla);

      if (current && current.x === x && current.y === y) continue;

      previous.current.set(p.tla, current ?? { x, y, t: now });
      target.current.set(p.tla, { x, y, t: now });
    }
  }, [positions, geometry]);

  useEffect(() => {
    if (!geometry) return;
    let frame = 0;

    const tick = () => {
      const now = performance.now();

      for (const [tla, to] of target.current) {
        const node = groupRefs.current.get(tla);
        if (!node) continue;

        const from = previous.current.get(tla) ?? to;
        const span = to.t - from.t;

        // A gap longer than STALE_MS means the driver has pitted or the feed
        // stalled. Continuing to interpolate slides the car across the infield.
        const alpha = span > 0 && span < STALE_MS
          ? Math.min(1, (now - to.t) / span + 1)
          : 1;

        const x = alpha >= 1 ? to.x : lerp(from.x, to.x, alpha);
        const y = alpha >= 1 ? to.y : lerp(from.y, to.y, alpha);
        node.setAttribute("transform", `translate(${x.toFixed(0)}, ${y.toFixed(0)})`);
      }

      frame = requestAnimationFrame(tick);
    };

    frame = requestAnimationFrame(tick);
    return () => cancelAnimationFrame(frame);
  }, [geometry]);

  if (!geometry) {
    return (
      <section className={s.panel}>
        <div className={s.header}>
          <div className={s.marker} />
          <h2 className={s.title}>Track map</h2>
          <div className={s.rule} />
        </div>
        {circuitKey === null ? (
          <div className={s.empty}>NO SESSION</div>
        ) : (
          <TyreLoader block label="Loading circuit" detail={circuitName || undefined} />
        )}
      </section>
    );
  }

  const vb = geometry.viewBox;
  const marshalColour = TRACK_STATE[trackState].marshal;

  // Retired cars are removed entirely, or a ghost dot sits on the map for the
  // rest of the session.
  const retired = new Set(timing.filter((r) => r.status === "retired").map((r) => r.tla));
  const inPit = new Set(timing.filter((r) => r.status === "pit").map((r) => r.tla));
  const visible = positions.filter((p) => !retired.has(p.tla));

  return (
    <section className={s.panel}>
      <div className={s.header}>
        <div className={s.marker} />
        <h2 className={s.title}>Track map</h2>
        <div className={s.rule} />
        <div className={s.circuit}>{circuitName || geometry.circuitName}</div>
      </div>

      <svg
        className={s.svg}
        viewBox={`${vb.x} ${vb.y} ${vb.width} ${vb.height}`}
        preserveAspectRatio="xMidYMid meet"
        role="img"
        aria-label={`Track map of ${geometry.circuitName}`}
      >
        {/* Marshal sectors sit UNDER the track surface so a flagged zone reads
            as a glow along the track rather than a stripe on top of it. */}
        {geometry.marshalSectors.map((sector) => (
          <path
            key={sector.number}
            d={sector.path}
            fill="none"
            stroke={marshalColour}
            strokeWidth={MARSHAL_STROKE}
            strokeLinecap="round"
            opacity={0.55}
          />
        ))}

        <path
          d={geometry.path}
          fill="none"
          stroke="var(--track-surface)"
          strokeWidth={TRACK_STROKE}
          strokeLinejoin="round"
          strokeLinecap="round"
        />

        <circle
          cx={geometry.startFinish.x}
          cy={geometry.startFinish.y}
          r={140}
          fill="var(--track-startfinish)"
        />

        {visible.map((p) => {
          const driver = drivers[p.tla];
          const isSelected = selected === p.tla;
          const [x, y] = toScreen(p.x, p.y, transform);

          return (
            <g
              key={p.tla}
              ref={(node) => { groupRefs.current.set(p.tla, node); }}
              transform={`translate(${x.toFixed(0)}, ${y.toFixed(0)})`}
              opacity={inPit.has(p.tla) ? 0.35 : 1}
            >
              <circle
                r={isSelected ? DOT_RADIUS_SELECTED : DOT_RADIUS}
                fill={driver?.color ?? "var(--neutral)"}
                stroke={isSelected ? "var(--text)" : "var(--bg)"}
                strokeWidth={isSelected ? 90 : 60}
              />
              {/* Twenty TLAs on a start-line grid overlap illegibly, so only the
                  selected driver is labelled. */}
              {isSelected && (
                <text x={360} y={130} fontSize={LABEL_SIZE} fill="var(--text)" fontWeight={700}>
                  {p.tla}
                </text>
              )}
            </g>
          );
        })}
      </svg>

      <div className={s.legend}>
        <div className={s.legendItem}>
          <div className={s.legendLine} style={{ background: "var(--track-surface)" }} />
          Track
        </div>
        <div className={s.legendItem}>
          <div className={s.legendDot} style={{ background: "var(--text-fainter)", opacity: 0.4 }} />
          In pit
        </div>
        <div className={s.legendItem}>Retired removed</div>
        <div className={s.legendItem}>{visible.length} cars</div>
      </div>
    </section>
  );
}
