import { NodeVisualState } from '../features/flow/graph/graph-builder';

/**
 * Cor nunca é o único sinal (seção 7.2 do prompt): every state also carries a distinct icon and
 * a text label, so the six states stay distinguishable with color entirely removed (e.g. a user
 * with color blindness, or a black-and-white printout).
 */
export interface NodeStatePresentation {
  readonly icon: string;
  readonly label: string;
}

export const NODE_STATE_PRESENTATION: Readonly<Record<NodeVisualState, NodeStatePresentation>> = {
  waiting: { icon: '⏳', label: 'Waiting' },
  running: { icon: '▶', label: 'Running' },
  completed: { icon: '✓', label: 'Completed' },
  slow: { icon: '⚠', label: 'Slow' },
  error: { icon: '✕', label: 'Error' },
  'not-executed': { icon: '∅', label: 'Not executed' },
};
