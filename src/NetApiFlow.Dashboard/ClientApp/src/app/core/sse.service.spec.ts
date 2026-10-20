import { TestBed } from '@angular/core/testing';
import { SseService } from './sse.service';
import { FlowEvent } from './models';

type Listener = (event: Event) => void;

class FakeEventSource {
  static instances: FakeEventSource[] = [];

  onopen: (() => void) | null = null;
  onerror: (() => void) | null = null;
  closed = false;

  private readonly listeners = new Map<string, Listener[]>();

  constructor(public readonly url: string) {
    FakeEventSource.instances.push(this);
  }

  addEventListener(type: string, listener: Listener): void {
    const existing = this.listeners.get(type) ?? [];
    existing.push(listener);
    this.listeners.set(type, existing);
  }

  close(): void {
    this.closed = true;
  }

  open(): void {
    this.onopen?.();
  }

  error(): void {
    this.onerror?.();
  }

  emit(type: string, data: unknown): void {
    const event = { data: JSON.stringify(data) } as MessageEvent<string>;
    for (const listener of this.listeners.get(type) ?? []) {
      listener(event);
    }
  }

  static latest(): FakeEventSource {
    const instance = FakeEventSource.instances.at(-1);
    if (!instance) {
      throw new Error('No FakeEventSource was constructed.');
    }

    return instance;
  }
}

function makeSpanStarted(eventId: number, traceId = 't1'): FlowEvent {
  return {
    eventId,
    type: 'SpanStarted',
    traceId,
    timestamp: '2026-01-01T00:00:00.000000Z',
    span: {
      spanId: `s${eventId}`,
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
    },
  };
}

describe('SseService', () => {
  let service: SseService;

  beforeEach(() => {
    FakeEventSource.instances = [];
    vi.stubGlobal('EventSource', FakeEventSource);
    TestBed.configureTestingModule({});
    service = TestBed.inject(SseService);
  });

  afterEach(() => {
    service.disconnect();
    vi.unstubAllGlobals();
    vi.useRealTimers();
  });

  it('connects to the relative stream URL and parses a named event into a FlowEvent', () => {
    const received: FlowEvent[] = [];
    service.events$.subscribe((event) => received.push(event));

    service.connect();
    const source = FakeEventSource.latest();
    expect(source.url).toBe('api/stream');

    source.open();
    expect(service.state()).toBe('open');

    source.emit('SpanStarted', makeSpanStarted(1));
    expect(received).toEqual([makeSpanStarted(1)]);
  });

  it('ignores ping events', () => {
    const received: FlowEvent[] = [];
    service.events$.subscribe((event) => received.push(event));

    service.connect();
    FakeEventSource.latest().emit('ping', {});

    expect(received).toEqual([]);
  });

  it('reconnects with exponential backoff and sends the last seen eventId as a query string', async () => {
    vi.useFakeTimers();

    service.connect();
    const first = FakeEventSource.latest();
    first.open();
    first.emit('SpanStarted', makeSpanStarted(7));

    first.error();
    expect(service.state()).toBe('closed');
    expect(first.closed).toBe(true);

    await vi.advanceTimersByTimeAsync(999);
    expect(FakeEventSource.instances.length).toBe(1); // backoff not elapsed yet

    await vi.advanceTimersByTimeAsync(1);
    expect(FakeEventSource.instances.length).toBe(2);
    expect(FakeEventSource.latest().url).toBe('api/stream?lastEventId=7');

    // Second failure: backoff doubles (2000ms), not another flat 1000ms.
    FakeEventSource.latest().error();
    await vi.advanceTimersByTimeAsync(1999);
    expect(FakeEventSource.instances.length).toBe(2);
    await vi.advanceTimersByTimeAsync(1);
    expect(FakeEventSource.instances.length).toBe(3);
  });

  it('emits reconnected$ after a reconnect, but not on the first connect', async () => {
    vi.useFakeTimers();
    let reconnectedCount = 0;
    service.reconnected$.subscribe(() => reconnectedCount++);

    service.connect();
    FakeEventSource.latest().open();
    expect(reconnectedCount).toBe(0);

    FakeEventSource.latest().error();
    await vi.advanceTimersByTimeAsync(1000);
    FakeEventSource.latest().open();

    expect(reconnectedCount).toBe(1);
  });

  it('does not reconnect after an explicit disconnect', async () => {
    vi.useFakeTimers();

    service.connect();
    FakeEventSource.latest().open();
    service.disconnect();
    FakeEventSource.latest().error();

    await vi.advanceTimersByTimeAsync(60000);
    expect(FakeEventSource.instances.length).toBe(1);
  });
});
