import * as dagre from '@dagrejs/dagre';
import { ComponentType, FlowSpan, FlowStatus, NodeVisualState } from '../../../core/models';
import { formatDurationMicros } from '../../../shared/duration-format';

export interface GraphNode {
  id: string;
  component: string;
  operation: string;
  type: ComponentType;
  /** Start order within the real trace, or `null` for a ghost (never-executed) node. */
  sequence: number | null;
  state: NodeVisualState;
  durationMicros: number | null;
  /**
   * Total size of the `(component, operation)` group this node belongs to, when that group is a
   * suspected N+1 (grouped across the whole trace, not by direct sibling, because each
   * query in a classic N+1 is a child of a different loop iteration, not of the same parent).
   * `null` when the node isn't part of such a group.
   */
  nPlusOneCount: number | null;
  x: number;
  y: number;
  width: number;
  height: number;
}

export interface GraphEdge {
  id: string;
  sourceId: string;
  targetId: string;
  points: { x: number; y: number }[];
}

export interface GraphResult {
  nodes: GraphNode[];
  edges: GraphEdge[];
}

export interface GraphBuilderInput {
  spans: FlowSpan[];
  traceStatus: FlowStatus;
  slowThresholdMicros: number;
  /**
   * This execution's own complete span set, when `spans` is only a partial reveal of it (a
   * replay animating toward it). Defaults to `spans` itself — nothing further of this same
   * execution left to reveal — which is always the case live (M1): there, `spans` already grows
   * in real time as the one and only record of what happened.
   */
  fullSpans?: FlowSpan[];
  /**
   * A different (earlier) execution of the same route, used to infer a branch this one's
   * `fullSpans` never took at all — as opposed to one `fullSpans` does contain but `spans`
   * hasn't revealed yet.
   */
  blueprintSpans?: FlowSpan[];
  /**
   * True while there is still more to reveal: a live trace still `Running`, or a replay that
   * has not reached its last step yet. A span this execution's own `fullSpans` proves it took
   * (just not yet revealed by `spans`) is `'waiting'` while this holds, `'not-executed'` only
   * once it no longer does. Defaults to `traceStatus === 'Running'` (A4).
   */
  inProgress?: boolean;
  /**
   * Dagre layout direction (guide seção 6.4): `'LR'` fits a wide, short canvas; `'TB'` fits a
   * canvas taller than it is wide (default mobile portrait, or a desktop canvas squeezed narrow
   * by the action/details panels). Decided by the caller from the canvas's own measured aspect
   * ratio, never by this pure function. Defaults to `'LR'`.
   */
  rankdir?: 'LR' | 'TB';
}

const NODE_WIDTH = 184;
const NODE_MAX_WIDTH = 320;
const NODE_HEIGHT = 60;
const NODE_LEFT_INSET_PX = 48;
const NODE_RIGHT_MARGIN_PX = 10;
const NODE_DURATION_GAP_PX = 8;
// Both labels are rendered at a fixed font size (flow-graph.component.scss), so an average
// advance width per character is enough to size the card without measuring real text — the
// component name is proportional sans-serif, the operation/duration pair is monospace.
const COMPONENT_CHAR_WIDTH_PX = 6.8;
const MONO_CHAR_WIDTH_PX = 6.3;
const GHOST_ID_PREFIX = 'ghost-';

/**
 * Minimum size of a `(component, operation)` group of `Database` steps to flag it as a suspected
 * N+1. Fixed on purpose: a per-instance configuration knob is only worth
 * adding if real usage shows this number wrong.
 */
export const N_PLUS_ONE_THRESHOLD = 3;

/**
 * A node this wide fits its component name and its operation + duration pair without
 * truncating either (a fixed card width used to force "/api/orders/{id:int}" and its
 * duration to overlap). Capped at `NODE_MAX_WIDTH` so one pathologically long route can't blow
 * up the whole graph's layout; `nodeOperationLabel` in the SVG component still truncates with an
 * ellipsis for whatever doesn't fit past that cap.
 */
