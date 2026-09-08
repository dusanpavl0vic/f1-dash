import { useCallback, useEffect, useRef, useState } from "react";
import type { SessionSnapshot, TrackState } from "@/features/live/model/types";

export type ToastTone = "info" | "warning" | "danger" | "success";

export interface Toast {
  id: number;
  title: string;
  detail?: string;
  tone: ToastTone;
}

const TRACK_TOAST: Record<TrackState, { title: string; detail: string; tone: ToastTone } | null> = {
  "none": { title: "GREEN FLAG", detail: "Track clear, racing resumed.", tone: "success" },
  "yellow": { title: "YELLOW FLAG", detail: "Hazard on track. No overtaking.", tone: "warning" },
  "safety-car": { title: "SAFETY CAR", detail: "Field bunching behind the safety car.", tone: "warning" },
  "vsc": { title: "VIRTUAL SAFETY CAR", detail: "Delta time enforced.", tone: "warning" },
  "red-flag": { title: "RED FLAG", detail: "Session stopped.", tone: "danger" },
  "chequered": { title: "CHEQUERED FLAG", detail: "Session complete.", tone: "info" },
};

/** How long a toast stays before dismissing itself. */
const LIFETIME_MS = 9_000;

/** Above this, older toasts are dropped rather than stacking off-screen. */
const MAX_VISIBLE = 4;

/**
 * Turns state *changes* into notifications.
 *
 * The distinction that matters: the snapshot describes what is true now, and a
 * toast announces that something became true. Everything here is therefore
 * comparison against the previous frame, held in a ref — putting the previous
 * value in state would re-render on every frame the projection runs.
 */
export function useToasts(session: SessionSnapshot): { toasts: Toast[]; dismiss(id: number): void } {
  const [toasts, setToasts] = useState<Toast[]>([]);

  const previousTrack = useRef<TrackState | null>(null);
  const seenPenalties = useRef(new Set<string>());
  const nextId = useRef(1);

  /**
   * Whether the first batch of penalties has been absorbed.
   *
   * Everything present when the page opens is history, not news — opening a
   * dashboard mid-race must not fire eight toasts for penalties given an hour
   * ago. This is a flag rather than a size comparison because the two are only
   * accidentally equal on the first frame, and diverge on the very next one.
   */
  const primed = useRef(false);

  const push = useCallback((toast: Omit<Toast, "id">) => {
    const id = nextId.current++;
    setToasts((current) => [...current, { ...toast, id }].slice(-MAX_VISIBLE));

    window.setTimeout(() => {
      setToasts((current) => current.filter((t) => t.id !== id));
    }, LIFETIME_MS);
  }, []);

  useEffect(() => {
    const track = session.trackState;

    // The first frame is not a transition. Announcing "GREEN FLAG" to someone
    // who just opened a green-flag session is noise.
    if (previousTrack.current !== null && previousTrack.current !== track) {
      const toast = TRACK_TOAST[track];
      if (toast) push(toast);
    }
    previousTrack.current = track;
  }, [session.trackState, push]);

  useEffect(() => {
    for (const penalty of session.penalties) {
      // Keyed by content: the feed repeats race control messages, and the same
      // penalty arriving twice must not notify twice.
      const key = `${penalty.tla}|${penalty.lap}|${penalty.penalty}|${penalty.reason}`;
      if (seenPenalties.current.has(key)) continue;

      seenPenalties.current.add(key);
      if (!primed.current) continue;

      push({
        title: `${penalty.tla} — ${penalty.penalty}`,
        detail: penalty.reason,
        tone: penalty.penalty === "UNDER INVESTIGATION" ? "info" : "danger",
      });
    }

    // Primed only once timing data has actually arrived. Priming on an empty
    // first frame would treat the session's real first penalty as history.
    if (session.timing.length > 0) primed.current = true;
  }, [session.penalties, session.timing.length, push]);

  const dismiss = useCallback((id: number) => {
    setToasts((current) => current.filter((t) => t.id !== id));
  }, []);

  return { toasts, dismiss };
}
