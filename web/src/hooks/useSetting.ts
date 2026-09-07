import { useCallback, useSyncExternalStore } from "react";

/**
 * A user preference that survives a reload.
 *
 * Backed by localStorage and shared through one subscriber set, so two
 * components reading the same setting always agree — two independent
 * `useState` copies would drift the moment one of them wrote.
 *
 * Every access is guarded: storage throws outright in some privacy modes, and
 * a settings toggle is never worth crashing a page over.
 */

const listeners = new Set<() => void>();
const cache = new Map<string, string | null>();

function notify(): void {
  for (const listener of listeners) listener();
}

function read(key: string): string | null {
  if (cache.has(key)) return cache.get(key) ?? null;

  let value: string | null;
  try {
    value = localStorage.getItem(key);
  } catch {
    value = null;
  }
  cache.set(key, value);
  return value;
}

function write(key: string, value: string): void {
  cache.set(key, value);
  try {
    localStorage.setItem(key, value);
  } catch {
    // The setting still applies for this session; it just will not be
    // remembered. Failing loudly here would help nobody.
  }
  notify();
}

function subscribe(callback: () => void): () => void {
  listeners.add(callback);
  return () => listeners.delete(callback);
}

export function useSetting(key: string, fallback: boolean): [boolean, (value: boolean) => void] {
  const value = useSyncExternalStore(
    subscribe,
    () => read(key),
    () => null,
  );

  const set = useCallback((next: boolean) => write(key, next ? "1" : "0"), [key]);

  return [value === null ? fallback : value === "1", set];
}

export const SETTINGS = {
  /**
   * Driver photographs.
   *
   * Headshot URLs arrive in the feed and point at F1's media server. Hot-linking
   * them is a grey area for a self-hosted dashboard, so it is a setting rather
   * than a hard-coded choice — and the layout is complete without them. Default
   * on, at the owner's explicit direction; anyone deploying this publicly should
   * consider turning it off.
   */
  driverPhotos: "apex.driverPhotos",
} as const;