function nodeWidthFor(component: string, operation: string, durationMicros: number | null): number {
  const durationLabel = durationMicros === null ? '' : formatDurationMicros(durationMicros);
  const operationAndDurationWidth =
    NODE_LEFT_INSET_PX +
    operation.length * MONO_CHAR_WIDTH_PX +
    (durationLabel ? NODE_DURATION_GAP_PX + durationLabel.length * MONO_CHAR_WIDTH_PX : 0) +
    NODE_RIGHT_MARGIN_PX;
  const componentWidth =
    NODE_LEFT_INSET_PX + component.length * COMPONENT_CHAR_WIDTH_PX + NODE_RIGHT_MARGIN_PX;

  return Math.min(NODE_MAX_WIDTH, Math.max(NODE_WIDTH, operationAndDurationWidth, componentWidth));
}

/** Ghost nodes (inferred from the blueprint, never actually executed) carry this id prefix. */
export function isGhostNodeId(id: string): boolean {
  return id.startsWith(GHOST_ID_PREFIX);
}

/**
 * Matches spans across executions (same route, different runs) or within one execution (N+1
 * siblings): a `spanId` is unique per execution and never repeats, so it can't be the key.
 */
export function spanGroupKey(component: string, operation: string): string {
  return `${component}::${operation}`;
}

interface MergedNode {
  id: string;
  component: string;
  operation: string;
  type: ComponentType;
  sequence: number | null;
  /** `null` marks a ghost node: present in the blueprint, absent from `fullSpans`. */
  status: FlowStatus | null;
  durationMicros: number | null;
  children: MergedNode[];
  /**
   * Fixed answer for a blueprint-only ghost (M1): this execution's own `fullSpans` already
   * proves whether that branch ran, so its state never depends on the global `inProgress`
   * flag the way a same-execution not-yet-revealed span's does. `null` for every other node
   * (real or same-execution ghost), which defers to the standard `inProgress` rule instead.
   */
  forcedState: NodeVisualState | null;
}

/**
 * Pure function: spans in, a laid-out graph out. No Angular, no HTTP, no DOM — `@dagrejs/dagre`
 * computes node positions only, never renders anything (no ready-made graph-rendering library is
 * allowed; the SVG component owns rendering).
 */
export function buildGraph(input: GraphBuilderInput): GraphResult {
  const tree = buildMergedTree(input);
  if (!tree) {
    return { nodes: [], edges: [] };
  }

  const layoutGraph = new dagre.graphlib.Graph();
  // rankdir 'LR' (guide seção 6.4): a left-to-right flow instead of a single vertical column is
  // what turns a 10-step trace into a readable graph instead of one tall, scroll-only strip (D4).
  // 'TB' is the caller's override for a canvas taller than it is wide, where a left-to-right flow
  // would instead force a few wide nodes into a narrow strip with most of the canvas empty.
  layoutGraph.setGraph({
    rankdir: input.rankdir ?? 'LR',
    nodesep: 16,
    ranksep: 44,
    marginx: 24,
    marginy: 24,
  });
  layoutGraph.setDefaultEdgeLabel(() => ({}));

  const flatNodes: MergedNode[] = [];
  const flatEdges: { parentId: string; childId: string }[] = [];

  const visit = (node: MergedNode, parentId: string | null): void => {
    flatNodes.push(node);
    layoutGraph.setNode(node.id, {
      width: nodeWidthFor(node.component, node.operation, node.durationMicros),
      height: NODE_HEIGHT,
    });
    if (parentId !== null) {
      layoutGraph.setEdge(parentId, node.id);
      flatEdges.push({ parentId, childId: node.id });
    }

    for (const child of node.children) {
      visit(child, node.id);
    }
  };

  visit(tree, null);
  dagre.layout(layoutGraph);

  const nPlusOneCounts = computeNPlusOneCounts(flatNodes);

  const nodes: GraphNode[] = flatNodes.map((node) => {
    const position = layoutGraph.node(node.id);
    return {
      id: node.id,
      component: node.component,
      operation: node.operation,
      type: node.type,
      sequence: node.sequence,
      state: resolveState(node, input),
      durationMicros: node.durationMicros,
      nPlusOneCount: nPlusOneCounts.get(node.id) ?? null,
      x: position.x,
      y: position.y,
      width: position.width,
      height: position.height,
    };
  });

  const edges: GraphEdge[] = flatEdges.map(({ parentId, childId }) => ({
    id: `${parentId}->${childId}`,
    sourceId: parentId,
    targetId: childId,
    points: layoutGraph.edge(parentId, childId)?.points ?? [],
  }));

  return { nodes, edges };
}

