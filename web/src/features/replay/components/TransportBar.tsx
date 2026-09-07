import { useCallback, useEffect, useState } from "react";
import s from "./TransportBar.module.css";

const API_URL = import.meta.env["VITE_API_URL"] ?? "http://localhost:4000";
const SPEEDS = [0.5, 1, 2, 4, 8, 16] as const;

interface Transport {
  positionMs: number;
  durationMs: number;
  playing: boolean;
  speed: number;
}

function clock(ms: number): string {
  const total = Math.max(0, Math.floor(ms / 1000));
  const h = Math.floor(total / 3600);
  const m = Math.floor((total % 3600) / 60);
  const sec = total % 60;

  return h > 0
    ? `${h}:${String(m).padStart(2, "0")}:${String(sec).padStart(2, "0")}`
    : `${m}:${String(sec).padStart(2, "0")}`;
}

/**
 * Video-style transport for a replay: play, pause, scrub and speed.
 *
 * Seeking is not free. State is delta-accumulated, so moving the position
 * rebuilds the session from the start up to the target — which is why the bar
 * says "rebuilding" instead of pretending the jump was instant. Everything
 * else takes effect immediately.
 */
export function TransportBar({ currentLap }: { currentLap?: number }) {
  const [state, setState] = useState<Transport | null>(null);
  const [dragging, setDragging] = useState<number | null>(null);
  const [seeking, setSeeking] = useState(false);

  useEffect(() => {
    let cancelled = false;

    const poll = () => {
      void fetch(`${API_URL}/api/session`)
        .then((r) => r.json() as Promise<Transport>)
        .then((d) => { if (!cancelled) setState(d); })
        .catch(() => undefined);
    };

    poll();
    // Once a second: the bar only needs to look alive, and the position moves
    // with session time, not with frames.
    const timer = window.setInterval(poll, 1000);

    return () => { cancelled = true; window.clearInterval(timer); };
  }, []);

  const control = useCallback(async (action: string, value?: number) => {
    const body = value === undefined ? { action } : { action, value };

    const next = await fetch(`${API_URL}/api/session/control`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(body),
    }).then((r) => (r.ok ? (r.json() as Promise<Transport>) : null)).catch(() => null);

    if (next && "playing" in next) setState((s0) => (s0 ? { ...s0, ...next } : s0));
  }, []);

  const seek = useCallback(async (ms: number) => {
    setSeeking(true);
    try {
      await control("seek", ms);
    } finally {
      setSeeking(false);
      setDragging(null);
    }
  }, [control]);

  if (!state || state.durationMs <= 0) return null;

  const position = dragging ?? state.positionMs;

  return (
    <div className={s.bar} role="group" aria-label="Replay controls">
      <button
        type="button"
        className={`${s.button} ${state.playing ? "" : s.buttonPrimary}`}
        aria-label={state.playing ? "Pause" : "Play"}
        onClick={() => void control(state.playing ? "pause" : "play")}
      >
        {state.playing ? "❚❚" : "▶"}
      </button>

      <div className={s.time}>{clock(position)}</div>

      <input
        className={s.scrub}
        type="range"
        min={0}
        max={state.durationMs}
        step={1000}
        value={position}
        aria-label="Session position"
        // Dragging updates locally; the seek fires on release, because each one
        // restarts the session and a seek per pixel would be unusable.
        onChange={(e) => setDragging(Number(e.target.value))}
        onMouseUp={(e) => void seek(Number((e.target as HTMLInputElement).value))}
        onTouchEnd={(e) => void seek(Number((e.target as HTMLInputElement).value))}
        onKeyUp={(e) => {
          if (["ArrowLeft", "ArrowRight", "Home", "End"].includes(e.key)) {
            void seek(Number((e.target as HTMLInputElement).value));
          }
        }}
      />

      <div className={`${s.time} ${s.timeTotal}`}>{clock(state.durationMs)}</div>

      {currentLap !== undefined && currentLap > 0 && (
        <div className={s.lapMark}>LAP {currentLap}</div>
      )}

      {seeking && <div className={s.seeking}>Rebuilding…</div>}

      <div className={s.speeds}>
        {SPEEDS.map((speed) => (
          <button
            key={speed}
            type="button"
            className={`${s.speed} ${Math.abs(state.speed - speed) < 0.01 ? s.speedActive : ""}`}
            onClick={() => void control("speed", speed)}
          >
            {speed}×
          </button>
        ))}
      </div>
    </div>
  );
}
