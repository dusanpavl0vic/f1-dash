import type { JsonObject } from "./merge";

export type ServerMessage =
  | {
      type: "snapshot";
      seq: number;
      ts: number;
      serverTime: number;
      data: JsonObject;
      /** "session-changed" when the server switched sessions under us. */
      reason?: string;
    }
  | { type: "delta"; seq: number; ts: number; data: JsonObject };

export type ConnectionStatus = "connecting" | "open" | "reconnecting" | "closed";

export interface LiveClientHandlers {
  onMessage(message: ServerMessage): void;
  onStatus(status: ConnectionStatus): void;
  /**
   * The last sequence the store has applied, asked for at connect time.
   *
   * A function rather than a value: the client reconnects long after it was
   * constructed, and the sequence it needs is the one from a moment ago.
   */
  lastSequence(): number;
}

const BACKOFF_INITIAL_MS = 1_000;
const BACKOFF_MAX_MS = 30_000;
const HEARTBEAT_INTERVAL_MS = 20_000;

/**
 * WebSocket client with exponential backoff and a heartbeat.
 *
 * The heartbeat is not optional. A socket that is open but dead is common on
 * mobile networks and behind corporate proxies, and without an explicit
 * liveness check the dashboard silently freezes on stale data — the failure the
 * user cannot detect, which is the worst kind.
 */
export class LiveClient {
  private socket: WebSocket | null = null;
  private backoffMs = BACKOFF_INITIAL_MS;
  private reconnectTimer: number | null = null;
  private heartbeatTimer: number | null = null;
  private lastMessageAt = 0;
  private closedByCaller = false;

  constructor(
    private readonly url: string,
    private readonly handlers: LiveClientHandlers,
  ) {}

  connect(): void {
    this.closedByCaller = false;
    this.open();
  }

  disconnect(): void {
    this.closedByCaller = true;
    this.clearTimers();
    this.socket?.close();
    this.socket = null;
    this.handlers.onStatus("closed");
  }

  private open(): void {
    this.handlers.onStatus(this.backoffMs === BACKOFF_INITIAL_MS ? "connecting" : "reconnecting");

    // Ask to resume. The server sends the deltas we missed when its backlog
    // still reaches that far, and a full snapshot when it does not — so this is
    // an optimisation the client never has to reason about: either way what
    // arrives is sufficient to be correct.
    const since = this.handlers.lastSequence();
    const url = since > 0
      ? `${this.url}${this.url.includes("?") ? "&" : "?"}since=${since}`
      : this.url;

    const socket = new WebSocket(url);
    this.socket = socket;

    socket.onopen = () => {
      this.backoffMs = BACKOFF_INITIAL_MS;
      this.lastMessageAt = Date.now();
      this.handlers.onStatus("open");
      this.startHeartbeat();
    };

    socket.onmessage = (event) => {
      this.lastMessageAt = Date.now();
      try {
        this.handlers.onMessage(JSON.parse(event.data as string) as ServerMessage);
      } catch {
        // A single malformed frame must not kill the connection.
      }
    };

    socket.onclose = () => this.scheduleReconnect();
    socket.onerror = () => socket.close();
  }

  private startHeartbeat(): void {
    this.clearHeartbeat();
    this.heartbeatTimer = window.setInterval(() => {
      // Silence for longer than two heartbeats means the socket is dead even
      // though the browser still reports it open.
      if (Date.now() - this.lastMessageAt > HEARTBEAT_INTERVAL_MS * 2) {
        this.socket?.close();
        return;
      }
      if (this.socket?.readyState === WebSocket.OPEN) {
        this.socket.send(JSON.stringify({ type: "ping" }));
      }
    }, HEARTBEAT_INTERVAL_MS);
  }

  private scheduleReconnect(): void {
    this.clearTimers();
    if (this.closedByCaller) return;

    this.handlers.onStatus("reconnecting");

    // ±20% jitter so a fleet of tabs does not reconnect in lockstep.
    const jittered = this.backoffMs * (0.8 + Math.random() * 0.4);
    this.reconnectTimer = window.setTimeout(() => this.open(), jittered);
    this.backoffMs = Math.min(this.backoffMs * 2, BACKOFF_MAX_MS);
  }

  private clearHeartbeat(): void {
    if (this.heartbeatTimer !== null) window.clearInterval(this.heartbeatTimer);
    this.heartbeatTimer = null;
  }

  private clearTimers(): void {
    this.clearHeartbeat();
    if (this.reconnectTimer !== null) window.clearTimeout(this.reconnectTimer);
    this.reconnectTimer = null;
  }
}