/**
 * A blueprint-only ghost (M1) already has its answer fixed at merge time (`forcedState`): this
 * execution's own `fullSpans` already proves whether that branch ran. Every other ghost is a
 * same-execution span `fullSpans` has but `spans` hasn't revealed yet (A4) — "waiting" while
 * `inProgress` holds, "not-executed" only once it no longer does, since there is no way to know
 * it won't show up until then.
 */
function resolveState(node: MergedNode, input: GraphBuilderInput): NodeVisualState {
  if (node.status === null) {
    if (node.forcedState) {
      return node.forcedState;
    }

    const inProgress = input.inProgress ?? input.traceStatus === 'Running';
    return inProgress ? 'waiting' : 'not-executed';
  }

  return stateFromSpanStatus(node.status, node.durationMicros, input.slowThresholdMicros);
}

/**
 * Shared with the waterfall (`details/waterfall.ts`, guide seção 6.6): a real span's visual state
 * depends only on its own status/duration and the slow threshold, regardless of whether it's
 * reached through the graph or the details panel.
 */
export function stateFromSpanStatus(
  status: FlowStatus,
  durationMicros: number | null,
  slowThresholdMicros: number,
): NodeVisualState {
  if (status === 'Error') {
    return 'error';
  }

  if (status === 'Running') {
    return 'running';
  }

  if (durationMicros !== null && durationMicros >= slowThresholdMicros) {
    return 'slow';
  }

  return 'completed';
}

/**
 * Groups real (non-ghost) `Database` nodes by `component::operation` across the whole merged
 * tree, regardless of their parent. Returns the group's size for every node whose group reached
 * `N_PLUS_ONE_THRESHOLD`, keyed by node id.
 */
function computeNPlusOneCounts(nodes: MergedNode[]): Map<string, number> {
  const idsByGroupKey = new Map<string, string[]>();

  for (const node of nodes) {
    if (node.status === null || node.type !== 'Database') {
      continue;
    }

    const key = spanGroupKey(node.component, node.operation);
    const ids = idsByGroupKey.get(key) ?? [];
    ids.push(node.id);
    idsByGroupKey.set(key, ids);
  }

  const counts = new Map<string, number>();
  for (const ids of idsByGroupKey.values()) {
    if (ids.length < N_PLUS_ONE_THRESHOLD) {
      continue;
    }

    for (const id of ids) {
      counts.set(id, ids.length);
    }
  }

  return counts;
}

/**
 * Same grouping rule as `computeNPlusOneCounts`, applied to a flat list of real spans (the shape
 * the details panel already has from `FlowTraceDetail.spans`). Returns the full
 * group, sorted by execution order, or an empty array when `spanId` isn't part of one.
 */
export function findNPlusOneGroup(spans: FlowSpan[], spanId: string): FlowSpan[] {
  const target = spans.find((span) => span.spanId === spanId);
  if (!target || target.type !== 'Database') {
    return [];
  }

  const group = spans.filter(
    (span) =>
      span.type === 'Database' &&
      span.component === target.component &&
      span.operation === target.operation,
  );

  return group.length >= N_PLUS_ONE_THRESHOLD ? group.sort((a, b) => a.sequence - b.sequence) : [];
}

function buildChildrenByParent(spans: FlowSpan[]): Map<string | null, FlowSpan[]> {
  const byParent = new Map<string | null, FlowSpan[]>();
  for (const span of spans) {
    const children = byParent.get(span.parentSpanId) ?? [];
    children.push(span);
    byParent.set(span.parentSpanId, children);
  }

  for (const children of byParent.values()) {
    children.sort((a, b) => a.sequence - b.sequence);
  }

  return byParent;
}

/**
 * A span is the trace's root when its `parentSpanId` is `null`, or when it doesn't match any
 * `spanId` in the same set — the backend normalizes a true root to `null`,
 * but this stays robust even against a non-conforming store that leaves a dangling ambient parent
 * id (e.g. the host framework's own Activity) instead.
 */
function findRoot(spans: FlowSpan[]): FlowSpan | undefined {
  return spans.find(
    (span) => span.parentSpanId === null || !spans.some((s) => s.spanId === span.parentSpanId),
  );
}

