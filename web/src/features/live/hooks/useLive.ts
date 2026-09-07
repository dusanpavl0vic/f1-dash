import { useSyncExternalStore } from "react";
import { liveStore } from "../store/liveStore";
import type { SessionSnapshot } from "../model/types";

/**
 * Components subscribe through these hooks, never to the raw store.
 *
 * The whole snapshot is exposed for now because the projection already runs
 * once per animation frame. Narrowing to per-driver slices is IMPL-12's job,
 * once there are enough subscribers for it to matter — the template's rule is
 * to raise code to the level that needs it, not ahead of time.
 */
export function useLiveSession(): SessionSnapshot {
  return useSyncExternalStore(liveStore.subscribe, liveStore.getSnapshot, liveStore.getSnapshot);
}

export function useConnectionStatus() {
  return useSyncExternalStore(liveStore.subscribe, liveStore.getStatus, liveStore.getStatus);
}
