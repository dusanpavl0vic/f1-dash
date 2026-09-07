import { useEffect, useReducer } from "react";

export interface TrackGeometry {
  circuitKey: number;
  circuitName: string;
  rotation: number;
  path: string;
  viewBox: { x: number; y: number; width: number; height: number };
  corners: { number: number; x: number; y: number; labelX: number; labelY: number }[];
  marshalSectors: { number: number; path: string }[];
  startFinish: { x: number; y: number };
}

const API_URL = import.meta.env["VITE_API_URL"] ?? "http://localhost:4000";

/**
 * Geometry is immutable per circuit and year, so it lives in a module cache
 * rather than in component state.
 *
 * The value is DERIVED from that cache during render and the effect only
 * fetches. Mirroring the cache into state and setting it in the effect body
 * causes a cascading render on every mount, which the React lint rule correctly
 * refuses.
 */
const cache = new Map<string, TrackGeometry>();
const inFlight = new Set<string>();

export function useTrackGeometry(
  circuitKey: number | null,
  year: number | null,
): TrackGeometry | null {
  const key = circuitKey !== null && year !== null ? `${circuitKey}-${year}` : null;
  const [, rerender] = useReducer((n: number) => n + 1, 0);

  useEffect(() => {
    if (key === null || cache.has(key) || inFlight.has(key)) return;

    inFlight.add(key);
    let cancelled = false;

    fetch(`${API_URL}/api/track/${circuitKey}/${year}`)
      .then((r) => (r.ok ? (r.json() as Promise<TrackGeometry>) : null))
      .then((data) => {
        if (data) cache.set(key, data);
        if (!cancelled) rerender();
      })
      // Upstream down and nothing cached: the map hides with a message and the
      // rest of the dashboard is unaffected.
      .catch(() => undefined)
      .finally(() => inFlight.delete(key));

    return () => { cancelled = true; };
  }, [key, circuitKey, year]);

  return key === null ? null : cache.get(key) ?? null;
}
