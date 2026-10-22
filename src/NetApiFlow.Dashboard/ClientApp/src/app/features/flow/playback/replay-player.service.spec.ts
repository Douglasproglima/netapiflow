import { TestBed } from '@angular/core/testing';
import { ReplayPlayerService } from './replay-player.service';
import { FlowSpan, FlowTraceDetail } from '../../../core/models';

function makeSpan(
  spanId: string,
  sequence: number,
  startedAt: string,
  durationMicros: number,
): FlowSpan {
  return {
    spanId,
    parentSpanId: null,
    sequence,
    component: 'C',
    operation: spanId,
    type: 'Service',
    startedAt,
    endedAt: null,
    durationMicros,
    status: 'Success',
    errorType: null,
    errorMessage: null,
    isErrorOrigin: false,
    tags: {},
  };
}

function makeTrace(spans: FlowSpan[]): FlowTraceDetail {
  return {
    traceId: 't1',
    method: 'GET',
    routeTemplate: '/api/orders/{id}',
    httpStatus: 200,
    status: 'Success',
    startedAt: spans[0]?.startedAt ?? '2026-01-01T00:00:00.000000Z',
    endedAt: null,
    durationMicros: 1_000_000,
    spanCount: spans.length,
    truncated: false,
    errorMessage: null,
    tags: {},
    spans,
  };
}

describe('ReplayPlayerService', () => {
  let service: ReplayPlayerService;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [ReplayPlayerService] });
    service = TestBed.inject(ReplayPlayerService);
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it('reveals spans step by step in start order, regardless of array order', () => {
    const trace = makeTrace([
      makeSpan('second', 1, '2026-01-01T00:00:00.050000Z', 10_000),
      makeSpan('first', 0, '2026-01-01T00:00:00.000000Z', 10_000),
      makeSpan('third', 2, '2026-01-01T00:00:00.100000Z', 10_000),
    ]);
    service.load(trace);

    expect(service.visibleSpans()).toEqual([]);

    service.stepForward();
    expect(service.visibleSpans().map((s) => s.spanId)).toEqual(['first']);

    service.stepForward();
    expect(service.visibleSpans().map((s) => s.spanId)).toEqual(['first', 'second']);

    service.stepForward();
    expect(service.visibleSpans().map((s) => s.spanId)).toEqual(['first', 'second', 'third']);
    expect(service.state()).toBe('finished');
  });

  it('never changes the displayed durationMicros of a span regardless of playback speed', () => {
    const trace = makeTrace([makeSpan('s1', 0, '2026-01-01T00:00:00.000000Z', 12_345)]);
    service.load(trace);

    for (const speed of [0.25, 1, 4]) {
      service.setSpeed(speed);
      service.restart();
      service.stepForward();
      expect(service.visibleSpans()[0].durationMicros).toBe(12_345);
    }
  });

  it('plays through the timeline automatically, pacing by speed, and stops changing state once finished', async () => {
    vi.useFakeTimers();
    const trace = makeTrace([
      makeSpan('first', 0, '2026-01-01T00:00:00.000000Z', 1_000),
      makeSpan('second', 1, '2026-01-01T00:00:00.001000Z', 1_000),
    ]);
    service.load(trace);
    service.setSpeed(1);

    service.play();
    expect(service.state()).toBe('playing');

    await vi.advanceTimersByTimeAsync(500); // under the 150ms-at-1x minimum doubled for two steps is still way more than 500ms total
    await vi.advanceTimersByTimeAsync(500);

    expect(service.state()).toBe('finished');
    expect(service.visibleSpans().map((s) => s.spanId)).toEqual(['first', 'second']);
  });

  it('plays faster at higher speed than at lower speed', async () => {
    vi.useFakeTimers();
    const trace = makeTrace([makeSpan('s1', 0, '2026-01-01T00:00:00.000000Z', 1_000)]);

    service.load(trace);
    service.setSpeed(4);
    service.play();
    await vi.advanceTimersByTimeAsync(40); // 150ms / 4 = 37.5ms, so this should be enough
    expect(service.state()).toBe('finished');

    service.load(trace);
    service.setSpeed(0.25);
    service.play();
    await vi.advanceTimersByTimeAsync(40);
    expect(service.state()).toBe('playing'); // 150ms / 0.25 = 600ms, nowhere near done yet
  });

  it('pause stops the timeline without losing progress, and restart resets it to the beginning', async () => {
    vi.useFakeTimers();
    const trace = makeTrace([
      makeSpan('first', 0, '2026-01-01T00:00:00.000000Z', 1_000),
      makeSpan('second', 1, '2026-01-01T00:00:00.001000Z', 1_000),
    ]);
    service.load(trace);
    service.play();

    await vi.advanceTimersByTimeAsync(150);
    service.pause();
    expect(service.state()).toBe('paused');
    const progressAtPause = service.visibleSpans().length;

    await vi.advanceTimersByTimeAsync(1000);
    expect(service.visibleSpans()).toHaveLength(progressAtPause); // no further progress while paused

    service.restart();
    expect(service.visibleSpans()).toEqual([]);
    expect(service.state()).toBe('paused');
  });

  it('stepBackward undoes one step, never going below zero', () => {
    const trace = makeTrace([
      makeSpan('first', 0, '2026-01-01T00:00:00.000000Z', 1_000),
      makeSpan('second', 1, '2026-01-01T00:00:00.001000Z', 1_000),
    ]);
    service.load(trace);
    service.stepForward();
    service.stepForward();
    expect(service.visibleSpans()).toHaveLength(2);

    service.stepBackward();
    expect(service.visibleSpans().map((s) => s.spanId)).toEqual(['first']);
    expect(service.state()).toBe('paused');

    service.stepBackward();
    service.stepBackward();
    expect(service.visibleSpans()).toEqual([]);
  });

  it('seek jumps directly to a step index, clamped to the valid range', () => {
    const trace = makeTrace([
      makeSpan('first', 0, '2026-01-01T00:00:00.000000Z', 1_000),
      makeSpan('second', 1, '2026-01-01T00:00:00.001000Z', 1_000),
    ]);
    service.load(trace);

    service.seek(1);
    expect(service.visibleSpans().map((s) => s.spanId)).toEqual(['first']);
    expect(service.state()).toBe('paused');

    service.seek(100);
    expect(service.visibleSpans()).toHaveLength(2);
    expect(service.state()).toBe('finished');

    service.seek(-5);
    expect(service.visibleSpans()).toEqual([]);
  });
});
