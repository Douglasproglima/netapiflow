import { Injectable, OnDestroy, inject, signal } from '@angular/core';
import { Subscription } from 'rxjs';
import { ApiService } from '../../../core/api.service';
import { SseService } from '../../../core/sse.service';
import { FlowEvent, FlowTraceDetail } from '../../../core/models';

/**
 * Follows a single trace live over SSE: applies only events for the trace being tracked,
 * deduplicated by eventId, and converges to the correct final state even if events arrive out of
 * order (each span/trace update is only applied if its eventId is not older than the last one
 * already applied to that same span/trace). On every reconnect (SseService's own exponential
 * backoff, D-22), it reloads the consolidated trace via GET /traces/{traceId} before resuming
 * live updates, so nothing is silently lost across the gap.
 */
@Injectable()
export class LiveTracePlayerService implements OnDestroy {
  private readonly api = inject(ApiService);
  private readonly sse = inject(SseService);

  private readonly traceSignal = signal<FlowTraceDetail | null>(null);
  readonly trace = this.traceSignal.asReadonly();

  private currentTraceId: string | null = null;
  private readonly seenEventIds = new Set<number>();
  private readonly lastEventIdBySpanId = new Map<string, number>();
  private lastTraceEventId = -1;
  private pendingEvents: FlowEvent[] = [];
  private loadingBaseline = false;
  private subscriptions: Subscription[] = [];
  private tracking = false;

  track(traceId: string): void {
    this.stop();
    this.currentTraceId = traceId;

    this.subscriptions.push(
      this.sse.events$.subscribe((event) => this.onEvent(event)),
      this.sse.reconnected$.subscribe(() => this.loadBaseline()),
    );

    this.tracking = true;
    this.sse.connect();
    this.loadBaseline();
  }

  stop(): void {
    for (const subscription of this.subscriptions) {
      subscription.unsubscribe();
    }

    this.subscriptions = [];
    if (this.tracking) {
      this.tracking = false;
      this.sse.disconnect();
    }
    this.currentTraceId = null;
    this.seenEventIds.clear();
    this.lastEventIdBySpanId.clear();
    this.lastTraceEventId = -1;
    this.pendingEvents = [];
    this.traceSignal.set(null);
  }

  ngOnDestroy(): void {
    this.stop();
  }

  private loadBaseline(): void {
    const traceId = this.currentTraceId;
    if (traceId === null) {
      return;
    }

    this.loadingBaseline = true;
    this.api.getTrace(traceId).subscribe((trace) => {
      if (this.currentTraceId !== traceId) {
        return; // Stopped or switched to a different trace while this request was in flight.
      }

      this.traceSignal.set(trace);
      this.loadingBaseline = false;

      const buffered = [...this.pendingEvents].sort((a, b) => a.eventId - b.eventId);
      this.pendingEvents = [];
      for (const event of buffered) {
        this.mergeEvent(event);
      }
    });
  }

  private onEvent(event: FlowEvent): void {
    if (event.traceId !== this.currentTraceId) {
      return;
    }

    if (this.seenEventIds.has(event.eventId)) {
      return;
    }

    this.seenEventIds.add(event.eventId);

    if (this.loadingBaseline) {
      this.pendingEvents.push(event);
      return;
    }

    this.mergeEvent(event);
  }

  private mergeEvent(event: FlowEvent): void {
    const current = this.traceSignal();
    if (!current) {
      return;
    }

    if (event.trace) {
      if (event.eventId < this.lastTraceEventId) {
        return;
      }

      this.lastTraceEventId = event.eventId;
      this.traceSignal.set({ ...current, ...event.trace, spans: current.spans });
      return;
    }

    if (event.span) {
      const spanId = event.span.spanId;
      const lastAppliedEventId = this.lastEventIdBySpanId.get(spanId) ?? -1;
      if (event.eventId < lastAppliedEventId) {
        return;
      }

      this.lastEventIdBySpanId.set(spanId, event.eventId);

      const spans = current.spans.filter((span) => span.spanId !== spanId);
      spans.push(event.span);
      spans.sort((a, b) => a.sequence - b.sequence);
      this.traceSignal.set({ ...current, spans, spanCount: spans.length });
    }
  }
}
