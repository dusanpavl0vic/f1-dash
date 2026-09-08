import { useMemo, useSyncExternalStore } from "react";
import { liveStore } from "../store/liveStore";
import type { PaceLine, SessionSnapshot, TimelineBand } from "../model/types";

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

/**
 * The accumulated session history — pace lines and the status timeline.
 *
 * `useLiveSession` is called for its subscription, not its value: the history
 * object is mutated in place, so it can never be the thing that signals a
 * change. Reading it after the snapshot has changed gives fresh values, and
 * memoising on the snapshot keeps the derivation to once per frame.
 */
export function useSessionHistory(focus: string[]): {
  pace: PaceLine[];
  paceLaps: number[];
  timeline: TimelineBand[];
} {
  const session = useLiveSession();
  const key = focus.join(",");

  return useMemo(() => {
    const history = liveStore.getHistory();
    return {
      pace: history.pace(focus),
      paceLaps: history.paceLaps(focus),
      timeline: history.timeline(),
    };
    // `session` is the frame signal; `key` is the stable form of `focus`.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [session, key]);
}

/** The broadcast delay, in seconds, and a setter that persists it. */
export function useDelay(): [number, (seconds: number) => void] {
  const seconds = useSyncExternalStore(
    liveStore.subscribe,
    liveStore.getDelaySeconds,
    liveStore.getDelaySeconds,
  );
  return [seconds, liveStore.setDelaySeconds];
}
