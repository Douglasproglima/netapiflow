import { buildTraceDiff, TraceDiffInput } from './trace-diff-builder';
import { ComponentType, FlowSpan, FlowStatus, FlowTraceDetail } from '../../../core/models';

const SLOW_THRESHOLD_MICROS = 250_000;

function span(
  overrides: Partial<FlowSpan> & Pick<FlowSpan, 'spanId' | 'component' | 'operation'>,
): FlowSpan {
  return {
    parentSpanId: null,
    sequence: 0,
    type: 'Service' as ComponentType,
    startedAt: '2026-01-01T00:00:00.000000Z',
    endedAt: '2026-01-01T00:00:00.100000Z',
    durationMicros: 100_000,
    status: 'Success' as FlowStatus,
    errorType: null,
    errorMessage: null,
    isErrorOrigin: false,
    tags: {},
    logs: [],
    ...overrides,
  };
}

function trace(traceId: string, spans: FlowSpan[]): FlowTraceDetail {
  return {
    traceId,
    method: 'GET',
    routeTemplate: '/api/orders/{id}',
    httpStatus: 200,
    status: 'Success',
    startedAt: '2026-01-01T00:00:00.000000Z',
    endedAt: '2026-01-01T00:00:00.500000Z',
    durationMicros: 500_000,
    spanCount: spans.length,
    truncated: false,
    errorMessage: null,
    tags: {},
    spans,
  };
}

function input(overrides: Partial<TraceDiffInput> & Pick<TraceDiffInput, 'traceA' | 'traceB'>) {
  return { slowThresholdMicros: SLOW_THRESHOLD_MICROS, ...overrides };
}

