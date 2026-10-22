import { buildGraph, GraphBuilderInput } from './graph-builder';
import { ComponentType, FlowSpan, FlowStatus } from '../../../core/models';

const SLOW_THRESHOLD_MICROS = 250_000;

function span(
  overrides: Partial<FlowSpan> &
    Pick<FlowSpan, 'spanId' | 'parentSpanId' | 'sequence' | 'component' | 'operation'>,
): FlowSpan {
  return {
    type: 'Service' as ComponentType,
    startedAt: '2026-01-01T00:00:00.000000Z',
    endedAt: '2026-01-01T00:00:00.100000Z',
    durationMicros: 100_000,
    status: 'Success' as FlowStatus,
    errorType: null,
    errorMessage: null,
    isErrorOrigin: false,
    tags: {},
    ...overrides,
  };
}

function input(
  overrides: Partial<GraphBuilderInput> & Pick<GraphBuilderInput, 'spans'>,
): GraphBuilderInput {
  return {
    traceStatus: 'Success',
    slowThresholdMicros: SLOW_THRESHOLD_MICROS,
    ...overrides,
  };
}

describe('buildGraph', () => {
  it('returns an empty graph when there are no spans and no blueprint', () => {
    const result = buildGraph(input({ spans: [] }));

    expect(result.nodes).toEqual([]);
    expect(result.edges).toEqual([]);
  });

  it('lays out a 3-level hierarchy with one node per span and one edge per parent/child pair', () => {
    const spans: FlowSpan[] = [
      span({
        spanId: 'root',
        parentSpanId: null,
        sequence: 0,
        component: 'OrdersController',
        operation: 'Get',
      }),
      span({
        spanId: 'mid',
        parentSpanId: 'root',
        sequence: 1,
        component: 'OrderService',
        operation: 'GetAsync',
      }),
      span({
        spanId: 'leaf',
        parentSpanId: 'mid',
        sequence: 2,
        component: 'OrderRepository',
        operation: 'FindAsync',
      }),
    ];

    const result = buildGraph(input({ spans }));

    expect(result.nodes.map((n) => n.id).sort()).toEqual(['leaf', 'mid', 'root']);
    expect(result.edges).toHaveLength(2);
    expect(result.edges).toContainEqual(
      expect.objectContaining({ sourceId: 'root', targetId: 'mid' }),
    );
    expect(result.edges).toContainEqual(
      expect.objectContaining({ sourceId: 'mid', targetId: 'leaf' }),
    );

    const byId = Object.fromEntries(result.nodes.map((n) => [n.id, n]));
    // Left-to-right layout (guide seção 6.4): each level strictly to the right of the previous one.
    expect(byId['mid'].x).toBeGreaterThan(byId['root'].x);
    expect(byId['leaf'].x).toBeGreaterThan(byId['mid'].x);
    expect(byId['root'].sequence).toBe(0);
    expect(byId['mid'].sequence).toBe(1);
    expect(byId['leaf'].sequence).toBe(2);
  });

  it('finds the root even when its parentSpanId is a dangling, non-null ambient id (D-31)', () => {
    // Real data shape: the root span's parentSpanId points at ASP.NET Core's own Activity, which
    // is never one of NetApiFlow's own spans — not `null` as the earlier synthetic fixtures above
    // always assumed.
    const spans: FlowSpan[] = [
      span({
        spanId: 'root',
        parentSpanId: '55aa63b602cd359e',
        sequence: 0,
        component: 'OrdersController',
        operation: 'Get',
      }),
      span({
        spanId: 'mid',
        parentSpanId: 'root',
        sequence: 1,
        component: 'OrderService',
        operation: 'GetAsync',
      }),
    ];

    const result = buildGraph(input({ spans }));

    expect(result.nodes.map((n) => n.id).sort()).toEqual(['mid', 'root']);
    expect(result.edges).toContainEqual(
      expect.objectContaining({ sourceId: 'root', targetId: 'mid' }),
    );
  });

  it('marks a successful span under the slow threshold as completed', () => {
    const spans = [
      span({
        spanId: 'root',
        parentSpanId: null,
        sequence: 0,
        component: 'C',
        operation: 'Op',
        durationMicros: 1000,
        status: 'Success',
      }),
    ];

    const result = buildGraph(input({ spans }));

    expect(result.nodes[0].state).toBe('completed');
  });

  it('marks a successful span at or above the slow threshold as slow', () => {
    const spans = [
      span({
        spanId: 'root',
        parentSpanId: null,
        sequence: 0,
        component: 'C',
        operation: 'Op',
        durationMicros: SLOW_THRESHOLD_MICROS,
        status: 'Success',
      }),
    ];

    const result = buildGraph(input({ spans }));

    expect(result.nodes[0].state).toBe('slow');
  });

  it('marks an errored span as error', () => {
    const spans = [
      span({
        spanId: 'root',
        parentSpanId: null,
        sequence: 0,
        component: 'C',
        operation: 'Op',
        status: 'Error',
      }),
    ];

    const result = buildGraph(input({ spans }));

    expect(result.nodes[0].state).toBe('error');
  });

  it('marks a still-running span as running', () => {
    const spans = [
      span({
        spanId: 'root',
        parentSpanId: null,
        sequence: 0,
        component: 'C',
        operation: 'Op',
        status: 'Running',
        endedAt: null,
        durationMicros: null,
      }),
    ];

    const result = buildGraph(input({ spans, traceStatus: 'Running' }));

    expect(result.nodes[0].state).toBe('running');
  });

  it('infers a not-executed step from the blueprint once the trace has concluded', () => {
    const spans = [
      span({
        spanId: 'root',
        parentSpanId: null,
        sequence: 0,
        component: 'OrdersController',
        operation: 'Get',
      }),
    ];
    const blueprintSpans = [
      span({
        spanId: 'bp-root',
        parentSpanId: null,
        sequence: 0,
        component: 'OrdersController',
        operation: 'Get',
      }),
      span({
        spanId: 'bp-cache-miss',
        parentSpanId: 'bp-root',
        sequence: 1,
        component: 'CacheMissHandler',
        operation: 'Reload',
      }),
    ];

    const result = buildGraph(input({ spans, traceStatus: 'Success', blueprintSpans }));

    const ghost = result.nodes.find((n) => n.component === 'CacheMissHandler');
    expect(ghost).toBeDefined();
    expect(ghost?.state).toBe('not-executed');
    expect(ghost?.sequence).toBeNull();
    expect(result.edges).toContainEqual(
      expect.objectContaining({ sourceId: 'root', targetId: ghost?.id }),
    );
  });

  it('infers a waiting step from the blueprint while the trace is still running', () => {
    const spans = [
      span({
        spanId: 'root',
        parentSpanId: null,
        sequence: 0,
        component: 'OrdersController',
        operation: 'Get',
        status: 'Running',
        endedAt: null,
        durationMicros: null,
      }),
    ];
    const blueprintSpans = [
      span({
        spanId: 'bp-root',
        parentSpanId: null,
        sequence: 0,
        component: 'OrdersController',
        operation: 'Get',
      }),
      span({
        spanId: 'bp-service',
        parentSpanId: 'bp-root',
        sequence: 1,
        component: 'OrderService',
        operation: 'GetAsync',
      }),
    ];

    const result = buildGraph(input({ spans, traceStatus: 'Running', blueprintSpans }));

    const ghost = result.nodes.find((n) => n.component === 'OrderService');
    expect(ghost?.state).toBe('waiting');
  });

  it('does not ghost a blueprint step that did execute, even if the real span has a different id', () => {
    const spans = [
      span({
        spanId: 'root',
        parentSpanId: null,
        sequence: 0,
        component: 'OrdersController',
        operation: 'Get',
      }),
      span({
        spanId: 'real-service',
        parentSpanId: 'root',
        sequence: 1,
        component: 'OrderService',
        operation: 'GetAsync',
      }),
    ];
    const blueprintSpans = [
      span({
        spanId: 'bp-root',
        parentSpanId: null,
        sequence: 0,
        component: 'OrdersController',
        operation: 'Get',
      }),
      span({
        spanId: 'bp-service',
        parentSpanId: 'bp-root',
        sequence: 1,
        component: 'OrderService',
        operation: 'GetAsync',
      }),
    ];

    const result = buildGraph(input({ spans, blueprintSpans }));

    expect(result.nodes).toHaveLength(2);
    expect(result.nodes.some((n) => n.state === 'not-executed' || n.state === 'waiting')).toBe(
      false,
    );
  });

  it('keeps real siblings untouched when only one blueprint branch is missing', () => {
    const spans = [
      span({
        spanId: 'root',
        parentSpanId: null,
        sequence: 0,
        component: 'OrdersController',
        operation: 'Get',
      }),
      span({
        spanId: 'taken-branch',
        parentSpanId: 'root',
        sequence: 1,
        component: 'CacheHitHandler',
        operation: 'Serve',
      }),
    ];
    const blueprintSpans = [
      span({
        spanId: 'bp-root',
        parentSpanId: null,
        sequence: 0,
        component: 'OrdersController',
        operation: 'Get',
      }),
      span({
        spanId: 'bp-taken',
        parentSpanId: 'bp-root',
        sequence: 1,
        component: 'CacheHitHandler',
        operation: 'Serve',
      }),
      span({
        spanId: 'bp-missed',
        parentSpanId: 'bp-root',
        sequence: 2,
        component: 'CacheMissHandler',
        operation: 'Reload',
      }),
    ];

    const result = buildGraph(input({ spans, blueprintSpans }));

    expect(result.nodes).toHaveLength(3);
    const takenBranch = result.nodes.find((n) => n.component === 'CacheHitHandler');
    expect(takenBranch?.state).toBe('completed');
    const missedBranch = result.nodes.find((n) => n.component === 'CacheMissHandler');
    expect(missedBranch?.state).toBe('not-executed');
  });
});
