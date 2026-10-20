import { buildWaterfall } from './waterfall';
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
    expect(buildWaterfall(makeTrace({ spans: [] }))).toEqual([]);
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

    const rows = buildWaterfall(trace);

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

    const [row] = buildWaterfall(trace);

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

    const [row] = buildWaterfall(trace);

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

    const [row] = buildWaterfall(trace);

    expect(row.widthPercent).toBeCloseTo(100, 0);
  });
});
