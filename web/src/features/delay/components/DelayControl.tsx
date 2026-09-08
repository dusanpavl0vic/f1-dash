import { useDelay } from "@/features/live/hooks/useLive";
import { liveStore } from "@/features/live/store/liveStore";
import s from "./DelayControl.module.css";

const STEPS = [0, 5, 15, 30, 60] as const;

/**
 * Holds the dashboard back to match a delayed television broadcast.
 *
 * Presets rather than a free slider. The number that matters is whatever makes
 * the dashboard agree with the picture on the screen, and that is found by
 * trying a few values during a session, not by typing a precise one.
 */
export function DelayControl() {
  const [seconds, setSeconds] = useDelay();
  const { delayCatchingUp, delayPending } = liveStore.getDiagnostics();

  return (
    <div className={s.wrap}>
      <span className={s.label}>Delay</span>

      <div className={s.steps} role="group" aria-label="Broadcast delay">
        {STEPS.map((step) => (
          <button
            key={step}
            type="button"
            className={`${s.step} ${seconds === step ? s.stepActive : ""}`}
            aria-pressed={seconds === step}
            onClick={() => setSeconds(step)}
          >
            {step === 0 ? "OFF" : `${step}s`}
          </button>
        ))}
      </div>

      {/* Lowering the delay drains at a bounded rate, so there is a period where
          the dashboard is neither at the old delay nor the new one. Saying so is
          better than a dashboard that looks stuck. */}
      {delayCatchingUp && (
        <span className={s.catching}>Catching up · {delayPending} queued</span>
      )}
    </div>
  );
}
