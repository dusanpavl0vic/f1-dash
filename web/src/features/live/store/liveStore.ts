import { merge, type JsonObject } from "../lib/merge";
import { LiveClient, type ConnectionStatus, type ServerMessage } from "../lib/wsClient";
import { selectSnapshot } from "../model/selectors";
import type { SessionSnapshot } from "../model/types";
import { DelayBuffer } from "./delayBuffer";
import { SessionHistory } from "./sessionHistory";

/**
 * The live F1 state, deliberately OUTSIDE React.
 *
 * Roughly ten deltas per second arrive against a deeply nested object. A React
 * state update per delta would re-render the tree and drop frames, so deltas
 * are merged into a module-level object and subscribers are notified at most
 * once per animation frame.
 *
 * See DECISIONS D-004 and docs/17 §3 — this is the one documented exception to
 * the template's three state categories.
 */

const DELAY_STORAGE_KEY = "apex.delaySeconds";

let rawState: JsonObject = {};
let derived: SessionSnapshot = selectSnapshot({});
let status: ConnectionStatus = "closed";

/** Server clock minus browser clock, for the delay buffer (docs/08). */
let clockSkewMs = 0;
let lastSequence = 0;
let sequenceGaps = 0;

const buffer = new DelayBuffer();
const history = new SessionHistory();

const listeners = new Set<() => void>();
let flushScheduled = false;
let dirty = false;
let client: LiveClient | null = null;

/** Kept running while a delay is set, so buffered messages are released on time. */
let tickHandle: number | null = null;
let lastTickAt = 0;

function readStoredDelay(): number {
  try {
    return Number(localStorage.getItem(DELAY_STORAGE_KEY)) || 0;
  } catch {
    // Private browsing, or storage disabled. A delay of zero is a fine default.
    return 0;
  }
}

function project(): void {
  derived = selectSnapshot(rawState);
  history.observe(derived);
  for (const listener of listeners) listener();
}

function scheduleFlush(): void {
  dirty = true;
  if (flushScheduled) return;
  flushScheduled = true;

  requestAnimationFrame(() => {
    flushScheduled = false;
    if (!dirty) return;
    dirty = false;

    // Projection runs once per frame, not once per delta.
    project();
  });
}

/** Applies one message to the state. Called after any delay has elapsed. */
function apply(message: ServerMessage): void {
  if (message.type === "snapshot") {
    // A snapshot REPLACES state, never merges into it. That is the whole point
    // of the message: on a session change the server sends an empty one, and
    // merging it would leave the previous session's drivers in place — which
    // rendered a 26-car field with two cars sharing P1.
    rawState = message.data;
    clockSkewMs = message.serverTime - Date.now();
    buffer.setSkew(clockSkewMs);
    lastSequence = message.seq;
    sequenceGaps = 0;

    // History describes the session that just ended. Carrying it into the next
    // one would draw the old race's safety car periods over the new race.
    if (message.reason === "session-changed") history.reset();
  } else {
    // A gap means deltas were lost; the store is no longer trustworthy and the
    // client should resync. Counted rather than hidden.
    if (lastSequence !== 0 && message.seq !== lastSequence + 1) sequenceGaps++;
    lastSequence = message.seq;
    merge(rawState, message.data);
  }
  scheduleFlush();
}

function handleMessage(message: ServerMessage): void {
  // With no delay the buffer is bypassed entirely rather than being asked to
  // release immediately: the common case should not pay for the feature.
  if (buffer.target === 0 && !buffer.catchingUp) {
    apply(message);
    return;
  }

  // A snapshot is applied at once even under delay. It is not an event to be
  // shown later — it is the state everything queued behind it patches, and
  // holding it back would apply deltas to state they do not belong to.
  if (message.type === "snapshot") {
    buffer.clear();
    apply(message);
    return;
  }

  buffer.push(message);
}

function tick(): void {
  tickHandle = requestAnimationFrame(tick);

  const now = Date.now();
  const elapsed = lastTickAt === 0 ? 0 : now - lastTickAt;
  lastTickAt = now;

  const due = buffer.drain(now, elapsed);
  for (const message of due) apply(message);
}

function startTicking(): void {
  if (tickHandle !== null) return;
  lastTickAt = 0;
  tickHandle = requestAnimationFrame(tick);
}

function stopTicking(): void {
  if (tickHandle !== null) cancelAnimationFrame(tickHandle);
  tickHandle = null;
}

function handleStatus(next: ConnectionStatus): void {
  status = next;
  scheduleFlush();
}

export const liveStore = {
  connect(url: string): void {
    if (client) return;

    buffer.setDelay(readStoredDelay());
    if (buffer.target > 0) startTicking();

    client = new LiveClient(url, {
      onMessage: handleMessage,
      onStatus: handleStatus,
      lastSequence: () => lastSequence,
    });
    client.connect();
  },

  disconnect(): void {
    client?.disconnect();
    client = null;
    stopTicking();
    buffer.clear();
  },

  subscribe(listener: () => void): () => void {
    listeners.add(listener);
    return () => listeners.delete(listener);
  },

  getSnapshot(): SessionSnapshot {
    return derived;
  },

  getStatus(): ConnectionStatus {
    return status;
  },

  /** The accumulated history, for charts that need more than the current frame. */
  getHistory(): SessionHistory {
    return history;
  },

  getDelaySeconds(): number {
    return buffer.target / 1000;
  },

  setDelaySeconds(seconds: number): void {
    buffer.setDelay(seconds);
    try {
      localStorage.setItem(DELAY_STORAGE_KEY, String(seconds));
    } catch {
      // Not being able to remember the setting is not a reason to refuse it.
    }

    // The ticker must keep running while catching up, even at a target of zero,
    // or the already-buffered messages would never be released.
    if (buffer.target > 0 || buffer.pending > 0) startTicking();
    else stopTicking();

    for (const listener of listeners) listener();
  },

  getDiagnostics() {
    return {
      clockSkewMs,
      lastSequence,
      sequenceGaps,
      topics: Object.keys(rawState).length,
      delayPending: buffer.pending,
      delayCatchingUp: buffer.catchingUp,
      delayOverflowed: buffer.overflowed,
    };
  },

  /** Test seam — lets the suite drive the store without a socket. */
  __applyForTest(message: ServerMessage): void {
    handleMessage(message);
  },
  __resetForTest(): void {
    rawState = {};
    derived = selectSnapshot({});
    lastSequence = 0;
    sequenceGaps = 0;
    buffer.setDelay(0);
    buffer.clear();
    history.reset();
  },
};
