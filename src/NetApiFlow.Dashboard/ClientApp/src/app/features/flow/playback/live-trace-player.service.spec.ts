import { TestBed } from '@angular/core/testing';
import { Subject } from 'rxjs';
import { LiveTracePlayerService } from './live-trace-player.service';
import { ApiService } from '../../../core/api.service';
import { SseService, SseConnectionState } from '../../../core/sse.service';
import { FlowEvent, FlowSpan, FlowTraceDetail } from '../../../core/models';

function makeTrace(spans: FlowSpan[]): FlowTraceDetail {
  return {
    traceId: 't1',
    method: 'GET',
    routeTemplate: '/api/orders/{id}',
    httpStatus: null,
    status: 'Running',
    startedAt: '2026-01-01T00:00:00.000000Z',
    endedAt: null,
    durationMicros: null,
    spanCount: spans.length,
    truncated: false,
    errorMessage: null,
    tags: {},
    spans,
  };
}

function makeSpan(spanId: string, overrides: Partial<FlowSpan> = {}): FlowSpan {
  return {
    spanId,
    parentSpanId: null,
    sequence: 0,
    component: 'C',
    operation: 'Op',
    type: 'Service',
    startedAt: '2026-01-01T00:00:00.000000Z',
    endedAt: null,
    durationMicros: null,
    status: 'Running',
    errorType: null,
    errorMessage: null,
    isErrorOrigin: false,
    tags: {},
    ...overrides,
  };
}

function spanEvent(eventId: number, span: FlowSpan): FlowEvent {
  return {
    eventId,
    type: 'SpanCompleted',
    traceId: 't1',
    timestamp: '2026-01-01T00:00:00.000000Z',
    span,
  };
}

describe('LiveTracePlayerService', () => {
  let service: LiveTracePlayerService;
  let getTraceCalls: Subject<FlowTraceDetail>[];
  let events: Subject<FlowEvent>;
  let reconnected: Subject<void>;

  beforeEach(() => {
    getTraceCalls = [];
    events = new Subject<FlowEvent>();
    reconnected = new Subject<void>();

    const fakeApi = {
      getTrace: () => {
        const subject = new Subject<FlowTraceDetail>();
        getTraceCalls.push(subject);
        return subject.asObservable();
      },
    };

    const fakeSse = {
      events$: events.asObservable(),
      reconnected$: reconnected.asObservable(),
      state: () => 'open' as SseConnectionState,
      connect: vi.fn(),
      disconnect: vi.fn(),
    };

    TestBed.configureTestingModule({
      providers: [
        LiveTracePlayerService,
        { provide: ApiService, useValue: fakeApi },
        { provide: SseService, useValue: fakeSse },
      ],
    });

    service = TestBed.inject(LiveTracePlayerService);
  });

  afterEach(() => {
    service.stop();
  });

  it('converges to the correct final state when events arrive out of order and duplicated', () => {
    service.track('t1');
    getTraceCalls[0].next(makeTrace([makeSpan('root')]));
    getTraceCalls[0].complete();

    // Newer version first (eventId 5), then an older, stale version of the same span (eventId
    // 3) that must NOT overwrite it, then a duplicate of the newer one that must be a no-op.
    const newer = spanEvent(5, makeSpan('s2', { status: 'Success', durationMicros: 100 }));
    const older = spanEvent(3, makeSpan('s2', { status: 'Running', durationMicros: null }));

    events.next(newer);
    events.next(older);
    events.next(newer);

    const span = service.trace()?.spans.find((s) => s.spanId === 's2');
    expect(span?.status).toBe('Success');
    expect(span?.durationMicros).toBe(100);
    expect(service.trace()?.spans).toHaveLength(2);
  });

  it('ignores events for a trace other than the one being tracked', () => {
    service.track('t1');
    getTraceCalls[0].next(makeTrace([makeSpan('root')]));
    getTraceCalls[0].complete();

    events.next({ ...spanEvent(1, makeSpan('other')), traceId: 'other-trace' });

    expect(service.trace()?.spans).toHaveLength(1);
  });

  it('buffers events that arrive while the baseline is loading, then applies them in order', () => {
    service.track('t1');

    // Arrives before the GET /traces/{traceId} response: must not be dropped.
    events.next(spanEvent(2, makeSpan('s2', { status: 'Success' })));

    getTraceCalls[0].next(makeTrace([makeSpan('root')]));
    getTraceCalls[0].complete();

    expect(service.trace()?.spans.find((s) => s.spanId === 's2')?.status).toBe('Success');
  });

  it('reloads the consolidated trace on reconnect before resuming live updates', () => {
    service.track('t1');
    getTraceCalls[0].next(makeTrace([makeSpan('root')]));
    getTraceCalls[0].complete();

    reconnected.next();
    expect(getTraceCalls).toHaveLength(2);

    getTraceCalls[1].next(makeTrace([makeSpan('root'), makeSpan('resynced')]));
    getTraceCalls[1].complete();

    expect(
      service
        .trace()
        ?.spans.map((s) => s.spanId)
        .sort(),
    ).toEqual(['resynced', 'root']);
  });

  it('stop() disconnects and clears the tracked state', () => {
    service.track('t1');
    getTraceCalls[0].next(makeTrace([makeSpan('root')]));
    getTraceCalls[0].complete();

    service.stop();

    expect(service.trace()).toBeNull();
  });
});
