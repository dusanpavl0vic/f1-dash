import type { ServerMessage } from "../lib/wsClient";

/**
 * Holds messages back so the dashboard matches a delayed television feed.
 *
 * The problem this solves is specific: a broadcast is typically 5-60 seconds
 * behind the timing feed, so an undelayed dashboard spoils an overtake before
 * it is shown. The viewer sets a delay and the dashboard waits.
 *
 * Two details make it correct rather than approximate:
 *
 * - **Server time, not arrival time.** Messages are released against the
 *   server's clock corrected for skew. Timing against arrival would drift with
 *   every network hiccup, and the drift would only ever accumulate.
 * - **Catch-up is rate-capped.** Lowering the delay from 60s to 0 must not
 *   flush a minute of state in one frame; the buffer drains at a bounded
 *   multiple of real time so positions animate instead of teleporting.
 */
export class DelayBuffer {
  private queue: { at: number; message: ServerMessage }[] = [];
  private delayMs = 0;
  private skewMs = 0;
  /** While catching up, the delay currently in effect, walking toward target. */
  private effectiveMs = 0;

  /** How much faster than real time the buffer may drain. */
  private static readonly CATCH_UP_RATE = 3;

  /** Messages are dropped past this point rather than growing without bound. */
  private static readonly MAX_QUEUED = 20_000;

  get pending(): number {
    return this.queue.length;
  }

  get target(): number {
    return this.delayMs;
  }

  /** True while the buffer is draining toward a newly lowered delay. */
  get catchingUp(): boolean {
    return this.effectiveMs > this.delayMs + 250;
  }

  /** True when the buffer hit its ceiling and messages were discarded. */
  overflowed = false;

  setSkew(skewMs: number): void {
    this.skewMs = skewMs;
  }

  setDelay(seconds: number): void {
    const next = Math.max(0, Math.min(120, seconds)) * 1000;

    // Raising the delay takes effect at once — it only means holding messages
    // longer, which is always safe. Lowering has to be walked down, so
    // `effectiveMs` starts wherever it currently is.
    if (next > this.effectiveMs) this.effectiveMs = next;
    this.delayMs = next;
  }

  push(message: ServerMessage): void {
    if (this.queue.length >= DelayBuffer.MAX_QUEUED) {
      this.overflowed = true;
      return;
    }
    this.queue.push({ at: message.ts, message });
  }

  /**
   * Everything due by now, in order.
   *
   * @param elapsedMs real time since the previous call, which bounds how far
   *   the effective delay may fall this frame.
   */
  drain(now: number, elapsedMs: number): ServerMessage[] {
    if (this.effectiveMs > this.delayMs) {
      const step = elapsedMs * (DelayBuffer.CATCH_UP_RATE - 1);
      this.effectiveMs = Math.max(this.delayMs, this.effectiveMs - step);
    }

    // The server's clock is the reference; the browser's is corrected onto it.
    const serverNow = now + this.skewMs;
    const cutoff = serverNow - this.effectiveMs;

    let count = 0;
    while (count < this.queue.length && this.queue[count]!.at <= cutoff) count++;
    if (count === 0) return [];

    const due = this.queue.splice(0, count);
    return due.map((e) => e.message);
  }

  clear(): void {
    this.queue = [];
    this.overflowed = false;
    this.effectiveMs = this.delayMs;
  }
}
