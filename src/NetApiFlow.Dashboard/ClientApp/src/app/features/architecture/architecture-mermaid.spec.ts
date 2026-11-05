import { architectureMapToMermaid } from './architecture-mermaid';
import { ArchitectureMap } from '../../core/models';

describe('architectureMapToMermaid', () => {
  it('renders an empty flowchart for a map with no nodes', () => {
    expect(architectureMapToMermaid({ totalExecutions: 0, nodes: [], edges: [] })).toBe(
      'flowchart TD',
    );
  });

  it('matches the expected flowchart text, with the occurrence percentage embedded in the label', () => {
    const map: ArchitectureMap = {
      totalExecutions: 412,
      nodes: [
        {
          component: 'OrdersController',
          operation: 'GetOrder',
          type: 'Controller',
          executionCount: 412,
        },
        { component: 'OrderCache', operation: 'TryGetAsync', type: 'Custom', executionCount: 58 },
      ],
      edges: [
        {
          fromComponent: 'OrdersController',
          fromOperation: 'GetOrder',
          toComponent: 'OrderCache',
          toOperation: 'TryGetAsync',
          callCount: 58,
        },
      ],
    };

    expect(architectureMapToMermaid(map)).toBe(
      [
        'flowchart TD',
        '  n0["OrdersController: GetOrder"]',
        '  n1["OrderCache: TryGetAsync (14%)"]',
        '  n0 --> n1',
      ].join('\n'),
    );
  });

  it('escapes double quotes in component/operation names', () => {
    const map: ArchitectureMap = {
      totalExecutions: 1,
      nodes: [
        {
          component: 'My"Controller',
          operation: 'Do"Thing',
          type: 'Controller',
          executionCount: 1,
        },
      ],
      edges: [],
    };

    expect(architectureMapToMermaid(map)).toBe(
      'flowchart TD\n  n0["My#quot;Controller: Do#quot;Thing"]',
    );
  });
});
