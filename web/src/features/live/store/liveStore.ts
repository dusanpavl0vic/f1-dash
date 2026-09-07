import { merge, type JsonObject } from "../lib/merge";
import { LiveClient, type ConnectionStatus, type ServerMessage } from "../lib/wsClient";
import { selectSnapshot } from "../model/selectors";
import type { SessionSnapshot } from "../model/types";

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

let rawState: JsonObject = {};
let derived: SessionSnapshot = selectSnapshot({});
let status: ConnectionStatus = "closed";

/** Server clock minus browser clock, for the delay buffer (docs/08). */
let clockSkewMs = 0;
let lastSequence = 0;
let sequenceGaps = 0;

const listeners = new Set<() => void>();
let flushScheduled = false;
let dirty = false;
let client: LiveClient | null = null;

function scheduleFlush(): void {
  dirty = true;
  if (flushScheduled) return;
  flushScheduled = true;

  requestAnimationFrame(() => {
    flushScheduled = false;
    if (!dirty) return;
    dirty = false;

    // Projection runs once per frame, not once per delta.
    derived = selectSnapshot(rawState);
    for (const listener of listeners) listener();
  });
}

function handleMessage(message: ServerMessage): void {
  if (message.type === "snapshot") {
    // A snapshot REPLACES state, never merges into it. That is the whole point
    // of the message: on a session change the server sends an empty one, and
    // merging it would leave the previous session's drivers in place — which
    // rendered a 26-car field with two cars sharing P1.
    rawState = message.data;
    clockSkewMs = message.serverTime - Date.now();
    lastSequence = message.seq;
    sequenceGaps = 0;
  } else {
    // A gap means deltas were lost; the store is no longer trustworthy and the
    // client should resync. Counted rather than hidden.
    if (lastSequence !== 0 && message.seq !== lastSequence + 1) sequenceGaps++;
    lastSequence = message.seq;
    merge(rawState, message.data);
  }
  scheduleFlush();
}

function handleStatus(next: ConnectionStatus): void {
  status = next;
  scheduleFlush();
}

export const liveStore = {
  connect(url: string): void {
    if (client) return;
    client = new LiveClient(url, { onMessage: handleMessage, onStatus: handleStatus });
    client.connect();
  },

  disconnect(): void {
    client?.disconnect();
    client = null;
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

  getDiagnostics() {
    return { clockSkewMs, lastSequence, sequenceGaps, topics: Object.keys(rawState).length };
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
  },
};
