import { buildArchitectureGraph } from './architecture-graph-builder';
import { ArchitectureMap } from '../../core/models';

const CONDITIONAL_MAP: ArchitectureMap = {
  totalExecutions: 412,
  nodes: [
    {
      component: 'OrdersController',
      operation: 'GetOrder',
      type: 'Controller',
      executionCount: 412,
    },
    { component: 'OrderService', operation: 'FindAsync', type: 'Service', executionCount: 412 },
    { component: 'OrderCache', operation: 'TryGetAsync', type: 'Custom', executionCount: 58 },
    {
      component: 'OrderRepository',
      operation: 'FindAsync',
      type: 'Repository',
      executionCount: 354,
    },
  ],
  edges: [
    {
      fromComponent: 'OrdersController',
      fromOperation: 'GetOrder',
      toComponent: 'OrderService',
      toOperation: 'FindAsync',
      callCount: 412,
    },
    {
      fromComponent: 'OrderService',
      fromOperation: 'FindAsync',
      toComponent: 'OrderCache',
      toOperation: 'TryGetAsync',
      callCount: 58,
    },
    {
      fromComponent: 'OrderService',
      fromOperation: 'FindAsync',
      toComponent: 'OrderRepository',
      toOperation: 'FindAsync',
      callCount: 354,
    },
  ],
};

describe('buildArchitectureGraph', () => {
  it('returns an empty graph when the route had no execution in the window', () => {
    const result = buildArchitectureGraph({ totalExecutions: 0, nodes: [], edges: [] });

    expect(result.nodes).toEqual([]);
    expect(result.edges).toEqual([]);
  });

  it('translates every node, embedding the occurrence percentage only below 100%', () => {
    const result = buildArchitectureGraph(CONDITIONAL_MAP);

    const byComponent = (component: string) => result.nodes.find((n) => n.component === component)!;

    expect(byComponent('OrdersController').operation).toBe('GetOrder');
    expect(byComponent('OrderService').operation).toBe('FindAsync');
    expect(byComponent('OrderCache').operation).toBe('TryGetAsync (14%)');
    expect(byComponent('OrderRepository').operation).toBe('FindAsync (86%)');
  });

  it('carries no sequence, duration or N+1 count, and a uniform completed state', () => {
    const result = buildArchitectureGraph(CONDITIONAL_MAP);

    for (const node of result.nodes) {
      expect(node.sequence).toBeNull();
      expect(node.durationMicros).toBeNull();
      expect(node.nPlusOneCount).toBeNull();
      expect(node.state).toBe('completed');
    }
  });

  it('maps every edge to the matching source/target node ids', () => {
    const result = buildArchitectureGraph(CONDITIONAL_MAP);

    const idOf = (component: string) => result.nodes.find((n) => n.component === component)!.id;

    expect(result.edges).toHaveLength(3);
    expect(result.edges).toContainEqual(
      expect.objectContaining({
        sourceId: idOf('OrdersController'),
        targetId: idOf('OrderService'),
      }),
    );
    expect(result.edges).toContainEqual(
      expect.objectContaining({ sourceId: idOf('OrderService'), targetId: idOf('OrderCache') }),
    );
    expect(result.edges).toContainEqual(
      expect.objectContaining({
        sourceId: idOf('OrderService'),
        targetId: idOf('OrderRepository'),
      }),
    );
  });
});