describe('buildTraceDiff', () => {
  it('matches a span present in both executions, with the duration delta B - A', () => {
    const traceA = trace('a', [
      span({
        spanId: 's1',
        component: 'OrderController',
        operation: 'Get',
        durationMicros: 100_000,
      }),
    ]);
    const traceB = trace('b', [
      span({
        spanId: 's2',
        component: 'OrderController',
        operation: 'Get',
        durationMicros: 150_000,
      }),
    ]);

    const rows = buildTraceDiff(input({ traceA, traceB }));

    expect(rows).toHaveLength(1);
    expect(rows[0]).toMatchObject({
      kind: 'matched',
      component: 'OrderController',
      operation: 'Get',
      countA: 1,
      countB: 1,
      durationMicrosA: 100_000,
      durationMicrosB: 150_000,
      deltaMicros: 50_000,
      stateA: 'completed',
      stateB: 'completed',
    });
  });

  it('marks a step present only in A as onlyInA, with B shown as not-executed and no zeroed duration', () => {
    const traceA = trace('a', [
      span({ spanId: 's1', component: 'OrderController', operation: 'Get' }),
      span({ spanId: 's2', component: 'OrderCache', operation: 'TryGet', durationMicros: 5_000 }),
    ]);
    const traceB = trace('b', [
      span({ spanId: 's3', component: 'OrderController', operation: 'Get' }),
    ]);

    const rows = buildTraceDiff(input({ traceA, traceB }));
    const cacheRow = rows.find((row) => row.component === 'OrderCache');

    expect(cacheRow).toMatchObject({
      kind: 'onlyInA',
      countA: 1,
      countB: 0,
      durationMicrosA: 5_000,
      durationMicrosB: null,
      deltaMicros: null,
      stateA: 'completed',
      stateB: 'not-executed',
    });
  });

  it('marks a step present only in B as onlyInB, with A shown as not-executed and no zeroed duration', () => {
    const traceA = trace('a', [
      span({ spanId: 's1', component: 'OrderController', operation: 'Get' }),
    ]);
    const traceB = trace('b', [
      span({ spanId: 's2', component: 'OrderController', operation: 'Get' }),
      span({ spanId: 's3', component: 'OrderCache', operation: 'TryGet', durationMicros: 5_000 }),
    ]);

    const rows = buildTraceDiff(input({ traceA, traceB }));
    const cacheRow = rows.find((row) => row.component === 'OrderCache');

    expect(cacheRow).toMatchObject({
      kind: 'onlyInB',
      countA: 0,
      countB: 1,
      durationMicrosA: null,
      durationMicrosB: 5_000,
      deltaMicros: null,
      stateA: 'not-executed',
      stateB: 'completed',
    });
  });

  it('sums repeated spans of the same key within one execution (N+1) into a single row', () => {
    const traceA = trace('a', [
      span({
        spanId: 's1',
        component: 'OrderRepository',
        operation: 'FindById',
        durationMicros: 10_000,
      }),
      span({
        spanId: 's2',
        component: 'OrderRepository',
        operation: 'FindById',
        durationMicros: 12_000,
      }),
      span({
        spanId: 's3',
        component: 'OrderRepository',
        operation: 'FindById',
        durationMicros: 11_000,
      }),
    ]);
    const traceB = trace('b', [
      span({
        spanId: 's4',
        component: 'OrderRepository',
        operation: 'FindById',
        durationMicros: 10_000,
      }),
    ]);

    const rows = buildTraceDiff(input({ traceA, traceB }));

    expect(rows).toHaveLength(1);
    expect(rows[0]).toMatchObject({
      kind: 'matched',
      countA: 3,
      countB: 1,
      durationMicrosA: 33_000,
      durationMicrosB: 10_000,
      deltaMicros: -23_000,
    });
  });

  it('compares the same execution against itself as all matched with zero delta', () => {
    const spans = [
      span({ spanId: 's1', component: 'OrderController', operation: 'Get', sequence: 0 }),
      span({
        spanId: 's2',
        component: 'OrderRepository',
        operation: 'FindById',
        sequence: 1,
        parentSpanId: 's1',
        durationMicros: 20_000,
      }),
    ];
    const traceA = trace('same', spans);
    const traceB = trace('same', spans);

    const rows = buildTraceDiff(input({ traceA, traceB }));

    expect(rows.every((row) => row.kind === 'matched')).toBe(true);
    expect(rows.every((row) => row.deltaMicros === 0)).toBe(true);
  });

  it('marks the aggregated state as error when any span of the group failed', () => {
    const traceA = trace('a', [
      span({
        spanId: 's1',
        component: 'OrderRepository',
        operation: 'FindById',
        status: 'Error',
        durationMicros: 5_000,
      }),
      span({
        spanId: 's2',
        component: 'OrderRepository',
        operation: 'FindById',
        durationMicros: 5_000,
      }),
    ]);
    const traceB = trace('b', [
      span({
        spanId: 's3',
        component: 'OrderRepository',
        operation: 'FindById',
        durationMicros: 5_000,
      }),
    ]);

    const rows = buildTraceDiff(input({ traceA, traceB }));

    expect(rows[0].stateA).toBe('error');
    expect(rows[0].stateB).toBe('completed');
  });

  it('marks the aggregated state as slow when the summed duration crosses the threshold', () => {
    const traceA = trace('a', [
      span({
        spanId: 's1',
        component: 'OrderRepository',
        operation: 'FindById',
        durationMicros: SLOW_THRESHOLD_MICROS,
      }),
    ]);
    const traceB = trace('b', [
      span({
        spanId: 's2',
        component: 'OrderRepository',
        operation: 'FindById',
        durationMicros: 1_000,
      }),
    ]);

    const rows = buildTraceDiff(input({ traceA, traceB }));

    expect(rows[0].stateA).toBe('slow');
    expect(rows[0].stateB).toBe('completed');
  });

  it('returns an empty list when both executions have no spans', () => {
    const traceA = trace('a', []);
    const traceB = trace('b', []);

    expect(buildTraceDiff(input({ traceA, traceB }))).toEqual([]);
  });
});
