import { TestBed } from '@angular/core/testing';
import { Subject } from 'rxjs';
import { EventFeedComponent } from './event-feed.component';
import { SseService } from '../../../core/sse.service';
import { FlowEvent } from '../../../core/models';

function spanStarted(eventId: number): FlowEvent {
  return {
    eventId,
    type: 'SpanStarted',
    traceId: 't1',
    timestamp: `2026-01-01T00:00:0${eventId}.000000Z`,
    span: {
      spanId: `s${eventId}`,
      parentSpanId: null,
      sequence: 0,
      component: 'OrderService',
      operation: 'GetAsync',
      type: 'Service',
      startedAt: `2026-01-01T00:00:0${eventId}.000000Z`,
      endedAt: null,
      durationMicros: null,
      status: 'Running',
      errorType: null,
      errorMessage: null,
      isErrorOrigin: false,
      tags: {},
    },
  };
}

describe('EventFeedComponent', () => {
  it('connects on init, lists events newest first, and disconnects on destroy', () => {
    const events$ = new Subject<FlowEvent>();
    const fakeSse = {
      events$: events$.asObservable(),
      connect: vi.fn(),
      disconnect: vi.fn(),
    };

    TestBed.configureTestingModule({
      providers: [{ provide: SseService, useValue: fakeSse }],
    });

    const fixture = TestBed.createComponent(EventFeedComponent);
    fixture.detectChanges();
    expect(fakeSse.connect).toHaveBeenCalledTimes(1);

    events$.next(spanStarted(1));
    events$.next(spanStarted(2));

    expect(fixture.componentInstance.events().map((event) => event.eventId)).toEqual([2, 1]);
    expect(fixture.componentInstance.eventLabel(spanStarted(1))).toBe('OrderService.GetAsync');

    fixture.destroy();
    expect(fakeSse.disconnect).toHaveBeenCalledTimes(1);
  });

  it('toggles collapsed state', () => {
    const fakeSse = {
      events$: new Subject<FlowEvent>().asObservable(),
      connect: vi.fn(),
      disconnect: vi.fn(),
    };

    TestBed.configureTestingModule({
      providers: [{ provide: SseService, useValue: fakeSse }],
    });

    const fixture = TestBed.createComponent(EventFeedComponent);
    fixture.detectChanges();

    expect(fixture.componentInstance.collapsed()).toBe(false);
    fixture.componentInstance.toggleCollapsed();
    expect(fixture.componentInstance.collapsed()).toBe(true);
    fixture.componentInstance.toggleCollapsed();
    expect(fixture.componentInstance.collapsed()).toBe(false);
  });
});
