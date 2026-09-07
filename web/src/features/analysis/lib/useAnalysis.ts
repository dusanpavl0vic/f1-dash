import { useCallback, useEffect, useReducer, useRef, useState } from "react";
import type { SectorComparison, SessionAnalysis, TelemetryLap } from "../model/types";

const API_URL = import.meta.env["VITE_API_URL"] ?? "http://localhost:4000";

/** How often a running session's analysis is re-read. */
const POLL_MS = 15_000;

export interface AnalysisState {
  analysis: SessionAnalysis | null;
  loading: boolean;
  error: string | null;
  refresh(): void;
}

/**
 * Polls the analysis for the running session.
 *
 * Polling rather than streaming on purpose: the analysis is a whole-session
 * document that changes once a lap, not a per-frame delta, and pushing it down
 * the live socket would put a hundred-kilobyte payload on the path that has to
 * stay under 300 ms.
 */
export function useAnalysis(enabled: boolean): AnalysisState {
  const [analysis, setAnalysis] = useState<SessionAnalysis | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [tick, refresh] = useReducer((n: number) => n + 1, 0);

  // Kept in a ref so the poll interval does not restart on every response.
  const loadingRef = useRef(false);

  useEffect(() => {
    if (!enabled) return;

    let cancelled = false;

    const load = async () => {
      if (loadingRef.current) return;
      loadingRef.current = true;
      setLoading(true);

      try {
        const response = await fetch(`${API_URL}/api/analysis`);
        if (!response.ok) {
          if (!cancelled) setError("No session is running.");
          return;
        }
        const data = (await response.json()) as SessionAnalysis;
        if (!cancelled) {
          setAnalysis(data);
          setError(null);
        }
      } catch {
        if (!cancelled) setError("Could not reach the server.");
      } finally {
        loadingRef.current = false;
        if (!cancelled) setLoading(false);
      }
    };

    void load();
    const timer = window.setInterval(() => void load(), POLL_MS);

    return () => {
      cancelled = true;
      window.clearInterval(timer);
    };
  }, [enabled, tick]);

  return { analysis, loading, error, refresh };
}

export function useSectorComparison(a: string | null, b: string | null) {
  const [comparison, setComparison] = useState<SectorComparison | null>(null);

  useEffect(() => {
    if (!a || !b || a === b) {
      return;
    }

    let cancelled = false;
    void fetch(`${API_URL}/api/analysis/compare?a=${encodeURIComponent(a)}&b=${encodeURIComponent(b)}`)
      .then((r) => (r.ok ? (r.json() as Promise<SectorComparison>) : null))
      .then((data) => { if (!cancelled && data) setComparison(data); })
      .catch(() => undefined);

    return () => { cancelled = true; };
  }, [a, b]);

  return a && b && a !== b ? comparison : null;
}

export interface TelemetryState {
  enabled: boolean;
  laps: number[];
  trace: TelemetryLap | null;
  load(lap: number): void;
}

export function useTelemetry(racingNumber: string | null): TelemetryState {
  // Both pieces of state are stored WITH the driver they belong to, so a driver
  // change is handled by derivation rather than by clearing state inside an
  // effect — which causes a cascading render.
  const [index, setIndex] = useState<{ number: string; enabled: boolean; laps: number[] } | null>(null);
  const [loaded, setLoaded] = useState<{ number: string; trace: TelemetryLap } | null>(null);

  useEffect(() => {
    if (!racingNumber) return;

    let cancelled = false;
    void fetch(`${API_URL}/api/analysis/telemetry/${racingNumber}`)
      .then((r) => r.json() as Promise<{ enabled: boolean; laps: number[] }>)
      .then((data) => { if (!cancelled) setIndex({ number: racingNumber, ...data }); })
      .catch(() => undefined);

    return () => { cancelled = true; };
  }, [racingNumber]);

  const load = useCallback((lap: number) => {
    if (!racingNumber) return;

    void fetch(`${API_URL}/api/analysis/telemetry/${racingNumber}/${lap}`)
      .then((r) => (r.ok ? (r.json() as Promise<TelemetryLap>) : null))
      .then((trace) => { if (trace) setLoaded({ number: racingNumber, trace }); })
      .catch(() => undefined);
  }, [racingNumber]);

  const current = index?.number === racingNumber ? index : null;

  return {
    enabled: current?.enabled ?? false,
    laps: current?.laps ?? [],
    trace: loaded?.number === racingNumber ? loaded.trace : null,
    load,
  };
}