function buildMergedTree(input: GraphBuilderInput): MergedNode | null {
  const fullSpans = input.fullSpans ?? input.spans;
  const revealedIds = new Set(input.spans.map((span) => span.spanId));
  const fullChildrenByParent = buildChildrenByParent(fullSpans);
  const blueprintChildrenByParent = buildChildrenByParent(input.blueprintSpans ?? []);

  const fullRoot = findRoot(fullSpans);
  if (fullRoot) {
    const blueprintRoot = findRoot(input.blueprintSpans ?? []);
    return mergeWithBlueprint(
      fullRoot,
      blueprintRoot,
      fullChildrenByParent,
      blueprintChildrenByParent,
      revealedIds,
    );
  }

  const blueprintRoot = findRoot(input.blueprintSpans ?? []);
  if (blueprintRoot) {
    // Nothing of this execution exists yet at all: there is no `fullSpans` status to judge
    // against, so defer to the standard `inProgress` rule (`forcedState: null`) same as before.
    return buildBlueprintOnlyGhost(blueprintRoot, blueprintChildrenByParent, null);
  }

  return null;
}

/**
 * Walks `fullSpans`' own shape (this execution's complete record), merging in a `blueprintSpans`
 * branch wherever `fullSpan`'s own children never include it (M1) — resolved immediately from
 * `fullSpan`'s own status, regardless of whether `fullSpan` itself has been revealed yet. A real
 * node not yet present among `spans` is masked to a same-execution ghost (A4): `status: null`,
 * `forcedState: null`, deferring to `inProgress`.
 */
function mergeWithBlueprint(
  fullSpan: FlowSpan,
  blueprintSpan: FlowSpan | undefined,
  fullChildrenByParent: Map<string | null, FlowSpan[]>,
  blueprintChildrenByParent: Map<string | null, FlowSpan[]>,
  revealedIds: Set<string>,
): MergedNode {
  const fullChildren = fullChildrenByParent.get(fullSpan.spanId) ?? [];
  const blueprintChildren = blueprintSpan
    ? (blueprintChildrenByParent.get(blueprintSpan.spanId) ?? [])
    : [];

  const matchedBlueprintIds = new Set<string>();
  const children: MergedNode[] = [];

  for (const fullChild of fullChildren) {
    const match = blueprintChildren.find(
      (candidate) =>
        !matchedBlueprintIds.has(candidate.spanId) &&
        candidate.component === fullChild.component &&
        candidate.operation === fullChild.operation,
    );

    if (match) {
      matchedBlueprintIds.add(match.spanId);
    }

    children.push(
      mergeWithBlueprint(
        fullChild,
        match,
        fullChildrenByParent,
        blueprintChildrenByParent,
        revealedIds,
      ),
    );
  }

  for (const blueprintChild of blueprintChildren) {
    if (!matchedBlueprintIds.has(blueprintChild.spanId)) {
      // `fullSpan` already proves this branch's fate: still running (might yet take it) or
      // concluded without it (never will), regardless of whether `fullSpan` itself is revealed.
      const forcedState: NodeVisualState =
        fullSpan.status === 'Running' ? 'waiting' : 'not-executed';
      children.push(
        buildBlueprintOnlyGhost(blueprintChild, blueprintChildrenByParent, forcedState),
      );
    }
  }

  const revealed = revealedIds.has(fullSpan.spanId);

  return {
    id: fullSpan.spanId,
    component: fullSpan.component,
    operation: fullSpan.operation,
    type: fullSpan.type,
    sequence: fullSpan.sequence,
    status: revealed ? fullSpan.status : null,
    durationMicros: revealed ? fullSpan.durationMicros : null,
    children,
    forcedState: null,
  };
}

function buildBlueprintOnlyGhost(
  blueprintSpan: FlowSpan,
  blueprintChildrenByParent: Map<string | null, FlowSpan[]>,
  forcedState: NodeVisualState | null,
): MergedNode {
  const children = (blueprintChildrenByParent.get(blueprintSpan.spanId) ?? []).map((child) =>
    buildBlueprintOnlyGhost(child, blueprintChildrenByParent, forcedState),
  );

  return {
    id: `${GHOST_ID_PREFIX}${blueprintSpan.spanId}`,
    component: blueprintSpan.component,
    operation: blueprintSpan.operation,
    type: blueprintSpan.type,
    sequence: null,
    status: null,
    durationMicros: null,
    children,
    forcedState,
  };
}
