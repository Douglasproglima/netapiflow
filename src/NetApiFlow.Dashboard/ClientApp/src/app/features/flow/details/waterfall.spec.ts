import { buildWaterfall, computeUnexplainedTime } from './waterfall';
import { FlowSpan, FlowTraceDetail } from '../../../core/models';

function makeTrace(
  overrides: Partial<FlowTraceDetail> & Pick<FlowTraceDetail, 'spans'>,
): FlowTraceDetail {
  return {
    traceId: 't1',
    method: 'GET',
    routeTemplate: '/api/orders/{id}',
    httpStatus: 200,
    status: 'Success',
    startedAt: '2026-01-01T00:00:00.000000Z',
    endedAt: '2026-01-01T00:00:00.100000Z',
    durationMicros: 100_000,
    spanCount: overrides.spans.length,
    truncated: false,
    errorMessage: null,
    tags: {},
    ...overrides,
  };
}

function makeSpan(overrides: Partial<FlowSpan> & Pick<FlowSpan, 'spanId' | 'startedAt'>): FlowSpan {
  return {
    parentSpanId: null,
    sequence: 0,
    component: 'C',
    operation: 'Op',
    type: 'Service',
    endedAt: null,
    durationMicros: null,
    status: 'Success',
    errorType: null,
    errorMessage: null,
    isErrorOrigin: false,
    tags: {},
    ...overrides,
  };
}

describe('buildWaterfall', () => {
  it('returns an empty list for a trace with no spans', () => {
    expect(buildWaterfall(makeTrace({ spans: [] }), 250_000)).toEqual([]);
  });

  it('orders rows by start time, not array order', () => {
    const trace = makeTrace({
      spans: [
        makeSpan({
          spanId: 'second',
          startedAt: '2026-01-01T00:00:00.050000Z',
          durationMicros: 10_000,
        }),
        makeSpan({
          spanId: 'first',
          startedAt: '2026-01-01T00:00:00.000000Z',
          durationMicros: 10_000,
        }),
      ],
    });

    const rows = buildWaterfall(trace, 250_000);

    expect(rows.map((r) => r.spanId)).toEqual(['first', 'second']);
  });

  it('computes offset and width as percentages of the trace total duration', () => {
    const trace = makeTrace({
      durationMicros: 100_000,
      spans: [
        makeSpan({
          spanId: 's1',
          startedAt: '2026-01-01T00:00:00.025000Z',
          durationMicros: 50_000,
        }),
      ],
    });

    const [row] = buildWaterfall(trace, 250_000);

    expect(row.offsetPercent).toBeCloseTo(25, 0);
    expect(row.widthPercent).toBeCloseTo(50, 0);
  });

  it('never derives the displayed duration from parsed timestamps', () => {
    const trace = makeTrace({
      durationMicros: 100_000,
      spans: [
        makeSpan({ spanId: 's1', startedAt: '2026-01-01T00:00:00.000000Z', durationMicros: 42 }),
      ],
    });

    const [row] = buildWaterfall(trace, 250_000);

    expect(row.durationMicros).toBe(42);
  });

  it('falls back to the furthest span end when the trace is still running', () => {
    const trace = makeTrace({
      status: 'Running',
      endedAt: null,
      durationMicros: null,
      spans: [
        makeSpan({
          spanId: 's1',
          startedAt: '2026-01-01T00:00:00.000000Z',
          endedAt: '2026-01-01T00:00:00.040000Z',
          durationMicros: 40_000,
        }),
      ],
    });

    const [row] = buildWaterfall(trace, 250_000);

    expect(row.widthPercent).toBeCloseTo(100, 0);
  });

  it('assigns the slow state when a span is at or above the slow threshold', () => {
    const trace = makeTrace({
      spans: [
        makeSpan({
          spanId: 's1',
          startedAt: '2026-01-01T00:00:00.000000Z',
          durationMicros: 300_000,
        }),
      ],
    });

    const [row] = buildWaterfall(trace, 250_000);

    expect(row.state).toBe('slow');
  });

  it('assigns error over the slow threshold', () => {
    const trace = makeTrace({
      spans: [
        makeSpan({
          spanId: 's1',
          startedAt: '2026-01-01T00:00:00.000000Z',
          durationMicros: 300_000,
          status: 'Error',
        }),
      ],
    });

    const [row] = buildWaterfall(trace, 250_000);

    expect(row.state).toBe('error');
  });
});

