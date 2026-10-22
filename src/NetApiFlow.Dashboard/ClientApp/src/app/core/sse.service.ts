import { Injectable, signal } from '@angular/core';
import { Subject } from 'rxjs';
import { FLOW_EVENT_TYPES, FlowEvent } from './models';

export type SseConnectionState = 'connecting' | 'open' | 'closed';

const INITIAL_RECONNECT_DELAY_MS = 1000;
const MAX_RECONNECT_DELAY_MS = 30000;

/**
 * Connects to GET /stream (docs/plano/contratos.md, seção 2) with a real `EventSource`, parsing
 * each named event into a `FlowEvent`. Owns reconnection: on a dropped connection it reconnects
 * with exponential backoff, sending the last event id it saw as `?lastEventId=` (D-22) — the
 * query string, not the `Last-Event-ID` header, because `EventSource` has no way to set a header
 * on a connection created by application code; only the browser's own automatic retry can do that.
 * The URL is relative (`api/stream`), resolved against whatever base path the host page sets
 * (risco R-03): never hardcode `/flow`.
 *
 * `connect`/`disconnect` are reference-counted: `LiveTracePlayerService` and an always-on event
 * feed (ADR-0010) can both want the stream open at the same time, each calling `connect()`/
 * `disconnect()` on its own lifecycle without tearing down the other's connection.
 */
@Injectable({ providedIn: 'root' })
export class SseService {
  private eventSource: EventSource | null = null;
  private lastEventId: number | null = null;
  private reconnectAttempt = 0;
  private reconnectTimer: ReturnType<typeof setTimeout> | null = null;
  private manuallyDisconnected = true;
  private hasConnectedBefore = false;
  private openCount = 0;

  private readonly eventsSubject = new Subject<FlowEvent>();
  private readonly reconnectedSubject = new Subject<void>();
  private readonly stateSignal = signal<SseConnectionState>('closed');

  /** Every parsed event, in the order it arrived on this connection (or a reconnected one). */
  readonly events$ = this.eventsSubject.asObservable();

  /** Fires after every reconnection (not the first connect), so a consumer can resync its state. */
  readonly reconnected$ = this.reconnectedSubject.asObservable();

  readonly state = this.stateSignal.asReadonly();

  connect(): void {
    this.openCount++;
    if (this.openCount > 1) {
      return;
    }

    this.manuallyDisconnected = false;
    this.hasConnectedBefore = false;
    this.lastEventId = null;
    this.reconnectAttempt = 0;
    this.openConnection();
  }

  disconnect(): void {
    this.openCount = Math.max(this.openCount - 1, 0);
    if (this.openCount > 0) {
      return;
    }

    this.manuallyDisconnected = true;
    this.clearReconnectTimer();
    this.eventSource?.close();
    this.eventSource = null;
    this.stateSignal.set('closed');
  }

  private openConnection(): void {
    this.stateSignal.set('connecting');
    const url =
      this.lastEventId !== null ? `api/stream?lastEventId=${this.lastEventId}` : 'api/stream';
    const source = new EventSource(url);
    this.eventSource = source;

    source.onopen = () => {
      this.reconnectAttempt = 0;
      this.stateSignal.set('open');
      if (this.hasConnectedBefore) {
        this.reconnectedSubject.next();
      }

      this.hasConnectedBefore = true;
    };

    source.onerror = () => {
      source.close();
      this.stateSignal.set('closed');
      if (!this.manuallyDisconnected) {
        this.scheduleReconnect();
      }
    };

    source.addEventListener('ping', () => {
      // Handshake/heartbeat only (D-20): nothing to apply.
    });

    for (const type of FLOW_EVENT_TYPES) {
      source.addEventListener(type, (event: Event) => {
        const flowEvent = JSON.parse((event as MessageEvent<string>).data) as FlowEvent;
        this.lastEventId = flowEvent.eventId;
        this.eventsSubject.next(flowEvent);
      });
    }
  }

  private scheduleReconnect(): void {
    const delayMs = Math.min(
      INITIAL_RECONNECT_DELAY_MS * 2 ** this.reconnectAttempt,
      MAX_RECONNECT_DELAY_MS,
    );
    this.reconnectAttempt++;
    this.reconnectTimer = setTimeout(() => this.openConnection(), delayMs);
  }

  private clearReconnectTimer(): void {
    if (this.reconnectTimer !== null) {
      clearTimeout(this.reconnectTimer);
      this.reconnectTimer = null;
    }
  }
}
