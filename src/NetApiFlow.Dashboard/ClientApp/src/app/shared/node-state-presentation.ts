import { NodeVisualState } from '../core/models';

/**
 * Cor nunca é o único sinal (seção 7.2 do prompt): every state also carries a distinct icon and
 * a text label, so the six states stay distinguishable with color entirely removed (e.g. a user
 * with color blindness, or a black-and-white printout).
 */
export interface NodeStatePresentation {
  /** Relative to the dashboard's own base path, per `docs/brand/node-states/` (ADR-0009). */
  readonly iconSrc: string;
  readonly label: string;
}

export const NODE_STATE_PRESENTATION: Readonly<Record<NodeVisualState, NodeStatePresentation>> = {
  waiting: { iconSrc: 'node-states/waiting.svg', label: 'Waiting' },
  running: { iconSrc: 'node-states/running.svg', label: 'Running' },
  completed: { iconSrc: 'node-states/completed.svg', label: 'Completed' },
  slow: { iconSrc: 'node-states/slow.svg', label: 'Slow' },
  error: { iconSrc: 'node-states/error.svg', label: 'Error' },
  'not-executed': { iconSrc: 'node-states/skipped.svg', label: 'Not executed' },
};
