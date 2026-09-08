import type { CarChannels, PaceLine, SessionSnapshot, TimelineBand, TrackState } from "../model/types";

/**
 * Accumulates what the feed does not keep.
 *
 * The timing feed publishes the *current* state: a driver's last lap time, the
 * track status right now. It has no history. Anything drawn over time — a pace
 * chart, a timeline of safety car periods — has to be built by watching the
 * stream go past, which is what this does.
 *
 * It is deliberately client-side and deliberately lossy: joining a session
 * halfway means the history starts halfway, and that is honest. The complete
 * version of the same data is what the server writes to `analysis.json`.
 */

/** How many laps the pace chart shows. */
const PACE_WINDOW = 10;

/** Used until the feed has published a team colour for a driver. */
const FALLBACK_COLOR = "var(--text-faintest)";

/** "1:21.456" or "21.456" to seconds; NaN when the feed has nothing yet. */
export function lapSeconds(text: string): number {
  if (!text) return Number.NaN;

  const parts = text.split(":");
  if (parts.length === 2) {
    return Number(parts[0]) * 60 + Number(parts[1]);
  }
  return Number(text);
}

interface DriverHistory {
  laps: Map<number, number>;
  color: string;
}

/**
 * How many channel frames to keep per driver.
 *
 * The feed publishes several times a second, so 240 is roughly the last minute
 * — enough for a trace, and bounded so a three-hour session cannot grow this
 * without limit.
 */
const TRACE_LENGTH = 240;

export class SessionHistory {
  private drivers = new Map<string, DriverHistory>();
  private bands: TimelineBand[] = [];
  private lastLapSeen = new Map<string, string>();
  private traces = new Map<string, CarChannels[]>();

  /** Watches one projected frame. Cheap enough to run every flush. */
  observe(snapshot: SessionSnapshot): void {
    const lap = snapshot.session.currentLap;

    for (const row of snapshot.timing) {
      const previous = this.lastLapSeen.get(row.tla);
      if (row.lastLap === previous || !row.lastLap) continue;

      this.lastLapSeen.set(row.tla, row.lastLap);

      const seconds = lapSeconds(row.lastLap);
      if (!Number.isFinite(seconds) || seconds <= 0) continue;

      let history = this.drivers.get(row.tla);
      if (!history) {
        history = { laps: new Map(), color: snapshot.drivers[row.tla]?.color ?? FALLBACK_COLOR };
        this.drivers.set(row.tla, history);
      }
      history.color = snapshot.drivers[row.tla]?.color ?? history.color;

      // Keyed by lap, so a repeated projection of the same frame cannot record
      // the same lap twice.
      history.laps.set(Math.max(1, lap - 1), seconds);
    }

    this.observeChannels(snapshot.channels);
    this.observeTrack(snapshot.trackState, Math.max(1, lap));
  }

  private observeChannels(channels: Record<string, CarChannels>): void {
    for (const [tla, current] of Object.entries(channels)) {
      let trace = this.traces.get(tla);
      if (!trace) {
        trace = [];
        this.traces.set(tla, trace);
      }

      // The projection runs every frame whether or not CarData moved, so an
      // unchanged reading is not a new sample. Recording it anyway would make
      // a stationary car look like a flat line of fresh data.
      const previous = trace[trace.length - 1];
      if (previous
        && previous.speed === current.speed
        && previous.rpm === current.rpm
        && previous.throttle === current.throttle
        && previous.brake === current.brake) {
        continue;
      }

      trace.push(current);
      if (trace.length > TRACE_LENGTH) trace.shift();
    }
  }

  /** The recent channel trace for one driver, oldest first. */
  trace(tla: string): CarChannels[] {
    return this.traces.get(tla) ?? [];
  }

  private observeTrack(state: TrackState, lap: number): void {
    const last = this.bands.at(-1);

    if (!last) {
      this.bands.push({ fromLap: lap, toLap: lap, label: state, state });
      return;
    }

    if (last.state === state) {
      last.toLap = Math.max(last.toLap, lap);
      return;
    }

    this.bands.push({ fromLap: last.toLap, toLap: lap, label: state, state });
  }

  /**
   * Pace lines for the given drivers over the last laps.
   *
   * Laps are aligned to a common axis rather than each driver getting its own,
   * because the whole point of the chart is comparing drivers at the same lap.
   */
  pace(tlas: string[]): PaceLine[] {
    const present = tlas.filter((t) => this.drivers.has(t));
    if (present.length === 0) return [];

    const lapNumbers = new Set<number>();
    for (const tla of present) {
      for (const lap of this.drivers.get(tla)!.laps.keys()) lapNumbers.add(lap);
    }

    const window = [...lapNumbers].sort((a, b) => a - b).slice(-PACE_WINDOW);

    return present.map((tla) => {
      const history = this.drivers.get(tla)!;
      return {
        tla,
        color: history.color,
        // NaN for a lap this driver has no time for — a pit lap, or a lap run
        // before they joined. The chart breaks the line rather than drawing a
        // straight segment across data that does not exist.
        laps: window.map((lap) => history.laps.get(lap) ?? Number.NaN),
      };
    });
  }

  /** The lap numbers the pace window covers, for the axis. */
  paceLaps(tlas: string[]): number[] {
    const lapNumbers = new Set<number>();
    for (const tla of tlas) {
      const history = this.drivers.get(tla);
      if (history) for (const lap of history.laps.keys()) lapNumbers.add(lap);
    }
    return [...lapNumbers].sort((a, b) => a - b).slice(-PACE_WINDOW);
  }

  timeline(): TimelineBand[] {
    return this.bands;
  }

  /** Every lap time recorded for one driver, oldest first. */
  lapsFor(tla: string): { lap: number; seconds: number }[] {
    const history = this.drivers.get(tla);
    if (!history) return [];

    return [...history.laps.entries()]
      .map(([lap, seconds]) => ({ lap, seconds }))
      .sort((a, b) => a.lap - b.lap);
  }

  reset(): void {
    this.drivers.clear();
    this.bands = [];
    this.lastLapSeen.clear();
    this.traces.clear();
  }
}
