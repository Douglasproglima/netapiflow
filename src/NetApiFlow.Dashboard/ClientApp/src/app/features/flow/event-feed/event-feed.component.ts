import { Component, OnDestroy, OnInit, inject, signal } from '@angular/core';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { SseService } from '../../../core/sse.service';
import { FlowEvent } from '../../../core/models';
import { eventTypeTint, EventTypeTint } from '../../../shared/event-type-tint';
import { TranslatePipe } from '../../../core/i18n/translate.pipe';

const MAX_VISIBLE_EVENTS = 200;

/**
 * Raw live event log for the `/flow` workspace (ADR-0010): every event from `GET /stream` as it
 * arrives, newest first, independent of whichever trace is currently tracked in the graph.
 * `SseService.connect()`/`disconnect()` are reference-counted precisely so this component can
 * keep the stream open on its own, alongside `LiveTracePlayerService`'s own connect/disconnect
 * calls, without either tearing down the other's connection.
 */
@Component({
  selector: 'app-event-feed',
  templateUrl: './event-feed.component.html',
  styleUrl: './event-feed.component.scss',
  imports: [MatCardModule, MatButtonModule, TranslatePipe],
})
export class EventFeedComponent implements OnInit, OnDestroy {
  private readonly sse = inject(SseService);
  private subscription = this.sse.events$.subscribe((event) => this.onEvent(event));

  readonly events = signal<FlowEvent[]>([]);
  readonly collapsed = signal(false);
  readonly connectionState = this.sse.state;

  toggleCollapsed(): void {
    this.collapsed.update((collapsed) => !collapsed);
  }

  ngOnInit(): void {
    this.sse.connect();
  }

  ngOnDestroy(): void {
    this.subscription.unsubscribe();
    this.sse.disconnect();
  }

  private onEvent(event: FlowEvent): void {
    this.events.update((events) => [event, ...events].slice(0, MAX_VISIBLE_EVENTS));
  }

  eventLabel(event: FlowEvent): string {
    const span = event.span;
    return span ? `${span.component}.${span.operation}` : event.traceId;
  }

  eventTint(event: FlowEvent): EventTypeTint {
    return eventTypeTint(event.type);
  }

  shortTraceId(traceId: string): string {
    return traceId.length > 8 ? traceId.slice(0, 8) : traceId;
  }
}
