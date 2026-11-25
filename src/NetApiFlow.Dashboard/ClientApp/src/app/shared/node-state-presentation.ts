import { NodeVisualState } from '../core/models';
import { TranslationKey } from '../core/i18n/translations';

/**
 * Color is never the only signal: every state also carries a distinct icon and
 * a text label, so the six states stay distinguishable with color entirely removed (e.g. a user
 * with color blindness, or a black-and-white printout).
 */
export interface NodeStatePresentation {
  /** Relative to the dashboard's own base path, per `docs/brand/node-states/`. */
  readonly iconSrc: string;
  readonly labelKey: TranslationKey;
}

export const NODE_STATE_PRESENTATION: Readonly<Record<NodeVisualState, NodeStatePresentation>> = {
  waiting: { iconSrc: 'node-states/waiting.svg', labelKey: 'node.state.waiting' },
  running: { iconSrc: 'node-states/running.svg', labelKey: 'node.state.running' },
  completed: { iconSrc: 'node-states/completed.svg', labelKey: 'node.state.completed' },
  slow: { iconSrc: 'node-states/slow.svg', labelKey: 'node.state.slow' },
  error: { iconSrc: 'node-states/error.svg', labelKey: 'node.state.error' },
  'not-executed': { iconSrc: 'node-states/skipped.svg', labelKey: 'node.state.notExecuted' },
  cancelled: { iconSrc: 'node-states/cancelled.svg', labelKey: 'node.state.cancelled' },
  timeout: { iconSrc: 'node-states/timeout.svg', labelKey: 'node.state.timeout' },
};

/**
 * States a graph node (a span) can actually show: cancellation and timeout are a whole trace's
 * own status, never a single span's. `/history`, the diff view and
 * recent-executions keep reading every key of `NODE_STATE_PRESENTATION` directly for a trace's
 * own status, untouched by this narrower list — it only governs the live graph's legend.
 */
export const GRAPH_NODE_VISUAL_STATES: readonly NodeVisualState[] = (
  Object.keys(NODE_STATE_PRESENTATION) as NodeVisualState[]
).filter((state) => state !== 'cancelled' && state !== 'timeout');
