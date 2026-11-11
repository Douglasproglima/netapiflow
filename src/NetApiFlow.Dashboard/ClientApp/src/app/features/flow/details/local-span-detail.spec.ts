import { FlowSpan, FlowTraceDetail } from '../../../core/models';
import { buildLocalSpanDetail } from './local-span-detail';

function span(overrides: Partial<FlowSpan>): FlowSpan {
  return {
    spanId: 's0',
    parentSpanId: null,
    sequence: 0,
    component: 'Component',
    operation: 'Operation',
    type: 'Service',
    startedAt: '2026-01-01T00:00:00.000000Z',
    endedAt: '2026-01-01T00:00:00.000100Z',
    durationMicros: 100,
    status: 'Success',
    errorType: null,
    errorMessage: null,
    isErrorOrigin: false,
    tags: {},
    logs: [],
    ...overrides,
  };
}

function trace(spans: FlowSpan[]): FlowTraceDetail {
  return {
    traceId: 't1',
    method: 'GET',
    routeTemplate: '/api/orders/{id}',
    httpStatus: 200,
    status: 'Success',
    startedAt: '2026-01-01T00:00:00.000000Z',
    endedAt: '2026-01-01T00:00:00.000300Z',
    durationMicros: 300,
    spanCount: spans.length,
    truncated: false,
    errorMessage: null,
    tags: {},
    spans,
  };
}

describe('buildLocalSpanDetail', () => {
  it('returns null when the span id is not part of the trace', () => {
    const t = trace([span({ spanId: 'root' })]);
    expect(buildLocalSpanDetail(t, 'missing')).toBeNull();
  });

  it('returns the span with no ancestors or children when it is alone', () => {
    const t = trace([span({ spanId: 'root' })]);
    const detail = buildLocalSpanDetail(t, 'root');

    expect(detail?.span.spanId).toBe('root');
    expect(detail?.ancestors).toEqual([]);
    expect(detail?.children).toEqual([]);
    expect(detail?.medianDurationMicros).toBeNull();
    expect(detail?.deltaFromMedianMicros).toBeNull();
  });

  it('orders ancestors from the trace root down to the direct parent', () => {
    const t = trace([
      span({ spanId: 'root', parentSpanId: null, component: 'Root', operation: 'Op0' }),
      span({ spanId: 'mid', parentSpanId: 'root', component: 'Mid', operation: 'Op1' }),
      span({ spanId: 'leaf', parentSpanId: 'mid', component: 'Leaf', operation: 'Op2' }),
    ]);

    const detail = buildLocalSpanDetail(t, 'leaf');

    expect(detail?.ancestors.map((ref) => ref.spanId)).toEqual(['root', 'mid']);
  });

  it('lists direct children only', () => {
    const t = trace([
      span({ spanId: 'root', parentSpanId: null }),
      span({ spanId: 'childA', parentSpanId: 'root' }),
      span({ spanId: 'childB', parentSpanId: 'root' }),
      span({ spanId: 'grandchild', parentSpanId: 'childA' }),
    ]);

    const detail = buildLocalSpanDetail(t, 'root');

    expect(detail?.children.map((ref) => ref.spanId).sort()).toEqual(['childA', 'childB']);
  });
});
