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

  it('lays out top-to-bottom instead of left-to-right when rankdir is TB', () => {
    const resultTb = buildArchitectureGraph(CONDITIONAL_MAP, 'TB');
    const resultLr = buildArchitectureGraph(CONDITIONAL_MAP, 'LR');

    const byComponent = (result: typeof resultTb, component: string) =>
      result.nodes.find((n) => n.component === component)!;

    // Top-to-bottom: the next level sits strictly below, same as the default LR case sits
    // strictly to the right — the opposite axis carries the rank order instead.
    expect(byComponent(resultTb, 'OrderService').y).toBeGreaterThan(
      byComponent(resultTb, 'OrdersController').y,
    );
    expect(byComponent(resultLr, 'OrderService').x).toBeGreaterThan(
      byComponent(resultLr, 'OrdersController').x,
    );
  });

  describe('how many times a step runs in each execution', () => {
    function labelOf(executionCount: number, totalExecutions: number): string {
      const result = buildArchitectureGraph(
        {
          totalExecutions,
          nodes: [
            { component: 'Database', operation: 'SELECT items', type: 'Database', executionCount },
          ],
          edges: [],
        },
        'LR',
      );
      return result.nodes[0].operation;
    }

    it('says how many times a step runs in each execution when it runs more than once', () => {
      expect(labelOf(15, 3)).toBe('SELECT items (×5)');
    });

    it('rounds a repeat that is not a whole number to one decimal', () => {
      expect(labelOf(7, 3)).toBe('SELECT items (×2.3)');
    });

    it('says nothing of repeats for a step that runs once in every execution', () => {
      expect(labelOf(3, 3)).toBe('SELECT items');
    });

    it('keeps saying how often a step that is skipped sometimes runs', () => {
      expect(labelOf(1, 3)).toBe('SELECT items (33%)');
    });
  });
});