describe('computeUnexplainedTime', () => {
  it('returns null for a trace still running (partial data would mislead)', () => {
    const trace = makeTrace({
      status: 'Running',
      endedAt: null,
      durationMicros: null,
      spans: [makeSpan({ spanId: 's1', startedAt: '2026-01-01T00:00:00.000000Z' })],
    });

    expect(computeUnexplainedTime(trace)).toBeNull();
  });

  it('returns null for a trace with no spans (no basis to judge coverage)', () => {
    expect(computeUnexplainedTime(makeTrace({ spans: [] }))).toBeNull();
  });

  it('computes the gap for a single span with no overlap', () => {
    const trace = makeTrace({
      durationMicros: 100_000,
      spans: [
        makeSpan({
          spanId: 's1',
          startedAt: '2026-01-01T00:00:00.000000Z',
          endedAt: '2026-01-01T00:00:00.060000Z',
          durationMicros: 60_000,
        }),
      ],
    });

    const result = computeUnexplainedTime(trace);

    expect(result?.unexplainedMicros).toBe(40_000);
    expect(result?.unexplainedPercent).toBeCloseTo(40, 0);
  });

  it('does not double-count a child span fully contained in its parent', () => {
    const trace = makeTrace({
      durationMicros: 100_000,
      spans: [
        makeSpan({
          spanId: 'parent',
          parentSpanId: null,
          startedAt: '2026-01-01T00:00:00.000000Z',
          endedAt: '2026-01-01T00:00:00.100000Z',
          durationMicros: 100_000,
        }),
        makeSpan({
          spanId: 'child',
          parentSpanId: 'parent',
          startedAt: '2026-01-01T00:00:00.020000Z',
          endedAt: '2026-01-01T00:00:00.070000Z',
          durationMicros: 50_000,
        }),
      ],
    });

    const result = computeUnexplainedTime(trace);

    // Naive sum (100ms + 50ms = 150ms) would overshoot the 100ms trace entirely.
    expect(result?.unexplainedMicros).toBe(0);
    expect(result?.unexplainedPercent).toBe(0);
  });

  it('does not double-count two sibling branches that overlap on the wall clock (Task.WhenAll)', () => {
    const trace = makeTrace({
      durationMicros: 100_000,
      spans: [
        makeSpan({
          spanId: 'branch-a',
          startedAt: '2026-01-01T00:00:00.000000Z',
          endedAt: '2026-01-01T00:00:00.060000Z',
          durationMicros: 60_000,
        }),
        makeSpan({
          spanId: 'branch-b',
          startedAt: '2026-01-01T00:00:00.030000Z',
          endedAt: '2026-01-01T00:00:00.080000Z',
          durationMicros: 50_000,
        }),
      ],
    });

    const result = computeUnexplainedTime(trace);

    // Union of [0,60] and [30,80] is [0,80] = 80ms covered, not the naive 110ms sum.
    expect(result?.unexplainedMicros).toBe(20_000);
    expect(result?.unexplainedPercent).toBeCloseTo(20, 0);
  });

  it('ignores spans still running when measuring coverage', () => {
    const trace = makeTrace({
      durationMicros: 100_000,
      spans: [
        makeSpan({
          spanId: 'done',
          startedAt: '2026-01-01T00:00:00.000000Z',
          endedAt: '2026-01-01T00:00:00.050000Z',
          durationMicros: 50_000,
        }),
        makeSpan({
          spanId: 'still-running',
          startedAt: '2026-01-01T00:00:00.050000Z',
          endedAt: null,
          durationMicros: null,
          status: 'Running',
        }),
      ],
    });

    const result = computeUnexplainedTime(trace);

    expect(result?.unexplainedMicros).toBe(50_000);
  });
});
