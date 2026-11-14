import { GRAPH_NODE_VISUAL_STATES, NODE_STATE_PRESENTATION } from './node-state-presentation';

describe('GRAPH_NODE_VISUAL_STATES', () => {
  it('excludes cancelled and timeout, the two states only a whole trace can have (M2)', () => {
    expect(GRAPH_NODE_VISUAL_STATES).not.toContain('cancelled');
    expect(GRAPH_NODE_VISUAL_STATES).not.toContain('timeout');
    expect(GRAPH_NODE_VISUAL_STATES).toEqual([
      'waiting',
      'running',
      'completed',
      'slow',
      'error',
      'not-executed',
    ]);
  });

  it('does not change NODE_STATE_PRESENTATION itself, still used in full by /history, the diff view and recent-executions', () => {
    expect(Object.keys(NODE_STATE_PRESENTATION)).toEqual([
      'waiting',
      'running',
      'completed',
      'slow',
      'error',
      'not-executed',
      'cancelled',
      'timeout',
    ]);
  });
});
